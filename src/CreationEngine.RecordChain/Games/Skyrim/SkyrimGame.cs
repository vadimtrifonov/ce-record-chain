using CreationEngine.RecordChain.Mo2;
using CreationEngine.RecordChain.Query;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace CreationEngine.RecordChain.Games.Skyrim;

internal sealed class SkyrimGame(SkyrimRelease release) : IGameSupport
{
    public string Mo2GameName => release == SkyrimRelease.SkyrimSE
        ? "Skyrim Special Edition"
        : "Skyrim VR";

    public IReadOnlyList<ActivePlugin> ReadActivePlugins(Mo2Profile profile)
    {
        var gameRelease = release == SkyrimRelease.SkyrimSE
            ? GameRelease.SkyrimSE
            : GameRelease.SkyrimVR;
        var explicitListings = PluginList.ReadExplicit(profile.PluginsPath, gameRelease);
        var implicitKeys = Implicits.Listings.Skyrim(release);
        var implicitListings = implicitKeys
            .Select(key => (ILoadOrderListingGetter)new LoadOrderListing(key, enabled: true));
        var enabledListings = explicitListings
            .Where(listing => listing.Enabled && !listing.Ghosted)
            .Where(listing => !implicitKeys.Contains(listing.ModKey));

        var cccPath = Path.Combine(profile.GameRoot, "Skyrim.ccc");
        var creationClubListings = release == SkyrimRelease.SkyrimSE && File.Exists(cccPath)
            ? PluginList.ReadNames(cccPath)
                .Where(key => profile.TryResolvePluginPath(key) is not null)
                .Select(key => (ILoadOrderListingGetter)new LoadOrderListing(key, enabled: true))
            : [];

        var orderedKeys = LoadOrder.OrderListings(
            implicitListings, enabledListings, creationClubListings, listing => listing.ModKey)
            .Select(listing => listing.ModKey);
        return PluginList.ResolveProviders(profile, orderedKeys, gameRelease);
    }

    public RecordSource OpenRecords(IReadOnlyList<ActivePlugin> plugins, IReadOnlyList<FormKey> formKeys) =>
        RecordSource.Open(plugins, formKeys,
            release == SkyrimRelease.SkyrimSE ? GameRelease.SkyrimSE : GameRelease.SkyrimVR,
            plugin =>
            SkyrimMod.CreateFromBinaryOverlay(new ModPath(plugin.ModKey, plugin.PhysicalPath), release));

    // xEdit TES5 definitions assign Partial Form (bit 14) to CELL, DIAL and WRLD.
    public bool IsPartial(IMajorRecordGetter record) =>
        (record.MajorRecordFlagsRaw & 0x0000_4000) != 0 &&
        record is ICellGetter or IDialogTopicGetter or IWorldspaceGetter;
}
