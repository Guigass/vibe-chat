using System.Text.RegularExpressions;

namespace VibeChat.ArchitectureTests;

internal sealed record ApiEndpointMap(string Source, string Method, string Path, string Registration);

internal static class ApiEndpointInventory
{
    // Include reads as boundaries: a GET permission must never satisfy a preceding POST.
    private static readonly Regex MapPattern = new(
        "\\bv1\\.Map(?<verb>Get|Post|Put|Patch|Delete)\\s*\\(\\s*\"(?<path>[^\"]+)\"",
        RegexOptions.Compiled);

    private static readonly Regex NonCode = new(
        "//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/|@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'",
        RegexOptions.Compiled);

    internal static ApiEndpointMap[] Read(IEnumerable<KeyValuePair<string, string>> sources)
    {
        var maps = new List<ApiEndpointMap>();
        foreach (var (file, source) in sources)
        {
            // Blank comments and literals without shifting offsets; keep the original route text.
            var code = NonCode.Replace(source, match => new string(' ', match.Length));
            var matches = MapPattern.Matches(source);
            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                if (code[match.Index] != 'v')
                {
                    continue;
                }
                var end = i + 1 < matches.Count ? matches[i + 1].Index : source.Length;
                maps.Add(new(file, match.Groups["verb"].Value.ToUpperInvariant(),
                    match.Groups["path"].Value, ReadRegistrationTail(code, match.Index, end)));
            }
        }

        if (maps.Count == 0)
        {
            throw new InvalidOperationException("No v1 endpoint maps found; authorization gates cannot pass on an empty inventory.");
        }

        return maps.ToArray();
    }

    private static string ReadRegistrationTail(string code, int start, int end)
    {
        var depth = 0;
        int? tailStart = null;
        for (var i = start; i < end; i++)
        {
            if (code[i] == '(') depth++;
            if (code[i] == ')')
            {
                depth--;
                if (depth == 0) tailStart ??= i + 1;
            }

            if (code[i] == ';' && depth == 0)
            {
                return tailStart is { } tail ? code[tail..i] : "";
            }
        }

        throw new InvalidOperationException("Could not delimit an endpoint registration; refusing to skip its authorization gate.");
    }

    internal static string[] MissingPermissionDeclarations(IEnumerable<ApiEndpointMap> maps) => maps
        .Where(map => map.Method is "POST" or "PUT" or "PATCH" or "DELETE")
        .Where(map => !Regex.IsMatch(map.Registration, @"\.(?:RequirePermission|AllowPermissionGateExempt)\s*\("))
        .Select(map => $"{map.Source}: {map.Method} {map.Path}")
        .ToArray();

    internal static string[] MissingMatrixEntries(IEnumerable<ApiEndpointMap> maps, string matrix)
    {
        var entries = Regex.Matches(matrix, @"(?m)^\|\s*(GET|POST|PUT|PATCH|DELETE)\s*\|\s*`([^`]+)`\s*\|")
            .Select(match => (Method: match.Groups[1].Value, Path: match.Groups[2].Value))
            .ToHashSet();
        return maps.Where(map => !entries.Contains((map.Method, map.Path)))
            .Select(map => $"{map.Source}: {map.Method} {map.Path}").ToArray();
    }
}
