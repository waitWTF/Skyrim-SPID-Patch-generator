using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using WearableDistributor;

public static class ScannerChecks
{
    public static void Run(string root)
    {
        UniformedProtectionChecks.Run();
        var folder = Path.Combine(root, "scanner-checks"); Directory.CreateDirectory(folder);
        var pluginList = Path.Combine(folder, "plugins.txt");
        File.WriteAllText(pluginList, "# active order\n*BaseWearables.esp\nDisabled.esp\n*OverrideNPCs.esp\n");
        Check(WearableScanService.ReadProfilePluginOrder(pluginList).SequenceEqual(["BaseWearables.esp", "OverrideNPCs.esp"]), "active plugin order and disabled markers are honored");
        File.WriteAllText(pluginList, "BaseWearables.esp\n");
        Check(WearableScanService.ReadProfilePluginOrder(pluginList).Count == 0, "unstarred plugins.txt entries do not silently become enabled");
        var inferred = WearableScanService.OrderByDependencies(["OverrideNPCs.esp", "BaseWearables.esp"], new Dictionary<string, List<string>> { ["OverrideNPCs.esp"] = ["BaseWearables.esp"] });
        Check(inferred.SequenceEqual(["BaseWearables.esp", "OverrideNPCs.esp"]), "inferred order respects master dependencies");

        var bandits = Choice("BanditFaction", 1, "Faction"); var farmers = Choice("WIFarmingFaction", 2, "Faction");
        var archer = Choice("EncClassBanditArcher", 3, "Class"); var farmerClass = Choice("Farmer", 4, "Class");
        var nord = Choice("NordRace", 5, "Race"); var breton = Choice("BretonRace", 6, "Race");
        var adult = Profile("0x000010~Skyrim.esm", "Skyrim.esm", "Female", bandits, archer, nord);
        var farm = Profile("0x000011~NewFarmers.esp", "NewFarmers.esp", "Male", farmers, farmerClass, breton);
        var child = Profile("0x000012~Skyrim.esm", "Skyrim.esm", "Female", bandits, archer, nord); child.IsChild = true;
        var service = new NpcTargetingService([bandits, farmers, archer, farmerClass, nord, breton], [adult, farm, child]);
        Check(new NpcTargetingService([farmers, breton], [Profile(farm.FormId, farm.Plugin, "Male", farmers, breton)]).GetGroupLabels("NewFarmers.esp").Contains("Farmers"), "WIFarmingFaction acronym identifies actual farming members without unrelated combat classes");
        Check(service.GetRoles(NpcTargetingService.OfficialCollection, "Bandits").Select(item => item.FormId).SequenceEqual([archer.FormId]), "group roles reflect its actual population");
        Check(service.GetRaces("NewFarmers.esp", "Farmers").Select(item => item.FormId).SequenceEqual([breton.FormId]), "mod origin collection includes NPCs using vanilla race and faction records");
        Check(!service.GetGroupLabels(NpcTargetingService.OfficialCollection).Contains("Farmers"), "empty groups are hidden in a collection");
        var rule = service.BuildRule("Bandits", NpcTargetingService.OfficialCollection);
        Check(rule.Filters.Count == 1 && service.CountMatches(rule) == 1, "children are excluded from exact audience membership");
        Check(service.CountMatches(rule, "Male") == 0, "NPC sex filters constrain estimates");
        var optionalRole = Choice("EncClassBanditArcher", 3, "Class"); optionalRole.Category = "NPC role";
        rule.Filters.Add(optionalRole);
        service.Resolve(rule, "Female");
        Check(rule.NpcResolutionComplete && rule.ResolvedNpcFormIds.SequenceEqual([adult.FormId]), "resolved targets respect base audience AND optional roles AND sex");
        Check(!NpcTargetingService.Matches(new DistributionTargetRule(), adult), "empty audiences never match everyone");
        Check(NpcTargetingService.FriendlyLabel(Choice("EncClassBanditMissile", 7, "Class")) == "Archers", "missile classes have a beginner friendly role label");
        Check(WearableFilterService.GetFilterReason(Path.Combine(folder, "Grace Outfit 3BA HDT.esp"), [], []) == "", "outfit names and body-fit metadata do not trigger substring exclusions");
        Check(WearableFilterService.GetFilterReason(Path.Combine(folder, "Skyrim Adventurer Armor.esp"), [], []) == "", "Skyrim themed standalone armor is not mistaken for the base game");
        Check(WearableFilterService.GetFilterReason(Path.Combine(folder, "Skyrim.esm"), [], []) == "Base game", "base game plugin is excluded exactly");
        Check(WearableFilterService.GetFilterReason(Path.Combine(folder, "CBBE.esp"), [], []) == "Body mod", "body plugins remain excluded");
        Check(!WearableFilterService.IsExcludedRecord(new WearableRecord { Name = "Magic scholar robe", EditorId = "ScholarRobe", Slots = "Body" }), "magic themed wearable names are not excluded as spells");

        var settingsFile = Path.Combine(folder, "settings.json");
        File.WriteAllText(settingsFile, "{\"DataPath\":null,\"DefaultChance\":\"oops\",\"IgnoredPlugins\":null,\"AdditionalExclusions\":[null,\" \"],\"DistributionSettings\":{\"Example.esp\":{\"Gender\":null,\"TargetRules\":null}}}");
        var settings = PreferencesService.Load(settingsFile);
        Check(settings.DefaultChance == "25" && settings.AdditionalExclusions.Count == 0 && settings.DistributionSettings["example.ESP"].TargetRules.Count == 0, "nullable imported settings normalize safely and retain case insensitive keys");
        PreferencesService.Save(settingsFile, settings); var previous = File.ReadAllText(settingsFile);
        settings.DefaultChance = "80"; PreferencesService.Save(settingsFile, settings);
        Check(File.ReadAllText(settingsFile + ".bak") == previous && PreferencesService.Load(settingsFile).DefaultChance == "80", "atomic settings save preserves previous backup");
        var canceled = false;
        try { new WearableScanService().Scan(folder, new AppPreferences(), null, null, new CancellationToken(true)); }
        catch (OperationCanceledException) { canceled = true; }
        Check(canceled, "scan cancellation stops before loading plugin records");
        var blockedSource = new PluginEntry { Plugin = "BlockedArmor.esp", ValidationIssue = "Missing master: RequiredAssets.esm" };
        var blockedPiece = new WearableRecord { Plugin = blockedSource.Plugin, LocalId = 0x801, Name = "Blocked piece", Slots = "Body" };
        var blockedOutput = Path.Combine(folder, "blocked-output"); var blocked = false;
        try { PatchGenerator.Generate([new PluginPatchOptions { Source = blockedSource, Pieces = [blockedPiece], TargetRules = [service.BuildRule("Bandits", NpcTargetingService.OfficialCollection)] }], blockedOutput, "SPID"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Missing master", StringComparison.OrdinalIgnoreCase)) { blocked = true; }
        Check(blocked && !File.Exists(Path.Combine(blockedOutput, PatchGenerator.OutputPluginName)) && !File.Exists(Path.Combine(blockedOutput, PatchGenerator.OutputIniName)), "invalid source masters stop generation without creating patch files");

        CheckBinaryScan(folder);
        Console.WriteLine("Scanner, targeting, and settings checks passed.");
    }

    private static void CheckBinaryScan(string folder)
    {
        var source = new SkyrimMod(ModKey.FromNameAndExtension("BaseWearables.esp"), SkyrimRelease.SkyrimSE);
        var actorKeyword = source.Keywords.AddNew("ActorTypeNPC");
        var race = source.Races.AddNew("AdultRace"); race.Flags = Race.Flag.Playable; race.Keywords = [actorKeyword.ToLink()];
        var farmer = source.Classes.AddNew("Farmer"); var mage = source.Classes.AddNew("CombatSorcerer");
        var faction = source.Factions.AddNew("JobFarmerFaction");
        var template = source.Npcs.AddNew("FarmerTemplate"); template.Race.SetTo(race); template.Class.SetTo(farmer);
        template.Factions.Add(new RankPlacement { Faction = faction.ToLink(), Rank = 0 });
        var npc = source.Npcs.AddNew("ContestFarmWorker"); npc.Name = "Farm worker"; npc.Template.SetTo(template);
        npc.Configuration.TemplateFlags = NpcConfiguration.TemplateFlag.Traits | NpcConfiguration.TemplateFlag.Stats | NpcConfiguration.TemplateFlag.Factions;
        var childRace = source.Races.AddNew("ChildRace"); childRace.Flags = Race.Flag.Playable | Race.Flag.Child; childRace.Keywords = [actorKeyword.ToLink()];
        var childNpc = source.Npcs.AddNew("EncounteredChild"); childNpc.Race.SetTo(childRace); childNpc.Class.SetTo(farmer);
        var unresolvedNpc = source.Npcs.AddNew("UnresolvedInheritedWorker"); unresolvedNpc.Race.SetTo(race); unresolvedNpc.Class.SetTo(farmer);
        unresolvedNpc.Template.SetTo(new FormKey(source.ModKey, 0xEFED)); unresolvedNpc.Configuration.TemplateFlags = NpcConfiguration.TemplateFlag.Traits;
        var armor = source.Armors.AddNew("TestAdventurerArmor"); armor.Name = "Adventurer body armor"; armor.BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body };
        var block = new CellBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock };
        var subBlock = new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock };
        var cell = new Cell(source.GetNextFormKey(), SkyrimRelease.SkyrimSE) { EditorID = "ScannerCheckCell" };
        var placed = new PlacedNpc(source.GetNextFormKey(), SkyrimRelease.SkyrimSE); placed.Base.SetTo(npc); cell.Persistent.Add(placed);
        var placedChild = new PlacedNpc(source.GetNextFormKey(), SkyrimRelease.SkyrimSE); placedChild.Base.SetTo(childNpc); cell.Persistent.Add(placedChild);
        var placedUnresolved = new PlacedNpc(source.GetNextFormKey(), SkyrimRelease.SkyrimSE); placedUnresolved.Base.SetTo(unresolvedNpc); cell.Persistent.Add(placedUnresolved);
        subBlock.Cells.Add(cell); block.SubBlocks.Add(subBlock); source.Cells.Add(block);
        source.WriteToBinary(Path.Combine(folder, source.ModKey.FileName));
        var patch = new SkyrimMod(ModKey.FromNameAndExtension("OverrideNPCs.esp"), SkyrimRelease.SkyrimSE);
        var winningNpc = patch.Npcs.GetOrAddAsOverride(npc); winningNpc.Class.SetTo(mage);
        winningNpc.Configuration.TemplateFlags &= ~NpcConfiguration.TemplateFlag.Stats;
        patch.WriteToBinary(Path.Combine(folder, patch.ModKey.FileName));
        var unsafeMod = new SkyrimMod(ModKey.FromNameAndExtension("NeedsMissingMaster.esp"), SkyrimRelease.SkyrimSE);
        var unsafeKeyword = unsafeMod.Keywords.AddNew("UnsafeKeyword");
        var unsafeArmor = unsafeMod.Armors.AddNew("UnsafeArmor"); unsafeArmor.BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body };
        unsafeArmor.Keywords = [new FormLink<IKeywordGetter>(new FormKey(ModKey.FromNameAndExtension("MissingMaster.esm"), 0x801))];
        var unsafeOverride = unsafeMod.Npcs.GetOrAddAsOverride(npc); unsafeOverride.Name = "Wrong winning name";
        unsafeMod.WriteToBinary(Path.Combine(folder, unsafeMod.ModKey.FileName));
        var dependent = new SkyrimMod(ModKey.FromNameAndExtension("NeedsUnsafeGear.esp"), SkyrimRelease.SkyrimSE);
        var dependentArmor = dependent.Armors.AddNew("DependentArmor"); dependentArmor.BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body }; dependentArmor.Keywords = [unsafeKeyword.ToLink()];
        dependent.WriteToBinary(Path.Combine(folder, dependent.ModKey.FileName));
        var list = Path.Combine(folder, "plugins.txt"); File.WriteAllText(list, "*BaseWearables.esp\n*OverrideNPCs.esp\n*NeedsMissingMaster.esp\n*NeedsUnsafeGear.esp\n");
        var result = new WearableScanService().Scan(folder, new AppPreferences { PluginListPath = list }, null, null, CancellationToken.None);
        var effective = result.Npcs.Single(profile => profile.FormId == Form(npc.FormKey));
        Check(effective.WinningPlugin == "OverrideNPCs.esp" && effective.TargetForms.Contains(Form(mage.FormKey)) && !effective.TargetForms.Contains(Form(farmer.FormKey)), "winning override supplies effective NPC class");
        Check(effective.Name == "Farm worker", "unusable plugins cannot override valid NPC records");
        Check(effective.CanReceiveWearables && effective.TargetForms.Contains(Form(faction.FormKey)) && effective.RaceFormId == Form(race.FormKey), "NPC templates inherit factions and traits and placed actor proves encounter reference");
        Check(!result.Npcs.Single(profile => profile.FormId == Form(template.FormKey)).IsEncounterable, "unplaced templates are excluded");
        Check(!result.Npcs.Single(profile => profile.FormId == Form(childNpc.FormKey)).CanReceiveWearables, "child race flags exclude a placed NPC from the wearable audience");
        Check(result.Npcs.Single(profile => profile.FormId == Form(unresolvedNpc.FormKey)).HasUnresolvedTemplate && !result.Npcs.Single(profile => profile.FormId == Form(unresolvedNpc.FormKey)).CanReceiveWearables, "unresolved template inheritance cannot become a broad or incorrectly classified audience");
        Check(result.Plugins.Count(plugin => plugin.CanPatch) == 1 && result.Plugins.Single(plugin => plugin.CanPatch).Plugin == "BaseWearables.esp", "new wearable records belong to source plugin rather than overrides");
        Check(result.Plugins.Where(plugin => !plugin.CanPatch).Count() == 2 && result.Plugins.Where(plugin => !plugin.CanPatch).All(plugin => !plugin.Selected), "missing and transitive unavailable masters disable patch selection");
    }

    private static KeywordChoice Choice(string id, uint local, string kind) => new() { EditorId = id, Plugin = "Skyrim.esm", LocalId = local, TargetKind = kind, Category = kind };
    private static NpcTargetProfile Profile(string id, string plugin, string gender, params KeywordChoice[] targets) => new()
    { FormId = id, Plugin = plugin, Gender = gender, Name = id, IsHumanoid = true, IsEncounterable = true, TargetForms = targets.Select(item => item.FormId).ToHashSet(StringComparer.OrdinalIgnoreCase) };
    private static string Form(FormKey key) => $"0x{key.ID:X6}~{key.ModKey.FileName}";
    private static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException("Scanner check failed: " + message); }
}
