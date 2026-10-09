using System.Text.Json;
using Xunit;
using static CreationEngine.RecordChain.Tests.RecordChainTestDriver;
using static CreationEngine.RecordChain.Tests.StarfieldFixture;

namespace CreationEngine.RecordChain.Tests;

public sealed class StarfieldTests(StarfieldFixture fixture) : IClassFixture<StarfieldFixture>
{
    [Theory]
    [InlineData("Starfield.esm")]
    [InlineData("Full.esm")]
    [InlineData("Medium.esm")]
    [InlineData("Small.esm")]
    public void ResolvesSeparatedMasterSpacesWithTheSameLocalId(string plugin)
    {
        var rows = ParseRows(fixture.Run(Key(plugin)));
        Assert.Equal(new[] { plugin, "Patch.esm" }, rows.Select(row => row.GetProperty("plugin").GetString()));
        Assert.All(rows, row => Assert.Equal(Key(plugin).ToString(), row.GetProperty("formKey").GetString()));
        Assert.Equal("Patch_" + Path.GetFileNameWithoutExtension(plugin), rows[^1].GetProperty("editorId").GetString());
        Assert.True(rows[0].GetProperty("origin").GetBoolean());
        Assert.True(rows[^1].GetProperty("winner").GetBoolean());
    }

    [Fact]
    public void ReadsMasterStylesFromWinningMo2Providers()
    {
        var row = ParseRows(fixture.Run(Key("Medium.esm")))[0];
        Assert.Equal("MediumKeyword", row.GetProperty("editorId").GetString());
        Assert.Equal(NormalizePath(Path.Combine(fixture.High, "Medium.esm")), row.GetProperty("pluginPath").GetString());
        var baseRow = ParseRows(fixture.Run(Key("Starfield.esm")))[0];
        Assert.Equal(NormalizePath(Path.Combine(fixture.Overwrite, "Starfield.esm")), baseRow.GetProperty("pluginPath").GetString());
    }

    [Fact]
    public void ResolvesSmallPluginsOwnRecordsAndInjectedRecords()
    {
        var own = Assert.Single(ParseRows(fixture.Run(Key("Patch.esm"))));
        Assert.Equal("Patch_Patch", own.GetProperty("editorId").GetString());
        Assert.True(own.GetProperty("origin").GetBoolean());
        Assert.True(own.GetProperty("winner").GetBoolean());
        var injected = ParseRows(fixture.Run(Key("Starfield.esm", 0xA00)));
        Assert.Equal(new[] { "Full.esm", "Patch.esm" }, injected.Select(row => row.GetProperty("plugin").GetString()));
        Assert.True(injected[0].GetProperty("origin").GetBoolean());
    }

    [Theory]
    [InlineData(0x810U, "Quest", true)]
    [InlineData(0x811U, "DialogTopic", true)]
    [InlineData(0x812U, "DialogResponses", false)]
    [InlineData(0x820U, "Worldspace", true)]
    [InlineData(0x830U, "Cell", true)]
    [InlineData(0x831U, "PlacedObject", false)]
    public void ResolvesNestedRecordsAndGameSpecificPartialForms(uint id, string type, bool partial)
    {
        var rows = ParseRows(fixture.Run(Key("Starfield.esm", id)));
        Assert.Equal(new[] { "Starfield.esm", "Patch.esm" }, rows.Select(row => row.GetProperty("plugin").GetString()));
        Assert.All(rows, row => Assert.Equal(type, row.GetProperty("type").GetString()));
        Assert.False(rows[0].GetProperty("partial").GetBoolean());
        Assert.Equal(partial, rows[^1].GetProperty("partial").GetBoolean());
        if (partial)
        {
            Assert.Equal(0x4000U, rows[^1].GetProperty("majorRecordFlagsRaw").GetUInt32());
        }
        if (type == "Quest")
        {
            Assert.Equal(JsonValueKind.Null, rows[^1].GetProperty("editorId").ValueKind);
        }
    }

    [Fact]
    public void RetainsDeletionAndDoesNotTreatEveryBit14AsPartial()
    {
        var deleted = ParseRows(fixture.Run(Key("Starfield.esm", 0x801)))[^1];
        Assert.True(deleted.GetProperty("deleted").GetBoolean());
        Assert.True(deleted.GetProperty("winner").GetBoolean());
        var keyword = Assert.Single(ParseRows(fixture.Run(Key("Patch.esm", 0x802))));
        Assert.False(keyword.GetProperty("partial").GetBoolean());
        Assert.Equal(0x4000U, keyword.GetProperty("majorRecordFlagsRaw").GetUInt32());
    }

    [Fact]
    public void ActivatesInstalledOfficialContentAndUsesFirstExistingCcc()
    {
        using var isolated = new StarfieldFixture();
        var official = Assert.Single(ParseRows(isolated.Run(Key("Constellation.esm"))));
        Assert.Equal(1, official.GetProperty("loadOrderIndex").GetInt32());

        string[] CccPlugins() => ParseRows(isolated.RunBatch(
                [Key("GameCcc.esm"), Key("DocumentsCcc.esm"), Key("ProfileCcc.esm")]))
            .Select(row => row.GetProperty("plugin").GetString()!).ToArray();

        // The profile CCC is a decoy, not a mapped game file.
        Assert.Equal(new[] { "GameCcc.esm" }, CccPlugins());
        var documentsCcc = Path.Combine(isolated.MyGamesFolder, "Starfield.ccc");
        File.WriteAllText(documentsCcc, string.Empty);
        Assert.Empty(CccPlugins());
        File.WriteAllText(documentsCcc, "DocumentsCcc.esm\n");
        Assert.Equal(new[] { "DocumentsCcc.esm" }, CccPlugins());
    }

    [Fact]
    public void AutoLoadsBlueprintsInTimestampOrderFromWinningProviders()
    {
        var rows = ParseRows(fixture.Run(Key("Starfield.esm", 0x803)));
        Assert.Equal(new[] { "Starfield.esm", "BlueprintShips-Starfield.esm", "BlueprintShips-Full.esm" },
            rows.Select(row => row.GetProperty("plugin").GetString()));
        Assert.Equal("BlueprintFullOverride", rows[^1].GetProperty("editorId").GetString());
        Assert.True(rows[^1].GetProperty("winner").GetBoolean());
        Assert.Equal(NormalizePath(Path.Combine(fixture.Low, "BlueprintShips-Full.esm")),
            rows[^1].GetProperty("pluginPath").GetString());
        var patchIndex = Assert.Single(ParseRows(fixture.Run(Key("Patch.esm")))).GetProperty("loadOrderIndex").GetInt32();
        Assert.True(rows[1].GetProperty("loadOrderIndex").GetInt32() > patchIndex);
    }

    [Fact]
    public void BlueprintOrderUsesReverseUppercaseNamesOnTimestampTies()
    {
        using var isolated = new StarfieldFixture();
        var timestamp = File.GetLastWriteTimeUtc(Path.Combine(isolated.Data, "BlueprintShips-Starfield.esm"));
        File.SetLastWriteTimeUtc(Path.Combine(isolated.Low, "BlueprintShips-Full.esm"), timestamp);
        isolated.WriteProfile("Tied", NormalPlugins.Select(line => line == "*Full.esm" ? "*full.esm" : line));

        var rows = ParseRows(isolated.Run(Key("Starfield.esm", 0x803), "Tied"));
        Assert.Equal(new[] { "Starfield.esm", "BlueprintShips-Starfield.esm", "BlueprintShips-full.esm" },
            rows.Select(row => row.GetProperty("plugin").GetString()));
    }

    [Fact]
    public void BlueprintOrderDoesNotRequireLoadorderTxt()
    {
        using var isolated = new StarfieldFixture();
        var key = Key("Starfield.esm", 0x803);
        var expected = isolated.Run(key);
        ParseRows(expected);
        var path = Path.Combine(isolated.Root, "profiles", DefaultProfile, "loadorder.txt");

        File.WriteAllText(path, string.Empty);
        Assert.Equal(expected, isolated.Run(key));
        File.Delete(path);
        Assert.Equal(expected, isolated.Run(key));
    }

    [Fact]
    public void BlueprintOwnedChainIsCompleteAndIndependentOfBatchPosition()
    {
        var key = Key("BlueprintShips-Starfield.esm");
        var single = ParseRows(fixture.Run(key));
        Assert.Equal(new[] { "Full.esm", "BlueprintShips-Starfield.esm" },
            single.Select(row => row.GetProperty("plugin").GetString()));
        Assert.Equal("OrdinaryBlueprintOverride", single[0].GetProperty("editorId").GetString());
        Assert.True(single[0].GetProperty("origin").GetBoolean());
        Assert.True(single[^1].GetProperty("winner").GetBoolean());

        var first = ParseRows(fixture.RunBatch([key, Key("Starfield.esm")]));
        var last = ParseRows(fixture.RunBatch([Key("Starfield.esm"), key]));
        foreach (var batch in new[] { first, last })
        {
            Assert.Equal(single.Select(row => row.GetRawText()), batch
                .Where(row => row.GetProperty("formKey").GetString() == key.ToString())
                .Select(row => row.GetRawText()));
        }
    }

    [Theory]
    [InlineData("\"\"", false)]
    [InlineData("''", false)]
    [InlineData("   ", false)]
    [InlineData("\"   \"", true)]
    [InlineData("'   '", true)]
    [InlineData("\"Inactive.esm\"", true)]
    [InlineData("'Inactive.esm'", true)]
    public void TestFileLoadingDependsOnDecodedIniValue(string value, bool configured)
    {
        var profile = "Quoted-" + Guid.NewGuid().ToString("N");
        fixture.WriteProfile(profile, NormalPlugins);
        File.WriteAllText(Path.Combine(fixture.Root, "profiles", profile, "StarfieldCustom.ini"),
            $"[General]\nsTestFile1={value}\n");
        var result = fixture.Run(Key("Starfield.esm"), profile);
        if (configured)
        {
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Stdout);
            Assert.Contains("sTestFile1", result.Stderr);
        }
        else
        {
            Assert.Equal(2, ParseRows(result).Count);
        }
    }

    [Fact]
    public void TestFileGuardUsesOnlyEffectiveGameInis()
    {
        using var isolated = new StarfieldFixture();
        var baseIni = Path.Combine(isolated.Root, "Game", "Starfield.ini");
        var customIni = Path.Combine(isolated.Root, "profiles", DefaultProfile, "StarfieldCustom.ini");
        var tweaks = Path.Combine(isolated.Root, "profiles", DefaultProfile, "initweaks.ini");
        File.WriteAllText(baseIni, "[General] ; section comment\nsTestFile10=Inactive.esm\n");
        File.WriteAllText(customIni, "[Display]\niSize W=1920\n");
        File.WriteAllText(tweaks, "[General]\nsTestFile10=\n");

        var result = isolated.RunBatch([Key("Starfield.esm"), Key("Small.esm")]);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("sTestFile10", result.Stderr);
        Assert.Contains(baseIni, result.Stderr);

        File.WriteAllText(customIni, "[General] ; section comment\nsTestFile10=\"\"\n");
        File.WriteAllText(tweaks, "[General]\nsTestFile10='Inactive.esm'\n");
        Assert.Equal(2, ParseRows(isolated.Run(Key("Starfield.esm"))).Count);
    }

    [Fact]
    public void TestFileGuardUsesTheProfilesSelectedIniDirectory()
    {
        using var isolated = new StarfieldFixture();
        var profileFolder = Path.Combine(isolated.Root, "profiles", DefaultProfile);
        File.WriteAllText(Path.Combine(isolated.MyGamesFolder, "StarfieldCustom.ini"),
            "[General]\nsTestFile1=Inactive.esm\n");
        Assert.Equal(2, ParseRows(isolated.Run(Key("Starfield.esm"))).Count);

        File.WriteAllText(Path.Combine(profileFolder, "settings.ini"), "[General]\nLocalSettings=false\n");
        var result = isolated.Run(Key("Starfield.esm"));
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains(Path.Combine(isolated.MyGamesFolder, "StarfieldCustom.ini"), result.Stderr);
    }

    [Fact]
    public void ResolvesGameSettingsWithConcreteVariantsSharingOneSignature()
    {
        var rows = ParseRows(fixture.Run(Key("Starfield.esm", 0x850)));
        Assert.Equal(new[] { "Starfield.esm", "Patch.esm" }, rows.Select(row => row.GetProperty("plugin").GetString()));
        Assert.All(rows, row => Assert.Equal("GameSettingInt", row.GetProperty("type").GetString()));
    }

    [Fact]
    public void UnreadableOverrideFailsWithoutPartialBatchOutput()
    {
        Assert.Equal(2, ParseRows(fixture.Run(Key("Starfield.esm"))).Count);
        var key = Key("Starfield.esm", 0x860);
        fixture.WriteProfile("WithoutUnreadableOverride", NormalPlugins.Where(line => line != "*Unrelated.esm"));
        var readable = Assert.Single(ParseRows(fixture.Run(key, "WithoutUnreadableOverride")));
        Assert.Equal("BaseSurfaceBlock", readable.GetProperty("editorId").GetString());

        var result = fixture.RunBatch([Key("Starfield.esm"), key]);
        Assert.True(result.ExitCode != 0, result.Stdout);
        Assert.Empty(result.Stdout);
        Assert.Contains(key.ToString(), result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void PreservesBatchOrderIncludingAbsentKeys()
    {
        var keys = new[] { Key("Small.esm"), Key("Patch.esm"), Key("Starfield.esm", 0xFFFF), Key("Starfield.esm", 0x831) };
        var rows = ParseRows(fixture.RunBatch(keys));
        Assert.Equal(new[] { keys[0], keys[0], keys[1], keys[3], keys[3] }.Select(key => key.ToString()),
            rows.Select(row => row.GetProperty("formKey").GetString()));
    }

    [Theory]
    [InlineData("Missing Master", "Medium.esm")]
    [InlineData("Misordered Master", "load before")]
    [InlineData("Missing Active", "Missing.esm")]
    [InlineData("Duplicate", "duplicate")]
    [InlineData("Unpaired Blueprint", "paired")]
    [InlineData("Invalid Blueprint", "Blueprint header flag")]
    [InlineData("Test Files", "sTestFile1")]
    public void RejectsUnresolvableOrUnsupportedProfilesWithoutPartialBatchOutput(string profile, string diagnostic)
    {
        var result = fixture.RunBatch([Key("Starfield.esm"), Key("Small.esm")], profile);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains(diagnostic, result.Stderr, StringComparison.OrdinalIgnoreCase);
    }
}
