using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace Skyrim.RecordChain;

internal static class RecordChainQuery
{
    private const uint PartialFormFlag = 0x0000_4000;

    internal static IReadOnlyList<RecordDefinitionRow> Execute(
        Mo2Profile profile,
        IReadOnlyList<FormKey> formKeys)
    {
        var providers = profile.ActivePlugins.ToDictionary(plugin => plugin.ModKey);
        using var loadOrder = new LoadOrder<ModListing<ISkyrimModGetter>>();

        foreach (var plugin in profile.ActivePlugins)
        {
            var mod = SkyrimMod.CreateFromBinaryOverlay(
                new ModPath(plugin.ModKey, plugin.PhysicalPath),
                GetSkyrimRelease(profile.Game));
            loadOrder.Add(new ModListing<ISkyrimModGetter>(mod));
        }

        ValidateLoadOrder(loadOrder.ListedOrder, providers);

        using var linkCache = loadOrder.ToImmutableLinkCache();
        var rows = new List<RecordDefinitionRow>();

        foreach (var formKey in formKeys)
        {
            rows.AddRange(ResolveChain(linkCache, providers, formKey));
        }

        return rows;
    }

    private static IReadOnlyList<RecordDefinitionRow> ResolveChain(
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        IReadOnlyDictionary<ModKey, ActivePlugin> providers,
        FormKey formKey)
    {
        // The record type is not part of a FormKey. One untyped lookup is required to
        // discover the concrete getter registration before the typed chain lookup.
#pragma warning disable CS0618
        if (!linkCache.TryResolveContext(formKey, out var winningContext, ResolveTarget.Winner))
#pragma warning restore CS0618
        {
            return [];
        }

        var getterType = winningContext.Record.Registration.GetterType;
        var contexts = linkCache
            .ResolveAllContexts(formKey, getterType, ResolveTarget.Origin)
            .ToArray();

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
                FormKey: formKey.ToString(),
                LoadOrderIndex: provider.LoadOrderIndex,
                Plugin: context.ModKey.FileName,
                PluginPath: provider.PhysicalPath,
                Type: record.Registration.Name,
                EditorId: record.EditorID,
                MajorRecordFlagsRaw: rawFlags,
                Deleted: record.IsDeleted,
                Partial: IsPartial(record, rawFlags),
                Origin: index == 0,
                Winner: index == contexts.Length - 1));
        }

        return rows;
    }

    private static void ValidateLoadOrder(
        IEnumerable<ModListing<ISkyrimModGetter>> listings,
        IReadOnlyDictionary<ModKey, ActivePlugin> providers)
    {
        foreach (var listing in listings)
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

    // xEdit 4.1.6 TES5 definitions assign bit 14 to CELL, DIAL, and WRLD.
    private static bool IsPartial(IMajorRecordGetter record, uint rawFlags) =>
        (rawFlags & PartialFormFlag) != 0 &&
        record is ICellGetter or IDialogTopicGetter or IWorldspaceGetter;

    private static SkyrimRelease GetSkyrimRelease(GameKind game) => game switch
    {
        GameKind.SkyrimSE => SkyrimRelease.SkyrimSE,
        GameKind.SkyrimVR => SkyrimRelease.SkyrimVR,
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null)
    };
}

internal sealed record RecordDefinitionRow(
    string FormKey,
    int LoadOrderIndex,
    string Plugin,
    string PluginPath,
    string Type,
    string? EditorId,
    uint MajorRecordFlagsRaw,
    bool Deleted,
    bool Partial,
    bool Origin,
    bool Winner);
