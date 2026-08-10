using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

public sealed class RuntimeSecretProtector(IOptions<RuntimeSettingsOptions> options)
{
    public const string AadPrefix = "vibechat/runtime-secret/v1";

    private readonly RuntimeSettingsOptions _options = options.Value;

    public bool IsEncryptionAvailable
    {
        get
        {
            try
            {
                _ = ResolveKeyBytes(_options.Encryption.ActiveKeyVersion);
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }

    public int ActiveKeyVersion => Math.Max(1, _options.Encryption.ActiveKeyVersion);

    public EncryptedSecretEnvelope Protect(
        string plaintext,
        string credentialKind,
        TenantId tenantId,
        WorkspaceId? workspaceId,
        string entityId,
        DateTimeOffset rotatedAt)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            throw new CryptographicException("Plaintext secret is required.");
        }

        var keyVersion = ActiveKeyVersion;
        var key = ResolveKeyBytes(keyVersion);
        var nonce = RandomNumberGenerator.GetBytes(EncryptedSecretEnvelope.NonceLength);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext.Trim());
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[EncryptedSecretEnvelope.TagLength];
        var aad = BuildAad(credentialKind, tenantId, workspaceId, entityId);

        try
        {
            using var aes = new AesGcm(key, EncryptedSecretEnvelope.TagLength);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, aad);

            return new EncryptedSecretEnvelope
            {
                Ciphertext = ciphertext,
                Nonce = nonce,
                Tag = tag,
                KeyVersion = keyVersion,
                FormatVersion = EncryptedSecretEnvelope.CurrentFormatVersion,
                MaskSuffix = SecretMasking.Suffix(plaintext),
                RotatedAt = rotatedAt
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public string Unprotect(
        EncryptedSecretEnvelope envelope,
        string credentialKind,
        TenantId tenantId,
        WorkspaceId? workspaceId,
        string entityId)
    {
        if (!envelope.IsPresent)
        {
            throw new CryptographicException("Encrypted secret envelope is incomplete.");
        }

        if (envelope.FormatVersion != EncryptedSecretEnvelope.CurrentFormatVersion)
        {
            throw new CryptographicException("Unsupported secret envelope format.");
        }

        var key = ResolveKeyBytes(envelope.KeyVersion!.Value);
        var plaintextBytes = new byte[envelope.Ciphertext!.Length];
        var aad = BuildAad(credentialKind, tenantId, workspaceId, entityId);

        try
        {
            using var aes = new AesGcm(key, EncryptedSecretEnvelope.TagLength);
            aes.Decrypt(envelope.Nonce!, envelope.Ciphertext, envelope.Tag!, plaintextBytes, aad);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (AuthenticationTagMismatchException ex)
        {
            throw new CryptographicException("Secret envelope authentication failed.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public EncryptedSecretEnvelope Reencrypt(
        EncryptedSecretEnvelope envelope,
        string credentialKind,
        TenantId tenantId,
        WorkspaceId? workspaceId,
        string entityId,
        DateTimeOffset rotatedAt)
    {
        var plaintext = Unprotect(envelope, credentialKind, tenantId, workspaceId, entityId);
        try
        {
            return Protect(plaintext, credentialKind, tenantId, workspaceId, entityId, rotatedAt);
        }
        finally
        {
            // Best-effort: plaintext is a managed string; avoid retaining references in locals.
            plaintext = string.Empty;
        }
    }

    public static byte[] BuildAad(
        string credentialKind,
        TenantId tenantId,
        WorkspaceId? workspaceId,
        string entityId)
    {
        var workspacePart = workspaceId?.Value.ToString("D") ?? string.Empty;
        var text = $"{AadPrefix}|{credentialKind}|{tenantId.Value:D}|{workspacePart}|{entityId}";
        return Encoding.UTF8.GetBytes(text);
    }

    private byte[] ResolveKeyBytes(int keyVersion)
    {
        if (keyVersion <= 0)
        {
            throw new CryptographicException("Key version must be positive.");
        }

        if (!_options.Encryption.Keys.TryGetValue(keyVersion.ToString(), out var encoded)
            || string.IsNullOrWhiteSpace(encoded)
            || string.Equals(encoded.Trim(), "CHANGE_ME", StringComparison.OrdinalIgnoreCase)
            || encoded.Trim().StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new CryptographicException($"Encryption key version {keyVersion} is not configured.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(encoded.Trim());
        }
        catch (FormatException ex)
        {
            throw new CryptographicException($"Encryption key version {keyVersion} is not valid base64.", ex);
        }

        if (key.Length != 32)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new CryptographicException($"Encryption key version {keyVersion} must be 32 bytes.");
        }

        return key;
    }
}
