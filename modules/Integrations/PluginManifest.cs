using System.Net;
using System.Net.Sockets;
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
    public DateTimeOffset? LastUsedAt { get; set; }
    public string? CallbackUrl { get; set; }
    public string[] SubscribedEvents { get; set; } = [];
    public string[] Hooks { get; set; } = [];

    /// <summary>AES-GCM callback signing secret. Empty when the plugin has no callback.</summary>
    public EncryptedSecretEnvelope CallbackSecret { get; set; } = new();
}

public sealed record PluginCommandDeclaration(string Name, string Description);

public sealed record PluginActionDeclaration(string Name, string Description);

public sealed record PluginCallbackDeclaration(string Url, string[] Events);

public sealed record PluginManifestDocument(
    string Schema,
    string Id,
    string Name,
    string Version,
    string[] Capabilities,
    PluginCommandDeclaration[] Commands,
    PluginActionDeclaration[] Actions,
    PluginCallbackDeclaration? Callback,
    string[] Hooks,
    string CanonicalJson);

public static class PluginManifestRules
{
    public const string SchemaV1 = "vibechat.plugin.manifest.v1";
    public const string CapabilityMessagesSend = "messages.send";
    public const string CapabilityCommandsSlash = "commands.slash";
    public const string CapabilityEventsSubscribe = "events.subscribe";
    public const string CapabilityUiHooks = "ui.hooks";
    public const string CapabilityMessagesInteract = "messages.interact";
    public const string CapabilityHistoryRead = "history.read";
    public const string CapabilityAttachmentsSend = "attachments.send";
    public const string HookMessageBadgeBot = "message.badge.bot";
    public const string HookMessageMenu = "message.menu";
    public const string BuiltinIncomingMessagesId = "incoming-messages";
    public const string BuiltinSlashNoteId = "slash-note";
    public const string BuiltinActionAckId = "action-ack";
    public const string SlashNoteCommand = "nota";
    public const string ActionAckName = "ack";
    public const int MaxManifestBytes = 4096;
    public const int MaxPluginsPerWorkspace = 20;
    public const int MaxPluginIdLength = 64;
    public const int MaxVersionLength = 32;
    public const int MaxCommands = 8;
    public const int MaxCommandNameLength = 32;
    public const int MaxCommandDescriptionLength = 120;
    public const int MaxCallbackUrlLength = 2048;

    public const string Invalid = "InvalidPluginManifest";
    public const string TooLarge = "PluginManifestTooLarge";
    public const string UnknownCapability = "UnknownPluginCapability";
    public const string UnknownBuiltin = "UnknownBuiltinPlugin";
    public const string AlreadyInstalled = "PluginAlreadyInstalled";
    public const string LimitReached = "PluginLimitReached";
    public const string NotFound = "PluginNotFound";

    private static readonly string[] AllowedProperties = ["schema", "id", "name", "version", "capabilities", "commands", "actions", "callback", "hooks"];
    private static readonly string[] AllowedCapabilities =
    [
        CapabilityMessagesSend,
        CapabilityCommandsSlash,
        CapabilityEventsSubscribe,
        CapabilityUiHooks,
        CapabilityMessagesInteract,
        CapabilityHistoryRead,
        CapabilityAttachmentsSend
    ];
    private static readonly string[] AllowedHooks = [HookMessageBadgeBot, HookMessageMenu];
    private static readonly Regex Slug = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SemVer = new("^\\d+\\.\\d+\\.\\d+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public const string IncomingMessagesJson =
        """
        {"schema":"vibechat.plugin.manifest.v1","id":"incoming-messages","name":"Incoming Messages API","version":"1.0.0","capabilities":["messages.send"]}
        """;

    public const string SlashNoteJson =
        """
        {"schema":"vibechat.plugin.manifest.v1","id":"slash-note","name":"Slash Note","version":"1.0.0","capabilities":["messages.send","commands.slash"],"commands":[{"name":"nota","description":"Publica uma nota no canal"}]}
        """;

    public const string ActionAckJson =
        """
        {"schema":"vibechat.plugin.manifest.v1","id":"action-ack","name":"Action Ack","version":"1.0.0","capabilities":["messages.send","messages.interact"],"actions":[{"name":"ack","description":"Confirma a mensagem"}]}
        """;

    public static bool RequiresPlatform(PluginManifestDocument manifest) =>
        manifest.Capabilities.Any(capability => capability != CapabilityMessagesSend);

    public static bool IsProductSlash(PluginManifestDocument manifest, string command) =>
        manifest.Id == BuiltinSlashNoteId
        && command == SlashNoteCommand
        && manifest.Callback is null
        && manifest.Capabilities.Contains(CapabilityMessagesSend, StringComparer.Ordinal)
        && manifest.Capabilities.Contains(CapabilityCommandsSlash, StringComparer.Ordinal)
        && manifest.Commands.Any(item => item.Name == command);

    public static bool IsProductAction(PluginManifestDocument manifest, string action) =>
        manifest.Id == BuiltinActionAckId
        && action == ActionAckName
        && manifest.Callback is null
        && manifest.Capabilities.Contains(CapabilityMessagesInteract, StringComparer.Ordinal)
        && manifest.Actions.Any(item => item.Name == action);

    public static string QualifiedCommand(string pluginId, string command) => $"{pluginId}/{command}";

    public static bool TrySplitCommand(string? raw, out string pluginId, out string command)
    {
        pluginId = string.Empty;
        command = string.Empty;
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();
        var slash = value.IndexOf('/');
        if (slash <= 0 || slash != value.LastIndexOf('/') || slash == value.Length - 1)
        {
            return false;
        }

        pluginId = value[..slash];
        command = value[(slash + 1)..];
        return Slug.IsMatch(pluginId) && Slug.IsMatch(command);
    }

    public static IReadOnlyList<PluginManifestDocument> Builtins(bool platformEnabled)
    {
        var items = new List<PluginManifestDocument>(3);
        foreach (var id in new[] { BuiltinIncomingMessagesId, BuiltinSlashNoteId, BuiltinActionAckId })
        {
            if (!TryGetBuiltin(id, out var manifest, out _) || manifest is null)
            {
                continue;
            }

            if (!platformEnabled && RequiresPlatform(manifest))
            {
                continue;
            }

            items.Add(manifest);
        }

        return items;
    }

    public static bool TryGetBuiltin(string? id, out PluginManifestDocument? manifest, out string error)
    {
        manifest = null;
        var key = id?.Trim();
        var json = key switch
        {
            BuiltinIncomingMessagesId => IncomingMessagesJson,
            BuiltinSlashNoteId => SlashNoteJson,
            BuiltinActionAckId => ActionAckJson,
            _ => null
        };
        if (json is null)
        {
            error = UnknownBuiltin;
            return false;
        }

        return TryParse(json, out manifest, out error);
    }

    public static bool TryAcceptCallbackUrl(string? raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = PluginPlatformErrors.InvalidCallbackUrl;
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim().Length > MaxCallbackUrlLength)
        {
            return false;
        }

        if (!WebhookDelivery.IsValidHttpsUrl(raw))
        {
            return false;
        }

        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var host = uri.IdnHost.Trim().TrimEnd('.');
        var lab = IsLabHost(host);
        if (!lab && ConnectorPolicies.IsBlockedHost(host))
        {
            error = PluginPlatformErrors.EgressBlocked;
            return false;
        }

        if (IPAddress.TryParse(host, out var address) && !lab && ConnectorPolicies.IsBlockedAddress(address))
        {
            error = PluginPlatformErrors.EgressBlocked;
            return false;
        }

        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        normalized = builder.Uri.AbsoluteUri;
        error = string.Empty;
        return true;
    }

    public static bool IsLabHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host == "127.0.0.1"
        || host == "::1"
        || host == "[::1]";

    public static async Task<bool> IsDeliveryAllowedAsync(string url, CancellationToken cancellationToken)
    {
        if (!TryAcceptCallbackUrl(url, out var normalized, out _))
        {
            return false;
        }

        var host = new Uri(normalized).IdnHost.Trim().TrimEnd('.');
        if (IsLabHost(host))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var literal))
        {
            return !ConnectorPolicies.IsBlockedAddress(literal);
        }

        try
        {
            var first = await Dns.GetHostAddressesAsync(host, cancellationToken);
            var second = await Dns.GetHostAddressesAsync(host, cancellationToken);
            if (first.Length == 0 || second.Length == 0)
            {
                return false;
            }

            return !first.Any(ConnectorPolicies.IsBlockedAddress)
                && !second.Any(ConnectorPolicies.IsBlockedAddress);
        }
        catch (SocketException)
        {
            return false;
        }
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

            if (!TryCapabilities(document.RootElement, out var capabilities, ref error))
            {
                return false;
            }

            var hasSlash = capabilities.Contains(CapabilityCommandsSlash, StringComparer.Ordinal);
            var hasEvents = capabilities.Contains(CapabilityEventsSubscribe, StringComparer.Ordinal);
            var hasHooks = capabilities.Contains(CapabilityUiHooks, StringComparer.Ordinal);
            var hasInteract = capabilities.Contains(CapabilityMessagesInteract, StringComparer.Ordinal);
            if (!TryCommands(document.RootElement, hasSlash, out var commands, ref error))
            {
                return false;
            }

            if (!TryActions(document.RootElement, hasInteract, out var actions, ref error))
            {
                return false;
            }

            if (!TryCallback(document.RootElement, id, hasSlash, hasEvents, hasInteract, out var callback, ref error))
            {
                return false;
            }

            if (!TryHooks(document.RootElement, hasHooks, out var hooks, ref error))
            {
                return false;
            }

            manifest = new PluginManifestDocument(
                schema,
                id,
                name,
                version,
                capabilities,
                commands,
                actions,
                callback,
                hooks,
                Canonical(schema, id, name, version, capabilities, commands, actions, callback, hooks));
            error = string.Empty;
            return true;
        }
    }

    private static bool TryCapabilities(JsonElement root, out string[] capabilities, ref string error)
    {
        capabilities = [];
        if (!root.TryGetProperty("capabilities", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var list = new List<string>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var capability = item.GetString() ?? string.Empty;
            if (Array.IndexOf(AllowedCapabilities, capability) < 0)
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

        capabilities = list.ToArray();
        return true;
    }

    private static bool TryCommands(JsonElement root, bool hasSlash, out PluginCommandDeclaration[] commands, ref string error)
    {
        commands = [];
        var present = root.TryGetProperty("commands", out var items);
        if (!hasSlash)
        {
            return !present;
        }

        if (!present || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var list = new List<PluginCommandDeclaration>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in item.EnumerateObject())
            {
                if (property.Name is not ("name" or "description"))
                {
                    return false;
                }
            }

            if (!TryString(item, "name", out var name)
                || name.Length > MaxCommandNameLength
                || !Slug.IsMatch(name)
                || !TryString(item, "description", out var description)
                || description.Length > MaxCommandDescriptionLength)
            {
                return false;
            }

            if (list.Any(existing => existing.Name == name))
            {
                return false;
            }

            list.Add(new PluginCommandDeclaration(name, description));
        }

        if (list.Count is 0 or > MaxCommands)
        {
            return false;
        }

        commands = list.ToArray();
        error = string.Empty;
        return true;
    }

    private static bool TryActions(JsonElement root, bool hasInteract, out PluginActionDeclaration[] actions, ref string error)
    {
        actions = [];
        var present = root.TryGetProperty("actions", out var items);
        if (!hasInteract)
        {
            return !present;
        }

        if (!present || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var list = new List<PluginActionDeclaration>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in item.EnumerateObject())
            {
                if (property.Name is not ("name" or "description"))
                {
                    return false;
                }
            }

            if (!TryString(item, "name", out var name)
                || name.Length > MaxCommandNameLength
                || !Slug.IsMatch(name)
                || !TryString(item, "description", out var description)
                || description.Length > MaxCommandDescriptionLength)
            {
                return false;
            }

            if (list.Any(existing => existing.Name == name))
            {
                return false;
            }

            list.Add(new PluginActionDeclaration(name, description));
        }

        if (list.Count is 0 or > PluginGovernedRules.MaxActions)
        {
            return false;
        }

        actions = list.ToArray();
        error = string.Empty;
        return true;
    }

    private static bool TryCallback(
        JsonElement root,
        string id,
        bool hasSlash,
        bool hasEvents,
        bool hasInteract,
        out PluginCallbackDeclaration? callback,
        ref string error)
    {
        callback = null;
        var present = root.TryGetProperty("callback", out var item);
        if (!present)
        {
            if (hasEvents || (hasSlash && id != BuiltinSlashNoteId) || (hasInteract && id != BuiltinActionAckId))
            {
                error = PluginPlatformErrors.InvalidCallbackUrl;
                return false;
            }

            return true;
        }

        if ((!hasSlash && !hasEvents && !hasInteract) || item.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in item.EnumerateObject())
        {
            if (property.Name is not ("url" or "events"))
            {
                return false;
            }
        }

        if (!TryString(item, "url", out var url) || !TryAcceptCallbackUrl(url, out var normalized, out error))
        {
            return false;
        }

        var events = Array.Empty<string>();
        if (item.TryGetProperty("events", out var eventItems))
        {
            if (eventItems.ValueKind != JsonValueKind.Array || !TryEventNames(eventItems, out events))
            {
                return false;
            }
        }

        if (hasEvents && events.Length == 0)
        {
            return false;
        }

        if (!hasEvents && events.Length > 0)
        {
            return false;
        }

        callback = new PluginCallbackDeclaration(normalized, events);
        error = string.Empty;
        return true;
    }

    private static bool TryEventNames(JsonElement items, out string[] events)
    {
        events = [];
        var list = new List<string>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var name = item.GetString() ?? string.Empty;
            if (!WebhookEventTypes.IsSubscribable(name) || list.Contains(name, StringComparer.Ordinal))
            {
                return false;
            }

            list.Add(name);
        }

        events = list.ToArray();
        return true;
    }

    private static bool TryHooks(JsonElement root, bool hasHooks, out string[] hooks, ref string error)
    {
        hooks = [];
        var present = root.TryGetProperty("hooks", out var items);
        if (!hasHooks)
        {
            return !present;
        }

        if (!present || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var list = new List<string>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var hook = item.GetString() ?? string.Empty;
            if (Array.IndexOf(AllowedHooks, hook) < 0)
            {
                error = PluginPlatformErrors.UnknownHook;
                return false;
            }

            if (list.Contains(hook, StringComparer.Ordinal))
            {
                return false;
            }

            list.Add(hook);
        }

        if (list.Count == 0)
        {
            return false;
        }

        hooks = list.ToArray();
        error = string.Empty;
        return true;
    }

    private static string Canonical(
        string schema,
        string id,
        string name,
        string version,
        string[] capabilities,
        PluginCommandDeclaration[] commands,
        PluginActionDeclaration[] actions,
        PluginCallbackDeclaration? callback,
        string[] hooks)
    {
        if (commands.Length == 0 && actions.Length == 0 && callback is null && hooks.Length == 0)
        {
            return JsonSerializer.Serialize(new { schema, id, name, version, capabilities });
        }

        var payload = new Dictionary<string, object?>
        {
            ["schema"] = schema,
            ["id"] = id,
            ["name"] = name,
            ["version"] = version,
            ["capabilities"] = capabilities
        };
        if (commands.Length > 0)
        {
            payload["commands"] = commands.Select(command => new { name = command.Name, description = command.Description }).ToArray();
        }

        if (actions.Length > 0)
        {
            payload["actions"] = actions.Select(action => new { name = action.Name, description = action.Description }).ToArray();
        }

        if (callback is not null)
        {
            payload["callback"] = callback.Events.Length == 0
                ? new Dictionary<string, object?> { ["url"] = callback.Url }
                : new Dictionary<string, object?> { ["url"] = callback.Url, ["events"] = callback.Events };
        }

        if (hooks.Length > 0)
        {
            payload["hooks"] = hooks;
        }

        return JsonSerializer.Serialize(payload);
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

public static class PluginCallbackSecret
{
    public const string Prefix = "vc_cb_";

    public static string CreateRaw()
    {
        Span<byte> bytes = stackalloc byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Prefix + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
