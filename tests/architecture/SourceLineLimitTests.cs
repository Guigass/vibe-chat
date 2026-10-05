using FluentAssertions;

namespace VibeChat.ArchitectureTests;

public sealed class SourceLineLimitTests
{
    [Fact]
    public void Repository_sources_respect_b183_line_limits()
    {
        var violations = SourceLineLimits.Evaluate(ReadScopedSources(FindRepoRoot()));

        violations.Should().BeEmpty(
            "B-183: Program.cs ≤ 500, Infrastructure registrars ≤ 600, "
            + "web services/stores/components ≤ 400 (grandfather ceilings must not grow):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(SourceLineLimits.Format)));
    }

    [Fact]
    public void Synthetic_service_over_the_limit_is_reported_with_path_count_and_limit()
    {
        var path = "apps/web/src/app/core/api/synthetic-api.service.ts";
        var contents = string.Join('\n', Enumerable.Repeat("export class SyntheticApi {}", 401)) + "\n";

        var violations = SourceLineLimits.Evaluate([(path, contents)]);

        violations.Should().ContainSingle();
        var violation = violations[0];
        violation.RelativePath.Should().Be(path);
        violation.Lines.Should().Be(401);
        violation.Limit.Should().Be(SourceLineLimits.WebUnitMaxLines);
        SourceLineLimits.Format(violation).Should().Contain(path).And.Contain("401").And.Contain("400");
    }

    [Fact]
    public void File_at_the_exact_limit_passes()
    {
        var contents = string.Join('\n', Enumerable.Repeat("export class AtLimit {}", 400)) + "\n";

        var violations = SourceLineLimits.Evaluate(
        [
            ("apps/web/src/app/core/api/at-limit-api.service.ts", contents),
            ("apps/api/Program.cs", Lines(SourceLineLimits.ApiProgramMaxLines)),
            ("src/VibeChat.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs",
                Lines(SourceLineLimits.InfrastructureWiringMaxLines)),
        ]);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Excluded_generated_migration_and_spec_files_are_ignored()
    {
        var huge = Lines(5_000);
        var violations = SourceLineLimits.Evaluate(
        [
            ("src/VibeChat.Infrastructure/Persistence/Migrations/20260101_Huge.cs", huge),
            ("src/VibeChat.Infrastructure/Persistence/Migrations/20260101_Huge.Designer.cs", huge),
            ("src/VibeChat.Infrastructure/Persistence/Migrations/VibeChatDbContextModelSnapshot.cs", huge),
            ("apps/web/src/app/core/api/messaging-api.service.spec.ts", huge),
            ("apps/web/node_modules/pkg/index.service.ts", huge),
            ("apps/api/bin/Release/Program.cs", huge),
        ]);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Grandfather_ceiling_rejects_growth()
    {
        var path = "apps/web/src/app/core/api/messaging-api.service.ts";
        var ceiling = SourceLineLimits.GrandfatherCeilings[path];

        SourceLineLimits.Evaluate([(path, Lines(ceiling))]).Should().BeEmpty();

        var violations = SourceLineLimits.Evaluate([(path, Lines(ceiling + 1))]);
        violations.Should().ContainSingle();
        violations[0].Lines.Should().Be(ceiling + 1);
        violations[0].Limit.Should().Be(ceiling);
    }

    [Fact]
    public void Grandfather_entries_are_still_above_the_default_web_limit()
    {
        var root = FindRepoRoot();
        foreach (var (relativePath, ceiling) in SourceLineLimits.GrandfatherCeilings)
        {
            var absolute = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(absolute).Should().BeTrue($"grandfather entry {relativePath} must exist or be removed");
            var lines = SourceLineLimits.CountLines(File.ReadAllText(absolute));
            lines.Should().BeLessThanOrEqualTo(ceiling, SourceLineLimits.Format(
                new LineLimitViolation(relativePath, lines, ceiling, "grandfather")));
            lines.Should().BeGreaterThan(SourceLineLimits.WebUnitMaxLines,
                $"{relativePath} is within the default limit; remove it from the grandfather list");
        }
    }

    private static string Lines(int count) =>
        string.Join('\n', Enumerable.Repeat("line", count)) + "\n";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "compose.yaml"))
                && File.Exists(Path.Combine(dir.FullName, "apps", "api", "Program.cs")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate the repository root from the test base directory.");
    }

    private static IEnumerable<(string RelativePath, string Contents)> ReadScopedSources(string repoRoot)
    {
        var program = Path.Combine(repoRoot, "apps", "api", "Program.cs");
        yield return ToPair(repoRoot, program);

        foreach (var path in Enumerate(Path.Combine(repoRoot, "src", "VibeChat.Infrastructure")))
        {
            yield return ToPair(repoRoot, path);
        }

        foreach (var path in Enumerate(Path.Combine(repoRoot, "apps", "web", "src")))
        {
            yield return ToPair(repoRoot, path);
        }
    }

    private static IEnumerable<string> Enumerate(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                var name = Path.GetFileName(directory);
                if (name is "bin" or "obj" or "node_modules" or "dist")
                {
                    continue;
                }

                pending.Push(directory);
            }

            foreach (var file in Directory.EnumerateFiles(current))
            {
                yield return file;
            }
        }
    }

    private static (string RelativePath, string Contents) ToPair(string repoRoot, string absolutePath)
    {
        var relative = Path.GetRelativePath(repoRoot, absolutePath);
        return (relative, File.ReadAllText(absolutePath));
    }
}
