using System.Text.Json;
using Mutagen.Bethesda.Plugins;
using Xunit;

namespace Skyrim.RecordChain.Tests;

internal sealed class RecordChainTestDriver(RecordChainFixture fixture)
{
    public RunResult Run(
        FormKey formKey,
        string game = "SkyrimSE",
        string? dataFolder = null,
        string? loadOrderPath = null) =>
        Run(formKey.ToString(), game, dataFolder, loadOrderPath);

    public RunResult Run(
        string formKey,
        string game = "SkyrimSE",
        string? dataFolder = null,
        string? loadOrderPath = null) =>
        Invoke(BuildArgs(
            formKey,
            game,
            dataFolder ?? fixture.DataFolder,
            loadOrderPath ?? fixture.LoadOrderPath));

    public RunResult RunBatch(
        string input,
        string source = "-",
        string game = "SkyrimSE",
        string? dataFolder = null,
        string? loadOrderPath = null) =>
        Invoke(
        [
            "--game", game,
            "--data-folder", dataFolder ?? fixture.DataFolder,
            "--load-order", loadOrderPath ?? fixture.LoadOrderPath,
            "--formkeys-from", source
        ],
            input);

    public static RunResult Invoke(string[] args, string input = "")
    {
        using var stdin = new StringReader(input);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = Program.Run(args, stdin, stdout, stderr);
        return new RunResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    public static List<JsonElement> ParseRows(RunResult result)
    {
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        return result.Stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();
    }

    public static string NormalizePath(string path) =>
        Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');

    private static string[] BuildArgs(
        string formKey,
        string game,
        string dataFolder,
        string loadOrderPath) =>
    [
        "--game", game,
        "--data-folder", dataFolder,
        "--load-order", loadOrderPath,
        formKey
    ];
}

internal sealed record RunResult(int ExitCode, string Stdout, string Stderr);
