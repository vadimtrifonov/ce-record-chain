using CreationEngine.RecordChain.Games;
using Mutagen.Bethesda.Plugins;

namespace CreationEngine.RecordChain.Query;

internal static class RecordChainQuery
{
    internal static IReadOnlyList<RecordDefinitionRow> Execute(
        IGameSupport game,
        IReadOnlyList<ActivePlugin> plugins,
        IReadOnlyList<FormKey> formKeys)
    {
        using var records = game.OpenRecords(plugins, formKeys);
        var rows = new List<RecordDefinitionRow>();

        foreach (var formKey in formKeys)
        {
            if (records.RecordTypes.TryGetValue(formKey, out var getterType))
            {
                try
                {
                    rows.AddRange(ResolveChain(records, getterType, game, plugins, formKey));
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException($"Failed to query {formKey}: {exception.Message}", exception);
                }
            }
        }

        return rows;
    }

    private static IReadOnlyList<RecordDefinitionRow> ResolveChain(
        RecordSource records,
        Type getterType,
        IGameSupport game,
        IReadOnlyList<ActivePlugin> plugins,
        FormKey formKey)
    {
        var rows = new List<RecordDefinitionRow>();
        foreach (var provider in plugins)
        {
            // The FormKey owner may load later. Ordinary per-plugin lookups also
            // include foreign-owned overrides and injected records.
            var record = records.FindRecord(provider.ModKey, formKey, getterType);
            if (record is null)
            {
                continue;
            }
            rows.Add(new RecordDefinitionRow(
                FormKey: formKey.ToString(),
                LoadOrderIndex: provider.LoadOrderIndex,
                Plugin: provider.ModKey.FileName,
                PluginPath: provider.PhysicalPath,
                Type: record.Registration.Name,
                EditorId: record.EditorID,
                MajorRecordFlagsRaw: unchecked((uint)record.MajorRecordFlagsRaw),
                Deleted: record.IsDeleted,
                Partial: game.IsPartial(record),
                Origin: rows.Count == 0,
                Winner: false));
        }

        if (rows.Count > 0)
        {
            rows[^1] = rows[^1] with { Winner = true };
        }
        return rows;
    }
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
