namespace Skyrim.RecordChain;

internal sealed class IniFile
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections;

    private IniFile(Dictionary<string, Dictionary<string, string>> sections)
    {
        _sections = sections;
    }

    internal static IniFile Read(string path)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var section = string.Empty;
        var lines = File.ReadAllLines(path);

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var raw = lines[lineIndex];
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('['))
            {
                if (!line.EndsWith(']') || line.Length < 3)
                {
                    throw new InvalidOperationException(
                        $"Malformed INI section in {path}, line {lineIndex + 1}: {raw}");
                }

                section = line[1..^1].Trim();
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                throw new InvalidOperationException(
                    $"Malformed INI entry in {path}, line {lineIndex + 1}: {raw}");
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (!sections.TryGetValue(section, out var values))
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sections.Add(section, values);
            }

            values[key] = value;
        }

        return new IniFile(sections);
    }

    internal string? Get(string section, string key) =>
        _sections.TryGetValue(section, out var values) &&
        values.TryGetValue(key, out var value)
            ? value
            : null;
}
