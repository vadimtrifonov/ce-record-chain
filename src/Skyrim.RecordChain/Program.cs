using System.Text.Json;
using System.Text.Json.Serialization;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace Skyrim.RecordChain;

public static class Program
{
    private const int OperationalError = 1;
    private const int UsageError = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false
    };

    public static int Main(string[] args) => Run(args, Console.In, Console.Out, Console.Error);

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr) =>
        Run(args, TextReader.Null, stdout, stderr);

    public static int Run(
        string[] args,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            WriteHelp(stdout);
            return 0;
        }

        try
        {
            var options = ParseArguments(args);
            var rows = Query(options, stdin);

            // Materialize every line before writing so failures never emit a partial chain or batch.
            var lines = rows
                .Select(row => JsonSerializer.Serialize(row, JsonOptions))
                .ToArray();

            foreach (var line in lines)
            {
                stdout.WriteLine(line);
            }

            return 0;
        }
        catch (CommandLineException ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            stderr.WriteLine("Run with --help for usage.");
            return UsageError;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return OperationalError;
        }
    }

    private static IReadOnlyList<RecordDefinitionRow> Query(CommandOptions options, TextReader stdin)
    {
        var game = ParseGame(options.Game);
        var dataFolder = Path.GetFullPath(options.DataFolder);
        var loadOrderPath = Path.GetFullPath(options.LoadOrderPath);

        if (!Directory.Exists(dataFolder))
        {
            throw new DirectoryNotFoundException($"Data folder does not exist: {dataFolder}");
        }

        if (!File.Exists(loadOrderPath))
        {
            throw new FileNotFoundException($"Load-order file does not exist: {loadOrderPath}");
        }

        var formKeys = ReadFormKeys(options, stdin);
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

        foreach (var formKey in formKeys)
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

    private static IReadOnlyList<FormKey> ReadFormKeys(
        CommandOptions options,
        TextReader stdin)
    {
        if (options.FormKey is not null)
        {
            return [ParseFormKey(options.FormKey)];
        }

        var source = options.FormKeysSource
                     ?? throw new InvalidOperationException("No FormKey input was configured.");
        if (source == "-")
        {
            return ReadFormKeys(stdin);
        }

        var inputPath = Path.GetFullPath(source);
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"FormKey input file does not exist: {inputPath}");
        }

        using var reader = File.OpenText(inputPath);
        return ReadFormKeys(reader);
    }

    private static IReadOnlyList<FormKey> ReadFormKeys(TextReader reader)
    {
        var formKeys = new List<FormKey>();
        var seen = new HashSet<FormKey>();
        var lineNumber = 0;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            var value = line.Trim();
            if (value.Length == 0)
            {
                throw new CommandLineException($"Empty FormKey on input line {lineNumber}.");
            }

            var formKey = ParseFormKey(value, lineNumber);
            if (!seen.Add(formKey))
            {
                throw new CommandLineException(
                    $"Duplicate FormKey on input line {lineNumber}: {formKey}");
            }

            formKeys.Add(formKey);
        }

        if (formKeys.Count == 0)
        {
            throw new CommandLineException("FormKey input is empty.");
        }

        return formKeys;
    }

    private static FormKey ParseFormKey(string value, int? lineNumber = null)
    {
        if (!FormKey.TryFactory(value, out var formKey) || formKey.IsNull)
        {
            var location = lineNumber is { } number ? $" on input line {number}" : string.Empty;
            throw new CommandLineException($"Invalid FormKey{location}: {value}");
        }

        return formKey;
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

    private static GameChoice ParseGame(string value)
    {
        if (value.Equals("SkyrimSE", StringComparison.OrdinalIgnoreCase))
        {
            return new GameChoice(GameRelease.SkyrimSE, SkyrimRelease.SkyrimSE);
        }

        if (value.Equals("SkyrimVR", StringComparison.OrdinalIgnoreCase))
        {
            return new GameChoice(GameRelease.SkyrimVR, SkyrimRelease.SkyrimVR);
        }

        throw new CommandLineException(
            $"Unsupported game '{value}'. Expected SkyrimSE or SkyrimVR.");
    }

    private static CommandOptions ParseArguments(string[] args)
    {
        string? game = null;
        string? dataFolder = null;
        string? loadOrderPath = null;
        string? formKey = null;
        string? formKeysSource = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--game":
                    game = ReadOptionValue(args, ref index, argument, game);
                    break;
                case "--data-folder":
                    dataFolder = ReadOptionValue(args, ref index, argument, dataFolder);
                    break;
                case "--load-order":
                    loadOrderPath = ReadOptionValue(args, ref index, argument, loadOrderPath);
                    break;
                case "--formkeys-from":
                    formKeysSource = ReadOptionValue(args, ref index, argument, formKeysSource);
                    break;
                default:
                    if (argument.StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new CommandLineException($"Unknown option: {argument}");
                    }

                    if (formKey is not null)
                    {
                        throw new CommandLineException("Expected exactly one positional FormKey.");
                    }

                    formKey = argument;
                    break;
            }
        }

        if (game is null || dataFolder is null || loadOrderPath is null)
        {
            throw new CommandLineException(
                "Required arguments: --game, --data-folder, and --load-order.");
        }

        if ((formKey is null) == (formKeysSource is null))
        {
            throw new CommandLineException(
                "Specify exactly one FormKey input: one positional FormKey or " +
                "--formkeys-from <path|->.");
        }

        return new CommandOptions(game, dataFolder, loadOrderPath, formKey, formKeysSource);
    }

    private static string ReadOptionValue(
        IReadOnlyList<string> args,
        ref int index,
        string option,
        string? existingValue)
    {
        if (existingValue is not null)
        {
            throw new CommandLineException($"Option specified more than once: {option}");
        }

        if (++index >= args.Count)
        {
            throw new CommandLineException($"Missing value for option: {option}");
        }

        return args[index];
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).Replace('\\', '/');

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine("Usage:");
        output.WriteLine("  skyrim-record-chain --game <SkyrimSE|SkyrimVR> --data-folder <Data> --load-order <plugins.txt> <FormKey>");
        output.WriteLine("  skyrim-record-chain --game <SkyrimSE|SkyrimVR> --data-folder <Data> --load-order <plugins.txt> --formkeys-from <path|->");
        output.WriteLine();
        output.WriteLine("Writes compact JSONL rows for each requested FormKey in input order.");
        output.WriteLine("Use --formkeys-from - to read one FormKey per line from standard input.");
    }

    private sealed record CommandOptions(
        string Game,
        string DataFolder,
        string LoadOrderPath,
        string? FormKey,
        string? FormKeysSource);

    private sealed record GameChoice(
        GameRelease GameRelease,
        SkyrimRelease SkyrimRelease);

    private sealed record Provider(
        int LoadOrderIndex,
        string PluginPath);

    private sealed record RecordDefinitionRow(
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

    private sealed class CommandLineException(string message) : Exception(message);
}
