using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Data;
using WearableDistributor;

public static class LocalizationChecks
{
    public static void Run(string parent)
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        var preference = Loc.Preference;
        var count = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Localization: " + description);
            count++; Console.WriteLine("PASS localization: " + description);
        }
        try
        {
            var languages = new[] { "en", "de", "fr", "es", "zh-Hans" };
            Check(languages.All(code => Loc.Languages.Any(language => language.Code == code)), "All five bundled catalogs load at runtime");
            foreach (var (requested, expected) in new[] { ("de-AT", "de"), ("fr-CA", "fr"), ("es-MX", "es"), ("zh-CN", "zh-Hans"), ("zh-SG", "zh-Hans"), ("zh-TW", "en"), ("zh-HK", "en"), ("zh-Hant", "en"), ("ar", "en") })
            {
                Loc.Initialize(requested);
                Check(Loc.ActiveLanguage == expected, "Culture routing " + requested + " -> " + expected);
            }
            Check(Loc.NormalizePreference("../not-a-language") == "auto" && Loc.NormalizePreference(null) == "auto", "Invalid and missing preferences use Automatic");
            Check(Loc.LanguagesFor("pt-BR").Any(language => language.Code == "pt-BR"), "An unavailable imported language remains selectable with fallback information");

            var english = ReadCatalog("en");
            var source = new PluginEntry { Plugin = "LanguageProbe.esp", SourcePath = "LanguageProbe.esp", Pieces = [new() { Plugin = "LanguageProbe.esp", LocalId = 0x800, Name = "衣装 Étoile", Slots = "Body", PartMode = "Required" }] };
            var options = new PluginPatchOptions { Source = source, Pieces = source.Pieces, Gender = "Female", Chance = "25", TargetRules = [new() { Group = "Bandits", Filters = [new() { Plugin = "Skyrim.esm", LocalId = 0x1234, EditorId = "BanditFaction", TargetKind = "Faction", Category = "Faction" }] }] };
            var originalOptions = JsonSerializer.Serialize(options);
            byte[]? expectedEsp = null;
            string? expectedIni = null;
            byte[]? expectedSkyEsp = null;
            string? expectedSkyIni = null;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-AT");
            foreach (var code in languages)
            {
                Loc.Initialize(code);
                Check(Loc.ActiveLanguage == code && CultureInfo.CurrentCulture.Name == "de-AT", code + " changes UI language without changing regional number parsing");
                Check(Loc.T("Required") != "Required" || code == "en", code + " loads real translated UI text");
                Check(Loc.T("LanguageProbe.esp") == "LanguageProbe.esp" && Loc.T("衣装 Étoile") == "衣装 Étoile", code + " preserves unknown mod names and Unicode data");
                Check(english.Strings.Keys.Where(key => key.StartsWith("xaml.", StringComparison.Ordinal)).All(key => !Loc.T(key).StartsWith("xaml.", StringComparison.Ordinal)), code + " resolves every XAML text key");
                var catalog = ReadCatalog(code);
                foreach (var pair in catalog.Strings)
                {
                    var template = pair.Key.StartsWith("xaml.", StringComparison.Ordinal) ? english.Strings[pair.Key] : pair.Key;
                    System.Text.CompositeFormat original;
                    try { original = System.Text.CompositeFormat.Parse(template); }
                    catch (FormatException) { continue; }
                    var translated = System.Text.CompositeFormat.Parse(pair.Value);
                    if (original.MinimumArgumentCount != translated.MinimumArgumentCount) throw new InvalidOperationException(code + " has invalid placeholders: " + pair.Key);
                    if (original.MinimumArgumentCount > 0)
                    {
                        var args = Enumerable.Range(0, original.MinimumArgumentCount).Select(i => (object)(i + 7)).ToArray();
                        _ = Loc.F(FormattableStringFactory.Create(template, args));
                    }
                }
                Check(true, code + " translated format templates retain their argument counts and format safely");
                Check(ReferenceEquals(Loc.ChoiceTextConverter.ConvertBack("translated", typeof(string), null!, Loc.Culture), Binding.DoNothing), code + " display converter cannot overwrite stored enum values");
                var saved = new AppPreferences { Language = code, DistributionSettings = new() { [source.Plugin] = new() { Gender = "Female", PartChoices = [new(source.Pieces[0].FormId, "Required", 50)] } } };
                var path = Path.Combine(parent, "localization", code, "settings.json");
                PreferencesService.Save(path, saved);
                var loaded = PreferencesService.Load(path);
                Check(loaded.Language == code && loaded.DistributionSettings[source.Plugin].Gender == "Female" && loaded.DistributionSettings[source.Plugin].PartChoices[0].Mode == "Required", code + " round-trips settings without translating identifiers or enums");
                var result = PatchGenerator.Generate([options], Path.Combine(parent, "localization", code, "SPID"), "SPID");
                var esp = File.ReadAllBytes(result.PluginPath); var ini = File.ReadAllText(result.DistributionPath);
                expectedEsp ??= esp; expectedIni ??= ini;
                Check(esp.SequenceEqual(expectedEsp) && ini == expectedIni, code + " creates byte-identical SPID ESP/INI output");
                var skyOptions = new PluginPatchOptions { Source = source, Pieces = source.Pieces, Chance = "100", Gender = "Female", TargetRules = [new() { Group = "Bandits", Filters = options.TargetRules[0].Filters, NpcResolutionComplete = true, ResolvedNpcFormIds = ["0x001234~Skyrim.esm"] }] };
                var sky = PatchGenerator.Generate([skyOptions], Path.Combine(parent, "localization", code, "SkyPatcher"), "SkyPatcher");
                var skyEsp = File.ReadAllBytes(sky.PluginPath); var skyIni = File.ReadAllText(sky.DistributionPath);
                expectedSkyEsp ??= skyEsp; expectedSkyIni ??= skyIni;
                Check(skyEsp.SequenceEqual(expectedSkyEsp) && skyIni == expectedSkyIni, code + " creates byte-identical SkyPatcher ESP/INI output");
                Check(JsonSerializer.Serialize(options) == originalOptions, code + " leaves configured outfit data untouched");
            }

            var overlay = Path.Combine(Loc.TranslationDirectory, "zz-localization-check-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                Directory.CreateDirectory(Loc.TranslationDirectory);
                File.WriteAllText(overlay, "{ broken JSON");
                Check(LoadCatalogs().Contains("de"), "Malformed community file does not remove built-in catalogs");
                File.WriteAllText(overlay, JsonSerializer.Serialize(new { language = "de", name = "Deutsch", strings = new Dictionary<string, string> { ["Probe {0}"] = "Wert {0}", ["Invalid {0}"] = "Falsch {1}" } }));
                var catalogs = LoadCatalogs();
                var strings = CatalogStrings(catalogs["de"]!);
                Check(strings["Probe {0}"] == "Wert {0}" && !strings.ContainsKey("Invalid {0}") && strings.ContainsKey("Required"), "Valid overlay merges with built-ins; invalid placeholders are rejected");
                File.WriteAllText(overlay, JsonSerializer.Serialize(new { language = "../invalid", name = "Invalid", strings = new { } }));
                Check(LoadCatalogs().Count >= 5, "Invalid catalog language cannot break local loading");
            }
            finally { if (File.Exists(overlay)) File.Delete(overlay); }
            Console.WriteLine("Localization checks passed: " + count);
        }
        finally
        {
            Loc.Initialize(preference);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    private sealed record Catalog(Dictionary<string, string> Strings);
    private static Catalog ReadCatalog(string code)
    {
        var assembly = typeof(Loc).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith(".Translations." + code + ".json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var document = JsonDocument.Parse(stream);
        return new(document.RootElement.GetProperty("strings").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!));
    }
    private static IDictionary LoadCatalogs() => (IDictionary)typeof(Loc).GetMethod("LoadCatalogs", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
    private static Dictionary<string, string> CatalogStrings(object catalog) => (Dictionary<string, string>)catalog.GetType().GetProperty("Strings")!.GetValue(catalog)!;
}
