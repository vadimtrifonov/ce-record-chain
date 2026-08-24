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

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr) =>
        Run(args, TextReader.Null, stdout, stderr);

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
            var request = CommandLine.Parse(args, stdin);
            var rows = RecordChainQuery.Execute(request);

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
}
