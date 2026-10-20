using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using WearableDistributor;

/// <summary>Independent binary fixtures for implicit plugins, leveled templates, and occupational labels.</summary>
public static class ScanReviewChecks
{
    public static void Run(string root)
    {
        CheckImplicitCreationClub(root);
        CheckNestedLeveledTemplates(root);
        CheckSemanticPopulations();
        Console.WriteLine("Independent scanner review checks passed.");
    }

    private static void CheckImplicitCreationClub(string root)
    {
        var game = Path.Combine(root, "scan-review-implicit");
        var data = Path.Combine(game, "Data"); Directory.CreateDirectory(data);
        new SkyrimMod(ModKey.FromNameAndExtension("Skyrim.esm"), SkyrimRelease.SkyrimSE).WriteToBinary(Path.Combine(data, "Skyrim.esm"));
        var resource = new SkyrimMod(ModKey.FromNameAndExtension("ccInstalledResource.esm"), SkyrimRelease.SkyrimSE);
        var keyword = resource.Keywords.AddNew("ImplicitResourceKeyword");
        resource.WriteToBinary(Path.Combine(data, resource.ModKey.FileName));
        var outfit = new SkyrimMod(ModKey.FromNameAndExtension("DependentAdventurerArmor.esp"), SkyrimRelease.SkyrimSE);
        var armor = outfit.Armors.AddNew("AdventurerBodyArmor"); armor.Name = "Adventurer armor";
        armor.BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body };
        armor.Keywords = [keyword.ToLink()];
        outfit.WriteToBinary(Path.Combine(data, outfit.ModKey.FileName));
        var disabled = new SkyrimMod(ModKey.FromNameAndExtension("DisabledArmor.esp"), SkyrimRelease.SkyrimSE);
        disabled.Armors.AddNew("DisabledBodyArmor").BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body };
        disabled.WriteToBinary(Path.Combine(data, disabled.ModKey.FileName));
        File.WriteAllText(Path.Combine(game, "Skyrim.ccc"), "# Installed implicit plugins\nccInstalledResource.esm\nccNotInstalled.esl\nccInstalledResource.esm\n");
        var profile = Path.Combine(game, "plugins.txt");
        File.WriteAllText(profile, "*DependentAdventurerArmor.esp\nDisabledArmor.esp\n");
        var result = new WearableScanService().Scan(data, new AppPreferences { PluginListPath = profile }, null, null, CancellationToken.None);
        Check(result.LoadOrder.SequenceEqual(["Skyrim.esm", "ccInstalledResource.esm", "DependentAdventurerArmor.esp"]), "installed implicit .ccc resource precedes the active dependent and absent/disabled plugins stay excluded");
        Check(result.Plugins.Single().CanPatch, "an installed .ccc master omitted from plugins.txt does not invalidate its dependent armor plugin");
        Check(!result.Diagnostics.Any(line => line.Contains("Missing or inactive master", StringComparison.OrdinalIgnoreCase)), "implicit .ccc loading eliminates false missing-master diagnostics");

        var staging = Path.Combine(game, "Staging"); Directory.CreateDirectory(staging);
        File.Copy(Path.Combine(data, outfit.ModKey.FileName), Path.Combine(staging, outfit.ModKey.FileName));
        var stagedResult = new WearableScanService().Scan(staging, new AppPreferences { PluginListPath = profile }, data, null, CancellationToken.None);
        Check(stagedResult.LoadOrder.SequenceEqual(result.LoadOrder) && stagedResult.Plugins.Single().CanPatch, "baseline Data supplies installed implicit resources when scanning a staging folder");
    }

    private static void CheckNestedLeveledTemplates(string root)
    {
        var folder = Path.Combine(root, "scan-review-templates"); Directory.CreateDirectory(folder);
        new SkyrimMod(ModKey.FromNameAndExtension("Skyrim.esm"), SkyrimRelease.SkyrimSE).WriteToBinary(Path.Combine(folder, "Skyrim.esm"));
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("LeveledTemplateFixture.esp"), SkyrimRelease.SkyrimSE);
        var actorType = mod.Keywords.AddNew("ActorTypeNPC");
        var nord = mod.Races.AddNew("NordRace"); nord.Flags = Race.Flag.Playable; nord.Keywords = [actorType.ToLink()];
        var breton = mod.Races.AddNew("BretonRace"); breton.Flags = Race.Flag.Playable; breton.Keywords = [actorType.ToLink()];
        var bandits = mod.Factions.AddNew("BanditFaction");
        var archer = mod.Classes.AddNew("EncClassBanditArcher");
        var melee = mod.Classes.AddNew("EncClassBanditMelee");
        var female = mod.Npcs.AddNew("FemaleNordArcherTemplate"); female.Name = "Female archer template";
        female.Race.SetTo(nord); female.Class.SetTo(archer); female.Configuration.Flags |= NpcConfiguration.Flag.Female;
        female.Factions.Add(new RankPlacement { Faction = bandits.ToLink(), Rank = 0 });
        var male = mod.Npcs.AddNew("MaleBretonMeleeTemplate"); male.Race.SetTo(breton); male.Class.SetTo(melee);
        male.Factions.Add(new RankPlacement { Faction = bandits.ToLink(), Rank = 0 });
        var inner = mod.LeveledNpcs.AddNew("InnerTemplateAlternatives"); inner.Entries = [Entry(female.FormKey), Entry(male.FormKey)];
        var outer = mod.LeveledNpcs.AddNew("OuterTemplateAlternatives"); outer.Entries = [Entry(inner.FormKey)];
        var child = mod.Npcs.AddNew("PlacedVariableBandit"); child.Name = "Variable bandit"; child.Template.SetTo(outer);
        child.Configuration.TemplateFlags = NpcConfiguration.TemplateFlag.Traits | NpcConfiguration.TemplateFlag.Stats | NpcConfiguration.TemplateFlag.Factions;
        var unresolvedList = mod.LeveledNpcs.AddNew("PartiallyMissingAlternatives");
        unresolvedList.Entries = [Entry(female.FormKey), Entry(new FormKey(mod.ModKey, 0xEFED))];
        var uncertain = mod.Npcs.AddNew("PlacedUncertainBandit"); uncertain.Race.SetTo(nord); uncertain.Class.SetTo(archer); uncertain.Template.SetTo(unresolvedList);
        uncertain.Configuration.TemplateFlags = child.Configuration.TemplateFlags;
        var cyclic = mod.LeveledNpcs.AddNew("CyclicTemplateAlternatives"); cyclic.Entries = [Entry(cyclic.FormKey)];
        var cycleChild = mod.Npcs.AddNew("PlacedCyclicBandit"); cycleChild.Race.SetTo(nord); cycleChild.Template.SetTo(cyclic);
        cycleChild.Configuration.TemplateFlags = child.Configuration.TemplateFlags;
        AddPlaced(mod, child.FormKey, uncertain.FormKey, cycleChild.FormKey);
        mod.WriteToBinary(Path.Combine(folder, mod.ModKey.FileName));
        var list = Path.Combine(folder, "plugins.txt"); File.WriteAllText(list, "*LeveledTemplateFixture.esp\n");
        var result = new WearableScanService().Scan(folder, new AppPreferences { PluginListPath = list }, null, null, CancellationToken.None);
        var profile = result.Npcs.Single(item => item.FormId == Form(child.FormKey));
        Check(profile.CanReceiveWearables && profile.Variants.Count == 2, "nested leveled templates retain each distinct adult humanoid outcome");
        Check(profile.Variants.Any(item => item.Gender == "Female" && item.TargetForms.Contains(Form(archer.FormKey)) && item.TargetForms.Contains(Form(nord.FormKey))) &&
              profile.Variants.Any(item => item.Gender == "Male" && item.TargetForms.Contains(Form(melee.FormKey)) && item.TargetForms.Contains(Form(breton.FormKey))),
              "race, sex, class, and inherited faction attributes stay correlated within each leveled outcome");
        Check(!result.Npcs.Single(item => item.FormId == Form(female.FormKey)).IsEncounterable && !result.Npcs.Single(item => item.FormId == Form(male.FormKey)).IsEncounterable,
            "template ancestry does not itself mark unplaced parent bases as encountered actors");
        Check(result.Npcs.Where(item => item.FormId == Form(uncertain.FormKey) || item.FormId == Form(cycleChild.FormKey)).All(item => item.HasUnresolvedTemplate && !item.CanReceiveWearables),
            "missing and cyclic leveled alternatives cannot be discarded to leave an incorrectly broad audience");
        var service = new NpcTargetingService(result.Targets, result.Npcs);
        var rule = service.BuildRule("Bandits", mod.ModKey.FileName);
        Check(service.CountMatches(rule) == 1 && service.CountMatches(rule, "Female") == 0, "a variable-sex NPC base is included for any sex but never under a specific sex");
        rule.Filters.Add(Choice(archer.FormKey, "EncClassBanditArcher", "Class", "NPC role"));
        Check(service.CountMatches(rule) == 0, "one matching template branch cannot qualify the base for an archer-only audience");
        rule.Filters.Add(Choice(melee.FormKey, "EncClassBanditMelee", "Class", "NPC role"));
        Check(service.CountMatches(rule) == 1, "OR role choices cover both branches while retaining the required bandit base population");
        rule.Filters.Add(Choice(nord.FormKey, "NordRace", "Race", "Race"));
        Check(service.CountMatches(rule) == 0, "one matching race branch cannot qualify a variable-race base");
        rule.Filters.Add(Choice(breton.FormKey, "BretonRace", "Race", "Race")); service.Resolve(rule);
        Check(rule.ResolvedNpcFormIds.SequenceEqual([profile.FormId]), "multiple allowed roles and races resolve to the one encounterable base without broadening to templates");
    }

    private static void CheckSemanticPopulations()
    {
        var wenchFaction = Choice(new FormKey(ModKey.FromNameAndExtension("Immersive Wenches.esp"), 0x800), "lalawench_generalwench_faction", "Faction", "Faction");
        var exclusion = Choice(new FormKey(ModKey.FromNameAndExtension("Skyrim.esm"), 0x801), "BardAudienceExcludedFaction", "Faction", "Faction");
        var trueBard = Choice(new FormKey(ModKey.FromNameAndExtension("Skyrim.esm"), 0x802), "JobBardFaction", "Faction", "Faction");
        var wench = Profile("0x000900~Immersive Wenches.esp", "Immersive Wenches.esp", "lalawench_riverwood_inn_PLUS", "Kristi", wenchFaction, exclusion);
        var bard = Profile("0x000901~Skyrim.esm", "Skyrim.esm", "EncounteredBard", "Bard", trueBard);
        var service = new NpcTargetingService([wenchFaction, exclusion, trueBard], [wench, bard]);
        Check(service.BuildRule("Wenches", "Immersive Wenches.esp").ResolvedNpcFormIds.SequenceEqual([wench.FormId]), "installed Wenches records with fused lalawench identifiers form their intended NPC group");
        Check(service.BuildRule("Bards", NpcTargetingService.AllCollection).ResolvedNpcFormIds.SequenceEqual([bard.FormId]), "BardAudienceExcludedFaction does not classify its NPC members as performing bards");
        Check(!service.GetGroupLabels("Immersive Wenches.esp").Contains("Bards"), "empty real bard populations are not offered under a wenches mod collection");
        var namedFarmer = Profile("0x000902~Skyrim.esm", "Skyrim.esm", "FarmerLookingVillager", "Farmer", exclusion);
        var nameOnlyService = new NpcTargetingService([exclusion], [namedFarmer]);
        Check(!nameOnlyService.GetGroupLabels(NpcTargetingService.OfficialCollection).Contains("Farmers"), "a farmer-looking NPC name or EditorID alone never qualifies as actual farming occupation evidence");
    }

    private static LeveledNpcEntry Entry(FormKey key) => new() { Data = new LeveledNpcEntryData { Level = 1, Count = 1, Reference = new FormLink<INpcSpawnGetter>(key) } };
    private static void AddPlaced(SkyrimMod mod, params FormKey[] bases)
    {
        var block = new CellBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock };
        var sub = new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock };
        var cell = new Cell(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE) { EditorID = "IndependentScanReviewCell" };
        foreach (var form in bases) { var reference = new PlacedNpc(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE); reference.Base.SetTo(form); cell.Persistent.Add(reference); }
        sub.Cells.Add(cell); block.SubBlocks.Add(sub); mod.Cells.Add(block);
    }
    private static KeywordChoice Choice(FormKey key, string editorId, string kind, string category) => new() { LocalId = key.ID, Plugin = key.ModKey.FileName, EditorId = editorId, TargetKind = kind, Category = category };
    private static NpcTargetProfile Profile(string form, string plugin, string editorId, string name, params KeywordChoice[] choices) => new() { FormId = form, Plugin = plugin, EditorId = editorId, Name = name, Gender = "Female", IsHumanoid = true, IsEncounterable = true, TargetForms = choices.Select(item => item.FormId).ToHashSet(StringComparer.OrdinalIgnoreCase) };
    private static string Form(FormKey key) => $"0x{key.ID:X6}~{key.ModKey.FileName}";
    private static void Check(bool success, string message) { if (!success) throw new InvalidOperationException("Independent scan review failed: " + message); }
}
