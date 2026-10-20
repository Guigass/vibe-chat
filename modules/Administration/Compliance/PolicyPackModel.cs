using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VibeChat.SharedKernel;

namespace VibeChat.Administration;

/// <summary>B-133. Off by default. A pack is a technical baseline, not a certification.</summary>
public sealed class PolicyPackOptions
{
    public const string SectionName = "Features:PolicyPacks";

    public bool Enabled { get; set; }
}

public static class PolicyPackSchema
{
    public const string Name = "policy-pack.v1";
    public const string Baseline = "technical";
}

public static class PolicyPackIds
{
    public const string SmallTeam = "small-team";
    public const string EnterpriseBaseline = "enterprise-baseline";
    public const string Regulated = "regulated";
}

public static class PolicyPackControls
{
    public const string AiEnabled = "ai.enabled";
    public const string AuditContentEnabled = "audit.contentEnabled";
    public const string AuthPasswordMinLength = "auth.passwordMinLength";
    public const string DlpSimulation = "dlp.simulation";
    public const string GuestsEnabled = "guests.enabled";
    public const string RetentionDays = "retention.days";
    public const string RetentionEnabled = "retention.enabled";
    public const string SharingExternalForward = "sharing.externalForward";
    public const string UploadsMaxMb = "uploads.maxMb";
}

public static class PolicyPackErrors
{
    public const string Disabled = "PolicyPackDisabled";
    public const string Unknown = "PolicyPackUnknown";
    public const string VersionUnknown = "PolicyPackVersionUnknown";
    public const string ControlUnknown = "PolicyPackControlUnknown";
    public const string ControlInvalid = "PolicyPackControlInvalid";
    public const string OverrideForbidden = "PolicyPackOverrideForbidden";
    public const string ImportRejected = "PolicyPackImportRejected";
    public const string ConfirmationRequired = "PolicyPackConfirmationRequired";
    public const string IdempotencyRequired = "PolicyPackIdempotencyRequired";
    public const string IdempotencyConflict = "PolicyPackIdempotencyConflict";
    public const string NotFound = "PolicyPackNotFound";
    public const string RollbackUnsafe = "PolicyPackRollbackUnsafe";
    public const string GuestsBlocked = "PolicyPackGuestsBlocked";

    public static int Status(string error) => error switch
    {
        OverrideForbidden or IdempotencyConflict or RollbackUnsafe or GuestsBlocked => 409,
        NotFound or Disabled => 404,
        _ => 400
    };
}

public static class PolicyPackLimits
{
    public const int MaxIdempotencyKeyLength = 200;
    public const int DefaultUploadCeilingMb = 10;
}

public sealed class PolicyPackAssignment
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string PackId { get; set; } = string.Empty;
    public int PackVersion { get; set; }
    public string OverridesJson { get; set; } = "{}";
    public string EffectiveJson { get; set; } = "{}";
    public string? PreviousPackId { get; set; }
    public int? PreviousPackVersion { get; set; }
    public string? PreviousOverridesJson { get; set; }
    public string? PreviousEffectiveJson { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public UserId AppliedBy { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
}

public sealed record PolicyPackCommandResult(int Status, object? Body, string? Error)
{
    public static PolicyPackCommandResult Ok(object body, int status = 200) => new(status, body, null);

    public static PolicyPackCommandResult Fail(string error) => new(PolicyPackErrors.Status(error), null, error);
}

public readonly record struct PolicyControlSpec(string Key, bool Integer, bool HigherIsStricter, bool Enforced, int? Min, int? Max);

public readonly record struct ResolvedControl(string Key, bool Integer, long Number, bool Flag, bool Locked, string Source, bool Enforced);

public readonly record struct PolicyDriftItem(string Key, JsonElement Expected, JsonElement Actual, bool Enforced, bool Drifted);

/// <summary>Declarative packs. No tenant id and no secret belongs in a manifest.</summary>
public static class PolicyPackRules
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static readonly PolicyControlSpec[] Specs =
    [
        new(PolicyPackControls.AiEnabled, false, false, true, null, null),
        new(PolicyPackControls.AuditContentEnabled, false, true, true, null, null),
        new(PolicyPackControls.AuthPasswordMinLength, true, true, false, 8, 128),
        new(PolicyPackControls.DlpSimulation, false, false, true, null, null),
        new(PolicyPackControls.GuestsEnabled, false, false, true, null, null),
        new(PolicyPackControls.RetentionDays, true, true, true, 1, 3650),
        new(PolicyPackControls.RetentionEnabled, false, true, true, null, null),
        new(PolicyPackControls.SharingExternalForward, false, false, false, null, null),
        new(PolicyPackControls.UploadsMaxMb, true, false, true, 1, 100)
    ];

    private static readonly Dictionary<string, PolicyControlSpec> ByKey =
        Specs.ToDictionary(x => x.Key, StringComparer.Ordinal);

    public static IReadOnlyList<(string Id, int Version, string Label)> Catalog() =>
    [
        (PolicyPackIds.SmallTeam, 1, "SmallTeam"),
        (PolicyPackIds.EnterpriseBaseline, 1, "EnterpriseBaseline"),
        (PolicyPackIds.Regulated, 1, "Regulated")
    ];

    public static bool UpgradeAvailable(int appliedVersion, int catalogVersion) => catalogVersion > appliedVersion;

    public static bool TryResolve(
        string? packId,
        int version,
        JsonElement overrides,
        int uploadCeilingMb,
        out string canonical,
        out string overridesCanonical,
        out string error)
    {
        canonical = string.Empty;
        overridesCanonical = "{}";
        if (!TryPack(packId, version, out var label, out var settings, out error))
        {
            return false;
        }

        if (!TryOverrides(overrides, out var parsed, out overridesCanonical, out error))
        {
            return false;
        }

        var ceiling = uploadCeilingMb < 1 ? PolicyPackLimits.DefaultUploadCeilingMb : uploadCeilingMb;
        var resolved = new List<ResolvedControl>(Specs.Length);
        foreach (var spec in Specs)
        {
            var baseline = settings[spec.Key];
            var number = baseline.Number;
            var flag = baseline.Flag;
            var source = "pack";
            var locked = baseline.Locked;
            if (parsed.TryGetValue(spec.Key, out var over))
            {
                if (locked && Weaker(spec, baseline.Number, baseline.Flag, over.Number, over.Flag))
                {
                    error = PolicyPackErrors.OverrideForbidden;
                    return false;
                }

                number = over.Number;
                flag = over.Flag;
                source = "override";
            }

            if (spec.Key == PolicyPackControls.UploadsMaxMb && number > ceiling)
            {
                number = ceiling;
            }

            if (spec.Key == PolicyPackControls.AuditContentEnabled && !flag)
            {
                error = PolicyPackErrors.OverrideForbidden;
                return false;
            }

            resolved.Add(new ResolvedControl(spec.Key, spec.Integer, number, flag, locked, source, spec.Enforced));
        }

        canonical = WriteEffective(packId!, version, label, resolved);
        error = string.Empty;
        return true;
    }

    public static bool TryReadImport(JsonElement document, out string packId, out int version, out JsonElement overrides, out string error)
    {
        packId = string.Empty;
        version = 0;
        overrides = default;
        if (document.ValueKind != JsonValueKind.Object || Banned(document) || Executable(document))
        {
            error = PolicyPackErrors.ImportRejected;
            return false;
        }

        if (!document.TryGetProperty("schema", out var schema) || schema.GetString() != PolicyPackSchema.Name)
        {
            error = PolicyPackErrors.ImportRejected;
            return false;
        }

        if (!document.TryGetProperty("packId", out var id) || id.ValueKind != JsonValueKind.String)
        {
            error = PolicyPackErrors.ImportRejected;
            return false;
        }

        packId = id.GetString() ?? string.Empty;
        if (!document.TryGetProperty("version", out var versionElement) || !versionElement.TryGetInt32(out version))
        {
            error = PolicyPackErrors.ImportRejected;
            return false;
        }

        if (document.TryGetProperty("overrides", out var over))
        {
            overrides = over;
        }

        error = string.Empty;
        return true;
    }

    public static string Redact(JsonNode node)
    {
        var copy = node.DeepClone();
        RedactInPlace(copy);
        return copy.ToJsonString(Json);
    }

    public static bool BlocksGuests(string effectiveJson)
    {
        using var document = JsonDocument.Parse(effectiveJson);
        foreach (var control in document.RootElement.GetProperty("controls").EnumerateArray())
        {
            if (control.GetProperty("key").GetString() == PolicyPackControls.GuestsEnabled)
            {
                return control.GetProperty("value").ValueKind == JsonValueKind.False;
            }
        }

        return false;
    }

    public static IReadOnlyList<ResolvedControl> ReadControls(string canonical)
    {
        using var document = JsonDocument.Parse(canonical);
        var list = new List<ResolvedControl>();
        foreach (var control in document.RootElement.GetProperty("controls").EnumerateArray())
        {
            var key = control.GetProperty("key").GetString() ?? string.Empty;
            var spec = ByKey[key];
            var value = control.GetProperty("value");
            var integer = spec.Integer;
            list.Add(new ResolvedControl(
                key,
                integer,
                integer ? value.GetInt64() : 0,
                !integer && value.GetBoolean(),
                control.GetProperty("locked").GetBoolean(),
                control.GetProperty("source").GetString() ?? "pack",
                control.GetProperty("enforced").GetBoolean()));
        }

        return list;
    }

    public static IReadOnlyList<PolicyDriftItem> Compare(string canonical, IReadOnlyDictionary<string, JsonElement> liveEnforced)
    {
        using var document = JsonDocument.Parse(canonical);
        var items = new List<PolicyDriftItem>();
        foreach (var control in document.RootElement.GetProperty("controls").EnumerateArray())
        {
            var key = control.GetProperty("key").GetString() ?? string.Empty;
            var expected = control.GetProperty("value").Clone();
            var enforced = control.GetProperty("enforced").GetBoolean();
            JsonElement actual;
            var drifted = false;
            if (enforced && liveEnforced.TryGetValue(key, out var live))
            {
                actual = live.Clone();
                drifted = !JsonElementEquals(expected, actual);
            }
            else
            {
                actual = expected.Clone();
            }

            items.Add(new PolicyDriftItem(key, expected, actual, enforced, drifted));
        }

        return items;
    }

    public static string RequestHash(string packId, int version, string overridesCanonical)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{packId}\n{version}\n{overridesCanonical}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string RollbackHash(string packId, int version, string overridesCanonical) =>
        RequestHash("rollback:" + packId, version, overridesCanonical);

    private static bool TryPack(
        string? packId,
        int version,
        out string label,
        out IReadOnlyDictionary<string, PackSetting> settings,
        out string error)
    {
        label = string.Empty;
        settings = SmallTeam;
        if (packId is not (PolicyPackIds.SmallTeam or PolicyPackIds.EnterpriseBaseline or PolicyPackIds.Regulated))
        {
            error = PolicyPackErrors.Unknown;
            return false;
        }

        var catalogVersion = 1;
        if (version != catalogVersion)
        {
            error = PolicyPackErrors.VersionUnknown;
            return false;
        }

        (label, settings) = packId switch
        {
            PolicyPackIds.EnterpriseBaseline => ("EnterpriseBaseline", Enterprise),
            PolicyPackIds.Regulated => ("Regulated", Regulated),
            _ => ("SmallTeam", SmallTeam)
        };
        error = string.Empty;
        return true;
    }

    private static bool TryOverrides(
        JsonElement overrides,
        out Dictionary<string, PackSetting> parsed,
        out string canonical,
        out string error)
    {
        parsed = new Dictionary<string, PackSetting>(StringComparer.Ordinal);
        if (overrides.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            canonical = "{}";
            error = string.Empty;
            return true;
        }

        if (overrides.ValueKind != JsonValueKind.Object || Banned(overrides))
        {
            canonical = "{}";
            error = PolicyPackErrors.ControlInvalid;
            return false;
        }

        foreach (var property in overrides.EnumerateObject())
        {
            if (!ByKey.TryGetValue(property.Name, out var spec) || !TryValue(spec, property.Value, out var setting))
            {
                canonical = "{}";
                error = ByKey.ContainsKey(property.Name) ? PolicyPackErrors.ControlInvalid : PolicyPackErrors.ControlUnknown;
                return false;
            }

            parsed[property.Name] = setting;
        }

        var obj = new JsonObject();
        foreach (var key in parsed.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            var setting = parsed[key];
            obj[key] = ByKey[key].Integer ? JsonValue.Create(setting.Number) : JsonValue.Create(setting.Flag);
        }

        canonical = obj.ToJsonString(Json);
        error = string.Empty;
        return true;
    }

    private static bool TryValue(PolicyControlSpec spec, JsonElement value, out PackSetting setting)
    {
        setting = default;
        if (spec.Integer)
        {
            if (!value.TryGetInt32(out var number) || number < spec.Min || number > spec.Max)
            {
                return false;
            }

            setting = new PackSetting(true, number, false);
            return true;
        }

        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        setting = new PackSetting(false, 0, value.GetBoolean());
        return true;
    }

    private static bool Weaker(PolicyControlSpec spec, long packNumber, bool packFlag, long nextNumber, bool nextFlag)
    {
        if (spec.Integer)
        {
            return spec.HigherIsStricter ? nextNumber < packNumber : nextNumber > packNumber;
        }

        return spec.HigherIsStricter ? packFlag && !nextFlag : !packFlag && nextFlag;
    }

    private static string WriteEffective(string packId, int version, string label, IReadOnlyList<ResolvedControl> controls)
    {
        var array = new JsonArray();
        foreach (var control in controls.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var item = new JsonObject
            {
                ["key"] = control.Key,
                ["value"] = control.Integer ? JsonValue.Create(control.Number) : JsonValue.Create(control.Flag),
                ["locked"] = control.Locked,
                ["source"] = control.Source,
                ["enforced"] = control.Enforced
            };
            array.Add(item);
        }

        var root = new JsonObject
        {
            ["schema"] = PolicyPackSchema.Name,
            ["packId"] = packId,
            ["version"] = version,
            ["label"] = label,
            ["baseline"] = PolicyPackSchema.Baseline,
            ["certification"] = false,
            ["controls"] = array
        };
        return root.ToJsonString(Json);
    }

    private static bool Banned(JsonElement element) => Walk(element, static name => IsBanned(name), static _ => false);

    private static bool Executable(JsonElement element) => Walk(element, static _ => false, static text => LooksExecutable(text));

    private static bool Walk(JsonElement element, Func<string, bool> name, Func<string, bool> text)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (name(property.Name) || Walk(property.Value, name, text))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (Walk(item, name, text))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.String:
                return text(element.GetString() ?? string.Empty);
            default:
                return false;
        }
    }

    private static bool IsBanned(string name)
    {
        var normalized = name.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);
        return normalized.Equals("tenantid", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("workspaceid", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("secret", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("token", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("password", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("authorization", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("apikey", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("credential", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("script", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("eval", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("function", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("code", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("password", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("token", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("tenantid", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("workspaceid", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksExecutable(string text) =>
        text.Contains("function(", StringComparison.OrdinalIgnoreCase)
        || text.Contains("eval(", StringComparison.OrdinalIgnoreCase)
        || text.Contains("<script", StringComparison.OrdinalIgnoreCase)
        || text.Contains("BEGIN ", StringComparison.Ordinal);

    private static void RedactInPlace(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(x => x.Key).ToArray())
                {
                    if (IsBanned(key))
                    {
                        obj.Remove(key);
                        continue;
                    }

                    RedactInPlace(obj[key]);
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    RedactInPlace(item);
                }

                break;
            case JsonValue value when value.TryGetValue<string>(out var text) && (LooksExecutable(text) || text.Contains("secret=", StringComparison.OrdinalIgnoreCase)):
                value.ReplaceWith("[redacted]");
                break;
        }
    }

    private static bool JsonElementEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Number => left.GetInt64() == right.GetInt64(),
            _ => left.GetRawText() == right.GetRawText()
        };
    }

    private readonly record struct PackSetting(bool Integer, long Number, bool Flag, bool Locked = false);

    private static PackSetting Bool(bool value, bool locked) => new(false, 0, value, locked);

    private static PackSetting Int(int value, bool locked) => new(true, value, false, locked);

    private static readonly Dictionary<string, PackSetting> SmallTeam = new(StringComparer.Ordinal)
    {
        [PolicyPackControls.AiEnabled] = Bool(true, false),
        [PolicyPackControls.AuditContentEnabled] = Bool(true, true),
        [PolicyPackControls.AuthPasswordMinLength] = Int(8, false),
        [PolicyPackControls.DlpSimulation] = Bool(true, false),
        [PolicyPackControls.GuestsEnabled] = Bool(true, false),
        [PolicyPackControls.RetentionDays] = Int(90, false),
        [PolicyPackControls.RetentionEnabled] = Bool(false, false),
        [PolicyPackControls.SharingExternalForward] = Bool(true, false),
        [PolicyPackControls.UploadsMaxMb] = Int(10, false)
    };

    private static readonly Dictionary<string, PackSetting> Enterprise = new(StringComparer.Ordinal)
    {
        [PolicyPackControls.AiEnabled] = Bool(false, true),
        [PolicyPackControls.AuditContentEnabled] = Bool(true, true),
        [PolicyPackControls.AuthPasswordMinLength] = Int(12, true),
        [PolicyPackControls.DlpSimulation] = Bool(true, false),
        [PolicyPackControls.GuestsEnabled] = Bool(false, true),
        [PolicyPackControls.RetentionDays] = Int(365, true),
        [PolicyPackControls.RetentionEnabled] = Bool(true, true),
        [PolicyPackControls.SharingExternalForward] = Bool(false, true),
        [PolicyPackControls.UploadsMaxMb] = Int(10, true)
    };

    private static readonly Dictionary<string, PackSetting> Regulated = new(StringComparer.Ordinal)
    {
        [PolicyPackControls.AiEnabled] = Bool(false, true),
        [PolicyPackControls.AuditContentEnabled] = Bool(true, true),
        [PolicyPackControls.AuthPasswordMinLength] = Int(14, true),
        [PolicyPackControls.DlpSimulation] = Bool(false, true),
        [PolicyPackControls.GuestsEnabled] = Bool(false, true),
        [PolicyPackControls.RetentionDays] = Int(2555, true),
        [PolicyPackControls.RetentionEnabled] = Bool(true, true),
        [PolicyPackControls.SharingExternalForward] = Bool(false, true),
        [PolicyPackControls.UploadsMaxMb] = Int(5, true)
    };
}
