using CreationEngine.RecordChain.Games;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;

namespace CreationEngine.RecordChain.Query;

internal sealed class RecordSource(
    LoadOrder<IModGetter> loadOrder,
    IReadOnlyDictionary<FormKey, Type> recordTypes) : IDisposable
{
    private readonly Dictionary<(ModKey Plugin, Type RecordType), Dictionary<FormKey, IMajorRecordGetter>> _caches = new();
    internal IReadOnlyDictionary<FormKey, Type> RecordTypes { get; } = recordTypes;

    internal IMajorRecordGetter? FindRecord(ModKey plugin, FormKey formKey, Type getterType)
    {
        var key = (plugin, getterType);
        if (!_caches.TryGetValue(key, out var cache))
        {
            // Mutagen 0.54.4's record link cache swallows enumeration errors.
            // Cache typed records directly so unreadable definitions are not lost.
            cache = loadOrder[plugin].EnumerateMajorRecords(getterType)
                .ToDictionary(record => record.FormKey);
            _caches.Add(key, cache);
        }
        return cache.GetValueOrDefault(formKey);
    }

    internal static RecordSource Open(
        IReadOnlyList<ActivePlugin> plugins,
        IReadOnlyList<FormKey> formKeys,
        GameRelease release,
        Func<ActivePlugin, IModGetter> import,
        Func<ModHeaderFrame, ModHeaderFrame, bool>? allowsLaterMaster = null)
    {
        var loadOrder = new LoadOrder<IModGetter>();
        try
        {
            // Validate header dependencies before import: separated-master imports
            // need a complete style lookup even to construct their overlays.
            var providers = plugins.ToDictionary(plugin => plugin.ModKey);
            foreach (var plugin in plugins)
            {
                var masters = MasterReferenceCollection.FromModHeader(plugin.ModKey, plugin.Header);
                foreach (var master in masters.Masters)
                {
                    if (!providers.TryGetValue(master.Master, out var masterProvider))
                    {
                        throw new InvalidOperationException(
                            $"Plugin '{plugin.ModKey.FileName}' requires master " +
                            $"'{master.Master.FileName}', which is not active.");
                    }

                    if (masterProvider.LoadOrderIndex >= plugin.LoadOrderIndex &&
                        !(allowsLaterMaster?.Invoke(masterProvider.Header, plugin.Header) ?? false))
                    {
                        throw new InvalidOperationException(
                            $"Plugin '{plugin.ModKey.FileName}' requires master " +
                            $"'{master.Master.FileName}' to load before it.");
                    }
                }
            }

            foreach (var plugin in plugins)
            {
                loadOrder.Add(import(plugin));
            }

            var types = RecordTypeIndex.Read(plugins, formKeys, release);
            return new RecordSource(loadOrder, types);
        }
        catch
        {
            loadOrder.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _caches.Clear();
        loadOrder.Dispose();
    }
}
