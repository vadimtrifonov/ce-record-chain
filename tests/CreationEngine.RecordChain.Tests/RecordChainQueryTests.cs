using System.Text.Json;
using Mutagen.Bethesda.Plugins;
using Xunit;
using static CreationEngine.RecordChain.Tests.RecordChainTestDriver;

namespace CreationEngine.RecordChain.Tests;

public sealed class RecordChainQueryTests(RecordChainFixture fixture) : IClassFixture<RecordChainFixture>
{
    private readonly RecordChainTestDriver _driver = new(fixture);

    [Fact]
    public void EmitsOriginToWinnerGoldenJsonl()
    {
        var result = _driver.Run(fixture.MultipleOverrides);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var goldenPath = Path.Combine(AppContext.BaseDirectory, "Golden", "multiple-overrides.jsonl");
        var encodedRoot = JsonSerializer.Serialize(NormalizePath(fixture.Mo2Root))[1..^1];
        var expected = File.ReadAllText(goldenPath)
            .Replace("<ROOT>", encodedRoot, StringComparison.Ordinal)
            .ReplaceLineEndings("\n");

        Assert.Equal(expected, result.Stdout.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void UsesOneFormKeySpellingForTheWholeChain()
    {
        const string requested = "000800:skyrim.esm";

        var rows = ParseRows(_driver.Run(requested));

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Equal(requested, row.GetProperty("formKey").GetString()));
    }

    [Fact]
    public void SelectsPhysicalPluginProvidersByMo2Priority()
    {
        var rows = ParseRows(_driver.Run(fixture.MultipleOverrides));

        Assert.Equal(
            NormalizePath(Path.Combine(fixture.OverwriteFolder, "Skyrim.esm")),
            rows[0].GetProperty("pluginPath").GetString());
        Assert.Equal(
            NormalizePath(Path.Combine(fixture.HighModFolder, "Early.esp")),
            rows[1].GetProperty("pluginPath").GetString());
        Assert.Equal(
            NormalizePath(Path.Combine(fixture.LowModFolder, "Late.esp")),
            rows[2].GetProperty("pluginPath").GetString());

        var gameDataRow = Assert.Single(ParseRows(_driver.Run(fixture.NewRecord)));
        Assert.Equal(
            NormalizePath(Path.Combine(fixture.DataFolder, "NewRecords.esp")),
            gameDataRow.GetProperty("pluginPath").GetString());
    }

    [Fact]
    public void PreservesDeletedWinnerAndRawFlags()
    {
        var result = _driver.Run(fixture.DeletedWinner);
        var rows = ParseRows(result);

        Assert.Equal(2, rows.Count);
        var winner = rows[^1];
        Assert.Equal("Late.esp", winner.GetProperty("plugin").GetString());
        Assert.True(winner.GetProperty("deleted").GetBoolean());
        Assert.False(winner.GetProperty("partial").GetBoolean());
        Assert.True(winner.GetProperty("winner").GetBoolean());
        Assert.NotEqual(0U, winner.GetProperty("majorRecordFlagsRaw").GetUInt32());
    }

    [Fact]
    public void PreservesPartialEmptyLookingDefinition()
    {
        var result = _driver.Run(fixture.PartialDefinition);
        var rows = ParseRows(result);

        Assert.Equal(2, rows.Count);
        var winner = rows[^1];
        Assert.Equal("Late.esp", winner.GetProperty("plugin").GetString());
        Assert.Equal(0x0000_4000U, winner.GetProperty("majorRecordFlagsRaw").GetUInt32());
        Assert.True(winner.GetProperty("partial").GetBoolean());
        Assert.Equal(JsonValueKind.Null, winner.GetProperty("editorId").ValueKind);
    }

    [Theory]
    [MemberData(nameof(EmbeddedRecords))]
    public void ResolvesParentAndEmbeddedRecord(FormKey formKey, string expectedType)
    {
        var result = _driver.Run(formKey);
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
        var result = _driver.Run(fixture.CreationClubRecord);
        var rows = ParseRows(result);

        Assert.Single(rows);
        Assert.Equal("ccFixture.esl", rows[0].GetProperty("plugin").GetString());
        Assert.Equal(5, rows[0].GetProperty("loadOrderIndex").GetInt32());
        Assert.Equal(
            NormalizePath(Path.Combine(fixture.LowModFolder, "ccFixture.esl")),
            rows[0].GetProperty("pluginPath").GetString());
    }

    [Fact]
    public void ResolvesGlobalWithConcreteVariantsSharingOneSignature()
    {
        var rows = ParseRows(_driver.Run("000804:Skyrim.esm"));
        Assert.Equal(new[] { "Skyrim.esm", "Late.esp" }, rows.Select(row => row.GetProperty("plugin").GetString()));
        Assert.All(rows, row => Assert.Equal("GlobalInt", row.GetProperty("type").GetString()));
    }

    [Fact]
    public void ResolvesLightPluginRecord()
    {
        var result = _driver.Run(fixture.LightRecord);
        var rows = ParseRows(result);

        Assert.Equal(new[] { "Light.esl", "LightPatch.esp" },
            rows.Select(x => x.GetProperty("plugin").GetString()));
        Assert.Equal("000800:Light.esl", rows[0].GetProperty("formKey").GetString());
    }

    [Fact]
    public void MarksActualFirstProviderAsOriginForInjectedRecord()
    {
        var result = _driver.Run(fixture.Injected);
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
        var result = _driver.Run(fixture.InactiveOverride);
        var rows = ParseRows(result);

        Assert.Single(rows);
        Assert.Equal("Skyrim.esm", rows[0].GetProperty("plugin").GetString());
        Assert.True(rows[0].GetProperty("winner").GetBoolean());
    }

    [Fact]
    public void ResolvesNewRecordWithoutPriorDefinition()
    {
        var result = _driver.Run(fixture.NewRecord);
        var rows = ParseRows(result);

        Assert.Single(rows);
        Assert.Equal("NewRecords.esp", rows[0].GetProperty("plugin").GetString());
        Assert.True(rows[0].GetProperty("origin").GetBoolean());
        Assert.True(rows[0].GetProperty("winner").GetBoolean());
    }

    [Fact]
    public void SupportsSkyrimVrWithMo2DefaultsAndNoOverwriteDirectory()
    {
        var result = _driver.Run(fixture.VrRecord, game: "SkyrimVR");
        var rows = ParseRows(result);

        Assert.Single(rows);
        Assert.Equal("VrNpc", rows[0].GetProperty("editorId").GetString());
        Assert.Equal(0, rows[0].GetProperty("loadOrderIndex").GetInt32());
    }

    [Fact]
    public void SkyrimVrIgnoresSkyrimCcc()
    {
        Assert.Empty(ParseRows(_driver.Run(fixture.VrCreationClubRecord, game: "SkyrimVR")));
    }

    [Fact]
    public void RecordChainCanBeEmpty()
    {
        Assert.Empty(ParseRows(_driver.Run(FormKey.Factory("00FFFF:Skyrim.esm"))));
    }

    [Fact]
    public void FailsWithoutOutputWhenActivePluginIsMissing()
    {
        var result = _driver.Run(
            fixture.MultipleOverrides,
            profile: fixture.MissingPluginProfile);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("MissingActive.esp", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailsWithoutOutputWhenRequiredMasterIsMissing()
    {
        var result = _driver.Run(
            fixture.MultipleOverrides,
            profile: fixture.MissingMasterProfile);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("MissingMaster.esm", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailsWithoutOutputWhenMasterLoadsAfterDependent()
    {
        var result = _driver.Run(
            fixture.LightRecord,
            profile: fixture.MisorderedMasterProfile);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("load before", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsMalformedFormKey()
    {
        var result = _driver.Run("not-a-form-key");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("FormKey", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HelpUsesStandardOutput()
    {
        var result = Invoke(["--help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ce-record-chain", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("--mo2-root", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("--profile", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("--formkeys-from", result.Stdout, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Stderr);
    }
}
