using Mutagen.Bethesda.Plugins;

namespace Skyrim.RecordChain;

internal static class CommandLine
{
    internal static bool IsHelp(IReadOnlyList<string> args) =>
        args.Count == 1 && args[0] is "--help" or "-h";

    internal static CommandOptions ParseOptions(string[] args)
    {
        string? game = null;
        string? mo2Root = null;
        string? profile = null;
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
                case "--mo2-root":
                    mo2Root = ReadOptionValue(args, ref index, argument, mo2Root);
                    break;
                case "--profile":
                    profile = ReadOptionValue(args, ref index, argument, profile);
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

        if (game is null || mo2Root is null || profile is null)
        {
            throw new CommandLineException(
                "Required arguments: --game, --mo2-root, and --profile.");
        }

        if ((formKey is null) == (formKeysSource is null))
        {
            throw new CommandLineException(
                "Specify exactly one FormKey input: one positional FormKey or " +
                "--formkeys-from <path|->.");
        }

        return new CommandOptions(
            ParseGame(game),
            GetMo2Root(mo2Root),
            ValidateProfileName(profile),
            formKey,
            formKeysSource);
    }

    internal static IReadOnlyList<FormKey> ReadFormKeys(
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

    internal static void WriteHelp(TextWriter output)
    {
        output.WriteLine("Usage:");
        output.WriteLine("  skyrim-record-chain --game <SkyrimSE|SkyrimVR> --mo2-root <instance> --profile <name> <FormKey>");
        output.WriteLine("  skyrim-record-chain --game <SkyrimSE|SkyrimVR> --mo2-root <instance> --profile <name> --formkeys-from <path|->");
        output.WriteLine();
        output.WriteLine("Writes compact JSONL rows for each requested FormKey in input order.");
        output.WriteLine("Use --formkeys-from - to read one FormKey per line from standard input.");
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

    private static GameKind ParseGame(string value)
    {
        if (value.Equals("SkyrimSE", StringComparison.OrdinalIgnoreCase))
        {
            return GameKind.SkyrimSE;
        }

        if (value.Equals("SkyrimVR", StringComparison.OrdinalIgnoreCase))
        {
            return GameKind.SkyrimVR;
        }

        throw new CommandLineException(
            $"Unsupported game '{value}'. Expected SkyrimSE or SkyrimVR.");
    }

    private static string GetMo2Root(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"MO2 instance directory does not exist: {fullPath}");
        }

        var iniPath = Path.Combine(fullPath, "ModOrganizer.ini");
        if (!File.Exists(iniPath))
        {
            throw new FileNotFoundException($"MO2 instance has no ModOrganizer.ini: {iniPath}");
        }

        return fullPath;
    }

    private static string ValidateProfileName(string profile)
    {
        profile = profile.Trim();
        if (profile.Length == 0 || profile is "." or ".." ||
            profile.IndexOfAny(['\\', '/']) >= 0)
        {
            throw new CommandLineException($"Invalid MO2 profile name: {profile}");
        }

        return profile;
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
}

internal sealed record CommandOptions(
    GameKind Game,
    string Mo2Root,
    string Profile,
    string? FormKey,
    string? FormKeysSource);

internal sealed class CommandLineException(string message) : Exception(message);
