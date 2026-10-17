using System.IO;
using System.Text.Json;

namespace WearableDistributor;

public static class PreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static AppPreferences Load(string path)
    {
        if (!File.Exists(path)) return new AppPreferences();
        return Normalize(JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The settings file is empty."));
    }

    public static AppPreferences Normalize(AppPreferences value)
    {
        if (value.SchemaVersion > 3) throw new InvalidDataException("These settings were created by a newer app. Update Skyrim SPID Patch Generator before importing them.");
        value.SchemaVersion = 2;
        value.DataPath = (value.DataPath ?? "").Trim(); value.PluginListPath = (value.PluginListPath ?? "").Trim();
        value.OutputPath = (value.OutputPath ?? "").Trim(); value.BackupOutputPath ??= "";
        value.Framework = value.Framework?.Equals("SkyPatcher", StringComparison.OrdinalIgnoreCase) == true ? "SkyPatcher" : "SPID";
        value.DefaultChance = int.TryParse(value.DefaultChance?.Trim().TrimEnd('%'), out var chance) && chance is >= 1 and <= 100 ? chance.ToString() : "25";
        value.IgnoredPlugins = new HashSet<string>((value.IgnoredPlugins ?? []).Where(ValidPlugin), StringComparer.OrdinalIgnoreCase);
        value.IncludedPlugins = new HashSet<string>((value.IncludedPlugins ?? []).Where(ValidPlugin), StringComparer.OrdinalIgnoreCase);
        value.AdditionalExclusions = (value.AdditionalExclusions ?? []).Where(term => !string.IsNullOrWhiteSpace(term)).Select(term => term.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        value.DistributionSettings = NormalizeSettings(value.DistributionSettings);
        value.DistributionPresets = NormalizeSettings(value.DistributionPresets);
        if (value.DistributionSettings.Values.Any(settings => settings.MerchantCategories.Count > 0)) value.SchemaVersion = 3;
        return value;
    }

    public static AppPreferences Clone(AppPreferences value) => Normalize(JsonSerializer.Deserialize<AppPreferences>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!);

    public static void Save(string path, AppPreferences value)
    {
        Normalize(value);
        var fullPath = Path.GetFullPath(path); var parent = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(parent);
        var temp = Path.Combine(parent, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, JsonOptions); stream.Flush(true);
            }
            if (File.Exists(fullPath)) File.Copy(fullPath, fullPath + ".bak", true);
            File.Move(temp, fullPath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static bool ValidPlugin(string plugin) => !string.IsNullOrWhiteSpace(plugin) && Path.GetFileName(plugin).Equals(plugin, StringComparison.Ordinal) &&
        (Path.GetExtension(plugin).ToLowerInvariant() is ".esm" or ".esp" or ".esl");

    private static Dictionary<string, SavedDistributionOptions> NormalizeSettings(Dictionary<string, SavedDistributionOptions>? source)
    {
        var result = new Dictionary<string, SavedDistributionOptions>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source ?? [])
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) continue;
            var settings = pair.Value;
            settings.OutfitName ??= "";
            settings.MerchantCategories = (settings.MerchantCategories ?? []).Where(MerchantStockService.Categories.Contains).Distinct().ToList();
            settings.Chance = int.TryParse(settings.Chance?.Trim().TrimEnd('%'), out var chance) && chance is >= 1 and <= 100 ? chance.ToString() : "25";
            settings.Gender = settings.Gender?.Equals("Female", StringComparison.OrdinalIgnoreCase) == true ? "Female" : settings.Gender?.Equals("Male", StringComparison.OrdinalIgnoreCase) == true ? "Male" : "Any";
            settings.ExcludedPieceFormIds = (settings.ExcludedPieceFormIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            settings.TargetRules = (settings.TargetRules ?? []).Where(rule => rule is not null).ToList();
            foreach (var rule in settings.TargetRules)
            {
                rule.Collection ??= NpcTargetingService.AllCollection; rule.Group ??= "";
                rule.Filters = (rule.Filters ?? []).Where(filter => filter is not null && ValidPlugin(filter.Plugin) && filter.LocalId != 0).ToList();
                foreach (var filter in rule.Filters) { filter.EditorId ??= ""; filter.Category ??= "Other"; filter.TargetKind ??= "Keyword"; }
            }
            result[pair.Key.Trim()] = settings;
        }
        return result;
    }
}
