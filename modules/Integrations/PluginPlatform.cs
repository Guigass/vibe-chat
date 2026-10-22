namespace VibeChat.Integrations;

/// <summary>
/// Advanced plugin capabilities (B-066). Off preserves B-109/B-110 send-only plugins.
/// </summary>
public sealed class PluginPlatformOptions
{
    public const string SectionName = "Features:Plugins";

    public bool Enabled { get; set; }
}

public static class PluginPlatformErrors
{
    public const string Disabled = "PluginPlatformDisabled";
    public const string CapabilityDenied = "PluginCapabilityDenied";
    public const string CommandNotFound = "PluginCommandNotFound";
    public const string OutOfScope = "PluginCommandOutOfScope";
    public const string ArgsRequired = "PluginCommandArgsRequired";
    public const string CallbackFailed = "PluginCallbackFailed";
    public const string InvalidCallbackUrl = "InvalidCallbackUrl";
    public const string EgressBlocked = "EgressBlocked";
    public const string UnknownHook = "UnknownPluginHook";
    public const string HistoryGrantNotFound = "PluginHistoryGrantNotFound";
    public const string HistoryWindowDenied = "PluginHistoryWindowDenied";
    public const string ActionExpired = "PluginActionExpired";
    public const string ActionNotFound = "PluginActionNotFound";
    public const string QuotaExceeded = "PluginQuotaExceeded";
    public const string AttachmentRejected = "PluginAttachmentRejected";
    public const string PluginDisabled = "PluginDisabled";
}

public static class PluginCommandCopy
{
    public static string Describe(string pluginId, string command, string? locale, string fallback)
    {
        if (pluginId != PluginManifestRules.BuiltinSlashNoteId
            || command != PluginManifestRules.SlashNoteCommand)
        {
            return fallback;
        }

        return locale switch
        {
            "en" => "Post a note in the channel",
            "es" => "Publica una nota en el canal",
            "fr" => "Publie une note dans le canal",
            "de" => "Veröffentlicht eine Notiz im Kanal",
            "it" => "Pubblica una nota nel canale",
            "ja" => "チャンネルにノートを投稿する",
            "zh-CN" => "在频道中发布笔记",
            "ko" => "채널에 노트를 게시합니다",
            "ru" => "Опубликовать заметку в канале",
            _ => "Publica uma nota no canal"
        };
    }
}
