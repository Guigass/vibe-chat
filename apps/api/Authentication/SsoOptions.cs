namespace VibeChat.Api.Authentication;

public sealed class SsoOptions
{
    public const string SectionName = "Identity:Sso";

    public bool Enabled { get; set; }
    public string? KeycloakBaseUrl { get; set; }
    public string Realm { get; set; } = "vibechat";
    public string? DirectoryClientId { get; set; }
    public string? DirectoryClientSecret { get; set; }
    public int TimeoutSeconds { get; set; } = 3;
}
