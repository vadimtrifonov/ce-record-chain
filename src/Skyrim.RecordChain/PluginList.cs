using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace Skyrim.RecordChain;

internal static class PluginList
{
    internal static IReadOnlyList<ActivePlugin> ReadActive(
        Mo2Profile profile,
        string pluginsPath,
        string creationClubPath)
    {
        var game = GetGame(profile.Game);
        var explicitListings = PluginListings
            .RawLoadOrderListingsFromPath(pluginsPath, game.GameRelease)
            .ToArray();

        var duplicate = explicitListings
            .GroupBy(listing => listing.ModKey)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Plugin list contains duplicate plugin '{duplicate.Key.FileName}'.");
        }

        var implicitModKeys = Implicits.Listings.Skyrim(game.SkyrimRelease);
        var implicitListings = implicitModKeys
            .Select<ModKey, ILoadOrderListingGetter>(modKey =>
                new LoadOrderListing(modKey, enabled: true))
            .ToArray();
        var enabledExplicitListings = explicitListings
            .Where(listing => listing.Enabled && !listing.Ghosted)
            .Where(listing => !implicitModKeys.Contains(listing.ModKey))
            .ToArray();

        var resolvedPaths = new Dictionary<ModKey, string>();
        var creationClubListings = profile.Game == GameKind.SkyrimSE
            ? ReadCreationClubListings(profile, creationClubPath, resolvedPaths)
            : Array.Empty<ILoadOrderListingGetter>();
        var orderedListings = LoadOrder.OrderListings(
                implicitListings,
                enabledExplicitListings,
                creationClubListings,
                listing => listing.ModKey)
            .ToArray();

        var active = new List<ActivePlugin>(orderedListings.Length);
        for (var index = 0; index < orderedListings.Length; index++)
        {
            var modKey = orderedListings[index].ModKey;
            if (!resolvedPaths.TryGetValue(modKey, out var physicalPath))
            {
                physicalPath = profile.TryResolvePluginPath(modKey);
            }

            if (physicalPath is null)
            {
                throw new FileNotFoundException(
                    $"Active plugin '{modKey.FileName}' cannot be resolved through the selected MO2 profile.");
            }

            active.Add(new ActivePlugin(modKey, index, physicalPath));
        }

        return active;
    }

    private static ILoadOrderListingGetter[] ReadCreationClubListings(
        Mo2Profile profile,
        string path,
        IDictionary<ModKey, string> resolvedPaths)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var listings = new List<ILoadOrderListingGetter>();
        var seen = new HashSet<ModKey>();
        var lines = File.ReadAllLines(path);

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var raw = lines[lineIndex];
            var name = raw.Trim();
            if (name.Length == 0 || name.StartsWith('#'))
            {
                continue;
            }

            if (!ModKey.TryFromNameAndExtension(name, out var modKey))
            {
                throw MalformedEntry(path, lineIndex + 1, raw, "invalid Creation Club plugin");
            }

            if (!seen.Add(modKey))
            {
                throw MalformedEntry(path, lineIndex + 1, raw, "duplicate Creation Club plugin");
            }

            var physicalPath = profile.TryResolvePluginPath(modKey);
            if (physicalPath is null)
            {
                continue;
            }

            resolvedPaths[modKey] = physicalPath;
            listings.Add(new LoadOrderListing(modKey, enabled: true));
        }

        return listings.ToArray();
    }

    private static GameChoice GetGame(GameKind game) => game switch
    {
        GameKind.SkyrimSE => new GameChoice(GameRelease.SkyrimSE, SkyrimRelease.SkyrimSE),
        GameKind.SkyrimVR => new GameChoice(GameRelease.SkyrimVR, SkyrimRelease.SkyrimVR),
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null)
    };

    private static InvalidOperationException MalformedEntry(
        string path,
        int line,
        string raw,
        string reason) =>
        new($"Malformed profile entry in {path}, line {line}: {reason}: {raw}");

    private sealed record GameChoice(
        GameRelease GameRelease,
        SkyrimRelease SkyrimRelease);
}
