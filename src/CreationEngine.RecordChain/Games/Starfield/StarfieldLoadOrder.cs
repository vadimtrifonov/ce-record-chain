using System.Runtime.InteropServices;
using System.Text;
using CreationEngine.RecordChain.Mo2;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;

namespace CreationEngine.RecordChain.Games.Starfield;

internal static class StarfieldLoadOrder
{
    private const string BlueprintPrefix = "BlueprintShips-";

    // MO2 game_bethesda, GameStarfield::primaryPlugins (Free Lanes / Terran Armada).
    // Optional official plugins are included only when installed in the profile.
    private static readonly string[] PrimaryPlugins =
    [
        "Starfield.esm", "Constellation.esm", "OldMars.esm", "ShatteredSpace.esm",
        "SFBGS00D.esm", "SFBGS050.esm", "SFBGS003.esm", "SFBGS004.esm",
        "SFBGS006.esm", "SFBGS007.esm", "SFBGS008.esm", "SFBGS047.esm"
    ];

    internal static IReadOnlyList<ActivePlugin> Read(Mo2Profile profile)
    {
        RejectTestFileLoading(profile);
        var explicitListings = PluginList.ReadExplicit(profile.PluginsPath, GameRelease.Starfield);
        var primary = PrimaryPlugins.Select(name => ModKey.FromFileName(name)).ToList();
        var cccPath = new[]
        {
            Path.Combine(profile.GetMyGamesFolder("Starfield"), "Starfield.ccc"),
            Path.Combine(profile.GameRoot, "Starfield.ccc")
        }.FirstOrDefault(File.Exists);
        if (cccPath is not null)
        {
            primary.AddRange(PluginList.ReadNames(cccPath));
        }

        var baseMaster = ModKey.FromFileName("Starfield.esm");
        var keys = primary
            .Where(key => key == baseMaster || profile.TryResolvePluginPath(key) is not null)
            .Concat(explicitListings.Where(listing => listing.Enabled && !listing.Ghosted)
                .Select(listing => listing.ModKey))
            .Distinct()
            .ToArray();
        var ordinary = PluginList.ResolveProviders(profile,
            keys.Where(key => !HasBlueprintPrefix(key)), GameRelease.Starfield);
        var blueprints = new Dictionary<ModKey, ActivePlugin>();
        foreach (var plugin in ordinary)
        {
            if (IsBlueprint(plugin.Header))
            {
                throw new InvalidOperationException(
                    $"Blueprint plugin '{plugin.ModKey.FileName}' requires the {BlueprintPrefix} prefix and a paired active plugin.");
            }

            var pairedKey = ModKey.FromFileName($"{BlueprintPrefix}{plugin.ModKey.Name}.esm");
            if (blueprints.ContainsKey(pairedKey))
            {
                continue;
            }
            var pairedPath = profile.TryResolvePluginPath(pairedKey);
            if (pairedPath is null)
            {
                continue;
            }

            var header = ModHeaderFrame.FromPath(new ModPath(pairedKey, pairedPath), GameRelease.Starfield);
            if (!IsBlueprint(header))
            {
                throw new InvalidOperationException(
                    $"Paired blueprint '{pairedKey.FileName}' is missing the Blueprint header flag: {pairedPath}");
            }

            blueprints.Add(pairedKey,
                new ActivePlugin(pairedKey, ordinary.Count + blueprints.Count, pairedPath, header));
        }

        foreach (var key in keys.Where(HasBlueprintPrefix))
        {
            if (!blueprints.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"Blueprint plugin '{key.FileName}' has no installed blueprint paired with an active main plugin.");
            }
        }

        // Auto-loaded blueprint masters use file timestamps, not MO2's loadorder.txt.
        var orderedBlueprints = blueprints.Values
            .OrderBy(plugin => File.GetLastWriteTimeUtc(plugin.PhysicalPath))
            .ThenByDescending(plugin => plugin.ModKey.FileName.ToString().ToUpperInvariant(), StringComparer.Ordinal);
        return ordinary.Concat(orderedBlueprints)
            .Select((plugin, index) => plugin with { LoadOrderIndex = index }).ToArray();
    }

    private static bool HasBlueprintPrefix(ModKey key) =>
        key.FileName.ToString().StartsWith(BlueprintPrefix, StringComparison.OrdinalIgnoreCase);

    private static bool IsBlueprint(ModHeaderFrame header) =>
        (header.Flags & StarfieldGame.BlueprintFlag) != 0;

    private static void RejectTestFileLoading(Mo2Profile profile)
    {
        var paths = new[]
        {
            Path.Combine(profile.GameRoot, "Starfield.ini"),
            Path.Combine(profile.GetIniFolder("Starfield"), "StarfieldCustom.ini")
        }.Where(File.Exists).ToArray();

        // Use Windows' game-INI rules, not the parser for MO2's QSettings files.
        // A missing key inherits the previous value; an explicit empty value clears it.
        const string missing = "\u0001";
        var value = new StringBuilder(260);
        for (var index = 1; index <= 10; index++)
        {
            var key = $"sTestFile{index}";
            string? configuredIn = null;
            foreach (var path in paths)
            {
                GetPrivateProfileStringW("General", key, missing, value, (uint)value.Capacity, path);
                if (value.ToString() != missing)
                {
                    configuredIn = value.Length == 0 ? null : path;
                }
            }
            if (configuredIn is not null)
            {
                throw new InvalidOperationException(
                    $"Unsupported Starfield {key} in {configuredIn}; activate plugins through MO2's plugin list instead.");
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetPrivateProfileStringW(
        string section, string key, string defaultValue, StringBuilder value, uint size, string path);
}
