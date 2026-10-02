using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VibeChat.SharedKernel;

namespace VibeChat.Integrations;

/// <summary>
/// Installed plugin: local manifest plus the bot identity from B-109.
/// The row is configuration, never executable code.
/// </summary>
public sealed class InstalledPlugin
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ManifestJson { get; set; } = string.Empty;
    public string[] Capabilities { get; set; } = [];
    public Guid BotId { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset InstalledAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record PluginManifestDocument(
    string Schema,
    string Id,
    string Name,
    string Version,
    string[] Capabilities,
    string CanonicalJson);

public static class PluginManifestRules
{
    public const string SchemaV1 = "vibechat.plugin.manifest.v1";
    public const string CapabilityMessagesSend = "messages.send";
    public const string BuiltinIncomingMessagesId = "incoming-messages";
    public const int MaxManifestBytes = 4096;
    public const int MaxPluginsPerWorkspace = 20;
    public const int MaxPluginIdLength = 64;
    public const int MaxVersionLength = 32;

    public const string Invalid = "InvalidPluginManifest";
    public const string TooLarge = "PluginManifestTooLarge";
    public const string UnknownCapability = "UnknownPluginCapability";
    public const string UnknownBuiltin = "UnknownBuiltinPlugin";
    public const string AlreadyInstalled = "PluginAlreadyInstalled";
    public const string LimitReached = "PluginLimitReached";
    public const string NotFound = "PluginNotFound";

    private static readonly string[] AllowedProperties = ["schema", "id", "name", "version", "capabilities"];
    private static readonly Regex Slug = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SemVer = new("^\\d+\\.\\d+\\.\\d+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public const string IncomingMessagesJson =
        """
        {"schema":"vibechat.plugin.manifest.v1","id":"incoming-messages","name":"Incoming Messages API","version":"1.0.0","capabilities":["messages.send"]}
        """;

    public static bool TryGetBuiltin(string? id, out PluginManifestDocument? manifest, out string error)
    {
        manifest = null;
        if (!string.Equals(id?.Trim(), BuiltinIncomingMessagesId, StringComparison.Ordinal))
        {
            error = UnknownBuiltin;
            return false;
        }

        return TryParse(IncomingMessagesJson, out manifest, out error);
    }

    public static bool TryParse(string? json, out PluginManifestDocument? manifest, out string error)
    {
        manifest = null;
        error = Invalid;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxManifestBytes)
        {
            error = TooLarge;
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (Array.IndexOf(AllowedProperties, property.Name) < 0)
                {
                    return false;
                }
            }

            if (!TryString(document.RootElement, "schema", out var schema) || schema != SchemaV1)
            {
                return false;
            }

            if (!TryString(document.RootElement, "id", out var id)
                || id.Length > MaxPluginIdLength
                || !Slug.IsMatch(id))
            {
                return false;
            }

            if (!TryString(document.RootElement, "name", out var name)
                || !BotIntegrationPolicies.TryNormalizeName(name, out name))
            {
                return false;
            }

            if (!TryString(document.RootElement, "version", out var version)
                || version.Length > MaxVersionLength
                || !SemVer.IsMatch(version))
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty("capabilities", out var capabilities)
                || capabilities.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var list = new List<string>();
            foreach (var item in capabilities.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                var capability = item.GetString() ?? string.Empty;
                if (capability != CapabilityMessagesSend)
                {
                    error = UnknownCapability;
                    return false;
                }

                if (list.Contains(capability, StringComparer.Ordinal))
                {
                    return false;
                }

                list.Add(capability);
            }

            if (list.Count == 0)
            {
                return false;
            }

            var parsed = list.ToArray();
            manifest = new PluginManifestDocument(schema, id, name, version, parsed, Canonical(schema, id, name, version, parsed));
            error = string.Empty;
            return true;
        }
    }

    private static string Canonical(string schema, string id, string name, string version, string[] capabilities)
    {
        return JsonSerializer.Serialize(new
        {
            schema,
            id,
            name,
            version,
            capabilities
        });
    }

    private static bool TryString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return value.Length > 0;
    }
}
