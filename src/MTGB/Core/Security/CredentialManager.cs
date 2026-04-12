using System.Runtime.InteropServices;
using System.Text;

namespace MTGB.Core.Security;

/// <summary>
/// Secure credential storage for MTGB using Windows Credential Manager.
/// Nothing sensitive is ever written to disk as plain text.
/// All secrets are scoped to the current Windows user account.
/// </summary>
public interface ICredentialManager
{
    /// <summary>Store a secret securely.</summary>
    void Save(CredentialKey key, string secret);

    /// <summary>Retrieve a stored secret. Returns null if not found.</summary>
    string? Load(CredentialKey key);

    /// <summary>Delete a stored secret.</summary>
    void Delete(CredentialKey key);

    /// <summary>Check whether a secret exists.</summary>
    bool Exists(CredentialKey key);
}

/// <summary>
/// All credential keys used by MTGB.
/// Each maps to a distinct entry in Windows Credential Manager.
/// </summary>
public enum CredentialKey
{
    /// <summary>SimplyPrint API key (manual entry auth path).</summary>
    ApiKey,

    /// <summary>OAuth2 access token.</summary>
    OAuthAccessToken,

    /// <summary>OAuth2 refresh token.</summary>
    OAuthRefreshToken,

    /// <summary>
    /// Per-installation webhook secret.
    /// Generated once on first run, used to validate 
    /// incoming SimplyPrint webhook payloads.
    /// </summary>
    WebhookSecret
}

/// <summary>
/// Windows Credential Manager implementation using Win32 DPAPI.
/// Credentials are encrypted by Windows, scoped to the current user,
/// and survive app reinstalls.
/// </summary>
public class WindowsCredentialManager : ICredentialManager
{
    // Vault name prefix — all MTGB entries appear grouped in 
    // Windows Credential Manager under "MTGB/"
    private const string VaultPrefix = "MTGB";

    private static string VaultName(CredentialKey key) =>
        $"{VaultPrefix}/{key}";

    /// <inheritdoc/>
    public void Save(CredentialKey key, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var credentialName = VaultName(key);
        var secretBytes = Encoding.UTF8.GetBytes(secret);

        var credential = new CREDENTIAL
        {
            Type = CREDENTIAL_TYPE.GENERIC,
            TargetName = credentialName,
            CredentialBlobSize = (uint)secretBytes.Length,
            CredentialBlob = Marshal.AllocHGlobal(secretBytes.Length),
            Persist = CREDENTIAL_PERSIST.LOCAL_MACHINE,
            UserName = Environment.UserName
        };

        try
        {
            Marshal.Copy(secretBytes, 0, credential.CredentialBlob, secretBytes.Length);

            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException(
                    $"Failed to save credential '{credentialName}' to Windows Credential Manager. " +
                    $"Win32 error: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            if (credential.CredentialBlob != IntPtr.Zero)
                Marshal.FreeHGlobal(credential.CredentialBlob);
        }
    }

    /// <inheritdoc/>
    public string? Load(CredentialKey key)
    {
        var credentialName = VaultName(key);

        if (!CredRead(credentialName, CREDENTIAL_TYPE.GENERIC, 0, out var credentialPtr))
            return null;

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);

            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return null;

            var secretBytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, secretBytes, 0, secretBytes.Length);
            return Encoding.UTF8.GetString(secretBytes);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    /// <inheritdoc/>
    public void Delete(CredentialKey key)
    {
        var credentialName = VaultName(key);

        // Silently succeed if the credential doesn't exist —
        // deleting something that isn't there is not an error.
        CredDelete(credentialName, CREDENTIAL_TYPE.GENERIC, 0);
    }

    /// <inheritdoc/>
    public bool Exists(CredentialKey key)
    {
        var credentialName = VaultName(key);

        if (!CredRead(credentialName, CREDENTIAL_TYPE.GENERIC, 0, out var credentialPtr))
            return false;

        CredFree(credentialPtr);
        return true;
    }

    // ── Win32 P/Invoke declarations ───────────────────────────────

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(
        [In] ref CREDENTIAL userCredential,
        [In] uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(
        string target,
        CREDENTIAL_TYPE type,
        int reservedFlag,
        out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(
        string target,
        CREDENTIAL_TYPE type,
        int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree([In] IntPtr buffer);

    // ── Win32 structures and enums ────────────────────────────────

    private enum CREDENTIAL_TYPE : uint
    {
        GENERIC = 1
    }

    private enum CREDENTIAL_PERSIST : uint
    {
        SESSION = 1,
        LOCAL_MACHINE = 2,
        ENTERPRISE = 3
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public CREDENTIAL_TYPE Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public CREDENTIAL_PERSIST Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }
}

/// <summary>
/// Manages the per-installation webhook secret.
/// Generated once on first run, stored in Credential Manager,
/// reused for the lifetime of the installation.
/// </summary>
public class WebhookSecretManager
{
    private readonly ICredentialManager _credentials;

    public WebhookSecretManager(ICredentialManager credentials)
    {
        _credentials = credentials;
    }

    /// <summary>
    /// Returns the webhook secret, generating and storing one
    /// if it doesn't already exist.
    /// </summary>
    public string GetOrCreate()
    {
        var existing = _credentials.Load(CredentialKey.WebhookSecret);

        if (existing is not null)
            return existing;

        // Generate a cryptographically secure random secret
        var secretBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(secretBytes);
        var secret = Convert.ToBase64String(secretBytes);

        _credentials.Save(CredentialKey.WebhookSecret, secret);
        return secret;
    }
}