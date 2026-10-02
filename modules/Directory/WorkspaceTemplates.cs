using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VibeChat.SharedKernel;

namespace VibeChat.Directory;

/// <summary>Imported workspace template (B-115). The manifest never stores a tenant id.</summary>
public sealed class WorkspaceTemplateRecord
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string ManifestJson { get; set; } = string.Empty;
    public UserId CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Owner/admin onboarding progress. Skip and resume do not require a template.</summary>
public sealed class WorkspaceOnboarding
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Status { get; set; } = OnboardingRules.Pending;
    public string? TemplateId { get; set; }
    public int? TemplateVersion { get; set; }
    public string ItemsJson { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Idempotent apply result for one workspace and Idempotency-Key.</summary>
public sealed class TemplateApplication
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public int TemplateVersion { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public UserId ActorUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record WorkspaceTemplateDocument(
    string Schema,
    string Id,
    int Version,
    IReadOnlyList<TemplateSpace> Spaces,
    TemplatePolicy? Policy,
    IReadOnlyList<string> Checklist,
    string CanonicalJson);

public sealed record TemplateSpace(string Key, string Name, IReadOnlyList<TemplateChannel> Channels);

public sealed record TemplateChannel(string Key, string Name, string Type, string? Topic);

public sealed record TemplatePolicy(
    bool EditEnabled,
    int? EditWindowMinutes,
    bool EditRolesRestricted,
    string[] EditRoles,
    bool EditAllowModeratorOverride,
    bool DeleteEnabled,
    int? DeleteWindowMinutes,
    bool DeleteRolesRestricted,
    string[] DeleteRoles,
    bool DeleteAllowModeratorOverride);

public sealed record TemplateExistingSpace(Guid Id, string Name);

public sealed record TemplateExistingChannel(Guid Id, Guid? SpaceId, string Name, string Type, string? Topic);

public sealed record TemplateExistingPolicy(
    bool EditEnabled,
    int? EditWindowMinutes,
    bool EditRolesRestricted,
    string[] EditRoles,
    bool EditAllowModeratorOverride,
    bool DeleteEnabled,
    int? DeleteWindowMinutes,
    bool DeleteRolesRestricted,
    string[] DeleteRoles,
    bool DeleteAllowModeratorOverride);

public sealed record TemplatePlanItem(
    string Kind,
    string Action,
    string Key,
    string? Name,
    string? Detail,
    string? SpaceKey);

public static class TemplatePlanKinds
{
    public const string Space = "space";
    public const string Channel = "channel";
    public const string Policy = "policy";
}

public static class TemplatePlanActions
{
    public const string Create = "create";
    public const string Reuse = "reuse";
    public const string Conflict = "conflict";
}

public static class TemplateConflictDetails
{
    public const string Type = "type";
    public const string Space = "space";
    public const string Policy = "policy";
}

public static class OnboardingRules
{
    public const string Pending = "pending";
    public const string Skipped = "skipped";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Open = "open";
    public const string Done = "done";
    public const string Invalid = "InvalidOnboarding";

    public static readonly string[] Statuses = [Skipped, InProgress, Completed];
    public static readonly string[] States = [Open, Done];

    public static readonly string[] DefaultChecklist =
    [
        WorkspaceTemplateRules.CheckInvite,
        WorkspaceTemplateRules.CheckReview,
        WorkspaceTemplateRules.CheckPolicy
    ];
}

/// <summary>
/// Declarative workspace templates (B-115). The catalog is data: spaces, channels,
/// descriptions (topic) and messaging-policy defaults. No members, secrets or scripts.
/// </summary>
public static class WorkspaceTemplateRules
{
    public const string SchemaV1 = "vibechat.workspace-template.v1";
    public const string UnknownSchema = "UnknownTemplateSchema";
    public const string UnknownField = "UnknownTemplateField";
    public const string Invalid = "InvalidTemplate";
    public const string NotFound = "TemplateNotFound";
    public const string Conflict = "TemplateConflict";
    public const string Reserved = "TemplateReserved";
    public const string LimitReached = "TemplateLimitReached";
    public const string IdempotencyReused = "IdempotencyKeyReused";
    public const string InvalidIdempotencyKey = "InvalidIdempotencyKey";
    public const string ExportEmpty = "TemplateExportEmpty";

    public const string TeamId = "team";
    public const string ProjectId = "project";
    public const string CommunityId = "community";
    public const string IncidentsId = "incidents";
    public const string ExportId = "exported";

    public const string CheckInvite = "invite-members";
    public const string CheckReview = "review-channels";
    public const string CheckPolicy = "confirm-policy";

    public const int MaxManifestBytes = 65_536;
    public const int MaxCustomTemplates = 20;
    public const int MaxSpaces = 20;
    public const int MaxChannelsPerSpace = 30;
    public const int MaxChecklist = 20;
    public const int MaxIdLength = 64;
    public const int MaxNameLength = 120;
    public const int MaxTopicLength = 250;
    public const int MaxIdempotencyKeyLength = 200;
    public const int MaxWindowMinutes = 525_600;

    private static readonly string[] RootProperties = ["schema", "id", "version", "spaces", "policyDefaults", "checklist"];
    private static readonly string[] SpaceProperties = ["key", "name", "channels"];
    private static readonly string[] ChannelProperties = ["key", "name", "type", "topic"];
    private static readonly string[] PolicyProperties =
    [
        "editEnabled", "editWindowMinutes", "editRolesRestricted", "editRoles", "editAllowModeratorOverride",
        "deleteEnabled", "deleteWindowMinutes", "deleteRolesRestricted", "deleteRoles", "deleteAllowModeratorOverride"
    ];
    private static readonly string[] ChecklistProperties = ["key"];
    private static readonly string[] ChannelTypes = ["Public", "Private", "Announcement", "Group"];
    private static readonly string[] Roles = ["Member", "Moderator", "Admin", "Auditor", "Guest"];
    private static readonly Regex Slug = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Matches <c>MessageLifecyclePolicyRules.Default</c> when no row exists.</summary>
    public static TemplatePolicy ImplicitPolicy { get; } = new(
        EditEnabled: true,
        EditWindowMinutes: null,
        EditRolesRestricted: false,
        EditRoles: [],
        EditAllowModeratorOverride: false,
        DeleteEnabled: true,
        DeleteWindowMinutes: null,
        DeleteRolesRestricted: false,
        DeleteRoles: [],
        DeleteAllowModeratorOverride: true);

    private const string TeamJson =
        """
        {"schema":"vibechat.workspace-template.v1","id":"team","version":1,"spaces":[{"key":"time","name":"Time","channels":[{"key":"geral","name":"geral","type":"Public","topic":"Conversas do time"},{"key":"avisos","name":"avisos","type":"Announcement","topic":"Avisos do time"}]}],"policyDefaults":{"editEnabled":true,"editWindowMinutes":null,"editRolesRestricted":false,"editRoles":[],"editAllowModeratorOverride":false,"deleteEnabled":true,"deleteWindowMinutes":1440,"deleteRolesRestricted":false,"deleteRoles":[],"deleteAllowModeratorOverride":true},"checklist":[{"key":"invite-members"},{"key":"review-channels"},{"key":"confirm-policy"}]}
        """;

    private const string ProjectJson =
        """
        {"schema":"vibechat.workspace-template.v1","id":"project","version":1,"spaces":[{"key":"projeto","name":"Projeto","channels":[{"key":"geral","name":"geral","type":"Public","topic":"Conversas do projeto"},{"key":"planejamento","name":"planejamento","type":"Public","topic":"Planejamento"},{"key":"entregas","name":"entregas","type":"Public","topic":"Entregas"}]}],"policyDefaults":{"editEnabled":true,"editWindowMinutes":10080,"editRolesRestricted":false,"editRoles":[],"editAllowModeratorOverride":false,"deleteEnabled":true,"deleteWindowMinutes":10080,"deleteRolesRestricted":false,"deleteRoles":[],"deleteAllowModeratorOverride":true},"checklist":[{"key":"invite-members"},{"key":"review-channels"},{"key":"confirm-policy"}]}
        """;

    private const string CommunityJson =
        """
        {"schema":"vibechat.workspace-template.v1","id":"community","version":1,"spaces":[{"key":"comunidade","name":"Comunidade","channels":[{"key":"boas-vindas","name":"boas-vindas","type":"Public","topic":"Boas-vindas"},{"key":"geral","name":"geral","type":"Public","topic":"Conversas da comunidade"},{"key":"off-topic","name":"off-topic","type":"Public","topic":"Fora do tema"}]}],"policyDefaults":{"editEnabled":true,"editWindowMinutes":null,"editRolesRestricted":false,"editRoles":[],"editAllowModeratorOverride":false,"deleteEnabled":true,"deleteWindowMinutes":null,"deleteRolesRestricted":false,"deleteRoles":[],"deleteAllowModeratorOverride":true},"checklist":[{"key":"invite-members"},{"key":"review-channels"},{"key":"confirm-policy"}]}
        """;

    private const string IncidentsJson =
        """
        {"schema":"vibechat.workspace-template.v1","id":"incidents","version":1,"spaces":[{"key":"incidentes","name":"Incidentes","channels":[{"key":"war-room","name":"war-room","type":"Public","topic":"Sala do incidente"},{"key":"status","name":"status","type":"Announcement","topic":"Status do incidente"},{"key":"pos-mortem","name":"pos-mortem","type":"Public","topic":"Aprendizados"}]}],"policyDefaults":{"editEnabled":true,"editWindowMinutes":60,"editRolesRestricted":false,"editRoles":[],"editAllowModeratorOverride":false,"deleteEnabled":true,"deleteWindowMinutes":60,"deleteRolesRestricted":false,"deleteRoles":[],"deleteAllowModeratorOverride":true},"checklist":[{"key":"invite-members"},{"key":"review-channels"},{"key":"confirm-policy"}]}
        """;

    private static readonly WorkspaceTemplateDocument[] BuiltinDocuments = BuildBuiltins();

    public static IReadOnlyList<WorkspaceTemplateDocument> Builtins => BuiltinDocuments;

    public static bool IsBuiltin(string? id) =>
        BuiltinDocuments.Any(x => x.Id == id);

    public static bool TryGetBuiltin(string? id, out WorkspaceTemplateDocument? document)
    {
        document = BuiltinDocuments.FirstOrDefault(x => x.Id == id?.Trim());
        return document is not null;
    }

    public static bool TryParse(string? json, out WorkspaceTemplateDocument? document, out string error, out string? path)
    {
        document = null;
        path = null;
        error = Invalid;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxManifestBytes)
        {
            return false;
        }

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        using (parsed)
        {
            return TryParseElement(parsed.RootElement, out document, out error, out path);
        }
    }

    public static IReadOnlyList<TemplatePlanItem> Plan(
        WorkspaceTemplateDocument template,
        IReadOnlyList<TemplateExistingSpace> spaces,
        IReadOnlyList<TemplateExistingChannel> channels,
        TemplateExistingPolicy? policy)
    {
        var items = new List<TemplatePlanItem>();
        var spaceByName = new Dictionary<string, TemplateExistingSpace>(StringComparer.OrdinalIgnoreCase);
        foreach (var space in spaces)
        {
            spaceByName.TryAdd(space.Name.Trim(), space);
        }

        var channelByName = new Dictionary<string, TemplateExistingChannel>(StringComparer.OrdinalIgnoreCase);
        foreach (var channel in channels)
        {
            channelByName.TryAdd(channel.Name.Trim(), channel);
        }

        var reusedSpaceIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var space in template.Spaces)
        {
            if (spaceByName.TryGetValue(space.Name.Trim(), out var existing))
            {
                reusedSpaceIds[space.Key] = existing.Id;
                items.Add(new TemplatePlanItem(TemplatePlanKinds.Space, TemplatePlanActions.Reuse, space.Key, space.Name, null, null));
            }
            else
            {
                items.Add(new TemplatePlanItem(TemplatePlanKinds.Space, TemplatePlanActions.Create, space.Key, space.Name, null, null));
            }
        }

        foreach (var space in template.Spaces)
        {
            Guid? targetSpaceId = reusedSpaceIds.TryGetValue(space.Key, out var id) ? id : null;
            foreach (var channel in space.Channels)
            {
                if (!channelByName.TryGetValue(channel.Name.Trim(), out var existing))
                {
                    items.Add(new TemplatePlanItem(
                        TemplatePlanKinds.Channel, TemplatePlanActions.Create, channel.Key, channel.Name, null, space.Key));
                    continue;
                }

                if (!string.Equals(existing.Type, channel.Type, StringComparison.Ordinal))
                {
                    items.Add(new TemplatePlanItem(
                        TemplatePlanKinds.Channel, TemplatePlanActions.Conflict, channel.Key, channel.Name, TemplateConflictDetails.Type, space.Key));
                    continue;
                }

                if (targetSpaceId is null || existing.SpaceId != targetSpaceId)
                {
                    items.Add(new TemplatePlanItem(
                        TemplatePlanKinds.Channel, TemplatePlanActions.Conflict, channel.Key, channel.Name, TemplateConflictDetails.Space, space.Key));
                    continue;
                }

                items.Add(new TemplatePlanItem(
                    TemplatePlanKinds.Channel, TemplatePlanActions.Reuse, channel.Key, channel.Name, null, space.Key));
            }
        }

        if (template.Policy is { } desired)
        {
            var same = policy is null
                ? PolicyEquals(desired, ImplicitPolicy)
                : PolicyEquals(desired, ToPolicy(policy));
            if (same)
            {
                items.Add(new TemplatePlanItem(TemplatePlanKinds.Policy, TemplatePlanActions.Reuse, "policy", null, null, null));
            }
            else if (policy is null)
            {
                items.Add(new TemplatePlanItem(TemplatePlanKinds.Policy, TemplatePlanActions.Create, "policy", null, null, null));
            }
            else
            {
                items.Add(new TemplatePlanItem(
                    TemplatePlanKinds.Policy, TemplatePlanActions.Conflict, "policy", null, TemplateConflictDetails.Policy, null));
            }
        }

        return items;
    }

    public static bool HasConflicts(IReadOnlyList<TemplatePlanItem> items) =>
        items.Any(x => x.Action == TemplatePlanActions.Conflict);

    public static bool TryExport(
        IReadOnlyList<TemplateExistingSpace> spaces,
        IReadOnlyList<TemplateExistingChannel> channels,
        TemplateExistingPolicy? policy,
        out string? json,
        out string error)
    {
        json = null;
        error = ExportEmpty;
        var spaceOrder = spaces.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var usedSpaceKeys = new HashSet<string>(StringComparer.Ordinal);
        var exportedSpaces = new List<TemplateSpace>();
        foreach (var space in spaceOrder)
        {
            var inSpace = channels
                .Where(x => x.SpaceId == space.Id && ChannelTypes.Contains(x.Type, StringComparer.Ordinal))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (inSpace.Count == 0)
            {
                continue;
            }

            var spaceKey = NextSlug(space.Name, usedSpaceKeys);
            var usedChannelKeys = new HashSet<string>(StringComparer.Ordinal);
            var exportedChannels = new List<TemplateChannel>();
            foreach (var channel in inSpace)
            {
                var name = channel.Name.Trim();
                if (name.Length is 0 or > MaxNameLength || HasControl(name))
                {
                    continue;
                }

                var topic = string.IsNullOrWhiteSpace(channel.Topic) ? null : channel.Topic.Trim();
                if (topic is not null && (topic.Length > MaxTopicLength || HasControl(topic)))
                {
                    topic = null;
                }

                exportedChannels.Add(new TemplateChannel(
                    NextSlug(name, usedChannelKeys),
                    name,
                    channel.Type,
                    topic));
            }

            if (exportedChannels.Count == 0)
            {
                continue;
            }

            var spaceName = space.Name.Trim();
            if (spaceName.Length is 0 or > MaxNameLength || HasControl(spaceName))
            {
                continue;
            }

            exportedSpaces.Add(new TemplateSpace(spaceKey, spaceName, exportedChannels));
        }

        if (exportedSpaces.Count == 0)
        {
            return false;
        }

        TemplatePolicy? exportedPolicy = policy is null ? null : ToPolicy(policy);
        var document = new WorkspaceTemplateDocument(
            SchemaV1,
            ExportId,
            1,
            exportedSpaces,
            exportedPolicy,
            OnboardingRules.DefaultChecklist,
            string.Empty);
        json = Canonical(document);
        error = string.Empty;
        return true;
    }

    public static IEnumerable<string> PropertyNames(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Walk(document.RootElement).ToArray();
    }

    private static IEnumerable<string> Walk(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var nested in Walk(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in Walk(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    private static WorkspaceTemplateDocument[] BuildBuiltins()
    {
        return
        [
            MustParse(TeamJson),
            MustParse(ProjectJson),
            MustParse(CommunityJson),
            MustParse(IncidentsJson)
        ];
    }

    private static WorkspaceTemplateDocument MustParse(string json)
    {
        if (!TryParse(json, out var document, out var error, out var path) || document is null)
        {
            throw new InvalidOperationException($"Built-in template failed to parse: {error} {path}");
        }

        return document;
    }

    private static bool TryParseElement(JsonElement root, out WorkspaceTemplateDocument? document, out string error, out string? path)
    {
        document = null;
        path = null;
        error = Invalid;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (root.TryGetProperty("schema", out var schemaProperty))
        {
            if (schemaProperty.ValueKind != JsonValueKind.String
                || schemaProperty.GetString() != SchemaV1)
            {
                error = UnknownSchema;
                path = "$.schema";
                return false;
            }
        }

        if (!RejectUnknown(root, RootProperties, "$", out error, out path))
        {
            return false;
        }

        if (!TryString(root, "schema", out var schema) || schema != SchemaV1)
        {
            error = UnknownSchema;
            path = "$.schema";
            return false;
        }

        if (!TryString(root, "id", out var id) || id.Length > MaxIdLength || !Slug.IsMatch(id))
        {
            path = "$.id";
            return false;
        }

        if (!root.TryGetProperty("version", out var versionProperty)
            || versionProperty.ValueKind != JsonValueKind.Number
            || !versionProperty.TryGetInt32(out var version)
            || version < 1)
        {
            path = "$.version";
            return false;
        }

        if (!root.TryGetProperty("spaces", out var spacesElement) || spacesElement.ValueKind != JsonValueKind.Array)
        {
            path = "$.spaces";
            return false;
        }

        var spaces = new List<TemplateSpace>();
        var spaceKeys = new HashSet<string>(StringComparer.Ordinal);
        var spaceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var channelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var spaceElement in spacesElement.EnumerateArray())
        {
            var spacePath = $"$.spaces[{index}]";
            if (spaceElement.ValueKind != JsonValueKind.Object
                || !RejectUnknown(spaceElement, SpaceProperties, spacePath, out error, out path))
            {
                path ??= spacePath;
                return false;
            }

            if (!TryString(spaceElement, "key", out var spaceKey) || spaceKey.Length > MaxIdLength || !Slug.IsMatch(spaceKey))
            {
                path = spacePath + ".key";
                return false;
            }

            if (!spaceKeys.Add(spaceKey))
            {
                path = spacePath + ".key";
                return false;
            }

            if (!TryName(spaceElement, "name", out var spaceName))
            {
                path = spacePath + ".name";
                return false;
            }

            if (!spaceNames.Add(spaceName))
            {
                path = spacePath + ".name";
                return false;
            }

            if (!spaceElement.TryGetProperty("channels", out var channelsElement) || channelsElement.ValueKind != JsonValueKind.Array)
            {
                path = spacePath + ".channels";
                return false;
            }

            var channels = new List<TemplateChannel>();
            var channelKeys = new HashSet<string>(StringComparer.Ordinal);
            var channelIndex = 0;
            foreach (var channelElement in channelsElement.EnumerateArray())
            {
                var channelPath = spacePath + $".channels[{channelIndex}]";
                if (channelElement.ValueKind != JsonValueKind.Object
                    || !RejectUnknown(channelElement, ChannelProperties, channelPath, out error, out path))
                {
                    path ??= channelPath;
                    return false;
                }

                if (!TryString(channelElement, "key", out var channelKey) || channelKey.Length > MaxIdLength || !Slug.IsMatch(channelKey))
                {
                    path = channelPath + ".key";
                    return false;
                }

                if (!channelKeys.Add(channelKey))
                {
                    path = channelPath + ".key";
                    return false;
                }

                if (!TryName(channelElement, "name", out var channelName) || !channelNames.Add(channelName))
                {
                    path = channelPath + ".name";
                    return false;
                }

                if (!TryString(channelElement, "type", out var type) || Array.IndexOf(ChannelTypes, type) < 0)
                {
                    path = channelPath + ".type";
                    return false;
                }

                string? topic = null;
                if (channelElement.TryGetProperty("topic", out var topicProperty))
                {
                    if (topicProperty.ValueKind != JsonValueKind.String)
                    {
                        path = channelPath + ".topic";
                        return false;
                    }

                    topic = topicProperty.GetString()?.Trim();
                    if (string.IsNullOrEmpty(topic))
                    {
                        topic = null;
                    }
                    else if (topic.Length > MaxTopicLength || HasControl(topic))
                    {
                        path = channelPath + ".topic";
                        return false;
                    }
                }

                channels.Add(new TemplateChannel(channelKey, channelName, type, topic));
                channelIndex++;
            }

            if (channels.Count == 0 || channels.Count > MaxChannelsPerSpace)
            {
                path = spacePath + ".channels";
                return false;
            }

            spaces.Add(new TemplateSpace(spaceKey, spaceName, channels));
            index++;
        }

        if (spaces.Count == 0 || spaces.Count > MaxSpaces)
        {
            path = "$.spaces";
            return false;
        }

        TemplatePolicy? policy = null;
        if (root.TryGetProperty("policyDefaults", out var policyElement))
        {
            if (policyElement.ValueKind != JsonValueKind.Object
                || !RejectUnknown(policyElement, PolicyProperties, "$.policyDefaults", out error, out path)
                || !TryPolicy(policyElement, out policy, out path))
            {
                path ??= "$.policyDefaults";
                return false;
            }
        }

        var checklist = new List<string>();
        if (root.TryGetProperty("checklist", out var checklistElement))
        {
            if (checklistElement.ValueKind != JsonValueKind.Array)
            {
                path = "$.checklist";
                return false;
            }

            var itemIndex = 0;
            foreach (var item in checklistElement.EnumerateArray())
            {
                var itemPath = $"$.checklist[{itemIndex}]";
                if (item.ValueKind != JsonValueKind.Object
                    || !RejectUnknown(item, ChecklistProperties, itemPath, out error, out path))
                {
                    path ??= itemPath;
                    return false;
                }

                if (!TryString(item, "key", out var key) || key.Length > MaxIdLength || !Slug.IsMatch(key) || checklist.Contains(key, StringComparer.Ordinal))
                {
                    path = itemPath + ".key";
                    return false;
                }

                checklist.Add(key);
                itemIndex++;
            }

            if (checklist.Count == 0 || checklist.Count > MaxChecklist)
            {
                path = "$.checklist";
                return false;
            }
        }
        else
        {
            checklist.AddRange(OnboardingRules.DefaultChecklist);
        }

        var built = new WorkspaceTemplateDocument(schema, id, version, spaces, policy, checklist, string.Empty);
        document = built with { CanonicalJson = Canonical(built) };
        error = string.Empty;
        return true;
    }

    private static bool TryPolicy(JsonElement element, out TemplatePolicy? policy, out string? path)
    {
        policy = null;
        path = "$.policyDefaults";
        if (!TryBool(element, "editEnabled", out var editEnabled)
            || !TryWindow(element, "editWindowMinutes", out var editWindow)
            || !TryBool(element, "editRolesRestricted", out var editRestricted)
            || !TryRoles(element, "editRoles", out var editRoles)
            || !TryBool(element, "editAllowModeratorOverride", out var editOverride)
            || !TryBool(element, "deleteEnabled", out var deleteEnabled)
            || !TryWindow(element, "deleteWindowMinutes", out var deleteWindow)
            || !TryBool(element, "deleteRolesRestricted", out var deleteRestricted)
            || !TryRoles(element, "deleteRoles", out var deleteRoles)
            || !TryBool(element, "deleteAllowModeratorOverride", out var deleteOverride))
        {
            return false;
        }

        if (editRestricted == (editRoles.Length == 0) || deleteRestricted == (deleteRoles.Length == 0))
        {
            return false;
        }

        policy = new TemplatePolicy(
            editEnabled,
            editWindow,
            editRestricted,
            editRoles,
            editOverride,
            deleteEnabled,
            deleteWindow,
            deleteRestricted,
            deleteRoles,
            deleteOverride);
        path = null;
        return true;
    }

    private static bool TryWindow(JsonElement element, string name, out int? minutes)
    {
        minutes = null;
        if (!element.TryGetProperty(name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
        {
            return false;
        }

        if (value is < 1 or > MaxWindowMinutes)
        {
            return false;
        }

        minutes = value;
        return true;
    }

    private static bool TryRoles(JsonElement element, string name, out string[] roles)
    {
        roles = [];
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var list = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var match = Roles.FirstOrDefault(role => role.Equals(item.GetString(), StringComparison.OrdinalIgnoreCase));
            if (match is null || list.Contains(match, StringComparer.Ordinal))
            {
                return false;
            }

            list.Add(match);
        }

        roles = list.ToArray();
        return true;
    }

    private static bool TryBool(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(name, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryName(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!TryString(element, name, out var raw))
        {
            return false;
        }

        value = raw.Trim();
        return value.Length is > 0 and <= MaxNameLength && !HasControl(value);
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool RejectUnknown(JsonElement element, string[] allowed, string path, out string error, out string? errorPath)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (Array.IndexOf(allowed, property.Name) < 0)
            {
                error = UnknownField;
                errorPath = path + "." + property.Name;
                return false;
            }
        }

        error = Invalid;
        errorPath = null;
        return true;
    }

    private static bool HasControl(string value) =>
        value.Any(char.IsControl);

    private static string NextSlug(string name, HashSet<string> used)
    {
        var chars = new List<char>();
        var dash = false;
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                chars.Add(c);
                dash = false;
            }
            else if (!dash && chars.Count > 0)
            {
                chars.Add('-');
                dash = true;
            }
        }

        var slug = new string(chars.ToArray()).Trim('-');
        if (slug.Length == 0)
        {
            slug = "item";
        }

        if (slug.Length > MaxIdLength)
        {
            slug = slug[..MaxIdLength].Trim('-');
        }

        var candidate = slug;
        var n = 2;
        while (!used.Add(candidate))
        {
            var suffix = "-" + n;
            var stem = slug;
            if (stem.Length + suffix.Length > MaxIdLength)
            {
                stem = stem[..Math.Max(1, MaxIdLength - suffix.Length)].Trim('-');
            }

            candidate = stem + suffix;
            n++;
        }

        return candidate;
    }

    private static bool PolicyEquals(TemplatePolicy left, TemplatePolicy right) =>
        left.EditEnabled == right.EditEnabled
        && left.EditWindowMinutes == right.EditWindowMinutes
        && left.EditRolesRestricted == right.EditRolesRestricted
        && left.EditAllowModeratorOverride == right.EditAllowModeratorOverride
        && left.DeleteEnabled == right.DeleteEnabled
        && left.DeleteWindowMinutes == right.DeleteWindowMinutes
        && left.DeleteRolesRestricted == right.DeleteRolesRestricted
        && left.DeleteAllowModeratorOverride == right.DeleteAllowModeratorOverride
        && RolesEqual(left.EditRoles, right.EditRoles)
        && RolesEqual(left.DeleteRoles, right.DeleteRoles);

    private static bool RolesEqual(string[] left, string[] right) =>
        left.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(right.OrderBy(x => x, StringComparer.Ordinal));

    private static TemplatePolicy ToPolicy(TemplateExistingPolicy policy) =>
        new(
            policy.EditEnabled,
            policy.EditWindowMinutes,
            policy.EditRolesRestricted,
            policy.EditRoles,
            policy.EditAllowModeratorOverride,
            policy.DeleteEnabled,
            policy.DeleteWindowMinutes,
            policy.DeleteRolesRestricted,
            policy.DeleteRoles,
            policy.DeleteAllowModeratorOverride);

    private static string Canonical(WorkspaceTemplateDocument document)
    {
        var payload = new Dictionary<string, object?>
        {
            ["schema"] = document.Schema,
            ["id"] = document.Id,
            ["version"] = document.Version,
            ["spaces"] = document.Spaces.Select(space => new Dictionary<string, object?>
            {
                ["key"] = space.Key,
                ["name"] = space.Name,
                ["channels"] = space.Channels.Select(channel =>
                {
                    var item = new Dictionary<string, object?>
                    {
                        ["key"] = channel.Key,
                        ["name"] = channel.Name,
                        ["type"] = channel.Type
                    };
                    if (channel.Topic is not null)
                    {
                        item["topic"] = channel.Topic;
                    }

                    return item;
                }).ToArray()
            }).ToArray()
        };

        if (document.Policy is { } policy)
        {
            payload["policyDefaults"] = new Dictionary<string, object?>
            {
                ["editEnabled"] = policy.EditEnabled,
                ["editWindowMinutes"] = policy.EditWindowMinutes,
                ["editRolesRestricted"] = policy.EditRolesRestricted,
                ["editRoles"] = policy.EditRoles,
                ["editAllowModeratorOverride"] = policy.EditAllowModeratorOverride,
                ["deleteEnabled"] = policy.DeleteEnabled,
                ["deleteWindowMinutes"] = policy.DeleteWindowMinutes,
                ["deleteRolesRestricted"] = policy.DeleteRolesRestricted,
                ["deleteRoles"] = policy.DeleteRoles,
                ["deleteAllowModeratorOverride"] = policy.DeleteAllowModeratorOverride
            };
        }

        payload["checklist"] = document.Checklist.Select(key => new Dictionary<string, object?> { ["key"] = key }).ToArray();
        return JsonSerializer.Serialize(payload);
    }
}
