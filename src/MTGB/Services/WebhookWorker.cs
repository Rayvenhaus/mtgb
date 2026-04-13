using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTGB.Config;
using MTGB.Core.Security;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MTGB.Services;

// ── Webhook payload models ────────────────────────────────────────

/// <summary>
/// Root webhook payload from SimplyPrint.
/// </summary>
public class WebhookPayload
{
    [JsonPropertyName("webhook_id")]
    public int WebhookId { get; init; }

    [JsonPropertyName("event")]
    public string Event { get; init; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("data")]
    public WebhookData? Data { get; init; }
}

/// <summary>
/// Data block within a webhook payload.
/// </summary>
public class WebhookData
{
    [JsonPropertyName("job")]
    public WebhookJob? Job { get; init; }

    [JsonPropertyName("printer")]
    public WebhookPrinter? Printer { get; init; }

    [JsonPropertyName("user")]
    public WebhookUser? User { get; init; }
}

public class WebhookJob
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("uid")]
    public string? Uid { get; init; }

    [JsonPropertyName("panel_url")]
    public string? PanelUrl { get; init; }

    [JsonPropertyName("filename")]
    public string? Filename { get; init; }

    [JsonPropertyName("started")]
    public long? Started { get; init; }

    [JsonPropertyName("ended")]
    public long? Ended { get; init; }
}

public class WebhookPrinter
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public class WebhookUser
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }
}

// ── Implementation ────────────────────────────────────────────────

/// <summary>
/// MTGB Webhook Receiver.
/// Spins up a local HTTP listener on a configurable port.
/// Receives real-time event POSTs from SimplyPrint.
/// Validates every request via the X-SP-Token header.
/// Hands valid events to the state diff engine.
///
/// Auto-registers the webhook with SimplyPrint on startup
/// and removes it cleanly on shutdown.
/// </summary>
public class WebhookWorker : BackgroundService
{
    private readonly ISimplyPrintApiClient _apiClient;
    private readonly IStateDiffEngine _diffEngine;
    private readonly INotificationManager _notificationManager;
    private readonly IAuthService _authService;
    private readonly ICredentialManager _credentials;
    private readonly WebhookSecretManager _secretManager;
    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<WebhookWorker> _logger;

    private HttpListener? _listener;
    private string? _webhookSecret;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WebhookWorker(
        ISimplyPrintApiClient apiClient,
        IStateDiffEngine diffEngine,
        INotificationManager notificationManager,
        IAuthService authService,
        ICredentialManager credentials,
        WebhookSecretManager secretManager,
        IOptions<AppSettings> settings,
        ILogger<WebhookWorker> logger)
    {
        _apiClient = apiClient;
        _diffEngine = diffEngine;
        _notificationManager = notificationManager;
        _authService = authService;
        _credentials = credentials;
        _secretManager = secretManager;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var webhookSettings = _settings.Value.Webhook;

        if (!webhookSettings.Enabled)
        {
            _logger.LogInformation(
                "Webhook receiver disabled in settings. " +
                "Polling will carry the burden alone, " +
                "stoically, without complaint.");
            return;
        }

        _logger.LogInformation(
            "Webhook worker starting on port {Port}.",
            webhookSettings.Port);

        // Get or generate the per-installation webhook secret
        _webhookSecret = _secretManager.GetOrCreate();

        // Start the HTTP listener
        if (!StartListener(webhookSettings.Port))
            return;

        // Register with SimplyPrint if not already registered
        await EnsureWebhookRegisteredAsync(stoppingToken);

        // Listen for incoming requests
        await ListenAsync(stoppingToken);

        // Clean up on shutdown
        await UnregisterWebhookAsync();
        StopListener();

        _logger.LogInformation(
            "Webhook worker stopped. " +
            "The Ministry has filed the appropriate forms.");
    }

    // ── Listener lifecycle ────────────────────────────────────────

    private bool StartListener(int port)
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(
                $"http://localhost:{port}/webhook/");
            _listener.Start();

            _logger.LogInformation(
                "Webhook listener started on " +
                "http://localhost:{Port}/webhook/", port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to start webhook listener on port {Port}. " +
                "Is something else using that port? " +
                "The Ministry demands an explanation.",
                port);
            return false;
        }
    }

    private void StopListener()
    {
        try
        {
            _listener?.Stop();
            _listener?.Close();
            _listener = null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error stopping webhook listener.");
        }
    }

    // ── Webhook registration ──────────────────────────────────────

    private async Task EnsureWebhookRegisteredAsync(
        CancellationToken ct)
    {
        if (!_authService.IsAuthenticated)
        {
            _logger.LogWarning(
                "Not authenticated — cannot register webhook. " +
                "Will retry on next startup.");
            return;
        }

        var settings = _settings.Value;
        var port = settings.Webhook.Port;
        var callbackUrl =
            $"http://localhost:{port}/webhook/";

        // If we have a registered webhook ID, assume it's still valid
        if (settings.Webhook.RegisteredWebhookId.HasValue)
        {
            _logger.LogInformation(
                "Using existing webhook registration " +
                "ID {WebhookId}.",
                settings.Webhook.RegisteredWebhookId);
            return;
        }

        _logger.LogInformation(
            "Registering webhook with SimplyPrint " +
            "at {CallbackUrl}.", callbackUrl);

        var webhookId = await _apiClient.RegisterWebhookAsync(
            callbackUrl, _webhookSecret!, ct);

        if (webhookId.HasValue)
        {
            _logger.LogInformation(
                "Webhook registered successfully. ID: {WebhookId}. " +
                "SimplyPrint will now report to us directly. " +
                "As it should.",
                webhookId.Value);
        }
        else
        {
            _logger.LogWarning(
                "Webhook registration failed. " +
                "Falling back to polling only. " +
                "We apologise for the inconvenience.");
        }
    }

    private async Task UnregisterWebhookAsync()
    {
        var webhookId = _settings.Value.Webhook.RegisteredWebhookId;

        if (!webhookId.HasValue) return;

        _logger.LogInformation(
            "Unregistering webhook {WebhookId} on shutdown.",
            webhookId.Value);

        var success = await _apiClient
            .DeleteWebhookAsync(webhookId.Value);

        if (success)
            _logger.LogInformation(
                "Webhook unregistered cleanly. " +
                "SimplyPrint has been stood down.");
        else
            _logger.LogWarning(
                "Failed to unregister webhook {WebhookId}. " +
                "It may need manual cleanup in SimplyPrint settings.",
                webhookId.Value);
    }

    // ── Request handling ──────────────────────────────────────────

    private async Task ListenAsync(CancellationToken ct)
    {
        if (_listener is null) return;

        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                // GetContextAsync doesn't accept CancellationToken
                // directly — we bridge it with a Task.WhenAny
                var contextTask = _listener.GetContextAsync();
                var cancelTask = Task.Delay(
                    Timeout.Infinite, ct);

                var completed = await Task.WhenAny(
                    contextTask, cancelTask);

                if (completed == cancelTask)
                    break;

                var context = await contextTask;

                // Handle each request on a background thread
                // so we can immediately accept the next one
                _ = Task.Run(
                    () => HandleRequestAsync(context, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException ex)
                when (ex.ErrorCode == 995)
            {
                // ERROR_OPERATION_ABORTED — listener was stopped
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Webhook listener encountered an error.");
            }
        }
    }

    private async Task HandleRequestAsync(
        HttpListenerContext context,
        CancellationToken ct)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // Only accept POST requests
            if (request.HttpMethod != "POST")
            {
                await RespondAsync(response,
                    HttpStatusCode.MethodNotAllowed,
                    "Method not allowed. We only speak POST here.");
                return;
            }

            // Read the raw body — needed for signature validation
            using var reader = new StreamReader(
                request.InputStream,
                request.ContentEncoding);
            var rawBody = await reader.ReadToEndAsync(ct);

            // Validate the SimplyPrint secret token
            if (!ValidateSecret(request, rawBody))
            {
                _logger.LogWarning(
                    "Webhook request rejected — " +
                    "invalid or missing X-SP-Token. " +
                    "The Ministry suspects foul play.");

                await RespondAsync(response,
                    HttpStatusCode.Unauthorized,
                    "Invalid token. " +
                    "The Ministry is watching.");
                return;
            }

            // Parse the payload
            var payload = JsonSerializer.Deserialize<WebhookPayload>(
                rawBody, JsonOptions);

            if (payload is null || string.IsNullOrWhiteSpace(payload.Event))
            {
                await RespondAsync(response,
                    HttpStatusCode.BadRequest,
                    "Invalid payload.");
                return;
            }

            // Respond immediately — don't make SimplyPrint wait
            // while we process the event
            await RespondAsync(response, HttpStatusCode.OK, "Bing.");

            // Process the event
            await ProcessPayloadAsync(payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error handling webhook request.");
            try
            {
                await RespondAsync(response,
                    HttpStatusCode.InternalServerError,
                    "Internal error. " +
                    "The Redundant Department has been notified.");
            }
            catch
            {
                // Response already sent — nothing we can do
            }
        }
    }

    // ── Secret validation ─────────────────────────────────────────

    /// <summary>
    /// Validates the X-SP-Token header against our stored secret.
    /// SimplyPrint sends the raw secret in this header —
    /// we compare using a constant-time comparison to prevent
    /// timing attacks.
    /// </summary>
    private bool ValidateSecret(
        HttpListenerRequest request,
        string rawBody)
    {
        if (string.IsNullOrWhiteSpace(_webhookSecret))
            return true; // No secret configured — skip validation

        // SimplyPrint sends the secret in X-SP-Token
        // Header names are case-insensitive
        var receivedToken =
            request.Headers["X-SP-Token"] ??
            request.Headers["X-Sp-Token"] ??
            request.Headers["x-sp-token"];

        if (string.IsNullOrWhiteSpace(receivedToken))
            return false;

        // Constant-time comparison — prevents timing attacks
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(receivedToken),
            Encoding.UTF8.GetBytes(_webhookSecret));
    }

    // ── Payload processing ────────────────────────────────────────

    private async Task ProcessPayloadAsync(
        WebhookPayload payload,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Webhook received: '{Event}' at {Timestamp}.",
            payload.Event,
            DateTimeOffset.FromUnixTimeSeconds(payload.Timestamp));

        var printerId = payload.Data?.Printer?.Id ?? 0;
        var printerName = payload.Data?.Printer?.Name
            ?? $"Printer {printerId}";
        var jobFilename = payload.Data?.Job?.Filename;

        var detectedEvent = _diffEngine.ProcessWebhookEvent(
            payload.Event,
            printerId,
            printerName,
            jobFilename);

        if (detectedEvent is null)
        {
            _logger.LogDebug(
                "Webhook event '{Event}' produced no " +
                "detectable change — ignored.",
                payload.Event);
            return;
        }

        await _notificationManager
            .ProcessEventAsync(detectedEvent, ct);
    }

    // ── Response helper ───────────────────────────────────────────

    private static async Task RespondAsync(
        HttpListenerResponse response,
        HttpStatusCode statusCode,
        string message)
    {
        try
        {
            response.StatusCode = (int)statusCode;
            response.ContentType = "text/plain; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(message);
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
            response.Close();
        }
        catch
        {
            // Client disconnected — not our problem
        }
    }
}