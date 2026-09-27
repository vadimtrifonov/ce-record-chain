using System.Text.Json;
using Mutagen.Bethesda.Plugins;
using Xunit;

namespace CreationEngine.RecordChain.Tests;

internal sealed class RecordChainTestDriver(RecordChainFixture fixture)
{
    public RunResult Run(
        FormKey formKey,
        string game = "SkyrimSE",
        string? mo2Root = null,
        string? profile = null) =>
        Run(formKey.ToString(), game, mo2Root, profile);

    public RunResult Run(
        string formKey,
        string game = "SkyrimSE",
        string? mo2Root = null,
        string? profile = null) =>
        Invoke(BuildArgs(
            formKey,
            game,
            GetRoot(game, mo2Root),
            profile ?? GetProfile(game)));

    public RunResult RunBatch(
        string input,
        string source = "-",
        string game = "SkyrimSE",
        string? mo2Root = null,
        string? profile = null) =>
        Invoke(
        [
            "--game", game,
            "--mo2-root", GetRoot(game, mo2Root),
            "--profile", profile ?? GetProfile(game),
            "--formkeys-from", source
        ],
            input);

    public static RunResult Invoke(string[] args, string input = "", string? documentsFolder = null)
    {
        using var stdin = new StringReader(input);
        return Invoke(args, stdin, documentsFolder);
    }

    public static RunResult Invoke(string[] args, TextReader stdin, string? documentsFolder = null)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = Program.Run(args, stdin, stdout, stderr, documentsFolder);
        return new RunResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    public static List<JsonElement> ParseRows(RunResult result)
    {
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Equal(string.Empty, result.Stderr);

        return result.Stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();
    }

    public static string NormalizePath(string path) =>
        Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');

    private string GetRoot(string game, string? mo2Root) =>
        mo2Root ?? (game.Equals("SkyrimVR", StringComparison.OrdinalIgnoreCase)
            ? fixture.VrMo2Root
            : fixture.Mo2Root);

    private string GetProfile(string game) =>
        game.Equals("SkyrimVR", StringComparison.OrdinalIgnoreCase)
            ? fixture.VrProfileName
            : fixture.ProfileName;

    private static string[] BuildArgs(
        string formKey,
        string game,
        string mo2Root,
        string profile) =>
    [
        "--game", game,
        "--mo2-root", mo2Root,
        "--profile", profile,
        formKey
    ];
}

internal sealed record RunResult(int ExitCode, string Stdout, string Stderr);
