using System.Security.Cryptography;
using System.Text;
using System;
using System.IO;
using System.Text.Json.Nodes;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using WearableDistributor;

var root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "test-results", "integration-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]));
Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(root, "localapp"));
Environment.SetEnvironmentVariable("WEARABLE_DISTRIBUTOR_SETTINGS_DIR", Path.Combine(root, "settings"));
Console.WriteLine("Evidence: " + root);
if (args.FirstOrDefault() == "variant-installed") { InstalledVariantAudit.Run(args[1], args[2], args.Skip(3).ToArray()); return; }
if (args.FirstOrDefault() == "mesh-audit") { MeshRepairAudit.Run(args[1], args[2]); return; }
if (args.FirstOrDefault() == "visibility")
{
    InstalledVisibilityAudit.Run(args[1], args[2]);
    return;
}
if (args.FirstOrDefault() == "audit")
{
    InstalledAudit.Run(args[1], args[2]);
    return;
}
try
{
    var a = Configuration("MageCore.esp", 0x800, "Mages");
    var resultA = PatchGenerator.Generate([a], root, "SPID");
    Assert(resultA.OutfitCount == 1, "first patch creates one outfit");
    Assert(ReadOutfits(resultA.PluginPath) == 1, "first plugin contains one outfit");
    Assert(File.ReadLines(resultA.DistributionPath).Count(line => line.StartsWith("Outfit = ", StringComparison.Ordinal)) == 1, "first SPID file contains one rule");

    var b = Configuration("VenomBlade.esp", 0x801, "Bandits");
    PatchGenerator.Generate([b], root, "SPID");
    Assert(ReadOutfits(resultA.PluginPath) == 2, "second run preserves the first outfit");
    Assert(ReadMasters(resultA.PluginPath).Contains("MageCore.esp") && ReadMasters(resultA.PluginPath).Contains("VenomBlade.esp"), "merging preserves and extends plugin masters");
    Assert(File.ReadLines(resultA.DistributionPath).Count(line => line.StartsWith("Outfit = ", StringComparison.Ordinal)) == 2, "second run merges rules");

    a.Chance = "80";
    a.TargetRules[0].Group = "Necromancers";
    PatchGenerator.Generate([a], root, "SPID");
    Assert(ReadOutfits(resultA.PluginPath) == 2, "repatching does not duplicate outfit records");
    var rules = File.ReadLines(resultA.DistributionPath).Where(line => line.StartsWith("Outfit = ", StringComparison.Ordinal)).ToList();
    Assert(rules.Count == 2, "repatching replaces one section and keeps the other");
    Assert(rules.Any(line => line.EndsWith("|80", StringComparison.Ordinal)), "repatching updates the selected chance");
    var manifest = PatchInventoryService.ReadManifest(root);
    Assert(manifest.Entries.Count == 2, "inventory tracks both source plugins");
    Assert(manifest.Entries.Single(entry => entry.SourcePlugin == "MageCore.esp").Chance == 80, "inventory updates repatched settings");

    var darkVariant = Configuration("MageCore.esp", 0x803, "Necromancers");
    darkVariant.OutfitName = "Dark Variant";
    PatchGenerator.Generate([a, darkVariant], root, "SPID");
    Assert(ReadOutfits(resultA.PluginPath) == 3, "one source plugin can contribute multiple named outfits");
    Assert(PatchInventoryService.ReadManifest(root).Entries.Count == 3, "inventory distinguishes named outfit variants");
    var variantEntry = PatchInventoryService.ReadManifest(root).Entries.Single(entry => entry.OutfitName == "Dark Variant");
    PatchInventoryService.Remove(root, variantEntry);
    Assert(ReadOutfits(resultA.PluginPath) == 2, "removing one named variant keeps its sibling and unrelated outfits");
    Assert(File.ReadLines(resultA.DistributionPath).Count(line => line.StartsWith("Outfit = ", StringComparison.Ordinal)) == 2, "removing one named variant removes only its distribution section");
    Assert(PatchInventoryService.ReadManifest(root).Entries.Count == 2, "removing a variant updates the inventory");
    PatchInventoryService.RestoreLastBackup(root);
    Assert(ReadOutfits(resultA.PluginPath) == 3, "restore recovers the previous generated plugin");
    Assert(PatchInventoryService.ReadManifest(root).Entries.Count == 3, "restore recovers inventory metadata");

    PatchGenerator.Generate([a], root, "SPID");
    Assert(ReadOutfits(resultA.PluginPath) == 3, "repatching one named outfit preserves an unselected sibling variant");
    Assert(PatchInventoryService.ReadManifest(root).Entries.Count == 3, "repatching one named outfit preserves sibling inventory entries");
    PatchGenerator.Generate([a, darkVariant], root, "SPID");

    var withoutAudience = Configuration("UnsafeFallback.esp", 0x805, "Bandits");
    withoutAudience.Keywords = withoutAudience.TargetRules[0].Filters.ToList();
    withoutAudience.TargetRules.Clear();
    var beforeRejectedGeneration = File.ReadAllBytes(resultA.PluginPath);
    var rejected = false;
    try { PatchGenerator.Generate([withoutAudience], root, "SPID"); }
    catch (InvalidOperationException) { rejected = true; }
    Assert(rejected, "available targets must never substitute for a missing selected audience");
    Assert(beforeRejectedGeneration.SequenceEqual(File.ReadAllBytes(resultA.PluginPath)), "rejected audience leaves previous ESP unchanged");

    File.Delete(PatchGenerator.ManifestPathFor(root));
    var c = Configuration("TravelerGear.esp", 0x804, "Adventurers");
    PatchGenerator.Generate([c], root, "SPID");
    Assert(PatchInventoryService.ReadManifest(root).Entries.Count == 4, "legacy patch sections migrate into the inventory when a new outfit is added");
    Assert(ReadOutfits(resultA.PluginPath) == 4, "adding to a legacy patch preserves its prior outfit records");
    PackageChecks.Run(root);
    InventoryHealthChecks.Run(root);

    var sky = Path.Combine(root, "skypatcher");
    Directory.CreateDirectory(sky);
    var skyConfiguration = Configuration("Wenches.esp", 0x802, "Wenches", "100");
    skyConfiguration.TargetRules[0].NpcResolutionComplete = true;
    skyConfiguration.TargetRules[0].ResolvedNpcFormIds = ["0x001234~Wenches.esp", "0x001235~Wenches.esp"];
    var skyResult = PatchGenerator.Generate([skyConfiguration], sky, "SkyPatcher");
    Assert(File.Exists(skyResult.DistributionPath), "SkyPatcher output is created in its expected folder");
    Assert(File.ReadLines(skyResult.DistributionPath).Any(line => line.Contains("outfitDefault=", StringComparison.Ordinal)), "SkyPatcher rule includes outfitDefault");

    CheckRuleSemantics(root);
    CheckOutputSafety(root);
    ScannerChecks.Run(root);
    ScanReviewChecks.Run(root);
    OutputReviewChecks.Run(root);
    OutfitSelectionChecks.Run();

    MeshSafetyChecks.Run(root);
    VariantChecks.Run(root);
    CurrentRegressionChecks.Run(root);
    Console.WriteLine("All Wearable Distributor integration checks passed.");
}
finally
{
    Console.WriteLine("Test artifacts retained in " + root);
}

static PluginPatchOptions Configuration(string plugin, uint localId, string group, string chance = "25")
{
    var source = new PluginEntry { Plugin = plugin, SourcePath = plugin, Pieces = [new WearableRecord { Plugin = plugin, LocalId = localId, Name = plugin + " Outfit", Slots = "Body", Include = true }] };
    var target = new KeywordChoice { EditorId = group + "Faction", Plugin = "Skyrim.esm", LocalId = 0x1, Category = "Faction", TargetKind = "Faction" };
    return new PluginPatchOptions
    {
        Source = source,
        Chance = chance,
        TargetRules = [new DistributionTargetRule { Collection = "Skyrim + official DLC", Group = group, Filters = [target] }],
        Pieces = source.Pieces
    };
}

static int ReadOutfits(string path)
{
    using var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(path), SkyrimRelease.SkyrimSE);
    return mod.Outfits.Count;
}

static HashSet<string> ReadMasters(string path)
{
    using var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(path), SkyrimRelease.SkyrimSE);
    return mod.ModHeader.MasterReferences.Select(reference => reference.Master.FileName.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("Integration check failed: " + message);
}

static void CheckRuleSemantics(string root)
{
    var nativePath = Path.Combine(root, "native-semantics");
    var native = Configuration("ArcherArmor.esp", 0x806, "Bandits");
    native.TargetRules[0].Filters.AddRange([
        Target("Faction", "Faction", 0x2, "SecondBandits"),
        Target("Role", "Class", 0x3, "BanditArcher"),
        Target("Role", "Class", 0x4, "BanditMage"),
        Target("Race", "Race", 0x5, "NordRace"),
        Target("Race", "Race", 0x6, "OrcRace"),
        Target("Actor type", "Keyword", 0x7, "ActorTypeNPC"),
        Target("Actor type", "Keyword", 0x8, "ActorTypeGhost")]);
    native.TargetRules.Add(new DistributionTargetRule { Group = "Other audience", Filters = [Target("Faction", "Faction", 0x9, "OtherBandits")] });
    var nativeResult = PatchGenerator.Generate([native], nativePath, "SPID");
    var nativeLines = File.ReadAllLines(nativeResult.DistributionPath);
    Assert(nativeLines.Count(line => line.StartsWith("Outfit = ", StringComparison.Ordinal)) == 1, "alternative categories and audiences use one outfit chance roll");
    Assert(nativeLines.Count(line => line.StartsWith("Keyword = ", StringComparison.Ordinal)) == 7, "native boolean compiler grows by groups and audiences, not their Cartesian product");
    Assert(nativeLines.Any(line => line.StartsWith("Keyword = ", StringComparison.Ordinal) && line.Contains("0x000001~Skyrim.esm,0x000002~Skyrim.esm", StringComparison.Ordinal)), "factions inside one category use OR");
    Assert(nativeLines.Any(line => line.StartsWith("Keyword = WD_Audience_", StringComparison.Ordinal) && line.Count(character => character == '+') == 3), "separate required categories use AND");

    var resolvedPath = Path.Combine(root, "resolved-semantics");
    var resolved = Configuration("ResolvedGear.esp", 0x807, "Bandits");
    resolved.TargetRules[0].NpcResolutionComplete = true;
    resolved.TargetRules[0].ResolvedNpcFormIds = ["0x001234~Skyrim.esm", "0x001235~Skyrim.esm"];
    resolved.TargetRules.Add(new DistributionTargetRule { Group = "Overlapping bandits", Filters = [Target("Faction", "Faction", 0x2, "Bandits")],
        NpcResolutionComplete = true, ResolvedNpcFormIds = ["0x001235~Skyrim.esm", "0x001236~Skyrim.esm"] });
    resolved.Gender = "Female";
    var resolvedResult = PatchGenerator.Generate([resolved], resolvedPath, "SPID");
    var resolvedLines = File.ReadAllLines(resolvedResult.DistributionPath);
    var rule = resolvedLines.Single(line => line.StartsWith("Outfit = ", StringComparison.Ordinal));
    Assert(rule.Split('|')[2].Split(',').Length == 3, "resolved overlapping audiences union unique NPCs before rolling chance");
    Assert(rule.EndsWith("|NONE|F/-C|NONE|25", StringComparison.Ordinal), "resolved SPID preserves sex, excludes children at runtime and keeps requested chance");
    Assert(!resolvedLines.Any(line => line.StartsWith("Keyword = ", StringComparison.Ordinal)), "resolved audience does not need auxiliary keyword rules");
    var stable = ReadOutfitIds(resolvedResult.PluginPath);
    var other = Configuration("OtherGear.esp", 0x808, "Other");
    PatchGenerator.Generate([other], resolvedPath, "SPID");
    Assert(ReadOutfitIds(resolvedResult.PluginPath)[stable.Keys.Single()] == stable.Values.Single(), "merging preserves existing local outfit FormIDs");

    var skyPath = Path.Combine(root, "sky-semantics");
    resolved.Chance = "100";
    var sky = PatchGenerator.Generate([resolved], skyPath, "SkyPatcher");
    var skyRule = File.ReadAllLines(sky.DistributionPath).Single(line => line.Contains("outfitDefault=", StringComparison.Ordinal));
    Assert(skyRule.StartsWith("filterByNpcs=Skyrim.esm|001234,Skyrim.esm|001235,Skyrim.esm|001236:outfitDefault=WearableDistributor.esp|", StringComparison.Ordinal), "SkyPatcher uses plugin|hex format and an exact unique NPC roster");
    Assert(!skyRule.Contains('~') && !skyRule.Contains("filterByFactions", StringComparison.Ordinal), "SkyPatcher does not emit SPID form syntax or broad cross-type OR filters");
    resolved.Chance = "25";
    ExpectFailure(() => PatchGenerator.Generate([resolved], skyPath, "SkyPatcher"), "probabilistic SkyPatcher outfits are rejected");
    resolved.Chance = "100";
    resolved.TargetRules[0].NpcResolutionComplete = false;
    ExpectFailure(() => PatchGenerator.Generate([resolved], Path.Combine(root, "mixed-resolution"), "SPID"), "partly resolved audience cannot silently broaden");
    ExpectFailure(() => PatchGenerator.Generate([native], Path.Combine(root, "unresolved-sky"), "SkyPatcher"), "SkyPatcher requires complete NPC resolution");
    resolved.TargetRules[0].NpcResolutionComplete = true;
    resolved.TargetRules[0].ResolvedNpcFormIds.Clear();
    ExpectFailure(() => PatchGenerator.Generate([resolved], Path.Combine(root, "empty-audience"), "SPID"), "a resolved audience with zero matches cannot be generated");

    var excessive = Configuration("TooManyGear.esp", 0x809, "Too many");
    excessive.TargetRules = Enumerable.Range(0, 513).Select(index => new DistributionTargetRule { Group = index.ToString(), Filters = [Target("Faction", "Faction", 1, "Bandits")] }).ToList();
    ExpectFailure(() => PatchGenerator.Generate([excessive], Path.Combine(root, "excessive"), "SPID"), "unbounded rule expansion is rejected");
}

static void CheckOutputSafety(string root)
{
    var path = Path.Combine(root, "output-safety");
    var config = Configuration("SafeGear.esp", 0x810, "Bandits");
    var result = PatchGenerator.Generate([config], path, "SPID");
    var paths = new[] { result.PluginPath, result.DistributionPath, PatchGenerator.ManifestPathFor(path) };
    var initial = paths.Select(File.ReadAllBytes).ToArray();
    ExpectFailure(() => PatchGenerator.Generate([config], path, "Unexpected framework"), "unknown framework cannot default to SPID");
    var sky = Configuration("SafeSky.esp", 0x811, "Bandits", "100");
    sky.TargetRules[0].NpcResolutionComplete = true;
    sky.TargetRules[0].ResolvedNpcFormIds = ["0x001234~Skyrim.esm"];
    ExpectFailure(() => PatchGenerator.Generate([sky], path, "SkyPatcher"), "frameworks cannot share a generated ESP in one output folder");
    AssertUnchanged(paths, initial, "rejected framework changes preserve every output file");

    File.SetAttributes(result.DistributionPath, File.GetAttributes(result.DistributionPath) | FileAttributes.ReadOnly);
    try { config.Chance = "80"; ExpectFailure(() => PatchGenerator.Generate([config], path, "SPID"), "read-only output is caught before replacement"); }
    finally { File.SetAttributes(result.DistributionPath, File.GetAttributes(result.DistributionPath) & ~FileAttributes.ReadOnly); }
    AssertUnchanged(paths, initial, "read-only preflight leaves the full previous patch intact");
    using (var locked = new FileStream(Path.Combine(path, ".WearableDistributor.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        ExpectFailure(() => PatchGenerator.Generate([config], path, "SPID"), "concurrent output updates are rejected");
    AssertUnchanged(paths, initial, "a busy output folder remains unchanged");

    PatchGenerator.Generate([config], path, "SPID");
    var current = paths.Select(File.ReadAllBytes).ToArray();
    var latest = LatestBackup(path);
    var backupMetadata = JsonNode.Parse(File.ReadAllText(Path.Combine(latest, "backup.json")))!;
    var first = backupMetadata["Files"]!.AsArray().First(item => item!["Existed"]!.GetValue<bool>())!;
    var backupFile = Path.Combine(latest, first["BackupFile"]!.GetValue<string>());
    var savedBytes = File.ReadAllBytes(backupFile);
    File.AppendAllText(backupFile, "corrupted");
    ExpectFailure(() => PatchInventoryService.RestoreLastBackup(path), "checksums reject corrupt backups");
    AssertUnchanged(paths, current, "corrupt backup restore changes no files");
    File.WriteAllBytes(backupFile, savedBytes);
    var metadataText = File.ReadAllText(Path.Combine(latest, "backup.json"));
    first["RelativePath"] = "../outside.txt";
    File.WriteAllText(Path.Combine(latest, "backup.json"), backupMetadata.ToJsonString());
    ExpectFailure(() => PatchInventoryService.RestoreLastBackup(path), "backup paths cannot escape the output folder");
    AssertUnchanged(paths, current, "unsafe backup metadata changes no files");
    File.WriteAllText(Path.Combine(latest, "backup.json"), metadataText);

    var interrupted = JsonNode.Parse(metadataText)!;
    interrupted["State"] = "Ready";
    File.WriteAllText(Path.Combine(latest, "backup.json"), interrupted.ToJsonString());
    ExpectFailure(() => PatchGenerator.Generate([config], path, "SPID"), "an interrupted transaction requires recovery before another update");
    PatchInventoryService.RestoreLastBackup(path);
    AssertUnchanged(paths, initial, "verified restore recovers plugin, INI and manifest together");

    var missingPath = Path.Combine(root, "missing-ini");
    var missing = PatchGenerator.Generate([Configuration("MissingGear.esp", 0x812, "Bandits")], missingPath, "SPID");
    File.Delete(missing.DistributionPath);
    var before = File.ReadAllBytes(missing.PluginPath);
    ExpectFailure(() => PatchGenerator.Generate([config], missingPath, "SPID"), "missing distribution file cannot silently discard old rules");
    Assert(before.SequenceEqual(File.ReadAllBytes(missing.PluginPath)), "inconsistent existing output remains unchanged");

    var manifest = JsonNode.Parse(File.ReadAllText(paths[2]))!;
    manifest["Entries"]![0]!["DistributionPath"] = "../outside.ini";
    File.WriteAllText(paths[2], manifest.ToJsonString());
    var beforeInvalid = paths.Select(File.ReadAllBytes).ToArray();
    ExpectFailure(() => PatchGenerator.Generate([config], path, "SPID"), "manifest output paths are restricted to known generated files");
    AssertUnchanged(paths, beforeInvalid, "invalid manifest paths cannot modify generated files");

    var emptyPath = Path.Combine(root, "initial-restore");
    var firstPatch = PatchGenerator.Generate([config], emptyPath, "SPID");
    PatchInventoryService.RestoreLastBackup(emptyPath);
    Assert(!File.Exists(firstPatch.PluginPath) && !File.Exists(firstPatch.DistributionPath) && !File.Exists(PatchGenerator.ManifestPathFor(emptyPath)), "restoring the pre-first-generation snapshot removes all newly generated files");
}

static KeywordChoice Target(string category, string kind, uint id, string editorId) => new() { Plugin = "Skyrim.esm", Category = category, TargetKind = kind, LocalId = id, EditorId = editorId };
static Dictionary<string, uint> ReadOutfitIds(string path)
{
    using var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(path), SkyrimRelease.SkyrimSE);
    return mod.Outfits.ToDictionary(outfit => outfit.EditorID!, outfit => outfit.FormKey.ID, StringComparer.OrdinalIgnoreCase);
}
static string LatestBackup(string outputDirectory)
{
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory)).ToUpperInvariant())))[..16];
    var backupRoot = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!, "WearableDistributor", "Backups", hash);
    return Directory.GetDirectories(backupRoot).OrderByDescending(Path.GetFileName, StringComparer.Ordinal).First();
}
static void AssertUnchanged(IReadOnlyList<string> paths, IReadOnlyList<byte[]> previous, string message) => Assert(paths.Select((path, index) => File.ReadAllBytes(path).SequenceEqual(previous[index])).All(value => value), message);
static void ExpectFailure(Action action, string message)
{
    var failed = false;
    try { action(); }
    catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException) { failed = true; }
    Assert(failed, message);
}
