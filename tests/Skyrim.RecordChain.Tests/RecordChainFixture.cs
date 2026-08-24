using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace Skyrim.RecordChain.Tests;

public sealed class RecordChainFixture : IDisposable
{
    private const SkyrimRelease SeRelease = SkyrimRelease.SkyrimSE;

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "skyrim-record-chain-tests", Guid.NewGuid().ToString("N"));
    public string DataFolder { get; }
    public string LoadOrderPath { get; }
    public string MissingPluginLoadOrderPath { get; }
    public string MissingMasterLoadOrderPath { get; }
    public string MisorderedMasterLoadOrderPath { get; }
    public string VrDataFolder { get; }
    public string VrLoadOrderPath { get; }

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

    public RecordChainFixture()
    {
        var seRoot = Path.Combine(Root, "SE");
        var vrRoot = Path.Combine(Root, "VR");
        DataFolder = Path.Combine(seRoot, "Data");
        LoadOrderPath = Path.Combine(seRoot, "plugins.txt");
        MissingPluginLoadOrderPath = Path.Combine(seRoot, "plugins-missing-plugin.txt");
        MissingMasterLoadOrderPath = Path.Combine(seRoot, "plugins-missing-master.txt");
        MisorderedMasterLoadOrderPath = Path.Combine(seRoot, "plugins-misordered-master.txt");
        VrDataFolder = Path.Combine(vrRoot, "Data");
        VrLoadOrderPath = Path.Combine(vrRoot, "plugins-vr.txt");

        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(VrDataFolder);

        BuildSeFixtures();
        BuildVrFixtures();
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
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

        foreach (var implicitName in new[] { "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" })
        {
            WriteMod(DataFolder, NewMod(implicitName, SeRelease));
        }

        var creationClub = NewMod("ccFixture.esl", SeRelease);
        creationClub.Npcs.Add(new Npc(CreationClubRecord, SeRelease) { EditorID = "CreationClubNpc" });
        WriteMod(DataFolder, creationClub);
        File.WriteAllLines(
            Path.Combine(Directory.GetParent(DataFolder)!.FullName, "Skyrim.ccc"),
            ["ccFixture.esl"]);

        var early = NewMod("Early.esp", SeRelease, "Skyrim.esm");
        early.Npcs.Add(new Npc(MultipleOverrides, SeRelease) { EditorID = "EarlyNpc" });
        WriteMod(DataFolder, early);

        var late = NewMod("Late.esp", SeRelease, "Skyrim.esm");
        late.Npcs.Add(new Npc(MultipleOverrides, SeRelease) { EditorID = "LateNpc" });
        late.Npcs.Add(new Npc(DeletedWinner, SeRelease) { EditorID = "DeletedNpc", IsDeleted = true });
        late.DialogTopics.Add(new DialogTopic(PartialDefinition, SeRelease)
        {
            MajorRecordFlagsRaw = 0x0000_4000
        });
        WriteMod(DataFolder, late);

        var inactive = NewMod("Inactive.esp", SeRelease, "Skyrim.esm");
        inactive.Npcs.Add(new Npc(InactiveOverride, SeRelease) { EditorID = "InactiveNpc" });
        WriteMod(DataFolder, inactive);

        var light = NewMod("Light.esl", SeRelease);
        light.Npcs.Add(new Npc(LightRecord, SeRelease) { EditorID = "LightNpc" });
        WriteMod(DataFolder, light);

        var lightPatch = NewMod("LightPatch.esp", SeRelease, "Light.esl");
        lightPatch.Npcs.Add(new Npc(LightRecord, SeRelease) { EditorID = "LightPatchNpc" });
        WriteMod(DataFolder, lightPatch);

        var injector = NewMod("Injector.esp", SeRelease, "Skyrim.esm");
        injector.Npcs.Add(new Npc(Injected, SeRelease) { EditorID = "InjectedNpc" });
        WriteMod(DataFolder, injector);

        var injectionPatch = NewMod("InjectionPatch.esp", SeRelease, "Skyrim.esm", "Injector.esp");
        injectionPatch.Npcs.Add(new Npc(Injected, SeRelease) { EditorID = "InjectionPatchNpc" });
        WriteMod(DataFolder, injectionPatch);

        var cellPatch = NewMod("CellPatch.esp", SeRelease, "Skyrim.esm");
        AddCell(cellPatch, "Patch", Cell, PlacedObject);
        WriteMod(DataFolder, cellPatch);

        var newRecords = NewMod("NewRecords.esp", SeRelease);
        newRecords.Npcs.Add(new Npc(NewRecord, SeRelease) { EditorID = "NewNpc" });
        WriteMod(DataFolder, newRecords);

        var badMaster = NewMod("BadMaster.esp", SeRelease, "MissingMaster.esm");
        badMaster.Npcs.Add(new Npc(
            FormKey.Factory("000800:MissingMaster.esm"),
            SeRelease)
        {
            EditorID = "MissingMasterNpc"
        });
        WriteMod(DataFolder, badMaster);

        var normalLines = new[]
        {
            "# Generated fixture load order",
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
        File.WriteAllLines(LoadOrderPath, normalLines);
        File.WriteAllLines(MissingPluginLoadOrderPath, normalLines.Append("*MissingActive.esp"));
        File.WriteAllLines(MissingMasterLoadOrderPath, normalLines.Append("*BadMaster.esp"));
        File.WriteAllLines(MisorderedMasterLoadOrderPath,
        [
            "# Master follows its dependent",
            "*LightPatch.esp",
            "*Light.esl"
        ]);
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

        File.WriteAllText(VrLoadOrderPath, "# No explicit plugins" + Environment.NewLine);
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

    private static void WriteMod(string dataFolder, SkyrimMod mod)
    {
        var path = Path.Combine(dataFolder, mod.ModKey.FileName);
        mod.BeginWrite
            .ToPath(path)
            .WithNoLoadOrder()
            .Write();
    }
}
