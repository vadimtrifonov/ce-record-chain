using CreationEngine.RecordChain.Mo2;
using CreationEngine.RecordChain.Query;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

namespace CreationEngine.RecordChain.Games;

internal interface IGameSupport
{
    string Mo2GameName { get; }
    IReadOnlyList<ActivePlugin> ReadActivePlugins(Mo2Profile profile);
    RecordSource OpenRecords(IReadOnlyList<ActivePlugin> plugins, IReadOnlyList<FormKey> formKeys);
    bool IsPartial(IMajorRecordGetter record);
}
