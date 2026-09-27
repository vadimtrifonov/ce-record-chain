using CreationEngine.RecordChain.Games;
using CreationEngine.RecordChain.Mo2;
using CreationEngine.RecordChain.Query;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CreationEngine.RecordChain;

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

    public static int Run(
        string[] args,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        string? documentsFolder = null)
    {
        if (CommandLine.IsHelp(args))
        {
            CommandLine.WriteHelp(stdout);
            return 0;
        }

        try
        {
            EnsureOutsideMo2Usvfs();
            var options = CommandLine.ParseOptions(args);

            // Validate the profile before a batch request can block on standard input.
            var game = GameSupport.Create(options.Game);
            var profile = Mo2Profile.Load(game.Mo2GameName, options.Mo2Root, options.Profile,
                documentsFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            var plugins = game.ReadActivePlugins(profile);
            var formKeys = CommandLine.ReadFormKeys(options, stdin);
            var rows = RecordChainQuery.Execute(game, plugins, formKeys);

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
        catch (CommandLineException exception)
        {
            stderr.WriteLine($"error: {exception.Message}");
            stderr.WriteLine("Run with --help for usage.");
            return UsageError;
        }
        catch (Exception exception)
        {
            stderr.WriteLine($"error: {exception.Message}");
            return OperationalError;
        }
    }

    private static void EnsureOutsideMo2Usvfs()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var injected = false;
        try
        {
            injected = Process.GetCurrentProcess().Modules
                .Cast<ProcessModule>()
                .Any(module => module.ModuleName.StartsWith("usvfs", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            // Module enumeration is a guard, not a prerequisite for normal execution.
        }

        if (injected)
        {
            throw new InvalidOperationException(
                "ce-record-chain must run outside MO2. USVFS hides the physical plugin locations.");
        }
    }
}
