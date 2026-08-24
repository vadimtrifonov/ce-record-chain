using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace Skyrim.RecordChain;

internal static class RecordChainQuery
{
    internal static IReadOnlyList<RecordDefinitionRow> Execute(RecordChainRequest request)
    {
        var game = GetGame(request.Game);
        var dataFolder = request.DataFolder;
        var loadOrderPath = request.LoadOrderPath;
        var activeListings = ReadActiveLoadOrder(game, dataFolder, loadOrderPath);
        ValidatePluginFiles(activeListings, dataFolder);

        using var loadOrder = LoadOrder.Import<ISkyrimModGetter>(
            dataFolder,
            activeListings,
            game.GameRelease);

        var providers = BuildProviderMap(activeListings, dataFolder);
        ValidateLoadOrder(loadOrder, providers);

        using var linkCache = loadOrder.ToImmutableLinkCache();
        var rows = new List<RecordDefinitionRow>();

        foreach (var formKey in request.FormKeys)
        {
            rows.AddRange(ResolveChain(linkCache, providers, formKey));
        }

        return rows;
    }

    private static IReadOnlyList<RecordDefinitionRow> ResolveChain(
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        IReadOnlyDictionary<ModKey, Provider> providers,
        FormKey formKey)
    {
        // The record type is not part of a FormKey. One untyped lookup is required to
        // discover the concrete getter registration before the typed chain lookup.
#pragma warning disable CS0618
        if (!linkCache.TryResolveContext(formKey, out var winningContext, ResolveTarget.Winner))
#pragma warning restore CS0618
        {
            throw new InvalidOperationException($"Record does not exist in the active load order: {formKey}");
        }

        var getterType = winningContext.Record.Registration.GetterType;
        var contexts = linkCache
            .ResolveAllContexts(formKey, getterType, ResolveTarget.Origin)
            .ToArray();

        if (contexts.Length == 0)
        {
            throw new InvalidOperationException($"Record does not exist in the active load order: {formKey}");
        }

        var rows = new List<RecordDefinitionRow>(contexts.Length);
        var previousIndex = -1;

        for (var index = 0; index < contexts.Length; index++)
        {
            var context = contexts[index];
            if (!providers.TryGetValue(context.ModKey, out var provider))
            {
                throw new InvalidOperationException(
                    $"Mutagen resolved provider '{context.ModKey}' outside the active load order.");
            }

            if (provider.LoadOrderIndex <= previousIndex)
            {
                throw new InvalidOperationException(
                    $"Mutagen returned a non-increasing definition chain for {formKey}.");
            }

            previousIndex = provider.LoadOrderIndex;
            var record = context.Record;
            var rawFlags = unchecked((uint)record.MajorRecordFlagsRaw);

            rows.Add(new RecordDefinitionRow(
                FormKey: record.FormKey.ToString(),
                LoadOrderIndex: provider.LoadOrderIndex,
                Plugin: context.ModKey.FileName,
                PluginPath: provider.PluginPath,
                Type: record.Registration.Name,
                EditorId: record.EditorID,
                MajorFlagsRaw: rawFlags,
                MajorFlags: GetMajorFlagNames(rawFlags),
                Deleted: record.IsDeleted,
                Origin: index == 0,
                Winner: index == contexts.Length - 1));
        }

        return rows;
    }

    private static ILoadOrderListingGetter[] ReadActiveLoadOrder(
        GameChoice game,
        string dataFolder,
        string loadOrderPath)
    {
        var explicitListings = PluginListings
            .RawLoadOrderListingsFromPath(loadOrderPath, game.GameRelease)
            .ToArray();

        var duplicate = explicitListings
            .GroupBy(listing => listing.ModKey)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Load-order file contains duplicate plugin '{duplicate.Key.FileName}'.");
        }

        var implicitModKeys = Implicits.Listings.Skyrim(game.SkyrimRelease);
        var implicitListings = implicitModKeys
            .Select<ModKey, ILoadOrderListingGetter>(modKey => new LoadOrderListing(modKey, enabled: true));
        var enabledExplicitListings = explicitListings
            .Where(listing => listing.Enabled && !listing.Ghosted)
            .Where(listing => !implicitModKeys.Contains(listing.ModKey));
        var creationClubListings = ReadCreationClubListings(dataFolder);

        return LoadOrder.OrderListings(
                implicitListings,
                enabledExplicitListings,
                creationClubListings,
                listing => listing.ModKey)
            .ToArray();
    }

    private static ILoadOrderListingGetter[] ReadCreationClubListings(string dataFolder)
    {
        var listingsPath = CreationClubListings.GetListingsPath(GameCategory.Skyrim, dataFolder);
        if (listingsPath is null || !File.Exists(listingsPath.Value.Path))
        {
            return Array.Empty<ILoadOrderListingGetter>();
        }

        return CreationClubListings
            .LoadOrderListingsFromPath(listingsPath.Value, dataFolder)
            .ToArray();
    }

    private static void ValidatePluginFiles(
        IEnumerable<ILoadOrderListingGetter> listings,
        string dataFolder)
    {
        foreach (var listing in listings)
        {
            var path = Path.Combine(dataFolder, listing.ModKey.FileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Active plugin '{listing.ModKey.FileName}' is missing from Data folder: {path}");
            }
        }
    }

    private static Dictionary<ModKey, Provider> BuildProviderMap(
        IReadOnlyList<ILoadOrderListingGetter> listings,
        string dataFolder)
    {
        var providers = new Dictionary<ModKey, Provider>(listings.Count);

        for (var index = 0; index < listings.Count; index++)
        {
            var modKey = listings[index].ModKey;
            providers.Add(modKey, new Provider(
                index,
                NormalizePath(Path.Combine(dataFolder, modKey.FileName))));
        }

        return providers;
    }

    private static void ValidateLoadOrder(
        ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> loadOrder,
        IReadOnlyDictionary<ModKey, Provider> providers)
    {
        foreach (var listing in loadOrder.ListedOrder)
        {
            var provider = providers[listing.ModKey];
            var mod = listing.Mod
                      ?? throw new InvalidOperationException(
                          $"Active plugin could not be loaded: {listing.ModKey.FileName}");

            foreach (var masterReference in mod.MasterReferences)
            {
                if (!providers.TryGetValue(masterReference.Master, out var masterProvider))
                {
                    throw new InvalidOperationException(
                        $"Plugin '{listing.ModKey.FileName}' requires master " +
                        $"'{masterReference.Master.FileName}', which is not active.");
                }

                if (masterProvider.LoadOrderIndex >= provider.LoadOrderIndex)
                {
                    throw new InvalidOperationException(
                        $"Plugin '{listing.ModKey.FileName}' requires master " +
                        $"'{masterReference.Master.FileName}' to load before it.");
                }
            }
        }
    }

    private static IReadOnlyList<string> GetMajorFlagNames(uint rawFlags)
    {
        if (rawFlags == 0)
        {
            return Array.Empty<string>();
        }

        return Enum.GetValues<SkyrimMajorRecord.SkyrimMajorRecordFlag>()
            .Select(flag => new
            {
                Flag = flag,
                Value = unchecked((uint)(int)flag)
            })
            .Where(item => item.Value != 0 && (rawFlags & item.Value) == item.Value)
            .OrderBy(item => item.Value)
            .Select(item => item.Flag.ToString())
            .ToArray();
    }

    private static GameChoice GetGame(GameKind game) => game switch
    {
        GameKind.SkyrimSE => new GameChoice(GameRelease.SkyrimSE, SkyrimRelease.SkyrimSE),
        GameKind.SkyrimVR => new GameChoice(GameRelease.SkyrimVR, SkyrimRelease.SkyrimVR),
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null)
    };

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).Replace('\\', '/');

    private sealed record GameChoice(
        GameRelease GameRelease,
        SkyrimRelease SkyrimRelease);

    private sealed record Provider(
        int LoadOrderIndex,
        string PluginPath);
}

internal enum GameKind
{
    SkyrimSE,
    SkyrimVR
}

internal sealed record RecordChainRequest(
    GameKind Game,
    string DataFolder,
    string LoadOrderPath,
    IReadOnlyList<FormKey> FormKeys);

internal sealed record RecordDefinitionRow(
    string FormKey,
    int LoadOrderIndex,
    string Plugin,
    string PluginPath,
    string Type,
    string? EditorId,
    uint MajorFlagsRaw,
    IReadOnlyList<string> MajorFlags,
    bool Deleted,
    bool Origin,
    bool Winner);
