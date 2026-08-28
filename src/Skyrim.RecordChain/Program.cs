using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    public static int Run(
        string[] args,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr)
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
            var profile = Mo2Profile.Load(options.Game, options.Mo2Root, options.Profile);
            var formKeys = CommandLine.ReadFormKeys(options, stdin);
            var rows = RecordChainQuery.Execute(profile, formKeys);

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
                "skyrim-record-chain must run outside MO2. USVFS hides the physical plugin locations.");
        }
    }
}
