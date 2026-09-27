using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using static CreationEngine.RecordChain.Tests.RecordChainTestDriver;

namespace CreationEngine.RecordChain.Tests;

public sealed class StarfieldFixture : IDisposable
{
    private const StarfieldRelease Release = StarfieldRelease.Starfield;
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "ce-record-chain-tests", "Starfield-" + Guid.NewGuid().ToString("N"));
    public string Data => Path.Combine(Root, "Game", "Data");
    public string High => Path.Combine(Root, "mods", "High");
    public string Low => Path.Combine(Root, "mods", "Low");
    public string Overwrite => Path.Combine(Root, "overwrite");
    public string DocumentsFolder => Path.Combine(Root, "Documents");
    public string MyGamesFolder => Path.Combine(DocumentsFolder, "My Games", "Starfield");
    public const string DefaultProfile = "Default";
    public static readonly string[] NormalPlugins =
    [
        "*Full.esm", "*Medium.esm", "*Small.esm", "*Patch.esm", "*Unrelated.esm",
        "Inactive.esm", "MissingInactive.esm", "*Ghost.esm.ghost"
    ];
    public static FormKey Key(string plugin, uint id = 0x800) => new(ModKey.FromFileName(plugin), id);

    public StarfieldFixture()
    {
        foreach (var directory in new[] { Data, High, Low, Overwrite, MyGamesFolder })
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(Path.Combine(Root, "ModOrganizer.ini"), $$"""
            [General]
            gameName=Starfield
            gamePath={{Path.Combine(Root, "Game").Replace('\\', '/')}}
            [Settings]
            profile_local_inis=true
            """);

        var baseMod = NewMod("Starfield.esm");
        AddKeyword(baseMod, Key("Starfield.esm"), "BaseKeyword");
        AddKeyword(baseMod, Key("Starfield.esm", 0x801), "DeletedKeyword");
        AddKeyword(baseMod, Key("Starfield.esm", 0x803), "BlueprintTarget");
        baseMod.GameSettings.Add(new GameSettingInt(Key("Starfield.esm", 0x850), Release)
        {
            EditorID = "iFixtureSetting", Data = 1
        });
        baseMod.SurfaceBlocks.Add(new SurfaceBlock(Key("Starfield.esm", 0x860), Release)
        {
            EditorID = "BaseSurfaceBlock"
        });
        AddContainers(baseMod, "Base", partial: false);
        Write(Data, baseMod);
        File.Copy(Path.Combine(Data, "Starfield.esm"), Path.Combine(Overwrite, "Starfield.esm"));

        var official = NewMod("Constellation.esm");
        AddKeyword(official, Key("Constellation.esm"), "Official");
        Write(Low, official);
        var ccc = NewMod("ProfileCcc.esm");
        AddKeyword(ccc, Key("ProfileCcc.esm"), "ProfileCcc");
        Write(High, ccc);
        var gameCcc = NewMod("GameCcc.esm");
        AddKeyword(gameCcc, Key("GameCcc.esm"), "GameCcc");
        Write(Data, gameCcc);
        File.WriteAllText(Path.Combine(Root, "Game", "Starfield.ccc"), "GameCcc.esm\nMissingOptional.esm\n");
        var documentsCcc = NewMod("DocumentsCcc.esm");
        AddKeyword(documentsCcc, Key("DocumentsCcc.esm"), "DocumentsCcc");
        Write(High, documentsCcc);

        var blueprint = NewMod("BlueprintShips-Starfield.esm");
        blueprint.ModHeader.Flags |= (StarfieldModHeader.HeaderFlag)0x800;
        AddKeyword(blueprint, Key(blueprint.ModKey.FileName), "BlueprintLocal");
        AddKeyword(blueprint, Key("Starfield.esm", 0x803), "BlueprintBaseWinner");
        Write(Data, blueprint, baseMod);

        var full = NewMod("Full.esm");
        AddKeyword(full, Key("Full.esm"), "FullKeyword");
        AddKeyword(full, Key("Starfield.esm", 0xA00), "InjectedKeyword");
        // A normal plugin can override a record owned by a later-loading blueprint.
        AddKeyword(full, Key(blueprint.ModKey.FileName), "OrdinaryBlueprintOverride");
        Write(High, full, baseMod, blueprint);
        var pairedFull = NewMod("BlueprintShips-Full.esm");
        pairedFull.ModHeader.Flags |= (StarfieldModHeader.HeaderFlag)0x800;
        AddKeyword(pairedFull, Key("Starfield.esm", 0x803), "BlueprintFullOverride");
        Write(Low, pairedFull, baseMod);
        File.Copy(Path.Combine(Low, pairedFull.ModKey.FileName), Path.Combine(Data, pairedFull.ModKey.FileName));
        // Both MO2's saved order and the losing physical copy disagree with the winning files' timestamps.
        var blueprintTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(Data, blueprint.ModKey.FileName), blueprintTime);
        File.SetLastWriteTimeUtc(Path.Combine(Low, pairedFull.ModKey.FileName), blueprintTime.AddDays(1));
        File.SetLastWriteTimeUtc(Path.Combine(Data, pairedFull.ModKey.FileName), blueprintTime.AddDays(-1));

        var medium = NewMod("Medium.esm");
        medium.IsMediumMaster = true;
        AddKeyword(medium, Key("Medium.esm"), "MediumKeyword");
        Write(High, medium);
        var losingMedium = NewMod("Medium.esm");
        losingMedium.IsSmallMaster = true;
        AddKeyword(losingMedium, Key("Medium.esm"), "WrongProvider");
        Write(Low, losingMedium);

        var small = NewMod("Small.esm");
        small.IsSmallMaster = true;
        AddKeyword(small, Key("Small.esm"), "SmallKeyword");
        Write(Low, small);

        var patch = NewMod("Patch.esm");
        patch.IsSmallMaster = true;
        foreach (var name in new[] { "Starfield.esm", "Full.esm", "Medium.esm", "Small.esm", "Patch.esm" })
        {
            AddKeyword(patch, Key(name), "Patch_" + Path.GetFileNameWithoutExtension(name));
        }
        patch.Keywords.Add(new Keyword(Key("Starfield.esm", 0x801), Release) { IsDeleted = true });
        AddKeyword(patch, Key("Starfield.esm", 0xA00), "PatchedInjection");
        patch.Keywords.Add(new Keyword(Key("Patch.esm", 0x802), Release)
        {
            MajorRecordFlagsRaw = 0x4000,
            EditorID = "NotAPartialContainer"
        });
        patch.GameSettings.Add(new GameSettingInt(Key("Starfield.esm", 0x850), Release)
        {
            EditorID = "iFixtureSetting", Data = 2
        });
        AddContainers(patch, "Patch", partial: true);
        Write(Low, patch, baseMod, full, medium, small);

        var inactive = NewMod("Inactive.esm");
        AddKeyword(inactive, Key("Starfield.esm"), "MustNotWin");
        Write(High, inactive, baseMod);
        var ghost = NewMod("Ghost.esm");
        AddKeyword(ghost, Key("Starfield.esm"), "GhostMustNotWin");
        Write(High, ghost, baseMod);
        File.Move(Path.Combine(High, "Ghost.esm"), Path.Combine(High, "Ghost.esm.ghost"));

        // Valid record framing with an unsupported component body, like the
        // unrelated SFBK that blocked an untyped lookup in a real Starfield profile.
        var unrelated = NewMod("Unrelated.esm");
        unrelated.SurfaceBlocks.Add(new SurfaceBlock(Key("Starfield.esm", 0x860), Release)
        {
            EditorID = "BlockEditorMetaData_Component"
        });
        Write(Low, unrelated, baseMod);
        var unrelatedPath = Path.Combine(Low, "Unrelated.esm");
        var bytes = File.ReadAllBytes(unrelatedPath);
        var editorIdOffset = bytes.AsSpan().IndexOf("EDID"u8);
        if (editorIdOffset < 0) throw new InvalidOperationException("Fixture EDID was not written");
        "BFCB"u8.CopyTo(bytes.AsSpan(editorIdOffset));
        File.WriteAllBytes(unrelatedPath, bytes);

        var badPair = NewMod("BadPair.esm");
        Write(Data, badPair);
        Write(Data, NewMod("BlueprintShips-BadPair.esm"));

        WriteProfile(DefaultProfile, NormalPlugins);
        WriteProfile("Missing Master", NormalPlugins.Where(line => line != "*Medium.esm"));
        WriteProfile("Misordered Master", ["*Patch.esm", "*Full.esm", "*Medium.esm", "*Small.esm"]);
        WriteProfile("Missing Active", NormalPlugins.Append("*Missing.esm"));
        WriteProfile("Duplicate", NormalPlugins.Append("*small.esm"));
        WriteProfile("Unpaired Blueprint", ["*BlueprintShips-Full.esm"]);
        WriteProfile("Invalid Blueprint", NormalPlugins.Append("*BadPair.esm"));
        WriteProfile("Test Files", NormalPlugins);
        File.WriteAllText(Path.Combine(Root, "profiles", "Test Files", "StarfieldCustom.ini"),
            "[General]\nsTestFile1=Inactive.esm\n");
    }

    public void WriteProfile(string name, IEnumerable<string> plugins)
    {
        var folder = Path.Combine(Root, "profiles", name);
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "modlist.txt"), ["+High", "+Low"]);
        File.WriteAllLines(Path.Combine(folder, "plugins.txt"), plugins);
        File.WriteAllLines(Path.Combine(folder, "loadorder.txt"),
            ["BlueprintShips-Full.esm", "BlueprintShips-Starfield.esm"]);
        File.WriteAllLines(Path.Combine(folder, "Starfield.ccc"), ["ProfileCcc.esm", "MissingOptional.esm"]);
    }

    internal RunResult Run(FormKey key, string profile = DefaultProfile) => Invoke(
        ["--game", "Starfield", "--mo2-root", Root, "--profile", profile, key.ToString()],
        documentsFolder: DocumentsFolder);

    internal RunResult RunBatch(IEnumerable<FormKey> keys, string profile = DefaultProfile) => Invoke(
        ["--game", "Starfield", "--mo2-root", Root, "--profile", profile, "--formkeys-from", "-"],
        string.Join('\n', keys), DocumentsFolder);

    public void Dispose() => Directory.Delete(Root, recursive: true);

    private static StarfieldMod NewMod(string name) => new(ModKey.FromFileName(name), Release)
    {
        IsMaster = true
    };

    private static void AddKeyword(StarfieldMod mod, FormKey key, string editorId) =>
        mod.Keywords.Add(new Keyword(key, Release) { EditorID = editorId });

    private static void AddContainers(StarfieldMod mod, string suffix, bool partial)
    {
        var flags = partial ? 0x4000 : 0;
        var quest = new Quest(Key("Starfield.esm", 0x810), Release)
        {
            EditorID = partial ? null : "BaseQuest",
            MajorRecordFlagsRaw = flags
        };
        var topic = new DialogTopic(Key("Starfield.esm", 0x811), Release)
        {
            EditorID = suffix + "Topic",
            MajorRecordFlagsRaw = flags
        };
        topic.Quest.SetTo(quest.FormKey);
        topic.Responses.Add(new DialogResponses(Key("Starfield.esm", 0x812), Release) { EditorID = suffix + "Response" });
        quest.DialogTopics.Add(topic);
        mod.Quests.Add(quest);
        mod.Worldspaces.Add(new Worldspace(Key("Starfield.esm", 0x820), Release)
        {
            EditorID = suffix + "Worldspace",
            MajorRecordFlagsRaw = flags
        });
        var cell = new Cell(Key("Starfield.esm", 0x830), Release)
        {
            EditorID = suffix + "Cell",
            MajorRecordFlagsRaw = flags
        };
        cell.Temporary.Add(new PlacedObject(Key("Starfield.esm", 0x831), Release) { EditorID = suffix + "Placed" });
        var subBlock = new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock };
        subBlock.Cells.Add(cell);
        var block = new CellBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock };
        block.SubBlocks.Add(subBlock);
        mod.Cells.Records.Add(block);
    }

    private static void Write(string folder, StarfieldMod mod, params StarfieldMod[] masters)
    {
        var writer = mod.BeginWrite.ToPath(Path.Combine(folder, mod.ModKey.FileName));
        if (masters.Length == 0)
        {
            writer.WithNoLoadOrder().Write();
        }
        else
        {
            writer.WithLoadOrder(masters).Write();
        }
    }
}
