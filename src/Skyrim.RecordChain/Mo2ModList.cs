namespace Skyrim.RecordChain;

internal static class Mo2ModList
{
    internal static IReadOnlyList<string> ReadEnabled(string modlistPath, string modsFolder)
    {
        var enabled = new List<string>();
        var seenManaged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(modlistPath);

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var raw = lines[lineIndex];
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var marker = line[0];
            if (marker is not ('+' or '-' or '*'))
            {
                throw MalformedEntry(modlistPath, lineIndex + 1, raw, "unknown mod marker");
            }

            var name = line[1..].Trim();
            if (name.Length == 0)
            {
                throw MalformedEntry(modlistPath, lineIndex + 1, raw, "missing mod name");
            }

            if (marker == '*' || name.EndsWith("_separator", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsSafeName(name))
            {
                throw MalformedEntry(modlistPath, lineIndex + 1, raw, "unsafe managed mod name");
            }

            if (!seenManaged.Add(name))
            {
                throw MalformedEntry(modlistPath, lineIndex + 1, raw, "duplicate managed mod");
            }

            if (marker == '-')
            {
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(modsFolder, name));
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException(
                    $"Enabled mod '{name}' from {modlistPath}, line {lineIndex + 1}, has no directory: {path}");
            }

            enabled.Add(path);
        }

        return enabled;
    }

    private static bool IsSafeName(string name) =>
        name is not ("." or "..") &&
        name.IndexOfAny(['\\', '/']) < 0 &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static InvalidOperationException MalformedEntry(
        string path,
        int line,
        string raw,
        string reason) =>
        new($"Malformed profile entry in {path}, line {line}: {reason}: {raw}");
}
