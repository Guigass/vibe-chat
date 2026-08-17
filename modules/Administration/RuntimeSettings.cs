namespace VibeChat.Administration;

/// <summary>Feature flag + AES-GCM keyring for encrypted external credentials (ADR-020).</summary>
public sealed class RuntimeSettingsOptions
{
    public const string SectionName = "RuntimeSettings";

    /// <summary>When false, consumers prefer legacy env behavior for non-secret settings; rotate/reencrypt return unavailable.</summary>
    public bool DatabaseOverridesEnabled { get; set; }

    public RuntimeEncryptionOptions Encryption { get; set; } = new();
}

public sealed class RuntimeEncryptionOptions
{
    public int ActiveKeyVersion { get; set; } = 1;

    /// <summary>Map of key version → base64-encoded 32-byte AES key.</summary>
    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.Ordinal);
}

public static class RuntimeSecretKinds
{
    public const string OpenRouterApiKey = "openrouter-api-key";
    public const string SmtpPassword = "smtp-password";
    public const string WebhookSigningSecret = "webhook-signing-secret";
    public const string VapidPrivateKey = "vapid-private-key";
}
