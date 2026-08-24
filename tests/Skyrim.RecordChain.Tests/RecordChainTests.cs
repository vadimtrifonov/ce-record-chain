using System.Text.Json;
using Mutagen.Bethesda.Plugins;
using Xunit;

namespace Skyrim.RecordChain.Tests;

public sealed class RecordChainTests(RecordChainFixture fixture) : IClassFixture<RecordChainFixture>
{
    [Fact]
    public void EmitsOriginToWinnerGoldenJsonl()
    {
        var result = Run(fixture.MultipleOverrides);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var goldenPath = Path.Combine(AppContext.BaseDirectory, "Golden", "multiple-overrides.jsonl");
        var expected = File.ReadAllText(goldenPath)
            .Replace("<DATA>", NormalizePath(fixture.DataFolder), StringComparison.Ordinal)
            .ReplaceLineEndings("\n");

        Assert.Equal(expected, result.Stdout.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void PreservesDeletedWinnerAndFlags()
    {
        var result = Run(fixture.DeletedWinner);
        var rows = ParseRows(result);

        Assert.Equal(2, rows.Count);
        var winner = rows[^1];
        Assert.Equal("Late.esp", winner.GetProperty("plugin").GetString());
        Assert.True(winner.GetProperty("deleted").GetBoolean());
        Assert.True(winner.GetProperty("winner").GetBoolean());
        Assert.Contains("Deleted", winner.GetProperty("majorFlags").EnumerateArray().Select(x => x.GetString()));
        Assert.NotEqual(0U, winner.GetProperty("majorFlagsRaw").GetUInt32());
    }

    [Fact]
    public void PreservesPartialEmptyLookingDefinition()
    {
        var result = Run(fixture.PartialDefinition);
        var rows = ParseRows(result);

        Assert.Equal(2, rows.Count);
        var winner = rows[^1];
        Assert.Equal("Late.esp", winner.GetProperty("plugin").GetString());
        Assert.Equal(0x0000_4000U, winner.GetProperty("majorFlagsRaw").GetUInt32());
        Assert.Equal(JsonValueKind.Null, winner.GetProperty("editorId").ValueKind);
    }

    [Theory]
    [MemberData(nameof(EmbeddedRecords))]
    public void ResolvesParentAndEmbeddedRecord(FormKey formKey, string expectedType)
    {
        var result = Run(formKey);
        var rows = ParseRows(result);

        Assert.Equal(new[] { "Skyrim.esm", "CellPatch.esp" },
            rows.Select(x => x.GetProperty("plugin").GetString()));
        Assert.All(rows, row => Assert.Equal(expectedType, row.GetProperty("type").GetString()));
    }

    public static TheoryData<FormKey, string> EmbeddedRecords => new()
    {
        { FormKey.Factory("000900:Skyrim.esm"), "Cell" },
        { FormKey.Factory("000901:Skyrim.esm"), "PlacedObject" }
    };

    [Fact]
    public void IncludesCreationClubListingsBeforeExplicitPlugins()
    {
        var result = Run(fixture.CreationClubRecord);
        var rows = ParseRows(result);

        Assert.Single(rows);
        Assert.Equal("ccFixture.esl", rows[0].GetProperty("plugin").GetString());
        Assert.Equal(5, rows[0].GetProperty("loadOrderIndex").GetInt32());
    }

    [Fact]
    public void ResolvesLightPluginRecord()
    {
        var result = Run(fixture.LightRecord);
        var rows = ParseRows(result);

        Assert.Equal(new[] { "Light.esl", "LightPatch.esp" },
            rows.Select(x => x.GetProperty("plugin").GetString()));
        Assert.Equal("000800:Light.esl", rows[0].GetProperty("formKey").GetString());
    }

    [Fact]
    public void MarksActualFirstProviderAsOriginForInjectedRecord()
    {
        var result = Run(fixture.Injected);
        var rows = ParseRows(result);

        Assert.Equal(new[] { "Injector.esp", "InjectionPatch.esp" },
            rows.Select(x => x.GetProperty("plugin").GetString()));
        Assert.True(rows[0].GetProperty("origin").GetBoolean());
        Assert.False(rows[1].GetProperty("origin").GetBoolean());
        Assert.Equal("000A00:Skyrim.esm", rows[0].GetProperty("formKey").GetString());
    }

    [Fact]
    public void ExcludesInactivePlugin()
    {
        var result = Run(fixture.InactiveOverride);
        var rows = ParseRows(result);

        Assert.Single(rows);
        Assert.Equal("Skyrim.esm", rows[0].GetProperty("plugin").GetString());
        Assert.True(rows[0].GetProperty("winner").GetBoolean());
    }

    [Fact]
    public void ResolvesNewRecordWithoutPriorDefinition()
    {
        var result = Run(fixture.NewRecord);
        var rows = ParseRows(result);

        Assert.Single(rows);
        Assert.Equal("NewRecords.esp", rows[0].GetProperty("plugin").GetString());
        Assert.True(rows[0].GetProperty("origin").GetBoolean());
        Assert.True(rows[0].GetProperty("winner").GetBoolean());
    }

    [Fact]
    public void SupportsSkyrimVr()
    {
        var result = Run(
            fixture.VrRecord,
            game: "SkyrimVR",
            dataFolder: fixture.VrDataFolder,
            loadOrderPath: fixture.VrLoadOrderPath);
        var rows = ParseRows(result);

        Assert.Single(rows);
        Assert.Equal("VrNpc", rows[0].GetProperty("editorId").GetString());
        Assert.Equal(0, rows[0].GetProperty("loadOrderIndex").GetInt32());
    }

    [Fact]
    public void FailsWithoutOutputWhenRecordDoesNotExist()
    {
        var result = Run(FormKey.Factory("00FFFF:Skyrim.esm"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("does not exist", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailsWithoutOutputWhenActivePluginIsMissing()
    {
        var result = Run(
            fixture.MultipleOverrides,
            loadOrderPath: fixture.MissingPluginLoadOrderPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("MissingActive.esp", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailsWithoutOutputWhenRequiredMasterIsMissing()
    {
        var result = Run(
            fixture.MultipleOverrides,
            loadOrderPath: fixture.MissingMasterLoadOrderPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("MissingMaster.esm", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailsWithoutOutputWhenMasterLoadsAfterDependent()
    {
        var result = Run(
            fixture.LightRecord,
            loadOrderPath: fixture.MisorderedMasterLoadOrderPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("load before", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsMalformedFormKey()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = Program.Run(
            BuildArgs("not-a-form-key", "SkyrimSE", fixture.DataFolder, fixture.LoadOrderPath),
            stdout,
            stderr);

        Assert.NotEqual(0, exitCode);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Contains("FormKey", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HelpUsesStandardOutput()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = Program.Run(["--help"], stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Contains("skyrim-record-chain", stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    private RunResult Run(
        FormKey formKey,
        string game = "SkyrimSE",
        string? dataFolder = null,
        string? loadOrderPath = null)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = Program.Run(
            BuildArgs(
                formKey.ToString(),
                game,
                dataFolder ?? fixture.DataFolder,
                loadOrderPath ?? fixture.LoadOrderPath),
            stdout,
            stderr);

        return new RunResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    private static string[] BuildArgs(string formKey, string game, string dataFolder, string loadOrderPath) =>
    [
        "--game", game,
        "--data-folder", dataFolder,
        "--load-order", loadOrderPath,
        formKey
    ];

    private static List<JsonElement> ParseRows(RunResult result)
    {
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        return result.Stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');

    private sealed record RunResult(int ExitCode, string Stdout, string Stderr);
}
