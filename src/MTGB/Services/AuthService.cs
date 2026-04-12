using IdentityModel.OidcClient;
using IdentityModel.OidcClient.Browser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTGB.Config;
using MTGB.Core.Security;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace MTGB.Services;

// ── Auth result models ────────────────────────────────────────────

public class AuthResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? UserName { get; init; }
    public int? OrganisationId { get; init; }
}

// ── Interface ─────────────────────────────────────────────────────

public interface IAuthService
{
    /// <summary>
    /// Whether the user is currently authenticated via either auth path.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Validate and store an API key.
    /// </summary>
    Task<AuthResult> LoginWithApiKeyAsync(
        string apiKey,
        int organisationId,
        CancellationToken ct = default);

    /// <summary>
    /// Launch the OAuth2 PKCE browser flow and store the resulting tokens.
    /// </summary>
    Task<AuthResult> LoginWithOAuthAsync(CancellationToken ct = default);

    /// <summary>
    /// Refresh the OAuth2 access token using the stored refresh token.
    /// Returns false if refresh fails and re-login is required.
    /// </summary>
    Task<bool> RefreshOAuthTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// Clear all stored credentials and sign out.
    /// </summary>
    void Logout();
}

// ── Implementation ────────────────────────────────────────────────

/// <summary>
/// Manages both authentication paths for MTGB.
/// API key path — simple, stores key in Credential Manager.
/// OAuth2 path — PKCE flow via browser, stores tokens in Credential Manager.
/// </summary>
public class AuthService : IAuthService
{
    private readonly ICredentialManager _credentials;
    private readonly ISimplyPrintApiClient _apiClient;
    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<AuthService> _logger;

    // OAuth2 configuration — injected at build time via CI.
    // Never hardcoded, never committed to source control.
    private const string OAuthAuthority =
        "https://simplyprint.io";
    private const string OAuthClientId =
        "${MTGB_OAUTH_CLIENT_ID}";
    private const string OAuthCallbackUrl =
        "http://localhost:7879/callback";
    private const string OAuthScopes =
        "user.read printers.read printers.actions " +
        "spools.read queue.read queue.write " +
        "print_history.read statistics.read";

    public AuthService(
        ICredentialManager credentials,
        ISimplyPrintApiClient apiClient,
        IOptions<AppSettings> settings,
        ILogger<AuthService> logger)
    {
        _credentials = credentials;
        _apiClient = apiClient;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsAuthenticated =>
        _settings.Value.AuthMode == AuthMode.ApiKey
            ? _credentials.Exists(CredentialKey.ApiKey)
            : _credentials.Exists(CredentialKey.OAuthAccessToken);

    // ── API key path ──────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AuthResult> LoginWithApiKeyAsync(
        string apiKey,
        int organisationId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return Fail("API key cannot be empty.");

        if (organisationId <= 0)
            return Fail("Organisation ID must be a positive number.");

        // Store temporarily to test
        _credentials.Save(CredentialKey.ApiKey, apiKey);

        var valid = await _apiClient.TestConnectionAsync(ct);

        if (!valid)
        {
            // Remove the invalid key — don't leave bad credentials stored
            _credentials.Delete(CredentialKey.ApiKey);
            return Fail(
                "API key is invalid or does not have access to " +
                "this organisation. Please check your SimplyPrint " +
                "account settings.");
        }

        _logger.LogInformation(
            "API key authentication successful for org {OrgId}.",
            organisationId);

        return new AuthResult { Success = true };
    }

    // ── OAuth2 PKCE path ──────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AuthResult> LoginWithOAuthAsync(
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Starting OAuth2 PKCE flow.");

            var browser = new LocalCallbackBrowser(OAuthCallbackUrl, _logger);

            var oidcOptions = new OidcClientOptions
            {
                Authority = OAuthAuthority,
                ClientId = OAuthClientId,
                RedirectUri = OAuthCallbackUrl,
                Scope = OAuthScopes,
                Browser = browser,
                Policy = new Policy
                {
                    RequireIdentityTokenSignature = false
                }
            };

            var oidcClient = new OidcClient(oidcOptions);
            var result = await oidcClient.LoginAsync(
                new LoginRequest(), ct);

            if (result.IsError)
            {
                _logger.LogWarning(
                    "OAuth2 login failed: {Error}", result.Error);
                return Fail(
                    $"Login failed: {result.ErrorDescription ?? result.Error}");
            }

            // Store tokens securely
            _credentials.Save(
                CredentialKey.OAuthAccessToken,
                result.AccessToken);

            if (!string.IsNullOrWhiteSpace(result.RefreshToken))
                _credentials.Save(
                    CredentialKey.OAuthRefreshToken,
                    result.RefreshToken);

            var userName = result.User?
                .FindFirst("name")?.Value ?? "SimplyPrint user";

            _logger.LogInformation(
                "OAuth2 authentication successful for {UserName}.",
                userName);

            return new AuthResult
            {
                Success = true,
                UserName = userName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth2 login threw an exception.");
            return Fail(
                "An unexpected error occurred during login. " +
                "Please try again.");
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RefreshOAuthTokenAsync(
        CancellationToken ct = default)
    {
        var refreshToken = _credentials.Load(
            CredentialKey.OAuthRefreshToken);

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            _logger.LogWarning(
                "No refresh token available — re-login required.");
            return false;
        }

        try
        {
            var oidcOptions = new OidcClientOptions
            {
                Authority = OAuthAuthority,
                ClientId = OAuthClientId,
                RedirectUri = OAuthCallbackUrl,
                Scope = OAuthScopes
            };

            var oidcClient = new OidcClient(oidcOptions);
            var result = await oidcClient.RefreshTokenAsync(
                refreshToken, cancellationToken: ct);

            if (result.IsError)
            {
                _logger.LogWarning(
                    "Token refresh failed: {Error}", result.Error);
                return false;
            }

            _credentials.Save(
                CredentialKey.OAuthAccessToken,
                result.AccessToken);

            if (!string.IsNullOrWhiteSpace(result.RefreshToken))
                _credentials.Save(
                    CredentialKey.OAuthRefreshToken,
                    result.RefreshToken);

            _logger.LogInformation("OAuth2 token refreshed successfully.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh threw an exception.");
            return false;
        }
    }

    /// <inheritdoc/>
    public void Logout()
    {
        _credentials.Delete(CredentialKey.ApiKey);
        _credentials.Delete(CredentialKey.OAuthAccessToken);
        _credentials.Delete(CredentialKey.OAuthRefreshToken);
        _logger.LogInformation("User logged out — credentials cleared.");
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static AuthResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}

// ── Local PKCE callback browser ───────────────────────────────────

/// <summary>
/// Opens the user's default browser for the OAuth2 login page,
/// then spins up a local HTTP listener to catch the callback.
/// Shuts down immediately after receiving the callback.
/// </summary>
internal class LocalCallbackBrowser : IBrowser
{
    private readonly string _callbackUrl;
    private readonly ILogger _logger;

    public LocalCallbackBrowser(string callbackUrl, ILogger logger)
    {
        _callbackUrl = callbackUrl;
        _logger = logger;
    }

    public async Task<BrowserResult> InvokeAsync(
        BrowserOptions options,
        CancellationToken ct = default)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(_callbackUrl.TrimEnd('/') + "/");
        listener.Start();

        _logger.LogInformation(
            "Listening for OAuth2 callback on {Url}", _callbackUrl);

        // Open the browser to the SimplyPrint login page
        Process.Start(new ProcessStartInfo
        {
            FileName = options.StartUrl,
            UseShellExecute = true
        });

        try
        {
            // Wait for the callback with a 2 minute timeout
            using var timeoutCts = CancellationTokenSource
                .CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(2));

            var context = await listener
                .GetContextAsync()
                .WaitAsync(timeoutCts.Token);

            var callbackUrl = context.Request.Url?.ToString() ?? string.Empty;

            // Respond to the browser so it doesn't hang
            var responseHtml =
                "<html><body style='font-family:Segoe UI;text-align:center;" +
                "padding:60px;background:#1a1a1a;color:#C9930E'>" +
                "<h2>M · T · G · B</h2>" +
                "<p style='color:#3BB273'>Login successful.</p>" +
                "<p style='color:#888'>You can close this window.<br>" +
                "It goes Bing.</p>" +
                "</body></html>";

            var responseBytes = Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentType = "text/html";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream
                .WriteAsync(responseBytes, ct);
            context.Response.Close();

            return new BrowserResult
            {
                ResultType = BrowserResultType.Success,
                Response = callbackUrl
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("OAuth2 login timed out.");
            return new BrowserResult
            {
                ResultType = BrowserResultType.Timeout,
                ErrorMessage = "Login timed out. Please try again."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth2 callback listener failed.");
            return new BrowserResult
            {
                ResultType = BrowserResultType.UnknownError,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            listener.Stop();
        }
    }
}