namespace VibeChat.ArchitectureTests;

internal enum SourceLimitKind
{
    None,
    ApiCompositionRoot,
    InfrastructureWiring,
    WebServiceOrStore,
    WebComponent,
}

internal readonly record struct LineLimitViolation(string RelativePath, int Lines, int Limit, string Rule);

/// <summary>
/// B-183 line gate. Limits match the spec; files that already exceeded the web
/// limit before this gate keep a frozen ceiling and must not grow.
/// </summary>
internal static class SourceLineLimits
{
    internal const int ApiProgramMaxLines = 500;
    internal const int InfrastructureWiringMaxLines = 600;
    internal const int WebUnitMaxLines = 400;

    // Baseline 2026-10-05 (post W19-1…W19-5). Refactoring these files is out of
    // scope for B-183. Drop an entry once the file is at or under WebUnitMaxLines.
    internal static readonly IReadOnlyDictionary<string, int> GrandfatherCeilings =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["apps/web/src/app/core/api/admin-api.service.ts"] = 435,
            ["apps/web/src/app/core/api/directory-api.service.ts"] = 526,
            ["apps/web/src/app/core/api/messaging-api.service.ts"] = 952,
            ["apps/web/src/app/shared/ui/attachment-preview/attachment-preview.ts"] = 407,
            ["apps/web/src/app/features/admin/admin-settings.page.ts"] = 545,
            ["apps/web/src/app/features/chat/command-palette/command-palette.ts"] = 568,
            ["apps/web/src/app/features/chat/notification-preferences-panel/notification-preferences-panel.ts"] = 613,
            ["apps/web/src/app/features/chat/thread-panel/thread-panel.ts"] = 718,
            ["apps/web/src/app/features/chat/timeline/timeline.ts"] = 863,
            ["apps/web/src/app/shared/ui/sidebar-nav/sidebar-nav.ts"] = 893,
            ["apps/web/src/app/layout/shell.page.ts"] = 1054,
            ["apps/web/src/app/layout/shell.page.html"] = 600,
            ["apps/web/src/app/features/admin/admin-settings.page.html"] = 992,
        };

    internal static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').TrimStart('/');

    internal static int CountLines(string contents)
    {
        if (contents.Length == 0)
        {
            return 0;
        }

        var lines = 0;
        for (var i = 0; i < contents.Length; i++)
        {
            if (contents[i] == '\n')
            {
                lines++;
            }
        }

        if (contents[^1] != '\n')
        {
            lines++;
        }

        return lines;
    }

    internal static bool IsExcluded(string relativePath)
    {
        var path = Normalize(relativePath);
        var segments = path.Split('/');
        foreach (var segment in segments)
        {
            if (segment is "node_modules" or "bin" or "obj" or "dist" or "Migrations")
            {
                return true;
            }
        }

        var name = segments[^1];
        return name.EndsWith(".spec.ts", StringComparison.Ordinal)
            || name.EndsWith("Designer.cs", StringComparison.Ordinal)
            || name.EndsWith("Snapshot.cs", StringComparison.Ordinal);
    }

    internal static SourceLimitKind Classify(string relativePath, string contents)
    {
        var path = Normalize(relativePath);
        if (IsExcluded(path))
        {
            return SourceLimitKind.None;
        }

        if (path == "apps/api/Program.cs")
        {
            return SourceLimitKind.ApiCompositionRoot;
        }

        if (path.StartsWith("src/VibeChat.Infrastructure/", StringComparison.Ordinal))
        {
            var name = path[(path.LastIndexOf('/') + 1)..];
            if (name == "Infrastructure.cs"
                || name.EndsWith("ServiceCollectionExtensions.cs", StringComparison.Ordinal))
            {
                return SourceLimitKind.InfrastructureWiring;
            }
        }

        if (path.StartsWith("apps/web/src/", StringComparison.Ordinal))
        {
            if (path.EndsWith(".service.ts", StringComparison.Ordinal)
                || path.EndsWith(".store.ts", StringComparison.Ordinal))
            {
                return SourceLimitKind.WebServiceOrStore;
            }

            if (path.EndsWith(".html", StringComparison.Ordinal)
                || (path.EndsWith(".ts", StringComparison.Ordinal)
                    && contents.Contains("@Component", StringComparison.Ordinal)))
            {
                return SourceLimitKind.WebComponent;
            }
        }

        return SourceLimitKind.None;
    }

    internal static int LimitFor(SourceLimitKind kind, string relativePath)
    {
        var standard = kind switch
        {
            SourceLimitKind.ApiCompositionRoot => ApiProgramMaxLines,
            SourceLimitKind.InfrastructureWiring => InfrastructureWiringMaxLines,
            SourceLimitKind.WebServiceOrStore or SourceLimitKind.WebComponent => WebUnitMaxLines,
            _ => int.MaxValue,
        };

        var path = Normalize(relativePath);
        if (GrandfatherCeilings.TryGetValue(path, out var ceiling))
        {
            return Math.Max(standard, ceiling);
        }

        return standard;
    }

    internal static IReadOnlyList<LineLimitViolation> Evaluate(
        IEnumerable<(string RelativePath, string Contents)> files)
    {
        var violations = new List<LineLimitViolation>();
        foreach (var (relativePath, contents) in files)
        {
            var kind = Classify(relativePath, contents);
            if (kind == SourceLimitKind.None)
            {
                continue;
            }

            var lines = CountLines(contents);
            var limit = LimitFor(kind, relativePath);
            if (lines > limit)
            {
                violations.Add(new(Normalize(relativePath), lines, limit, kind.ToString()));
            }
        }

        return violations;
    }

    internal static string Format(LineLimitViolation violation) =>
        $"{violation.RelativePath} has {violation.Lines} lines (limit {violation.Limit}, rule {violation.Rule})";
}
