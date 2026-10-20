using System.Text.RegularExpressions;

namespace WearableDistributor;

public static class OutfitPartPresets
{
    public static readonly string[] Names = ["Complete set", "Balanced", "Light accessories", "Essentials only"];
    private static string Words(WearableRecord p) => Regex.Replace(Regex.Replace(p.Name + " " + Regex.Replace(p.EditorId, "([a-z])([A-Z])", "$1 $2"), "([a-zA-Z])([0-9])", "$1 $2"), @"[_\-]", " ");
    public static bool IsLewd(WearableRecord p) => Regex.IsMatch(Words(p), @"\b(slutty|slut|lewd|nude|naked|topless|bottomless|nsfw|crotchless|crotch less|open crotch|exposed|revealing|skimpy|see through|seethrough|transparent|micro bikini|pasties|nipple|pantyless|braless|no panties|no bra|no underwear)\b", RegexOptions.IgnoreCase);
    private static bool HasCoreSlot(WearableRecord p) => OutfitPartSelection.BodySlots(p.Slots).Overlaps(new[] { "Slot 32", "Slot 33", "Slot 37" });
    public static bool IsWig(WearableRecord p) => !HasCoreSlot(p) && Regex.IsMatch(Words(p), @"\b(wig|hair)\b", RegexOptions.IgnoreCase);
    public static bool IsHeadgear(WearableRecord p) => !HasCoreSlot(p) && (IsWig(p) || Regex.IsMatch(Words(p), @"\b(hood|helmet|hat|mask|circlet|headband|hairband|veil)\b", RegexOptions.IgnoreCase));
    public static bool IsOptional(WearableRecord p)
    {
        // Unknown garments stay required. Slot evidence alone cannot establish coverage.
        var slots = OutfitPartSelection.BodySlots(p.Slots);
        if (HasCoreSlot(p) || Regex.IsMatch(Words(p), @"\b(top|upper|bottom|lower|pelvis|pants|panty|panties|trousers|skirt|dress|robe|bodysuit|cuirass|shirt|tunic|waist|bra|corset|stockings|pantyhose)\b", RegexOptions.IgnoreCase)) return false;
        return IsHeadgear(p) || Regex.IsMatch(Words(p), @"\b(cape|cloak|poncho|scarf|necklace|amulet|ring|earrings?|bracelet|brooch|choker|backpack|pouch|belt|pauldron|shoulder)\b", RegexOptions.IgnoreCase);
    }
    public static List<string> Warnings(IEnumerable<WearableRecord> source)
    {
        var all = source.ToList(); var selected = all.Where(p => p.Include).ToList(); var notes = new List<string>();
        var uncertain = selected.Where(p => p.PartMode == "Required" && !IsOptional(p) && !OutfitPartSelection.BodySlots(p.Slots).Contains("Slot 32") &&
            !Regex.IsMatch(Words(p), @"\b(boots?|shoes?|heels?|gloves?|gauntlets?|pants|trousers|skirt|dress|robe|top|upper|lower|bottom|pelvis|waist|shirt|tunic|stockings|pantyhose)\b", RegexOptions.IgnoreCase)).ToList();
        if (uncertain.Count > 0) notes.Add("Check modular clothing: uncertain parts remain required: " + string.Join(", ", uncertain.Take(6).Select(p => p.Name)) + ". Names and slots cannot prove mesh compatibility.");
        var alternatives = all.Where(p => !p.Include && !p.IsLewd && !IsWig(p) && !IsOptional(p) && selected.Any(s => OutfitPartSelection.BodySlots(s.Slots).Overlaps(OutfitPartSelection.BodySlots(p.Slots)))).ToList();
        if (alternatives.Count > 0) notes.Add("Review garment alternatives: some pieces overlap the chosen set and were left out: " + string.Join(", ", alternatives.Take(6).Select(p => p.Name)) + ". If they are needed together, use Custom and review the mod's instructions.");
        if (selected.Count > 0 && !selected.Any(p => OutfitPartSelection.BodySlots(p.Slots).Contains("Slot 32"))) notes.Add("No main body garment detected. This may be an accessory-only or unusual modular set. An NPC outfit replaces its normal clothes; check coverage before generating.");
        if (selected.Any(p => p.PartMode == "Optional" && !IsOptional(p))) notes.Add("A clothing/unknown part was made optional manually. Its omission may leave the NPC partly undressed; dependent parts are not inferred automatically.");
        if (selected.Any(IsLewd)) notes.Add("Lewd variant explicitly included. Detection uses names/EditorIDs only; review the actual outfit yourself.");
        if (selected.Any(p => p.PartMode == "Optional")) notes.Add("Optional chances are evaluated by Skyrim's outfit leveled lists after the overall outfit roll. Pieces may be re-evaluated on outfit/inventory resets; this is not a guaranteed permanent wardrobe or loot percentage.");
        return notes;
    }
    public static string Apply(PluginPatchOptions config, string preset, bool lewd = false)
    {
        var chance = preset switch { "Complete set" => 100, "Balanced" => 50, "Light accessories" => 25, _ => 0 };
        var candidates = config.Pieces.Where(p => !IsWig(p) && (lewd || !IsLewd(p))).OrderByDescending(p => lewd && IsLewd(p)).ThenBy(p => p.LocalId).ToList();
        if (!string.IsNullOrWhiteSpace(config.PreferredColor) && candidates.Any(p => p.Variant.Equals(config.PreferredColor, StringComparison.OrdinalIgnoreCase)))
            candidates = candidates.Where(p => p.Variant == "Standard / unmarked" || p.Variant.Equals(config.PreferredColor, StringComparison.OrdinalIgnoreCase)).ToList();
        var combined = OutfitPartSelection.CombinedAlternatives(candidates);
        candidates = candidates.Where(p => !combined.Contains(p.FormId)).ToList();
        var colors = candidates.Select(p => p.Variant).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var families = colors.SelectMany(color => OutfitPartSelection.FindCompatibleSet(candidates.Where(p => p.Variant == OutfitVariantPlan.Unmarked || p.Variant == color), lewd)).Select(OutfitVariantPlan.Family).ToHashSet();
        var chosen = candidates.Where(p => families.Contains(OutfitVariantPlan.Family(p))).Select(p => p.FormId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        config.MatchVariants = true;
        foreach (var p in config.Pieces)
        {
            p.OptionalChance = chance is > 0 and < 100 ? chance : 50;
            p.PartMode = !chosen.Contains(p.FormId) ? "Excluded" : !IsOptional(p) || chance == 100 ? "Required" : chance == 0 ? "Excluded" : "Optional";
        }
        config.PartPreset = lewd ? "Lewd variant (explicit)" : preset;
        config.AllowLewdParts = lewd;
        return Summary(config);
    }
    public static string Summary(PluginPatchOptions config)
    {
        if (config.MatchVariants && config.DistributeToNpcs)
        {
            try
            {
                var branches = OutfitVariantPlan.Create(config);
                return $"{config.PartPreset}: choose 1 of {branches.Count} matching color set(s) · up to {branches.Max(b => b.Groups.Count(g => g.Mode == "Required"))} required garment groups · up to {branches.Max(b => b.Groups.Count(g => g.Mode == "Optional"))} optional groups · {config.Pieces.Count(p => !p.Include)} excluded";
            }
            catch (InvalidOperationException) { /* The readable validation notice explains incomplete groups. */ }
        }
        return $"{config.PartPreset}: {config.Pieces.Count(p => p.PartMode == "Required")} required versions · {config.Pieces.Count(p => p.PartMode == "Optional")} optional versions · {config.Pieces.Count(p => !p.Include)} excluded";
    }
    public static List<string> Errors(PluginPatchOptions config)
    {
        var errors = new List<string>(); var selected = config.Pieces.Where(p => p.Include).ToList();
        if (selected.Any(p => p.PartMode == "Optional" && p.OptionalChance is < 1 or > 99)) errors.Add("Optional chances must be whole numbers from 1 to 99. Use Required for 100% or Excluded for 0%.");
        if (selected.Any(IsLewd) && !config.AllowLewdParts) errors.Add("Use the explicit lewd-variant button before including a marked lewd piece.");
        errors.AddRange(OutfitMeshPolicy.Errors(config));
        if (!config.DistributeToNpcs) return errors;
        if (!selected.Any(p => p.PartMode == "Required")) errors.Add("Keep at least one required part so an outfit cannot roll completely empty.");
        if (config.MatchVariants)
        {
            try { OutfitVariantPlan.Create(config); } catch (InvalidOperationException error) { errors.Add(error.Message); }
            return errors;
        }
        for (var i = 0; i < selected.Count; i++) for (var j = i + 1; j < selected.Count; j++)
            if (OutfitPartSelection.BodySlots(selected[i].Slots).Overlaps(OutfitPartSelection.BodySlots(selected[j].Slots)))
            { errors.Add($"Choose one alternative: {selected[i].Name} / {selected[j].Name} share body slots. Exclude one, or create a separate named outfit."); return errors; }
        return errors;
    }
}
