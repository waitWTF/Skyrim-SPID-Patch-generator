using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;

namespace WearableDistributor;

public sealed record LanguageOption(string Code, string Name);

/// <summary>Local JSON text resources. Only explicit UI text calls are translated; patch data stays invariant.</summary>
public static class Loc
{
    private sealed class Catalog
    {
        public string Language { get; set; } = "";
        public string Name { get; set; } = "";
        public Dictionary<string, string> Strings { get; set; } = new(StringComparer.Ordinal);
    }

    private static readonly CultureInfo WindowsLanguage = CultureInfo.CurrentUICulture;
    private static readonly Dictionary<string, Catalog> catalogs = LoadCatalogs();
    private static IReadOnlyDictionary<string, string> active = new Dictionary<string, string>();
    public static string ActiveLanguage { get; private set; } = "en";
    public static string Preference { get; private set; } = "auto";
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(ActiveLanguage);
    public static IValueConverter ChoiceTextConverter => new ChoiceConverter();
    public static string TranslationDirectory => Path.Combine(AppContext.BaseDirectory, "Translations");
    public static IReadOnlyList<LanguageOption> Languages => new[] { new LanguageOption("auto", T("Automatic — Windows display language")) }
        .Concat(catalogs.Select(p => new LanguageOption(p.Key, p.Value.Name)).OrderBy(p => p.Name, StringComparer.CurrentCulture)).ToList();

    public static IReadOnlyList<LanguageOption> LanguagesFor(string? preference)
    {
        var options = Languages.ToList();
        var code = NormalizePreference(preference);
        if (options.All(o => !o.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
        {
            var culture = CultureInfo.GetCultureInfo(code);
            var fallback = Resolve(culture);
            var name = catalogs.TryGetValue(fallback, out var c) ? c.Name : "English";
            options.Add(new LanguageOption(code, F($"{culture.NativeName} (fallback: {name})")));
        }
        return options;
    }

    public static string NormalizePreference(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Equals("auto", StringComparison.OrdinalIgnoreCase)) return "auto";
        try { return CultureInfo.GetCultureInfo(code.Trim()).Name; }
        catch (CultureNotFoundException) { return "auto"; }
    }

    public static void InitializeFromSettings()
    {
        var preference = "auto";
        try
        {
            var file = Path.Combine(BuildInfo.SettingsRoot, "settings.json");
            if (File.Exists(file))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (p.Name.Equals("Language", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                        preference = p.Value.GetString() ?? "auto";
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { /* Existing preferences recovery handles the original file. Localization never rewrites it. */ }
        Initialize(preference);
    }

    public static void Initialize(string? preference)
    {
        var code = NormalizePreference(preference);
        Preference = code;
        var requested = code == "auto" ? WindowsLanguage : CultureInfo.GetCultureInfo(code);
        ActiveLanguage = Resolve(requested);
        active = catalogs.TryGetValue(ActiveLanguage, out var catalog) ? catalog.Strings : new Dictionary<string, string>();
        // Regional number/date culture is deliberately not changed: plugin operands and saved values are data.
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
    }

    private static string Resolve(CultureInfo requested)
    {
        for (var c = requested; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            if (catalogs.ContainsKey(c.Name)) return c.Name;
            if (c.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) || c.Name.Equals("zh-SG", StringComparison.OrdinalIgnoreCase) || c.Name.Equals("zh", StringComparison.OrdinalIgnoreCase))
            {
                // Do not silently substitute Simplified Chinese for an explicitly Traditional script/region.
                if (!requested.Name.Contains("Hant", StringComparison.OrdinalIgnoreCase) && requested.Name is not ("zh-TW" or "zh-HK" or "zh-MO") && catalogs.ContainsKey("zh-Hans")) return "zh-Hans";
            }
        }
        return "en";
    }

    public static string T(string english)
    {
        if (active.TryGetValue(english, out var translation) && !string.IsNullOrWhiteSpace(translation)) return translation;
        return catalogs.TryGetValue("en", out var fallback) && fallback.Strings.TryGetValue(english, out var source) ? source : english;
    }

    public static string F(FormattableString message)
    {
        var template = T(message.Format);
        try { return string.Format(Culture, template, message.GetArguments()); }
        catch (FormatException) { return message.ToString(CultureInfo.InvariantCulture); }
    }

    public static DataTemplate ChoiceTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding { Converter = new ChoiceConverter() });
        return new DataTemplate { VisualTree = text };
    }

    public static string AudienceLabel(string label) => string.Join(" · ", label.Split(" · ").Select(part =>
    {
        var count = Regex.Match(part, @"^(\d+) NPCs$");
        if (count.Success) return F($"{count.Groups[1].Value} NPCs");
        var protection = Regex.Match(part, @"^⚠ Protected \((\d+)\)$");
        if (protection.Success) return F($"⚠ Protected ({protection.Groups[1].Value})");
        if (part.StartsWith("Added by ", StringComparison.Ordinal)) return F($"Added by {part[9..]}");
        return T(part);
    }));

    public sealed class ChoiceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string s ? T(s) : value;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    private static Dictionary<string, Catalog> LoadCatalogs()
    {
        var result = new Dictionary<string, Catalog>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(Loc).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.Contains(".Translations.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is not null) Read(stream, result, merge: false);
        }
        try
        {
            if (Directory.Exists(TranslationDirectory))
                foreach (var file in Directory.EnumerateFiles(TranslationDirectory, "*.json").Order(StringComparer.Ordinal).Take(100))
                    try
                    {
                        if (new FileInfo(file).Length > 4_194_304) continue;
                        using var stream = File.OpenRead(file); Read(stream, result, merge: true);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return result;
    }

    private static void Read(Stream stream, Dictionary<string, Catalog> result, bool merge)
    {
        try
        {
            var c = JsonSerializer.Deserialize<Catalog>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 8 });
            if (c is null || string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 100 || c.Strings is null) return;
            var culture = CultureInfo.GetCultureInfo(c.Language);
            if (string.IsNullOrEmpty(culture.Name)) return;
            c.Language = culture.Name;
            c.Strings = c.Strings.Where(p => p.Key.Length <= 16_384 && p.Value is not null && p.Value.Length <= 32_768 && ValidPlaceholders(p.Key, p.Value))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            if (merge && result.TryGetValue(c.Language, out var builtIn))
            {
                var combined = new Dictionary<string, string>(builtIn.Strings, StringComparer.Ordinal);
                foreach (var p in c.Strings) if (!string.IsNullOrWhiteSpace(p.Value)) combined[p.Key] = p.Value;
                c.Strings = combined;
            }
            result[c.Language] = c;
        }
        catch (Exception e) when (e is JsonException or CultureNotFoundException or ArgumentException or IOException) { }
    }

    private static bool ValidPlaceholders(string source, string translated)
    {
        try
        {
            var a = System.Text.CompositeFormat.Parse(source);
            var b = System.Text.CompositeFormat.Parse(translated);
            if (a.MinimumArgumentCount != b.MinimumArgumentCount) return false;
            static string Signature(string s) => string.Join(",", Regex.Matches(s, @"(?<!\{)\{(\d+)(?:[,}:])").Select(m => m.Groups[1].Value).Distinct().Order());
            return Signature(source) == Signature(translated);
        }
        catch (FormatException) { return source == translated || (!source.Contains('{') && !translated.Contains('{') && !translated.Contains('}')); }
    }
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}

/// <summary>Allow longer translations to wrap without changing button Content or control-valued labels.</summary>
public sealed class WrappingTextTemplateSelector : DataTemplateSelector
{
    private readonly DataTemplate textTemplate;
    public WrappingTextTemplateSelector()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding());
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        text.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        text.SetValue(FrameworkElement.MaxWidthProperty, 240d);
        textTemplate = new DataTemplate { VisualTree = text };
    }
    public override DataTemplate SelectTemplate(object item, DependencyObject container) => item is string ? textTemplate : base.SelectTemplate(item, container);
}
