using System.IO;
using System.Text.Json.Nodes;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using WearableDistributor;

public static class CurrentRegressionChecks
{
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    public static void Run(string parent)
    {
        var root = Path.Combine(parent, "current-regressions"); Directory.CreateDirectory(root);
        void Scenario(string name, Action<string> action)
        {
            var folder = Path.Combine(root, "case-" + (Passed + Failed + 1).ToString("00")); Directory.CreateDirectory(folder);
            try { action(folder); Passed++; Console.WriteLine("PASS " + name); }
            catch (Exception error) { Failed++; Console.WriteLine("FAIL " + name + " :: " + error); }
        }
        Scenario("Indexed uniform protection matches original policy including inherited variants", path => {
            var choices=new[]{
                new KeywordChoice{Plugin="Skyrim.esm",LocalId=1,EditorId="GuardSolitudeFaction",TargetKind="Faction"},
                new KeywordChoice{Plugin="Skyrim.esm",LocalId=2,EditorId="DLC1DawnguardFaction",TargetKind="Faction"},
                new KeywordChoice{Plugin="Skyrim.esm",LocalId=3,EditorId="BanditFaction",TargetKind="Faction"},
                new KeywordChoice{Plugin="Skyrim.esm",LocalId=4,EditorId="RedguardRace",TargetKind="Race"},
                new KeywordChoice{Plugin="Skyrim.esm",LocalId=5,EditorId="FarmerOutfit",TargetKind="Outfit"}};
            var byId=choices.ToDictionary(choice=>choice.FormId,StringComparer.OrdinalIgnoreCase);
            var profiles=Enumerable.Range(0,100).Select(i=>new NpcTargetProfile{
                FormId=$"0x{0x1000+i:X6}~Skyrim.esm",Plugin="Skyrim.esm",Name=i%7==0?"Housecarl":"Redguard citizen",EditorId="DemoCitizen"+i,
                Gender="Female",IsHumanoid=true,IsEncounterable=true,
                TargetForms=new HashSet<string>(new[]{choices[i%choices.Length].FormId},StringComparer.OrdinalIgnoreCase),
                Variants=i%3==0?new(){new NpcTargetVariant{Gender="Female",TargetForms=new HashSet<string>(new[]{choices[(i+1)%choices.Length].FormId},StringComparer.OrdinalIgnoreCase)}}:new()
            }).ToList();
            var expected=profiles.Select(profile=>(Protected:UniformedNpcPolicy.IsProtected(profile,byId),Guard:UniformedNpcPolicy.IsGuard(profile,byId))).ToList();
            var targeting=new NpcTargetingService(choices,profiles);
            Require(profiles.Select((profile,i)=>profile.IsProtectedUniformed==expected[i].Protected && profile.IsProtectedGuard==expected[i].Guard).All(same=>same),"Indexed protection must match existing policy exactly");
            foreach(var group in new[]{"Bandits","Dawnguard","All NPCs"})
                Require(targeting.CountProtectedMembers(group,NpcTargetingService.AllCollection)==targeting.BuildRule(group,NpcTargetingService.AllCollection).ProtectedNpcCount,"Dropdown protected count matches actual group rule");
        });
        Scenario("Generates real ESP/INI/manifest, not a draft JSON", path => {
            var output = PatchGenerator.Generate([Config()], path, "SPID");
            Require(File.Exists(output.PluginPath) && File.Exists(output.DistributionPath), "Both game files required");
            using var esp = Open(output.PluginPath); Require(esp.Outfits.Count == 1, "One OTFT");
            Require(PatchInventoryService.ReadManifest(path).Entries.Count == 1, "Manifest records actual outfit");
        });
        Scenario("Optional parts retain exact percentage in generated leveled items", path => {
            var cfg = Config(); cfg.Pieces[1].PartMode = "Optional"; cfg.Pieces[1].OptionalChance = 35;
            var output = PatchGenerator.Generate([cfg], path, "SPID"); using var esp = Open(output.PluginPath);
            Require(esp.LeveledItems.Count == 1 && Math.Abs(100 * (1.0 - esp.LeveledItems.Single().ChanceNone) - 35) < .01, "35% read back exactly");
            Require(File.ReadLines(output.DistributionPath).Count(line => line.StartsWith("Outfit =")) == 1, "Only one overall SPID roll");
            Require(PatchHealthService.Inspect(path, PatchInventoryService.ReadManifest(path)).Values.Single().Status == "Ready", "Optional output passes health");
        });
        Scenario("Repatch optional chance reuses record and preserves unrelated outfit", path => {
            var cfg = Config(); cfg.Pieces[1].PartMode = "Optional"; cfg.Pieces[1].OptionalChance = 25;
            var other = Config("OtherSet.esp"); var output = PatchGenerator.Generate([cfg, other], path, "SPID");
            uint optionalId; using(var esp=Open(output.PluginPath)) optionalId=esp.LeveledItems.Single().FormKey.ID;
            cfg.Pieces[1].OptionalChance = 80; PatchGenerator.Generate([cfg], path, "SPID");
            using var next=Open(output.PluginPath); Require(next.Outfits.Count == 2 && next.LeveledItems.Single().FormKey.ID==optionalId, "Stable records and merge");
            Require(Math.Abs(100*(1.0-next.LeveledItems.Single().ChanceNone)-80)<.01, "New percentage read back");
        });
        Scenario("Optional to Required prunes obsolete leveled items", path => {
            var cfg=Config(); cfg.Pieces[1].PartMode="Optional"; var output=PatchGenerator.Generate([cfg],path,"SPID");
            cfg.Pieces[1].PartMode="Required"; PatchGenerator.Generate([cfg],path,"SPID");
            using var esp=Open(output.PluginPath); Require(esp.LeveledItems.Count==0 && esp.Outfits.Single().Items!.Count==2,"No orphan list");
        });
        Scenario("Removing an optional outfit preserves sibling optional lists", path => {
            var a=Config();a.Pieces[1].PartMode="Optional"; var b=Config("Sibling.esp");b.Pieces[1].PartMode="Optional";
            var output=PatchGenerator.Generate([a,b],path,"SPID"); var entry=PatchInventoryService.ReadManifest(path).Entries.Single(e=>e.SourcePlugin==a.Source.Plugin);
            PatchInventoryService.Remove(path,entry);using var esp=Open(output.PluginPath);
            Require(esp.Outfits.Count==1 && esp.LeveledItems.Count==1,"Only requested entry removed");
        });
        Scenario("Merchant-only output needs no audience or valid NPC chance", path => {
            var cfg=ShopConfig();cfg.Chance="not an NPC chance";cfg.TargetRules.Clear();var output=PatchGenerator.Generate([cfg],path,"SPID");
            Require(output.DistributionCount==0 && output.MerchantDistributionCount>0,"Only shop distribution");
            Require(PatchInventoryService.ReadManifest(path).Entries.Single().DistributeToNpcs==false,"Shop state retained");
        });
        Scenario("Clothing traders get armor but never bundled weapons", path => {
            var cfg=ShopConfig();PatchGenerator.Generate([cfg],path,"SPID");var rules=PatchInventoryService.ReadManifest(path).Entries.Single().MerchantStock;
            Require(rules.Any(r=>r.Category==MerchantStockService.Clothing && r.ItemKind=="Armor"),"Clothing stock kept");
            Require(!rules.Any(r=>r.Category==MerchantStockService.Clothing && r.ItemKind=="Weapon"),"Weapons blocked for clothing");
            Require(rules.Count(r=>r.ItemKind=="Weapon")==2,"Weapon reaches smith and general goods");
        });
        Scenario("Shared clothing/smith stock container cannot receive weapons", path => {
            var cfg=ShopConfig();cfg.MerchantTargets=[new("Skyrim.esm",0x100,"Smith",MerchantStockService.Blacksmiths),new("Skyrim.esm",0x100,"Clothes",MerchantStockService.Clothing)];
            cfg.MerchantCategories=[MerchantStockService.Blacksmiths,MerchantStockService.Clothing];PatchGenerator.Generate([cfg],path,"SPID");
            Require(!PatchInventoryService.ReadManifest(path).Entries.Single().MerchantStock.Any(rule=>rule.ItemKind=="Weapon"),"Shared-container weapon blocked");
        });
        Scenario("Switch NPC outfit to merchant-only removes its old NPC rule", path => {
            var cfg=Config();var output=PatchGenerator.Generate([cfg],path,"SPID");var shop=ShopConfig();PatchGenerator.Generate([shop],path,"SPID");
            Require(!File.ReadLines(output.DistributionPath).Any(line=>line.StartsWith("Outfit =")),"Old NPC rule removed");
            Require(PatchInventoryService.ReadManifest(path).Entries.Count==1,"Entry replaced, not duplicated");
        });
        Scenario("Female-only outfit rejects wrong NPC sex but permits shop stock", path => {
            var cfg=Config(); foreach(var piece in cfg.Pieces)piece.SupportedGender="Female";cfg.Gender="Male";
            Reject(()=>PatchGenerator.Generate([cfg],path,"SPID")); cfg.DistributeToNpcs=false;cfg.TargetRules.Clear();cfg.MerchantCategories=[MerchantStockService.Clothing];cfg.MerchantTargets=[new("Skyrim.esm",0x300,"Clothes",MerchantStockService.Clothing)];
            Require(PatchGenerator.Generate([cfg],path,"SPID").MerchantDistributionCount>0,"Shop stock has no wearer sex requirement");
        });
        Scenario("Normal presets never select marked lewd variants or wigs", path => {
            foreach(var preset in OutfitPartPresets.Names){var cfg=Config();cfg.Pieces.Add(Part(0x805,"Robe Slutty","Body"));cfg.Pieces.Add(Part(0x806,"Hair Wig","Hair"));OutfitPartPresets.Apply(cfg,preset);
                Require(!cfg.Pieces.Where(p=>p.IsLewd||OutfitPartPresets.IsWig(p)).Any(p=>p.Include),"Normal preset excludes explicit parts: "+preset);}
        });
        Scenario("Lewd opt-in resets on a normal preset", path => {
            var cfg=Config();cfg.Pieces.Add(Part(0x805,"Robe Slutty","Body"));OutfitPartPresets.Apply(cfg,"Complete set",true);
            Require(cfg.AllowLewdParts && cfg.Pieces.Any(p=>p.Include&&p.IsLewd),"Explicit opt-in works");OutfitPartPresets.Apply(cfg,"Balanced");Require(!cfg.AllowLewdParts && !cfg.Pieces.Any(p=>p.Include&&p.IsLewd),"Reset removes opt-in");
        });
        Scenario("Unapproved lewd selection cannot alter existing output", path => {
            var cfg=Config();var output=PatchGenerator.Generate([cfg],path,"SPID");var before=File.ReadAllBytes(output.PluginPath);cfg.Pieces[0].Name="Robe Slutty";
            Reject(()=>PatchGenerator.Generate([cfg],path,"SPID"));Require(before.SequenceEqual(File.ReadAllBytes(output.PluginPath)),"Previous output unchanged");
        });
        Scenario("Overlapping garment alternatives cannot produce invisible NPCs", path => {
            var cfg=Config();cfg.Pieces.Add(Part(0x805,"Alternative cuirass","Body"));Reject(()=>PatchGenerator.Generate([cfg],path,"SPID"));Require(!File.Exists(Path.Combine(path,PatchGenerator.OutputPluginName)),"No invalid patch committed");
        });
        Scenario("Legacy merchants-only output needs no empty NPC comment section", path => {
            var cfg=Config(); cfg.DistributeToNpcs=false; cfg.TargetRules.Clear(); cfg.MerchantCategories=[MerchantStockService.Clothing]; cfg.MerchantTargets=[new("Skyrim.esm",0x300,"Clothes",MerchantStockService.Clothing)];
            var output=PatchGenerator.Generate([cfg],path,"SPID"); File.WriteAllText(output.DistributionPath,"; Legacy shop stock: no NPC outfit rules.\n");
            Require(PatchHealthService.Inspect(path,PatchInventoryService.ReadManifest(path)).Values.Single().Status=="Ready","Validated trader stock does not require an empty NPC comment block");
            var other=Config("AnotherOutfit.esp"); PatchGenerator.Generate([other],path,"SPID");
            Require(PatchInventoryService.ReadManifest(path).Entries.Count==2,"Legacy shop stock survives an unrelated outfit update");
            using var esp=Open(output.PluginPath); var id=esp.Outfits.Single(o=>o.EditorID==PatchGenerator.StableEditorIdFor(cfg.Source.Plugin,cfg.OutfitName)).FormKey.ID;
            File.AppendAllText(output.DistributionPath,$"Outfit = 0x{id:X6}~{PatchGenerator.OutputPluginName}|NONE|NONE|NONE|NONE|NONE|100\n");
            Require(PatchHealthService.Inspect(path,PatchInventoryService.ReadManifest(path)).Any(pair=>!pair.Key.DistributeToNpcs&&pair.Value.Status=="Rules need repair"),"An unexpected NPC rule cannot hide behind legacy merchant compatibility");
        });
        Scenario("All-optional NPC outfit is rejected before it can roll naked", path => {
            var cfg=Config();foreach(var piece in cfg.Pieces)piece.PartMode="Optional";Reject(()=>PatchGenerator.Generate([cfg],path,"SPID"));
        });
        Scenario("Discovery reads two output libraries independently of destination", path => {
            var a=Path.Combine(path,"ProfileA");var b=Path.Combine(path,"ProfileB");PatchGenerator.Generate([Config()],a,"SPID");PatchGenerator.Generate([Config()],b,"SPID");
            var entries=DetectedPatchReader.Read(PatchFileDiscovery.Find([path]));Require(entries.Count==2 && entries.All(e=>e.PresentInPlugin),"Both profile files found");Require(entries.Select(e=>e.Directory).Distinct().Count()==2,"Locations retained");
        });
        Scenario("Inventory absence recovers actual ESP outfit and optional pieces", path => {
            var cfg=Config();cfg.Pieces[1].PartMode="Optional";PatchGenerator.Generate([cfg],path,"SPID");File.Delete(PatchGenerator.ManifestPathFor(path));
            var entry=DetectedPatchReader.Read([Path.Combine(path,PatchGenerator.OutputPluginName)]).Single();Require(entry.PresentInPlugin && !entry.CanManage && entry.Entry.IncludedPieces.Count==2,"Read-only recovery follows leveled items");
        });
        Scenario("Inventory cannot claim a source not present in the generated ESP", path => {
            PatchGenerator.Generate([Config()],path,"SPID");var manifest=JsonNode.Parse(File.ReadAllText(PatchGenerator.ManifestPathFor(path)))!;manifest["Entries"]![0]!["SourcePlugin"]="Fabricated.esp";File.WriteAllText(PatchGenerator.ManifestPathFor(path),manifest.ToJsonString());
            var found=DetectedPatchReader.Read([Path.Combine(path,PatchGenerator.OutputPluginName)]);Require(!found.Any(e=>e.Entry.SourcePlugin=="Fabricated.esp"&&e.PresentInPlugin),"No false already-patched status");
        });
        Scenario("Bad manifest JSON does not conceal a readable ESP", path => {
            PatchGenerator.Generate([Config()],path,"SPID");File.WriteAllText(PatchGenerator.ManifestPathFor(path),"{not valid");var found=DetectedPatchReader.Read([Path.Combine(path,PatchGenerator.OutputPluginName)]);Require(found.Any(e=>e.PresentInPlugin&&!e.CanManage),"Fallback recovery works");
        });
        Scenario("Null manifest collections cannot crash all patch discovery", path => {
            PatchGenerator.Generate([Config()],path,"SPID");var manifest=JsonNode.Parse(File.ReadAllText(PatchGenerator.ManifestPathFor(path)))!;manifest["Entries"]![0]!["PartChoices"]=null;manifest["Entries"]![0]!["MerchantStock"]=null;File.WriteAllText(PatchGenerator.ManifestPathFor(path),manifest.ToJsonString());
            Require(DetectedPatchReader.Read([Path.Combine(path,PatchGenerator.OutputPluginName)]).Any(e=>e.PresentInPlugin),"Corrupt metadata does not erase actual source links");
        });
        Scenario("Null manifest entries cannot crash all patch discovery", path => {
            PatchGenerator.Generate([Config()],path,"SPID");var manifest=JsonNode.Parse(File.ReadAllText(PatchGenerator.ManifestPathFor(path)))!;manifest["Entries"]!.AsArray().Add((JsonNode?)null);File.WriteAllText(PatchGenerator.ManifestPathFor(path),manifest.ToJsonString());
            Require(DetectedPatchReader.Read([Path.Combine(path,PatchGenerator.OutputPluginName)]).Any(e=>e.PresentInPlugin),"Null entry isolated");
        });
        Scenario("Broken ESP is isolated from another profile's healthy output", path => {
            var good=Path.Combine(path,"Good");PatchGenerator.Generate([Config()],good,"SPID");var bad=Path.Combine(path,"Bad");Directory.CreateDirectory(bad);File.WriteAllBytes(Path.Combine(bad,PatchGenerator.OutputPluginName),[1,2,3,4,5]);
            Require(DetectedPatchReader.Read(PatchFileDiscovery.Find([path])).Any(e=>e.Directory==good&&e.PresentInPlugin),"Other profile remains visible");
        });
        Scenario("Missing INI is flagged instead of reported ready", path => {
            var output=PatchGenerator.Generate([Config()],path,"SPID");File.Delete(output.DistributionPath);var found=DetectedPatchReader.Read([output.PluginPath]).Single();Require(found.PresentInPlugin && found.Health!="Files found","Actual ESP retained with missing-rules warning");
        });
        Scenario("File discovery ignores backups and does not scan them as profiles", path => {
            PatchGenerator.Generate([Config()],Path.Combine(path,"Current"),"SPID");PatchGenerator.Generate([Config("Old.esp")],Path.Combine(path,"Backups","Old"),"SPID");
            Require(DetectedPatchReader.Read(PatchFileDiscovery.Find([path])).Count==1,"Backup excluded");
        });
        Scenario("Experimental file family is read-only in the release writer", path => {
            var output=PatchGenerator.Generate([Config()],path,"SPID");var released=Path.Combine(path,"Release");Directory.CreateDirectory(released);
            var esp=Path.Combine(released,"SkyrimSPIDPresetTest.esp");File.Copy(output.PluginPath,esp);var before=File.ReadAllBytes(esp);
            var found=DetectedPatchReader.Read([esp]);Require(found.Any(e=>e.PresentInPlugin&&!e.CanManage),"Released ESP recovered read-only");Require(before.SequenceEqual(File.ReadAllBytes(esp)),"No write during inspection");Reject(()=>PatchGenerator.Generate([Config()],released,"SPID"));Require(before.SequenceEqual(File.ReadAllBytes(esp)),"Writer refuses released folder");
        });
        Scenario("Equipment classifier permits connected recipes and enchantments", path => {
            var records=new List<WearableContentRecord>{new("armor","Armor",["enchant"]),new("enchant","ObjectEffect",["effect"]),new("effect","MagicEffect",[]),new("recipe","ConstructibleObject",["armor"])};
            Require(WearableContentClassifier.Classify(records,["armor"])=="","Equipment support kept");
            records.Add(new("unrelatedSpell","Spell",[]));Require(WearableContentClassifier.Classify(records,["armor"]).Contains("Mixed content"),"Unrelated spells hidden");
        });
        Scenario("Actual binary armor-only versus NPC/quest content classification", path => {
            WriteBase(path);var pure=Gear("ArcherArmor.esp");pure.Weapons.AddNew("MatchingBow");pure.WriteToBinary(Path.Combine(path,pure.ModKey.FileName));
            var mixed=Gear("AdventurerArmor.esp");mixed.Npcs.AddNew("Follower");mixed.Quests.AddNew("Quest");mixed.WriteToBinary(Path.Combine(path,mixed.ModKey.FileName));
            var result=Scan(path,["ArcherArmor.esp","AdventurerArmor.esp"]);Require(!result.Plugins.Single(p=>p.Plugin=="ArcherArmor.esp").IsFiltered,"Pure actual binary retained");Require(result.Plugins.All(p=>!p.Selected),"Scanning must not automatically check outfits for setup");Require(result.Plugins.Single(p=>p.Plugin=="ArcherArmor.esp").Weapons.Count==1,"Bundled weapon found");Require(result.Plugins.Single(p=>p.Plugin=="AdventurerArmor.esp").IsFiltered,"Title cannot override mixed actual records");
        });
        Scenario("Official/CC records excluded from candidate selection", path => {
            WriteBase(path);var cc=Gear("ccTestArmor001.esl");cc.WriteToBinary(Path.Combine(path,cc.ModKey.FileName));var outfit=Gear("Skyrim Adventurer Armor.esp");outfit.WriteToBinary(Path.Combine(path,outfit.ModKey.FileName));
            var result=Scan(path,["ccTestArmor001.esl","Skyrim Adventurer Armor.esp"]);Require(result.Plugins.Count==1 && result.Plugins[0].Plugin=="Skyrim Adventurer Armor.esp","Only actual official names/prefixes excluded");
        });
        Scenario("Implicit Skyrim.esm is supplied even when plugins.txt omits it", path => {
            var data=Path.Combine(path,"Data");Directory.CreateDirectory(data);WriteBase(data);var stage=Path.Combine(path,"Staged");Directory.CreateDirectory(stage);var gear=Gear("MageCore.esp");gear.ModHeader.MasterReferences.Add(new MasterReference{Master=ModKey.FromFileName("Skyrim.esm")});gear.WriteToBinary(Path.Combine(stage,gear.ModKey.FileName));
            var list=Path.Combine(path,"plugins.txt");File.WriteAllText(list,"*MageCore.esp\n");var result=new WearableScanService().Scan(stage,new AppPreferences{PluginListPath=list,BaseGameDataPath=data},null,null,CancellationToken.None);
            Require(result.Plugins.Single().CanPatch && result.LoadOrder.Contains("Skyrim.esm"),"No false missing/inactive master");
        });
        Scenario("Cached audience matching changes when sex or filters change", path => {
            var farmer=Choice("Farmer",1,"Class");var race=Choice("NordRace",2,"Race");var otherRace=Choice("BretonRace",3,"Race");
            var female=Profile(0x900,"Female",farmer,race);var male=Profile(0x901,"Male",farmer,otherRace);var target=new NpcTargetingService([farmer,race,otherRace],[female,male]);var rule=target.BuildRule("Farmers",NpcTargetingService.OfficialCollection);
            Require(target.CountMatches(rule)==2 && target.CountMatches(rule,"Female")==1,"Sex invalidates memo");rule.Filters.Add(otherRace);Require(target.CountMatches(rule,"Female")==0 && target.CountMatches(rule)==1,"New race invalidates memo");rule.Filters.Remove(otherRace);Require(target.CountMatches(rule)==2,"Removed refinement invalidates memo");
        });
        Scenario("Optional state and saved choices round-trip across sessions", path => {
            var cfg=Config();cfg.Pieces[1].IsRequired=false;cfg.Pieces[1].OptionalChance=80;Require(cfg.Pieces[1].Include && cfg.Pieces[1].PartMode=="Optional","Unticking Required creates Optional");
            var prefs=new AppPreferences{SmartFilterRevision=WearableFilterService.Revision};prefs.DistributionSettings[cfg.Source.Plugin]=new SavedDistributionOptions{DistributeToNpcs=false,MerchantCategories=[MerchantStockService.Clothing],PartChoices=cfg.Pieces.Select(p=>new SavedPartChoice(p.FormId,p.PartMode,p.OptionalChance)).ToList()};
            var file=Path.Combine(path,"settings.json");PreferencesService.Save(file,prefs);var loaded=PreferencesService.Load(file).DistributionSettings[cfg.Source.Plugin];Require(!loaded.DistributeToNpcs && loaded.MerchantCategories.Single()==MerchantStockService.Clothing && loaded.PartChoices[1].Mode=="Optional" && loaded.PartChoices[1].Chance==80,"Saved settings retained");
        });
        Scenario("Privacy-safe error reports exclude supplied exception text and paths", path => {
            var assembly=typeof(App).Assembly;var log=assembly.GetType("WearableDistributor.SupportLog")!;
            var eventType=assembly.GetType("WearableDistributor.DiagnosticEvent")!;var levelType=assembly.GetType("WearableDistributor.DiagnosticLevel")!;
            var marker="PRIVATE-TEST-MARKER C:\\Users\\NeverRecordThis\\mods\\SecretMod.esp";
            var method=log.GetMethod("Record",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
            var file=(string?)method.Invoke(null,[Enum.Parse(eventType,"PatchSetupFailed"),new InvalidOperationException(marker),Enum.Parse(levelType,"Error")]);
            Require(file is not null && File.Exists(file),"Error saved in isolated test directory");var content=File.ReadAllText(file!);Require(!content.Contains("PRIVATE-TEST-MARKER")&&!content.Contains("NeverRecordThis")&&!content.Contains("SecretMod.esp"),"No arbitrary text or personal paths");
        });
        Scenario("Null inventory entry rejects edits without touching the ESP", path => {
            var output=PatchGenerator.Generate([Config()],path,"SPID");var before=File.ReadAllBytes(output.PluginPath);var node=JsonNode.Parse(File.ReadAllText(PatchGenerator.ManifestPathFor(path)))!;node["Entries"]!.AsArray().Add((JsonNode?)null);File.WriteAllText(PatchGenerator.ManifestPathFor(path),node.ToJsonString());
            Reject(()=>PatchGenerator.Generate([Config()],path,"SPID"));Require(before.SequenceEqual(File.ReadAllBytes(output.PluginPath)),"No malformed metadata committed");
        });
        Console.WriteLine($"Current regressions: {Passed} passed, {Failed} failed.");
        if(Failed>0)throw new InvalidOperationException("Current regressions failed: "+Failed);
    }
    private static PluginPatchOptions Config(string plugin="RegressionArmor.esp")
    {
        var pieces=new List<WearableRecord>{Part(0x800,"Robe","Body",plugin),Part(0x801,"Hood","Head",plugin)};
        return new PluginPatchOptions{Source=new PluginEntry{Plugin=plugin,Pieces=pieces},Pieces=pieces,Chance="25",TargetRules=[new DistributionTargetRule{Group="Bandits",Filters=[Choice("BanditFaction",1,"Faction")],NpcResolutionComplete=true,ResolvedNpcFormIds=["0x001234~Skyrim.esm"]}]};
    }
    private static PluginPatchOptions ShopConfig()
    {
        var cfg=Config();cfg.DistributeToNpcs=false;cfg.TargetRules.Clear();cfg.MerchantCategories=MerchantStockService.Categories.ToList();cfg.MerchantTargets=[new("Skyrim.esm",0x100,"Smith",MerchantStockService.Blacksmiths),new("Skyrim.esm",0x200,"General",MerchantStockService.GeneralGoods),new("Skyrim.esm",0x300,"Clothes",MerchantStockService.Clothing)];cfg.Source.Weapons=[new WearableRecord{Plugin=cfg.Source.Plugin,LocalId=0x810,Name="Matching sword",Category="Weapon"}];return cfg;
    }
    private static WearableRecord Part(uint id,string name,string slots,string plugin="RegressionArmor.esp")=>new(){Plugin=plugin,LocalId=id,Name=name,EditorId=name,Slots=slots};
    private static KeywordChoice Choice(string name,uint id,string kind)=>new(){Plugin="Skyrim.esm",LocalId=id,EditorId=name,TargetKind=kind,Category=kind};
    private static NpcTargetProfile Profile(uint id,string sex,params KeywordChoice[] targets)=>new(){FormId=$"0x{id:X6}~Skyrim.esm",Plugin="Skyrim.esm",Gender=sex,IsHumanoid=true,IsEncounterable=true,TargetForms=targets.Select(t=>t.FormId).ToHashSet(StringComparer.OrdinalIgnoreCase)};
    private static ISkyrimModDisposableGetter Open(string path)=>SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(path),SkyrimRelease.SkyrimSE);
    private static SkyrimMod Gear(string name){var mod=new SkyrimMod(ModKey.FromFileName(name),SkyrimRelease.SkyrimSE);mod.Armors.AddNew("BodyArmor").BodyTemplate=new BodyTemplate{FirstPersonFlags=BipedObjectFlag.Body};return mod;}
    private static void WriteBase(string path)=>new SkyrimMod(ModKey.FromFileName("Skyrim.esm"),SkyrimRelease.SkyrimSE).WriteToBinary(Path.Combine(path,"Skyrim.esm"));
    private static WearableScanResult Scan(string path,string[] plugins){var list=Path.Combine(path,"plugins.txt");File.WriteAllLines(list,plugins.Select(p=>"*"+p));return new WearableScanService().Scan(path,new AppPreferences{PluginListPath=list},null,null,CancellationToken.None);}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static void Reject(Action action){try{action();}catch(Exception error)when(error is InvalidOperationException or InvalidDataException){return;}throw new InvalidOperationException("Invalid setup was not rejected");}
}
