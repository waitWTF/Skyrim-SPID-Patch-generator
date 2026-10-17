using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace WearableDistributor;

public sealed record ScanProgress(string Stage, int Completed, int Total, string CurrentPlugin = "")
{
    public double Percent => Total == 0 ? 0 : 100.0 * Completed / Total;
}

public sealed class WearableScanResult
{
    public List<PluginEntry> Plugins { get; } = [];
    public List<KeywordChoice> Targets { get; } = [];
    public List<NpcTargetProfile> Npcs { get; } = [];
    public List<MerchantTarget> Merchants { get; } = [];
    public List<(string Path, string Line)> DistributionRules { get; } = [];
    public List<string> Diagnostics { get; } = [];
    public List<string> LoadOrder { get; } = [];
    public bool HasProfileLoadOrder { get; set; }
    public string CreateReport() => $"Skyrim SPID Patch Generator scan report — {DateTime.Now:g}\n" +
        $"Wearable plugins: {Plugins.Count}; eligible NPC bases: {Npcs.Count(npc => npc.CanReceiveWearables)}; target records: {Targets.Count}\n" +
        $"Load order: {(HasProfileLoadOrder ? "active profile list plus installed implicit masters/Creation Club" : "inferred dependency order; select a profile list for exact override priority")}\n\n" +
        string.Join("\n", Diagnostics) + "\n\nPlugins in scan order:\n" + string.Join("\n", LoadOrder);
}

public sealed class WearableScanService
{
    private static readonly string[] OfficialNames = ["Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm"];
    private static readonly HashSet<string> AssetFolders = new([".git", "Backups", "meshes", "textures", "sound", "sounds", "music", "scripts", "source", "interface", "strings", "grass", "shadersfx", "shadercache", "CalienteTools", "Nemesis_Engine", "Pandora_Engine", "animations"], StringComparer.OrdinalIgnoreCase);

    public WearableScanResult Scan(string root, AppPreferences preferences, string? baselineDataPath, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var result = new WearableScanResult();
        progress?.Report(new ScanProgress("Finding installed plugins", 0, 0));
        var discovered = EnumerateFiles(root, cancellationToken, result.Diagnostics).ToList();
        var paths = SelectPluginPaths(root, discovered.Where(IsPluginFile), result.Diagnostics);
        if (!string.IsNullOrWhiteSpace(baselineDataPath) && Directory.Exists(baselineDataPath))
            foreach (var name in OfficialNames)
            {
                var file = Path.Combine(baselineDataPath, name);
                if (!paths.ContainsKey(name) && File.Exists(file)) paths[name] = file;
            }
        var implicitOrder = ReadImplicitPluginOrder(root, baselineDataPath, paths, result.Diagnostics);
        var order = ReadProfilePluginOrder(preferences.PluginListPath, result.Diagnostics);
        order.RemoveAll(name => name.Equals(PatchGenerator.OutputPluginName, StringComparison.OrdinalIgnoreCase));
        result.HasProfileLoadOrder = !string.IsNullOrWhiteSpace(preferences.PluginListPath);
        if (result.HasProfileLoadOrder)
        {
            foreach (var missing in order.Where(name => !paths.ContainsKey(name))) result.Diagnostics.Add($"Missing active plugin: {missing}. Its records cannot be used until the scan folder contains it.");
            order = OfficialNames.Where(paths.ContainsKey).Concat(implicitOrder).Concat(order).Distinct(StringComparer.OrdinalIgnoreCase).Where(paths.ContainsKey).ToList();
        }
        else
        {
            order = OfficialNames.Where(paths.ContainsKey).Concat(implicitOrder).Concat(paths.Keys.Order(StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            result.Diagnostics.Add("No active profile plugin list selected. Disabled staging mods may be included; override priority is inferred from masters and filenames.");
        }
        var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in order.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(paths[name]), SkyrimRelease.SkyrimSE);
                headers[name] = mod.ModHeader.MasterReferences.Select(master => master.Master.FileName.String).ToList();
            }
            catch (Exception ex) { result.Diagnostics.Add($"Cannot read {name}: {ex.Message}"); order.Remove(name); }
        }
        if (!result.HasProfileLoadOrder) order = OrderByDependencies(order, headers, result.Diagnostics);
        var positions = order.Select((name, index) => (name, index)).ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
        var invalid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in order)
        {
            var issue = ValidateMasters(name, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(issue)) result.Diagnostics.Add($"Skipped unusable plugin records: {name}: {issue}.");
            foreach (var master in headers[name]) if (positions.TryGetValue(master, out var index) && index > positions[name]) result.Diagnostics.Add($"Load order warning: {master} appears after dependent {name}.");
        }
        var inis = discovered.Where(path => Path.GetExtension(path).Equals(".ini", StringComparison.OrdinalIgnoreCase) && WearableFilterService.IsDistributionIni(path))
            .Select(path => (Path: path, Text: ReadText(path, result.Diagnostics))).ToList();
        result.DistributionRules.AddRange(inis.SelectMany(file => file.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.TrimStart().StartsWith(';') && !string.IsNullOrWhiteSpace(line)).Select(line => (file.Path, line.Trim()))));

        var npcs = new Dictionary<string, NpcSnapshot>(StringComparer.OrdinalIgnoreCase);
        var races = new Dictionary<string, RaceSnapshot>(StringComparer.OrdinalIgnoreCase);
        var targets = new Dictionary<string, KeywordChoice>(StringComparer.OrdinalIgnoreCase);
        var packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var placed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var quests = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var leveled = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var containers = new Dictionary<string, MerchantTarget>(StringComparer.OrdinalIgnoreCase);
        var vendorReferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var vendors = new Dictionary<string, VendorSnapshot>(StringComparer.OrdinalIgnoreCase);
        var listNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var done = 0;
        foreach (var name in order)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgress("Reading plugins and NPC populations", done, order.Count, name));
            try
            {
                using var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(paths[name]), SkyrimRelease.SkyrimSE);
                var validationIssue = ValidateMasters(name, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                var stage = new PluginSnapshot();
                if (string.IsNullOrWhiteSpace(validationIssue))
                {
                    foreach (var npc in mod.Npcs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        stage.Npcs[Form(npc.FormKey)] = npc.IsDeleted ? null : new NpcSnapshot(npc, name);
                    }
                    foreach (var race in mod.Races)
                    {
                        stage.Races[Form(race.FormKey)] = race.IsDeleted ? null : new RaceSnapshot(race);
                        Remember(race, "Race", "Race");
                    }
                    foreach (var keyword in mod.Keywords) Remember(keyword, "Keyword", keyword.EditorID?.StartsWith("Job", StringComparison.OrdinalIgnoreCase) == true ? "Occupation" : "NPC type");
                    foreach (var faction in mod.Factions)
                    {
                        Remember(faction, "Faction", "Faction");
                        stage.Vendors[Form(faction.FormKey)] = faction.IsDeleted || !faction.Flags.HasFlag(Faction.FactionFlag.Vendor) || faction.MerchantContainer.FormKeyNullable is not { } chest || faction.VendorValues is null || faction.VendorValues.OnlyBuysStolenItems
                            ? null : new VendorSnapshot(faction.EditorID ?? "", Form(chest), Form(faction.VendorBuySellList.FormKeyNullable ?? FormKey.Null));
                    }
                    foreach (var container in mod.Containers)
                        stage.Containers[Form(container.FormKey)] = container.IsDeleted ? null : new MerchantTarget(container.FormKey.ModKey.FileName, container.FormKey.ID, container.EditorID ?? "", "");
                    foreach (var formList in mod.FormLists) stage.ListNames[Form(formList.FormKey)] = formList.IsDeleted ? null : formList.EditorID ?? "";
                    foreach (var actorClass in mod.Classes) Remember(actorClass, "Class", "Occupation / class");
                    foreach (var package in mod.Packages) stage.Packages[Form(package.FormKey)] = package.IsDeleted ? null : package.EditorID ?? "";
                    foreach (var list in mod.LeveledNpcs)
                        stage.Leveled[Form(list.FormKey)] = list.IsDeleted ? null : (list.Entries ?? []).Where(entry => entry.Data is not null && !entry.Data.Reference.IsNull).Select(entry => Form(entry.Data!.Reference.FormKey)).ToList();
                    foreach (var reference in mod.EnumerateMajorRecords())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (reference is IPlacedNpcGetter actor)
                            stage.Placed[Form(actor.FormKey)] = actor.IsDeleted || actor.Base.IsNull ? null : Form(actor.Base.FormKey);
                        else if (reference is IPlacedObjectGetter obj)
                        {
                            var key = Form(obj.FormKey);
                            var baseKey = Form(obj.Base.FormKey);
                            // Retain container placements only, rather than every static, light and door in the game.
                            if (!obj.IsDeleted && !obj.Base.IsNull && (containers.ContainsKey(baseKey) || stage.Containers.ContainsKey(baseKey))) stage.VendorReferences[key] = baseKey;
                            else if (vendorReferences.ContainsKey(key)) stage.VendorReferences[key] = null;
                        }
                    }
                    foreach (var quest in mod.Quests)
                        stage.Quests[Form(quest.FormKey)] = quest.IsDeleted ? null : quest.EnumerateFormLinks().Where(link => !link.FormKey.IsNull).Select(link => Form(link.FormKey)).ToList();
                }
                var pieces = mod.Armors.Where(armor => !armor.IsDeleted && armor.FormKey.ModKey.Equals(mod.ModKey)).Select(armor => new WearableRecord
                {
                    Plugin = name, LocalId = armor.FormKey.ID, EditorId = armor.EditorID ?? "", Name = armor.Name?.String ?? armor.EditorID ?? "Unnamed armor",
                    Slots = armor.BodyTemplate?.FirstPersonFlags.ToString() ?? "", SourcePath = paths[name]
                }).Where(item => !WearableFilterService.IsExcludedRecord(item)).ToList();
                var weapons = pieces.Count == 0 ? [] : mod.Weapons.Where(weapon => !weapon.IsDeleted && weapon.FormKey.ModKey.Equals(mod.ModKey) && !weapon.MajorFlags.HasFlag(Weapon.MajorFlag.NonPlayable))
                    .Select(weapon => new WearableRecord { Plugin = name, LocalId = weapon.FormKey.ID, EditorId = weapon.EditorID ?? "", Name = weapon.Name?.String ?? weapon.EditorID ?? "Weapon", Category = "Weapon", SourcePath = paths[name] }).ToList();
                // Commit only after the entire plugin was parsed. A late parser error cannot leave partial winning overrides behind.
                Apply(npcs, stage.Npcs); Apply(races, stage.Races); Apply(targets, stage.Targets); Apply(packages, stage.Packages);
                Apply(placed, stage.Placed); Apply(quests, stage.Quests); Apply(leveled, stage.Leveled);
                Apply(containers, stage.Containers); Apply(vendorReferences, stage.VendorReferences); Apply(vendors, stage.Vendors); Apply(listNames, stage.ListNames);
                if (pieces.Count > 0)
                {
                    var reason = WearableFilterService.GetFilterReason(paths[name], preferences.AdditionalExclusions, inis);
                    if (!string.IsNullOrWhiteSpace(validationIssue)) reason = Append(reason, validationIssue);
                    if (!preferences.IncludeAccessories && pieces.All(WearableFilterService.IsAccessoryOnly)) reason = Append(reason, "Accessory-only items (enable in Preferences to scan these)");
                    result.Plugins.Add(new PluginEntry { Plugin = name, SourcePath = paths[name], Pieces = pieces, Weapons = weapons, FilterReason = reason, ValidationIssue = validationIssue,
                        Selected = string.IsNullOrWhiteSpace(validationIssue) && (string.IsNullOrWhiteSpace(reason) || preferences.IncludedPlugins.Contains(name)) && !preferences.IgnoredPlugins.Contains(name) });
                }
                result.LoadOrder.Add(name);
                void Remember(ISkyrimMajorRecordGetter record, string kind, string category)
                {
                    var id = Form(record.FormKey);
                    if (record.IsDeleted) stage.Targets[id] = null;
                    else if (!string.IsNullOrWhiteSpace(record.EditorID)) stage.Targets[id] = new KeywordChoice { EditorId = record.EditorID, Plugin = record.FormKey.ModKey.FileName,
                        LocalId = record.FormKey.ID, TargetKind = kind, Category = category };
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { invalid[name] = "Plugin could not be fully parsed"; result.Diagnostics.Add($"Cannot scan {name}: {ex.Message}. Partial records were discarded."); }
            done++;
        }

        progress?.Report(new ScanProgress("Resolving NPC templates and target groups", done, order.Count));
        var references = placed.Values.Concat(quests.Values.SelectMany(list => list)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reachable = new HashSet<string>(references, StringComparer.OrdinalIgnoreCase); var pending = new Queue<string>(references);
        while (pending.TryDequeue(out var id))
            if (leveled.TryGetValue(id, out var children)) foreach (var child in children) if (reachable.Add(child)) pending.Enqueue(child);
        var resolved = new Dictionary<string, List<NpcSnapshot>>(StringComparer.OrdinalIgnoreCase);
        var templateWarnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in npcs.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var alternatives = Resolve(id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (alternatives.Count == 0) { var unknown = npcs[id].Copy(); unknown.InheritanceComplete = false; alternatives = [unknown]; }
            var snapshot = alternatives[0];
            var variants = alternatives.Select(alternative =>
            {
                races.TryGetValue(alternative.Race, out var race);
                var forms = new HashSet<string>(alternative.Factions.Concat(alternative.Keywords), StringComparer.OrdinalIgnoreCase);
                forms.UnionWith(race?.Keywords ?? []);
                if (!string.IsNullOrWhiteSpace(alternative.Class)) forms.Add(alternative.Class);
                if (!string.IsNullOrWhiteSpace(alternative.Race)) forms.Add(alternative.Race);
                var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (alternative.Packages.Any(package => packages.TryGetValue(package, out var label) &&
                    (label.Contains("farming", StringComparison.OrdinalIgnoreCase) || label.Contains("farmwork", StringComparison.OrdinalIgnoreCase) || label.Contains("farmfield", StringComparison.OrdinalIgnoreCase)))) tags.Add("Farmer");
                return (Variant: new NpcTargetVariant { Gender = alternative.Female ? "Female" : "Male", TargetForms = forms, OccupationTags = tags },
                    Child: race?.Child == true || (race?.EditorId.Contains("child", StringComparison.OrdinalIgnoreCase) ?? false),
                    Humanoid: race?.Playable == true || forms.Any(form => targets.TryGetValue(form, out var keyword) && keyword.EditorId.Equals("ActorTypeNPC", StringComparison.OrdinalIgnoreCase)));
            }).ToList();
            var profile = new NpcTargetProfile
            {
                FormId = id, Plugin = snapshot.OriginPlugin, WinningPlugin = snapshot.WinningPlugin, Name = snapshot.Name, EditorId = snapshot.EditorId,
                Gender = alternatives.All(item => item.Female == snapshot.Female) ? (snapshot.Female ? "Female" : "Male") : "Any",
                RaceFormId = alternatives.All(item => item.Race.Equals(snapshot.Race, StringComparison.OrdinalIgnoreCase)) ? snapshot.Race : "",
                TargetForms = variants.SelectMany(item => item.Variant.TargetForms).ToHashSet(StringComparer.OrdinalIgnoreCase),
                OccupationTags = variants.SelectMany(item => item.Variant.OccupationTags).ToHashSet(StringComparer.OrdinalIgnoreCase), Variants = variants.Select(item => item.Variant).ToList(),
                HasUnresolvedTemplate = alternatives.Any(item => !item.InheritanceComplete), UsesTemplateInventory = snapshot.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Inventory),
                IsChild = variants.Any(item => item.Child), IsHumanoid = variants.All(item => item.Humanoid),
                IsEncounterable = reachable.Contains(id) && !alternatives.Any(item => item.FacePreset) && !IsTestEditorId(snapshot.EditorId) && !id.Equals("0x000007~Skyrim.esm", StringComparison.OrdinalIgnoreCase),
                EncounterEvidence = reachable.Contains(id) ? "Referenced by a placed actor, reachable leveled list, or quest" : "No encounter reference found"
            };
            result.Npcs.Add(profile);
            if (profile.CanReceiveWearables) result.Targets.Add(new KeywordChoice { EditorId = profile.Name, Plugin = profile.Plugin, LocalId = npcs[id].LocalId, TargetKind = "Npc", Category = "NPC audience" });
        }
        var used = result.Npcs.Where(profile => profile.CanReceiveWearables).SelectMany(profile => profile.TargetForms).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var vendorChestReferences = vendors.Values.Select(vendor => vendor.ContainerReference).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sharedWithNonVendor = vendorReferences.Where(pair => !vendorChestReferences.Contains(pair.Key)).Select(pair => pair.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (factionId, vendor) in vendors)
        {
            if (!used.Contains(factionId) || !vendorReferences.TryGetValue(vendor.ContainerReference, out var baseId) || !containers.TryGetValue(baseId, out var container)) continue;
            if (sharedWithNonVendor.Contains(baseId)) continue;
            var category = MerchantStockService.Classify(vendor.EditorId + " " + listNames.GetValueOrDefault(vendor.BuySellList, "") + " " + container.EditorId);
            if (category is not null) result.Merchants.Add(container with { Category = category });
        }
        var distinctMerchants = result.Merchants.DistinctBy(target => target.Plugin + "|" + target.LocalId + "|" + target.Category, StringComparer.OrdinalIgnoreCase).ToList();
        result.Merchants.Clear(); result.Merchants.AddRange(distinctMerchants);
        result.Diagnostics.Add("Merchant stock targets: " + string.Join(", ", MerchantStockService.Categories.Select(category => category + "=" + result.Merchants.Count(target => target.Category == category))) + ". Derived from encountered NPC vendor factions and their linked merchant containers.");
        result.Targets.AddRange(targets.Values.Where(target => used.Contains(target.FormId)));
        result.Targets.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.EditorId, right.EditorId)); result.Plugins.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Plugin, right.Plugin));
        result.Diagnostics.AddRange(templateWarnings);
        result.Diagnostics.Add($"NPC population: {result.Npcs.Count} effective bases; {result.Npcs.Count(profile => profile.CanReceiveWearables)} adult humanoids with encounter references. Children, creatures, player, face presets, and unreferenced templates are excluded.");
        var inherited = result.Npcs.Count(profile => profile.CanReceiveWearables && profile.UsesTemplateInventory);
        if (inherited > 0) result.Diagnostics.Add($"{inherited} eligible NPC bases inherit inventory/outfit data. SkyPatcher default outfits on these bases need in-game verification; template owners are never broadened into the audience.");
        var variable = result.Npcs.Count(profile => profile.CanReceiveWearables && profile.Variants.Count > 1);
        if (variable > 0) result.Diagnostics.Add($"{variable} eligible NPC bases have multiple leveled-template outcomes. Role/race/sex selections include a base only when every possible outcome matches.");
        progress?.Report(new ScanProgress("Scan complete", order.Count, order.Count));
        return result;

        string ValidateMasters(string name, HashSet<string> visiting)
        {
            if (invalid.TryGetValue(name, out var known)) return known;
            if (!visiting.Add(name)) return invalid[name] = "Plugin master dependency cycle";
            foreach (var master in headers.GetValueOrDefault(name) ?? [])
            {
                if (!positions.ContainsKey(master)) return invalid[name] = $"Missing or inactive master: {master}";
                var upstream = ValidateMasters(master, visiting);
                if (!string.IsNullOrWhiteSpace(upstream)) return invalid[name] = $"Required master {master} cannot load: {upstream}";
            }
            visiting.Remove(name); return "";
        }
        List<NpcSnapshot> Resolve(string id, HashSet<string> visiting)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (resolved.TryGetValue(id, out var cached)) return cached;
            if (!visiting.Add(id)) { templateWarnings.Add($"NPC template cycle: {id}. Uncertain branches are excluded."); return []; }
            List<NpcSnapshot> values = [];
            if (npcs.TryGetValue(id, out var original))
            {
                var child = original.Copy();
                if (string.IsNullOrWhiteSpace(child.Template) || child.TemplateFlags == 0) values.Add(child);
                else
                {
                    var parents = Resolve(child.Template, visiting);
                    if (parents.Count == 0) { child.InheritanceComplete = false; values.Add(child); templateWarnings.Add($"Unresolved NPC template: {id} → {child.Template}. Missing/cyclic alternatives are excluded."); }
                    else foreach (var parent in parents)
                    {
                        var item = child.Copy(); item.InheritanceComplete = parent.InheritanceComplete;
                        if (child.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Traits)) { item.Race = parent.Race; item.Female = parent.Female; }
                        if (child.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Stats)) item.Class = parent.Class;
                        if (child.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Factions)) item.Factions = [.. parent.Factions];
                        if (child.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Keywords)) item.Keywords = [.. parent.Keywords];
                        if (child.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.AIPackages)) item.Packages = [.. parent.Packages];
                        if (child.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.BaseData)) item.Name = parent.Name;
                        values.Add(item);
                    }
                }
            }
            else if (leveled.TryGetValue(id, out var branches))
                foreach (var branch in branches)
                {
                    var choices = Resolve(branch, visiting); if (choices.Count == 0) { values.Clear(); break; }
                    values.AddRange(choices);
                }
            visiting.Remove(id);
            values = values.DistinctBy(item => item.Signature(), StringComparer.OrdinalIgnoreCase).ToList();
            if (values.Count > 512) { templateWarnings.Add($"NPC template {id} has over 512 distinct outcomes. Excluded until it can be resolved safely."); values.Clear(); }
            return resolved[id] = values;
        }
    }

    private static void Apply<T>(Dictionary<string, T> destination, Dictionary<string, T?> staged) where T : class
    { foreach (var pair in staged) if (pair.Value == null) destination.Remove(pair.Key); else destination[pair.Key] = pair.Value; }
    private static string Append(string existing, string addition) => string.IsNullOrWhiteSpace(existing) ? addition : existing + "; " + addition;
    public static List<string> ReadProfilePluginOrder(string path, List<string>? diagnostics = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return [];
        if (!File.Exists(path)) throw new FileNotFoundException("The selected active profile plugin list does not exist. Choose its current location in Settings.", path);
        var lines = File.ReadAllLines(path).Select(line => line.Trim().TrimStart('\uFEFF')).Where(line => line.Length > 0 && !line.StartsWith('#') && !line.StartsWith(';')).ToList();
        var markerMode = Path.GetFileName(path).Equals("plugins.txt", StringComparison.OrdinalIgnoreCase) || lines.Any(line => line.StartsWith('*'));
        var order = lines.Where(line => !markerMode || line.StartsWith('*')).Select(line => line.TrimStart('*').Trim()).Where(IsPluginFile).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (order.Count == 0) diagnostics?.Add("The selected profile list contains no active plugins. Only installed implicit official/Creation Club masters will be scanned.");
        return order;
    }
    private static List<string> ReadImplicitPluginOrder(string root, string? baseline, Dictionary<string, string> paths, List<string> diagnostics)
    {
        var candidates = new[] { Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root)) ?? root, "Skyrim.ccc"), Path.Combine(root, "Skyrim.ccc"),
            string.IsNullOrWhiteSpace(baseline) ? "" : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(baseline)) ?? baseline, "Skyrim.ccc") };
        var ccc = candidates.FirstOrDefault(File.Exists); if (ccc == null) return [];
        var list = new List<string>();
        foreach (var line in File.ReadAllLines(ccc).Select(line => line.Trim().TrimStart('\uFEFF')).Where(line => !line.StartsWith('#') && !line.StartsWith(';') && IsPluginFile(line)))
        {
            var name = Path.GetFileName(line); if (string.IsNullOrWhiteSpace(name) || name != line) continue;
            if (!paths.ContainsKey(name) && !string.IsNullOrWhiteSpace(baseline)) { var file = Path.Combine(baseline, name); if (File.Exists(file)) paths[name] = file; }
            if (paths.ContainsKey(name)) list.Add(name);
        }
        diagnostics.Add($"Included {list.Count} installed implicit Creation Club/resource plugins listed by {ccc}."); return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static List<string> OrderByDependencies(IReadOnlyList<string> names, IReadOnlyDictionary<string, List<string>> masters, List<string>? diagnostics = null)
    {
        var result = new List<string>(); var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names) Visit(name); return result;
        void Visit(string name)
        {
            if (visited.Contains(name)) return;
            if (!visiting.Add(name)) { diagnostics?.Add($"Plugin dependency cycle involving {name}."); return; }
            foreach (var master in masters.GetValueOrDefault(name) ?? []) if (names.Contains(master, StringComparer.OrdinalIgnoreCase)) Visit(master);
            visiting.Remove(name); visited.Add(name); result.Add(name);
        }
    }
    private static Dictionary<string, string> SelectPluginPaths(string root, IEnumerable<string> files, List<string> diagnostics)
    {
        var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in files.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Key == null || group.Key.Equals(PatchGenerator.OutputPluginName, StringComparison.OrdinalIgnoreCase)) continue;
            var options = group.OrderBy(path => Path.GetDirectoryName(path)?.Equals(root, StringComparison.OrdinalIgnoreCase) == true ? 0 : 1).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(); selected[group.Key] = options[0];
            if (options.Count > 1) diagnostics.Add($"Duplicate plugin {group.Key}: using {options[0]}. Staging file priority is ambiguous; scan deployed Data or launch through MO2 for the effective winner.");
        }
        return selected;
    }
    private static IEnumerable<string> EnumerateFiles(string root, CancellationToken cancellationToken, List<string> diagnostics)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The selected Skyrim Data or mod staging folder does not exist.");
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested(); string[] files; string[] children;
            try { files = Directory.GetFiles(directory); children = Directory.GetDirectories(directory); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { diagnostics.Add($"Skipped folder {directory}: {ex.Message}"); continue; }
            foreach (var file in files) if (IsPluginFile(file) || Path.GetExtension(file).Equals(".ini", StringComparison.OrdinalIgnoreCase)) yield return file;
            foreach (var child in children)
            {
                if (AssetFolders.Contains(Path.GetFileName(child))) continue;
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) { diagnostics.Add($"Skipped linked folder {child} to prevent recursive loops. Select it directly if needed."); continue; }
                    pending.Push(child);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { diagnostics.Add($"Skipped folder {child}: {ex.Message}"); }
            }
        }
    }
    private static string ReadText(string path, List<string> diagnostics) { try { return File.ReadAllText(path); } catch (Exception ex) { diagnostics.Add($"Cannot read INI {path}: {ex.Message}"); return ""; } }
    private static bool IsTestEditorId(string editorId) => System.Text.RegularExpressions.Regex.IsMatch(
        NpcTargetingService.Humanize(editorId), @"(^|\W)(?:test|testing|dummy|dev)(?:\W|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static bool IsPluginFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".esp" or ".esm" or ".esl";
    private static string Form(FormKey key) => key.IsNull ? "" : $"0x{key.ID:X6}~{key.ModKey.FileName}";
    private sealed class PluginSnapshot
    {
        public Dictionary<string, MerchantTarget?> Containers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string?> VendorReferences { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, VendorSnapshot?> Vendors { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string?> ListNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, NpcSnapshot?> Npcs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RaceSnapshot?> Races { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, KeywordChoice?> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string?> Packages { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string?> Placed { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>?> Quests { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>?> Leveled { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
    private sealed record VendorSnapshot(string EditorId, string ContainerReference, string BuySellList);
    private sealed class RaceSnapshot(IRaceGetter race)
    {
        public string EditorId { get; } = race.EditorID ?? "";
        public bool Child { get; } = race.Flags.HasFlag(Race.Flag.Child);
        public bool Playable { get; } = race.Flags.HasFlag(Race.Flag.Playable);
        public List<string> Keywords { get; } = (race.Keywords ?? []).Where(link => !link.IsNull).Select(link => Form(link.FormKey)).ToList();
    }
    private sealed class NpcSnapshot
    {
        public string Name = "", EditorId = "", OriginPlugin = "", WinningPlugin = "", Race = "", Class = "", Template = "";
        public uint LocalId; public bool Female, FacePreset; public bool InheritanceComplete = true; public NpcConfiguration.TemplateFlag TemplateFlags;
        public List<string> Factions = [], Keywords = [], Packages = [];
        public NpcSnapshot(INpcGetter npc, string winner)
        {
            Name = npc.Name?.String ?? npc.EditorID ?? "Unnamed NPC"; EditorId = npc.EditorID ?? ""; OriginPlugin = npc.FormKey.ModKey.FileName; WinningPlugin = winner; LocalId = npc.FormKey.ID;
            Race = Form(npc.Race.FormKey); Class = Form(npc.Class.FormKey); Template = Form(npc.Template.FormKey);
            Female = npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female); FacePreset = npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.IsCharGenFacePreset); TemplateFlags = npc.Configuration.TemplateFlags;
            Factions = (npc.Factions ?? []).Where(faction => !faction.Faction.IsNull && faction.Rank >= 0).Select(faction => Form(faction.Faction.FormKey)).ToList();
            if (!npc.CrimeFaction.IsNull) Factions.Add(Form(npc.CrimeFaction.FormKey));
            Keywords = (npc.Keywords ?? []).Where(link => !link.IsNull).Select(link => Form(link.FormKey)).ToList(); Packages = (npc.Packages ?? []).Where(link => !link.IsNull).Select(link => Form(link.FormKey)).ToList();
        }
        public NpcSnapshot Copy() => (NpcSnapshot)MemberwiseClone();
        public string Signature() => string.Join("|", Race, Class, Female, FacePreset, InheritanceComplete, string.Join(",", Factions.Order(StringComparer.OrdinalIgnoreCase)), string.Join(",", Keywords.Order(StringComparer.OrdinalIgnoreCase)), string.Join(",", Packages.Order(StringComparer.OrdinalIgnoreCase)));
    }
}
