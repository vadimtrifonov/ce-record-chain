using System.Reflection;
using Loqui;
using CreationEngine.RecordChain.Games;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Mapping;
using Mutagen.Bethesda.Plugins.Utility;

namespace CreationEngine.RecordChain.Query;

internal static class RecordTypeIndex
{
    internal static IReadOnlyDictionary<FormKey, Type> Read(
        IReadOnlyList<ActivePlugin> plugins,
        IReadOnlyList<FormKey> formKeys,
        GameRelease release)
    {
        var getterTypes = MajorRecordTypeEnumerator.GetMajorRecordTypesFor(release.ToCategory())
            .SelectMany(registration => registration.GetterType
                .GetCustomAttributes<AssociatedRecordTypesAttribute>()
                .SelectMany(attribute => attribute.Types)
                .Select(type => (type, registration.ClassType)))
            .Distinct()
            .GroupBy(item => item.type)
            .ToDictionary(group => group.Key, group => CommonGetterType(group.Select(item => item.ClassType).ToArray()));
        using var styles = new LoadOrder<IModMasterStyledGetter>(plugins.Select(plugin =>
            new KeyedMasterStyle(plugin.ModKey, plugin.Header.MasterStyle)));
        var remaining = formKeys.ToHashSet();
        var types = new Dictionary<FormKey, Type>();
        var origins = formKeys.Select(key => key.ModKey).ToHashSet();

        // Discover signatures from headers, not an untyped record enumeration:
        // parsing unrelated record types can fail on unsupported component bodies.
        // Prefer originating plugins; injected records are found in the remaining providers.
        foreach (var plugin in plugins.OrderByDescending(plugin => origins.Contains(plugin.ModKey)))
        {
            if (remaining.Count == 0)
            {
                break;
            }

            var header = plugin.Header;
            var masters = SeparatedMasterPackage.Factory(release, plugin.ModKey, header.MasterStyle,
                MasterReferenceCollection.FromModHeader(plugin.ModKey, header), styles);
            using var stream = new MutagenBinaryReadStream(
                new ModPath(plugin.ModKey, plugin.PhysicalPath),
                new ParsingMeta(release, plugin.ModKey, masters));
            var locations = RecordLocator.GetLocations(stream, new RecordInterest(),
                (reader, record) => remaining.Contains(
                    reader.MetaData.MasterReferences.GetFormKey(record.FormID, reference: false)));
            foreach (var location in locations.ListedRecords.Values)
            {
                if (!getterTypes.TryGetValue(location.Record, out var getterType))
                {
                    throw new InvalidOperationException(
                        $"Unsupported record type {location.Record} for {location.FormKey} in {plugin.ModKey.FileName}.");
                }
                types.Add(location.FormKey, getterType);
                remaining.Remove(location.FormKey);
            }
        }

        return types;
    }

    private static Type CommonGetterType(IReadOnlyList<Type> classes)
    {
        // GMST, GLOB and OMOD variants share a signature. Query their registered
        // base getter so the cache returns every definition exactly once.
        var common = classes[0];
        while (classes.Any(type => !common.IsAssignableFrom(type)))
        {
            common = common.BaseType ?? throw new InvalidOperationException("Record variants have no common base type.");
        }
        if (!LoquiRegistration.TryGetRegister(common, out var registration))
        {
            throw new InvalidOperationException($"Record type has no Mutagen registration: {common}");
        }
        return registration.GetterType;
    }
}
