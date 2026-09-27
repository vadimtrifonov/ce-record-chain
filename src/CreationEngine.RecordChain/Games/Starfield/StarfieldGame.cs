using CreationEngine.RecordChain.Mo2;
using CreationEngine.RecordChain.Query;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;

namespace CreationEngine.RecordChain.Games.Starfield;

internal sealed class StarfieldGame : IGameSupport
{
    // The Blueprint flag is not named in Mutagen 0.54.4's header enum.
    internal const int BlueprintFlag = 0x0000_0800;

    public string Mo2GameName => "Starfield";

    public IReadOnlyList<ActivePlugin> ReadActivePlugins(Mo2Profile profile) =>
        StarfieldLoadOrder.Read(profile);

    public RecordSource OpenRecords(IReadOnlyList<ActivePlugin> plugins, IReadOnlyList<FormKey> formKeys)
    {
        var styles = plugins.Select(plugin =>
            new KeyedMasterStyle(plugin.ModKey, plugin.Header.MasterStyle)).ToArray();
        return RecordSource.Open(plugins, formKeys, GameRelease.Starfield,
            plugin => StarfieldMod.Create(StarfieldRelease.Starfield)
                .FromPath(new ModPath(plugin.ModKey, plugin.PhysicalPath))
                .WithKnownMasters(styles)
                .Construct(),
            // Blueprint and ordinary plugins occupy separate load-order sections.
            // An ordinary plugin may reference a blueprint that loads later.
            (master, dependent) => (master.Flags & BlueprintFlag) != 0 && (dependent.Flags & BlueprintFlag) == 0);
    }

    // xEdit SF1 also defines Partial Form on QUST, unlike TES5.
    public bool IsPartial(IMajorRecordGetter record) =>
        (record.MajorRecordFlagsRaw & 0x0000_4000) != 0 &&
        record is ICellGetter or IDialogTopicGetter or IWorldspaceGetter or IQuestGetter;
}
