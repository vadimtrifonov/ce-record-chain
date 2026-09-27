using CreationEngine.RecordChain.Mo2;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Order;

namespace CreationEngine.RecordChain.Games;

internal static class PluginList
{
    internal static ILoadOrderListingGetter[] ReadExplicit(string path, GameRelease release)
    {
        var listings = PluginListings.RawLoadOrderListingsFromPath(path, release).ToArray();
        var duplicate = listings.GroupBy(listing => listing.ModKey)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Plugin list contains duplicate plugin '{duplicate.Key.FileName}': {path}");
        }

        return listings;
    }

    internal static IReadOnlyList<ModKey> ReadNames(string path)
    {
        var keys = new List<ModKey>();
        var seen = new HashSet<ModKey>();
        var lineNumber = 0;
        foreach (var raw in File.ReadLines(path))
        {
            lineNumber++;
            var name = raw.Trim();
            if (name.Length == 0 || name.StartsWith('#'))
            {
                continue;
            }

            if (!ModKey.TryFromNameAndExtension(name, out var key) || !seen.Add(key))
            {
                throw new InvalidOperationException(
                    $"Malformed or duplicate plugin in {path}, line {lineNumber}: {raw}");
            }

            keys.Add(key);
        }

        return keys;
    }

    internal static IReadOnlyList<ActivePlugin> ResolveProviders(
        Mo2Profile profile,
        IEnumerable<ModKey> orderedKeys,
        GameRelease release) => orderedKeys.Select((key, index) =>
        {
            var path = profile.TryResolvePluginPath(key) ?? throw new FileNotFoundException(
                $"Active plugin '{key.FileName}' cannot be resolved through the selected MO2 profile.");
            return new ActivePlugin(key, index, path,
                ModHeaderFrame.FromPath(new ModPath(key, path), release));
        }).ToArray();
}

internal sealed record ActivePlugin(
    ModKey ModKey,
    int LoadOrderIndex,
    string PhysicalPath,
    ModHeaderFrame Header);
