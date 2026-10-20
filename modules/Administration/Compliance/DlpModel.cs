using System.Text.RegularExpressions;
using VibeChat.SharedKernel;

namespace VibeChat.Administration;

/// <summary>B-130. Off by default. Missing policy stays in simulation so enabling the flag does not block.</summary>
public sealed class DlpOptions
{
    public const string SectionName = "Features:Dlp";

    public bool Enabled { get; set; }
}

public static class DlpActions
{
    public const string Share = "share";
    public const string Guest = "guest";
    public const string Export = "export";
    public const string Webhook = "webhook";
    public const string Ai = "ai";
    public const string Download = "download";
    public const string Federation = "federation";

    public static readonly string[] All = [Share, Guest, Export, Webhook, Ai, Download, Federation];

    public static bool Known(string? action) =>
        action is Share or Guest or Export or Webhook or Ai or Download or Federation;
}

public static class DlpEffects
{
    public const string Allow = "allow";
    public const string Warn = "warn";
    public const string Block = "block";
    public const string Quarantine = "quarantine";

    public static bool Configurable(string? effect) => effect is Warn or Block or Quarantine;
}

public static class DlpScopes
{
    public const string Workspace = "workspace";
    public const string Space = "space";
    public const string Channel = "channel";

    public static bool Known(string? scope) => scope is Workspace or Space or Channel;
}

public static class DlpReasons
{
    public const string Allow = "Allow";
    public const string Warn = "Warn";
    public const string Block = "Block";
    public const string Quarantine = "Quarantine";
    public const string Simulated = "Simulated";
    public const string NotInspectable = "NotInspectable";
    public const string Exception = "Exception";
}

public static class DlpErrors
{
    public const string Disabled = "DlpDisabled";
    public const string Blocked = "DlpBlocked";
    public const string Quarantined = "DlpQuarantined";
    public const string DowngradeBlocked = "DlpDowngradeBlocked";
    public const string LabelInvalid = "DlpLabelInvalid";
    public const string PatternInvalid = "DlpPatternInvalid";
    public const string ScopeInvalid = "DlpScopeInvalid";
    public const string ExpiryInvalid = "DlpExpiryInvalid";
    public const string ActionInvalid = "DlpActionInvalid";
    public const string EffectInvalid = "DlpEffectInvalid";
    public const string NotFound = "DlpNotFound";
    public const string IdempotencyRequired = "DlpIdempotencyRequired";
    public const string IdempotencyConflict = "DlpIdempotencyConflict";
}

public static class DlpLimits
{
    public const int MaxKeyLength = 32;
    public const int MaxNameLength = 80;
    public const int MaxPatternLength = 200;
    public const int MaxReasonLength = 200;
    public const int MaxIdempotencyKeyLength = 200;
    public const int MaxRank = 100;
    public const int MaxBodyScan = 8000;
    public const int MaxMatches = 20;
    public const int MaxExceptionDays = 30;
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
}

public static class DlpLabels
{
    public const string Public = "public";
    public const string Internal = "internal";
    public const string Confidential = "confidential";
    public const string Restricted = "restricted";

    public static readonly (string Key, int Rank)[] Builtin =
    [
        (Public, 0),
        (Internal, 1),
        (Confidential, 2),
        (Restricted, 3)
    ];

    public static bool IsBuiltin(string? key) =>
        key is Public or Internal or Confidential or Restricted;

    public static bool TryRank(string key, IReadOnlyDictionary<string, int> custom, out int rank)
    {
        foreach (var builtin in Builtin)
        {
            if (builtin.Key == key)
            {
                rank = builtin.Rank;
                return true;
            }
        }

        return custom.TryGetValue(key, out rank);
    }
}

public readonly record struct DlpAssignmentInput(string Scope, Guid ScopeId, string LabelKey);

public readonly record struct DlpRuleInput(string Action, int MinRank, string Effect);

public readonly record struct DlpDetectorInput(Guid Id, string Pattern, int RaiseRank, bool Enabled);

public readonly record struct DlpExceptionInput(string Action, string Scope, Guid ScopeId, DateTimeOffset ExpiresAt, bool Revoked);

public readonly record struct DlpFalsePositiveInput(Guid DetectorId, Guid SubjectId);

public sealed record DlpOutcome(
    string Action,
    string Effect,
    string ReasonCode,
    bool Proceed,
    bool Inspectable,
    string? LabelKey,
    int Rank,
    int MatchCount,
    bool Simulation,
    bool Recorded);

public static class DlpRules
{
    public static bool IsInspectable(string? channelType, bool contentAuditEnabled) =>
        contentAuditEnabled && !ContentAuditRules.IsConfidentialE2eeType(channelType);

    public static string? ResolveLabel(
        IReadOnlyList<DlpAssignmentInput> assignments,
        Guid workspaceId,
        Guid? spaceId,
        Guid? channelId)
    {
        if (channelId is Guid channel)
        {
            var channelLabel = assignments.FirstOrDefault(x => x.Scope == DlpScopes.Channel && x.ScopeId == channel);
            if (channelLabel.LabelKey is not null)
            {
                return channelLabel.LabelKey;
            }
        }

        if (spaceId is Guid space)
        {
            var spaceLabel = assignments.FirstOrDefault(x => x.Scope == DlpScopes.Space && x.ScopeId == space);
            if (spaceLabel.LabelKey is not null)
            {
                return spaceLabel.LabelKey;
            }
        }

        var workspaceLabel = assignments.FirstOrDefault(x => x.Scope == DlpScopes.Workspace && x.ScopeId == workspaceId);
        return workspaceLabel.LabelKey;
    }

    public static int? ParentRank(
        IReadOnlyList<DlpAssignmentInput> assignments,
        IReadOnlyDictionary<string, int> ranks,
        string childScope,
        Guid workspaceId,
        Guid? spaceId)
    {
        if (childScope == DlpScopes.Channel && spaceId is Guid space)
        {
            var spaceLabel = assignments.FirstOrDefault(x => x.Scope == DlpScopes.Space && x.ScopeId == space);
            if (spaceLabel.LabelKey is not null && DlpLabels.TryRank(spaceLabel.LabelKey, ranks, out var spaceRank))
            {
                return spaceRank;
            }
        }

        if (childScope is DlpScopes.Channel or DlpScopes.Space)
        {
            var workspaceLabel = assignments.FirstOrDefault(x => x.Scope == DlpScopes.Workspace && x.ScopeId == workspaceId);
            if (workspaceLabel.LabelKey is not null && DlpLabels.TryRank(workspaceLabel.LabelKey, ranks, out var workspaceRank))
            {
                return workspaceRank;
            }
        }

        return null;
    }

    public static bool IsDowngrade(int? parentRank, int childRank) =>
        parentRank is int parent && childRank < parent;

    public static bool ValidCustomKey(string? key) =>
        key is not null
        && key.Length is >= 2 and <= DlpLimits.MaxKeyLength
        && !DlpLabels.IsBuiltin(key)
        && key[0] is >= 'a' and <= 'z'
        && key.All(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    public static bool TryCompile(string? pattern, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > DlpLimits.MaxPatternLength)
        {
            error = DlpErrors.PatternInvalid;
            return false;
        }

        try
        {
            _ = new Regex(pattern, RegexOptions.None, DlpLimits.MatchTimeout).IsMatch(string.Empty);
            return true;
        }
        catch (ArgumentException)
        {
            error = DlpErrors.PatternInvalid;
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            error = DlpErrors.PatternInvalid;
            return false;
        }
    }

    public static int CountMatches(string pattern, string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return 0;
        }

        var sample = body.Length > DlpLimits.MaxBodyScan ? body[..DlpLimits.MaxBodyScan] : body;
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, DlpLimits.MatchTimeout);
            var count = 0;
            var match = regex.Match(sample);
            while (match.Success && count < DlpLimits.MaxMatches)
            {
                count++;
                match = match.NextMatch();
            }

            return count;
        }
        catch (ArgumentException)
        {
            return 0;
        }
        catch (RegexMatchTimeoutException)
        {
            return 0;
        }
    }

    public static bool ExceptionCovers(
        DlpExceptionInput exception,
        string action,
        Guid workspaceId,
        Guid? spaceId,
        Guid? channelId,
        DateTimeOffset now)
    {
        if (exception.Revoked || exception.ExpiresAt <= now || exception.Action != action)
        {
            return false;
        }

        return exception.Scope switch
        {
            DlpScopes.Channel => channelId is Guid channel && exception.ScopeId == channel,
            DlpScopes.Space => spaceId is Guid space && exception.ScopeId == space,
            DlpScopes.Workspace => exception.ScopeId == workspaceId,
            _ => false
        };
    }

    public static bool ValidExceptionWindow(DateTimeOffset now, DateTimeOffset expiresAt) =>
        expiresAt > now && expiresAt <= now.AddDays(DlpLimits.MaxExceptionDays);

    public static DlpOutcome Evaluate(
        string action,
        bool simulation,
        bool contentAuditEnabled,
        string? channelType,
        Guid workspaceId,
        Guid? spaceId,
        Guid? channelId,
        string? body,
        Guid? subjectId,
        IReadOnlyList<DlpAssignmentInput> assignments,
        IReadOnlyDictionary<string, int> customRanks,
        IReadOnlyList<DlpRuleInput> rules,
        IReadOnlyList<DlpDetectorInput> detectors,
        IReadOnlyList<DlpExceptionInput> exceptions,
        IReadOnlyList<DlpFalsePositiveInput> falsePositives,
        DateTimeOffset now)
    {
        var e2ee = ContentAuditRules.IsConfidentialE2eeType(channelType);
        var inspectable = IsInspectable(channelType, contentAuditEnabled);
        var labelKey = ResolveLabel(assignments, workspaceId, spaceId, channelId);
        var rank = -1;
        if (labelKey is not null && DlpLabels.TryRank(labelKey, customRanks, out var labelRank))
        {
            rank = labelRank;
        }
        else
        {
            labelKey = null;
        }

        var matchCount = 0;
        if (inspectable && !string.IsNullOrEmpty(body))
        {
            foreach (var detector in detectors)
            {
                if (!detector.Enabled)
                {
                    continue;
                }

                if (subjectId is Guid subject
                    && falsePositives.Any(x => x.DetectorId == detector.Id && x.SubjectId == subject))
                {
                    continue;
                }

                var hits = CountMatches(detector.Pattern, body);
                if (hits == 0)
                {
                    continue;
                }

                matchCount += hits;
                if (detector.RaiseRank > rank)
                {
                    rank = detector.RaiseRank;
                }
            }
        }

        var covered = exceptions.Any(x => ExceptionCovers(x, action, workspaceId, spaceId, channelId, now));
        var rule = rules.FirstOrDefault(x => x.Action == action && rank >= x.MinRank);
        var effect = covered
            ? DlpEffects.Allow
            : rule.Action is null ? DlpEffects.Allow : rule.Effect;
        var reason = covered
            ? DlpReasons.Exception
            : effect switch
            {
                DlpEffects.Warn => DlpReasons.Warn,
                DlpEffects.Block => DlpReasons.Block,
                DlpEffects.Quarantine => DlpReasons.Quarantine,
                _ => e2ee ? DlpReasons.NotInspectable : DlpReasons.Allow
            };
        var simulated = simulation && effect is DlpEffects.Block or DlpEffects.Quarantine;
        if (simulated)
        {
            reason = DlpReasons.Simulated;
        }

        var proceed = effect is DlpEffects.Allow or DlpEffects.Warn || simulated;
        var recorded = effect is not DlpEffects.Allow
            || simulated
            || covered
            || matchCount > 0
            || (e2ee && reason == DlpReasons.NotInspectable);

        return new DlpOutcome(
            action,
            simulated ? rule.Effect : effect,
            reason,
            proceed,
            inspectable,
            labelKey,
            rank,
            matchCount,
            simulated,
            recorded);
    }
}

public sealed class DlpPolicy
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public bool Simulation { get; set; } = true;
    public int Version { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DataLabel
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Key { get; set; } = string.Empty;
    public int Rank { get; set; }
    public int Version { get; set; } = 1;
    public UserId CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class LabelAssignment
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string ScopeKind { get; set; } = string.Empty;
    public Guid ScopeId { get; set; }
    public string LabelKey { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public UserId UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DlpRule
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Action { get; set; } = string.Empty;
    public int MinRank { get; set; }
    public string Effect { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DlpDetector
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public int RaiseRank { get; set; }
    public bool Enabled { get; set; } = true;
    public int Version { get; set; } = 1;
    public UserId CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class DlpException
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ScopeKind { get; set; } = string.Empty;
    public Guid ScopeId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public UserId CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class DlpFalsePositive
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public Guid DetectorId { get; set; }
    public Guid SubjectId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public UserId ReviewedBy { get; set; }
    public DateTimeOffset ReviewedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class DlpDecisionRecord
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Effect { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public bool Simulation { get; set; }
    public bool Inspectable { get; set; }
    public string? LabelKey { get; set; }
    public int MatchCount { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? SubjectId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
