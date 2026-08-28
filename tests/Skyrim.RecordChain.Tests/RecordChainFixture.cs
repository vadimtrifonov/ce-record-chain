using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace Skyrim.RecordChain.Tests;

public sealed class RecordChainFixture : IDisposable
{
    private const SkyrimRelease SeRelease = SkyrimRelease.SkyrimSE;

    public string Root { get; } = Path.Combine(
        Path.GetTempPath(),
        "skyrim-record-chain-tests",
        "Tést-" + Guid.NewGuid().ToString("N"));

    public string Mo2Root => Path.Combine(Root, "SE");
    public string GameRoot => Path.Combine(Mo2Root, "Game Root");
    public string DataFolder => Path.Combine(GameRoot, "Data");
    public string ModsFolder => Path.Combine(Mo2Root, "managed-mods");
    public string ProfilesFolder => Path.Combine(Mo2Root, "named-profiles");
    public string OverwriteFolder => Path.Combine(Mo2Root, "output");
    public string HighModFolder => Path.Combine(ModsFolder, "High");
    public string LowModFolder => Path.Combine(ModsFolder, "Low");

    public string ProfileName => "Default";
    public string MissingPluginProfile => "Missing Plugin";
    public string MissingMasterProfile => "Missing Master";
    public string MisorderedMasterProfile => "Misordered Master";
    public string MalformedModlistProfile => "Malformed Modlist";
    public string MissingEnabledModProfile => "Missing Enabled Mod";
    public string MissingProfileFilesProfile => "Missing Files";

    public string VrMo2Root => Path.Combine(Root, "VR");
    public string VrGameRoot => Path.Combine(VrMo2Root, "Game Root");
    public string VrDataFolder => Path.Combine(VrGameRoot, "Data");
    public string VrProfileName => "Default";

    public FormKey MultipleOverrides { get; } = FormKey.Factory("000800:Skyrim.esm");
    public FormKey DeletedWinner { get; } = FormKey.Factory("000801:Skyrim.esm");
    public FormKey InactiveOverride { get; } = FormKey.Factory("000802:Skyrim.esm");
    public FormKey PartialDefinition { get; } = FormKey.Factory("000803:Skyrim.esm");
    public FormKey Cell { get; } = FormKey.Factory("000900:Skyrim.esm");
    public FormKey PlacedObject { get; } = FormKey.Factory("000901:Skyrim.esm");
    public FormKey Injected { get; } = FormKey.Factory("000A00:Skyrim.esm");
    public FormKey CreationClubRecord { get; } = FormKey.Factory("000800:ccFixture.esl");
    public FormKey LightRecord { get; } = FormKey.Factory("000800:Light.esl");
    public FormKey NewRecord { get; } = FormKey.Factory("000800:NewRecords.esp");
    public FormKey VrRecord { get; } = FormKey.Factory("000800:Skyrim.esm");
    public FormKey VrCreationClubRecord { get; } = FormKey.Factory("000800:VrClub.esl");

    public RecordChainFixture()
    {
        CreateSeLayout();
        BuildSeFixtures();
        CreateVrLayout();
        BuildVrFixtures();
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private void CreateSeLayout()
    {
        foreach (var directory in new[]
                 {
                     DataFolder,
                     ProfilesFolder,
                     OverwriteFolder,
                     HighModFolder,
                     LowModFolder,
                     Path.Combine(ModsFolder, "Disabled"),
                     Path.Combine(ModsFolder, "Unmanaged")
                 })
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            Path.Combine(Mo2Root, "ModOrganizer.ini"),
            $$"""
            [General]
            gameName=Skyrim Special Edition
            gamePath={{EncodeQSettingsByteArray(GameRoot)}}

            [Settings]
            base_directory={{Mo2Root.Replace("\\", "/", StringComparison.Ordinal)}}
            mod_directory=%BASE_DIR%/managed-mods
            profiles_directory=%BASE_DIR%/named-profiles
            overwrite_directory=%BASE_DIR%/output
            """.ReplaceLineEndings());
    }

    private void BuildSeFixtures()
    {
        var skyrim = NewMod("Skyrim.esm", SeRelease);
        skyrim.Npcs.Add(new Npc(MultipleOverrides, SeRelease) { EditorID = "BaseNpc" });
        skyrim.Npcs.Add(new Npc(DeletedWinner, SeRelease) { EditorID = "DeletedNpc" });
        skyrim.Npcs.Add(new Npc(InactiveOverride, SeRelease) { EditorID = "ActiveOnlyNpc" });
        skyrim.DialogTopics.Add(new DialogTopic(PartialDefinition, SeRelease)
        {
            EditorID = "BaseTopic"
        });
        AddCell(skyrim, "Base", Cell, PlacedObject);
        WriteMod(DataFolder, skyrim);
        CopyMod(DataFolder, LowModFolder, skyrim.ModKey.FileName);
        CopyMod(DataFolder, HighModFolder, skyrim.ModKey.FileName);
        CopyMod(DataFolder, OverwriteFolder, skyrim.ModKey.FileName);

        foreach (var implicitName in new[] { "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" })
        {
            WriteMod(DataFolder, NewMod(implicitName, SeRelease));
        }

        var creationClub = NewMod("ccFixture.esl", SeRelease);
        creationClub.Npcs.Add(new Npc(CreationClubRecord, SeRelease) { EditorID = "CreationClubNpc" });
        WriteMod(LowModFolder, creationClub);
        File.WriteAllLines(
            Path.Combine(GameRoot, "Skyrim.ccc"),
            ["ccFixture.esl", "MissingClub.esl"]);

        var early = NewMod("Early.esp", SeRelease, "Skyrim.esm");
        early.Npcs.Add(new Npc(MultipleOverrides, SeRelease) { EditorID = "EarlyNpc" });
        WriteMod(DataFolder, early);
        CopyMod(DataFolder, LowModFolder, early.ModKey.FileName);
        CopyMod(DataFolder, HighModFolder, early.ModKey.FileName);

        var late = NewMod("Late.esp", SeRelease, "Skyrim.esm");
        late.Npcs.Add(new Npc(MultipleOverrides, SeRelease) { EditorID = "LateNpc" });
        late.Npcs.Add(new Npc(DeletedWinner, SeRelease) { EditorID = "DeletedNpc", IsDeleted = true });
        late.DialogTopics.Add(new DialogTopic(PartialDefinition, SeRelease)
        {
            MajorRecordFlagsRaw = 0x0000_4000
        });
        WriteMod(DataFolder, late);
        CopyMod(DataFolder, LowModFolder, late.ModKey.FileName);

        var inactive = NewMod("Inactive.esp", SeRelease, "Skyrim.esm");
        inactive.Npcs.Add(new Npc(InactiveOverride, SeRelease) { EditorID = "InactiveNpc" });
        WriteMod(HighModFolder, inactive);

        var light = NewMod("Light.esl", SeRelease);
        light.Npcs.Add(new Npc(LightRecord, SeRelease) { EditorID = "LightNpc" });
        WriteMod(LowModFolder, light);

        var lightPatch = NewMod("LightPatch.esp", SeRelease, "Light.esl");
        lightPatch.Npcs.Add(new Npc(LightRecord, SeRelease) { EditorID = "LightPatchNpc" });
        WriteMod(LowModFolder, lightPatch);

        var injector = NewMod("Injector.esp", SeRelease, "Skyrim.esm");
        injector.Npcs.Add(new Npc(Injected, SeRelease) { EditorID = "InjectedNpc" });
        WriteMod(HighModFolder, injector);

        var injectionPatch = NewMod("InjectionPatch.esp", SeRelease, "Skyrim.esm", "Injector.esp");
        injectionPatch.Npcs.Add(new Npc(Injected, SeRelease) { EditorID = "InjectionPatchNpc" });
        WriteMod(HighModFolder, injectionPatch);

        var cellPatch = NewMod("CellPatch.esp", SeRelease, "Skyrim.esm");
        AddCell(cellPatch, "Patch", Cell, PlacedObject);
        WriteMod(LowModFolder, cellPatch);

        var newRecords = NewMod("NewRecords.esp", SeRelease);
        newRecords.Npcs.Add(new Npc(NewRecord, SeRelease) { EditorID = "NewNpc" });
        WriteMod(DataFolder, newRecords);
        CopyMod(DataFolder, Path.Combine(ModsFolder, "Disabled"), newRecords.ModKey.FileName);
        CopyMod(DataFolder, Path.Combine(ModsFolder, "Unmanaged"), newRecords.ModKey.FileName);

        var badMaster = NewMod("BadMaster.esp", SeRelease, "MissingMaster.esm");
        badMaster.Npcs.Add(new Npc(
            FormKey.Factory("000800:MissingMaster.esm"),
            SeRelease)
        {
            EditorID = "MissingMasterNpc"
        });
        WriteMod(LowModFolder, badMaster);

        var modlist = new[]
        {
            "# Strongest managed priority first",
            "+High",
            "-Disabled",
            "*Unmanaged",
            "-Fixture_separator",
            "+Low"
        };
        var normalPlugins = new[]
        {
            "# Generated fixture plugin list",
            "*Early.esp",
            "*Late.esp",
            "Inactive.esp",
            "MissingInactive.esp",
            "*Light.esl",
            "*LightPatch.esp",
            "*Injector.esp",
            "*InjectionPatch.esp",
            "*CellPatch.esp",
            "*NewRecords.esp"
        };

        WriteProfile(ProfileName, modlist, normalPlugins);
        WriteProfile(MissingPluginProfile, modlist, normalPlugins.Append("*MissingActive.esp"));
        WriteProfile(MissingMasterProfile, modlist, normalPlugins.Append("*BadMaster.esp"));
        WriteProfile(
            MisorderedMasterProfile,
            modlist,
            ["# Master follows its dependent", "*LightPatch.esp", "*Light.esl"]);
        WriteProfile(MalformedModlistProfile, modlist.Append("?Broken"), normalPlugins);
        WriteProfile(MissingEnabledModProfile, ["+Missing Mod"], ["# No explicit plugins"]);

        var missingFilesFolder = Path.Combine(ProfilesFolder, MissingProfileFilesProfile);
        Directory.CreateDirectory(missingFilesFolder);
        File.WriteAllLines(Path.Combine(missingFilesFolder, "modlist.txt"), modlist);
    }

    private void CreateVrLayout()
    {
        Directory.CreateDirectory(VrDataFolder);
        Directory.CreateDirectory(Path.Combine(VrMo2Root, "mods"));
        Directory.CreateDirectory(Path.Combine(VrMo2Root, "profiles", VrProfileName));

        File.WriteAllText(
            Path.Combine(VrMo2Root, "ModOrganizer.ini"),
            $$"""
            [General]
            gameName=Skyrim VR
            gamePath={{EncodeQSettingsByteArray(VrGameRoot)}}
            """.ReplaceLineEndings());
    }

    private void BuildVrFixtures()
    {
        var skyrim = NewMod("Skyrim.esm", SkyrimRelease.SkyrimVR);
        skyrim.Npcs.Add(new Npc(VrRecord, SkyrimRelease.SkyrimVR) { EditorID = "VrNpc" });
        WriteMod(VrDataFolder, skyrim);

        foreach (var implicitName in new[]
                 {
                     "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm", "SkyrimVR.esm"
                 })
        {
            WriteMod(VrDataFolder, NewMod(implicitName, SkyrimRelease.SkyrimVR));
        }

        var creationClub = NewMod("VrClub.esl", SkyrimRelease.SkyrimVR);
        creationClub.Npcs.Add(new Npc(VrCreationClubRecord, SkyrimRelease.SkyrimVR)
        {
            EditorID = "VrCreationClubNpc"
        });
        WriteMod(VrDataFolder, creationClub);
        File.WriteAllText(Path.Combine(VrGameRoot, "Skyrim.ccc"), "VrClub.esl" + Environment.NewLine);

        var profileFolder = Path.Combine(VrMo2Root, "profiles", VrProfileName);
        File.WriteAllText(
            Path.Combine(profileFolder, "modlist.txt"),
            "*Unmanaged" + Environment.NewLine);
        File.WriteAllText(
            Path.Combine(profileFolder, "plugins.txt"),
            "# No explicit plugins" + Environment.NewLine);
    }

    private void WriteProfile(
        string name,
        IEnumerable<string> modlist,
        IEnumerable<string> plugins)
    {
        var folder = Path.Combine(ProfilesFolder, name);
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "modlist.txt"), modlist);
        File.WriteAllLines(Path.Combine(folder, "plugins.txt"), plugins);
    }

    private static string EncodeQSettingsByteArray(string value)
    {
        var encoded = new StringBuilder("@ByteArray(");
        var previousWasHexEscape = false;
        foreach (var valueByte in Encoding.UTF8.GetBytes(value))
        {
            if (valueByte == '\\')
            {
                encoded.Append("\\\\");
                previousWasHexEscape = false;
            }
            else if (valueByte is >= 0x20 and < 0x7F &&
                     !(previousWasHexEscape && IsHexDigit(valueByte)))
            {
                encoded.Append((char)valueByte);
                previousWasHexEscape = false;
            }
            else
            {
                encoded.Append($"\\x{valueByte:x2}");
                previousWasHexEscape = true;
            }
        }

        return encoded.Append(')').ToString();

        static bool IsHexDigit(byte valueByte) =>
            valueByte is >= (byte)'0' and <= (byte)'9' or
                >= (byte)'A' and <= (byte)'F' or
                >= (byte)'a' and <= (byte)'f';
    }

    private static SkyrimMod NewMod(string fileName, SkyrimRelease release, params string[] masters)
    {
        var modKey = ModKey.FromFileName(fileName);
        var mod = new SkyrimMod(modKey, release, forceUseLowerFormIDRanges: true)
        {
            IsMaster = modKey.Type == ModType.Master,
            IsSmallMaster = modKey.Type == ModType.Light
        };

        foreach (var master in masters)
        {
            mod.ModHeader.MasterReferences.Add(new MasterReference
            {
                Master = ModKey.FromFileName(master)
            });
        }

        return mod;
    }

    private static void AddCell(SkyrimMod mod, string suffix, FormKey cellKey, FormKey placedKey)
    {
        var placed = new PlacedObject(placedKey, mod.SkyrimRelease)
        {
            EditorID = $"{suffix}Placed"
        };
        var cell = new Cell(cellKey, mod.SkyrimRelease)
        {
            EditorID = $"{suffix}Cell"
        };
        cell.Temporary.Add(placed);

        var subBlock = new CellSubBlock
        {
            BlockNumber = 0,
            GroupType = GroupTypeEnum.InteriorCellSubBlock,
            LastModified = 1
        };
        subBlock.Cells.Add(cell);

        var block = new CellBlock
        {
            BlockNumber = 0,
            GroupType = GroupTypeEnum.InteriorCellBlock,
            LastModified = 1
        };
        block.SubBlocks.Add(subBlock);
        mod.Cells.Records.Add(block);
    }

    private static void WriteMod(string folder, SkyrimMod mod)
    {
        var path = Path.Combine(folder, mod.ModKey.FileName);
        mod.BeginWrite
            .ToPath(path)
            .WithNoLoadOrder()
            .Write();
    }

    private static void CopyMod(string sourceFolder, string destinationFolder, string fileName)
    {
        File.Copy(
            Path.Combine(sourceFolder, fileName),
            Path.Combine(destinationFolder, fileName));
    }
}
