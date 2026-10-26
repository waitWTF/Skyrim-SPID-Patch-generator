using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace WearableDistributor;

/// <summary>A FOMOD-style, scrollable outfit wizard. The footer and outfit navigation stay visible.</summary>
public sealed class PatchOptionsWindow : Window
{
    private readonly List<PluginPatchOptions> options = [];
    private readonly List<PageState> pages = [];
    private readonly IReadOnlyList<KeywordChoice> availableKeywords;
    private readonly NpcTargetingService targeting;
    private IReadOnlyList<AdvancedTargetChoice>? advancedChoices;
    private readonly Dictionary<string, KeywordChoice> availableByKey;
    private readonly DistributionPresetCatalog commonPresets;
    private readonly bool hasNpcScan;
    private readonly string defaultChance;
    private readonly string framework;
    private readonly IReadOnlyDictionary<string, SavedDistributionOptions> savedOptions;
    private readonly Action<string, SavedDistributionOptions> saveOptions;
    private readonly IReadOnlyDictionary<string, SavedDistributionOptions> presets;
    private readonly Action<string, SavedDistributionOptions> savePreset;
    private readonly Action<string> removeSavedOptions;
    private readonly Action<string> removePreset;
    private readonly Action<IReadOnlyDictionary<string, SavedDistributionOptions>>? saveBatch;
    private readonly Dictionary<PluginPatchOptions, string> restorationNotes = [];
    private int currentIndex;
    private bool changingStep;
    private TextBlock stepLabel = null!;
    private ProgressBar stepProgress = null!;
    private ContentControl pageHost = null!;
    private ListBox outfitSteps = null!;
    private Button backButton = null!;
    private Button nextButton = null!;
    private Button skipButton = null!;
    private readonly ISet<string> skippedOutfits;
    private readonly IReadOnlyList<MerchantTarget> merchantTargets;
    public IReadOnlyList<PluginPatchOptions> Options => options.Where(config => !IsSkipped(config)).ToList();
    public IReadOnlyList<PluginPatchOptions> SkippedOptions => options.Where(IsSkipped).ToList();

    internal bool CanReviewSavedSetup()
    {
        // Never silently approve a new suggestion, a stale audience, or an incompatible restored setting.
        for (var i = 0; i < pages.Count; i++)
        {
            var config = pages[i].Config;
            if (!savedOptions.ContainsKey(GetSavedKey(config)) || restorationNotes.ContainsKey(config)) { ShowPage(i); return false; }
            pages[i].Commit();
            if (!pages[i].Validate()) { ShowPage(i); return false; }
        }
        return true;
    }

    public PatchOptionsWindow(IEnumerable<PluginEntry> plugins, IEnumerable<KeywordChoice> keywords, string defaultChance, string framework,
        IReadOnlyDictionary<string, SavedDistributionOptions> savedOptions, Action<string, SavedDistributionOptions> saveOptions,
        IReadOnlyDictionary<string, SavedDistributionOptions>? presets = null, Action<string, SavedDistributionOptions>? savePreset = null,
        Action<string>? removeSavedOptions = null, Action<string>? removePreset = null, IEnumerable<NpcTargetProfile>? npcProfiles = null,
        ISet<string>? skippedOutfits = null, IEnumerable<MerchantTarget>? merchantTargets = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? requestedOutfits = null,
        Action<IReadOnlyDictionary<string, SavedDistributionOptions>>? saveBatch = null, AudiencePreparation? preparation = null)
    {
        availableKeywords = keywords.DistinctBy(item => item.TargetKind + "|" + item.FormId, StringComparer.OrdinalIgnoreCase).ToList();
        availableByKey = availableKeywords.ToDictionary(choice => choice.TargetKind + "|" + choice.FormId, StringComparer.OrdinalIgnoreCase);
        advancedChoices = preparation?.AdvancedTargets;
        var scannedNpcs = npcProfiles?.ToList() ?? [];
        hasNpcScan = scannedNpcs.Count > 0;
        targeting = preparation?.Targeting ?? new NpcTargetingService(availableKeywords, scannedNpcs);
        commonPresets = preparation?.Presets ?? new DistributionPresetCatalog(targeting);
        this.defaultChance = defaultChance; this.framework = framework;
        this.savedOptions = savedOptions; this.saveOptions = saveOptions;
        this.presets = presets ?? new Dictionary<string, SavedDistributionOptions>();
        this.savePreset = savePreset ?? ((_, _) => { });
        this.removeSavedOptions = removeSavedOptions ?? (_ => { });
        this.removePreset = removePreset ?? (_ => { });
        this.saveBatch = saveBatch;
        this.skippedOutfits = skippedOutfits ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        this.merchantTargets = merchantTargets?.ToList() ?? [];
        foreach (var plugin in plugins)
        {
            if (requestedOutfits != null && requestedOutfits.TryGetValue(plugin.Plugin, out var names))
            {
                foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var key = name.Equals(Path.GetFileNameWithoutExtension(plugin.Plugin), StringComparison.OrdinalIgnoreCase) ? plugin.Plugin : plugin.Plugin + "|outfit|" + name;
                    savedOptions.TryGetValue(key, out var requestedSaved);
                    AddInitialConfiguration(plugin, name, requestedSaved);
                }
                continue;
            }
            savedOptions.TryGetValue(plugin.Plugin, out var saved);
            AddInitialConfiguration(plugin, Path.GetFileNameWithoutExtension(plugin.Plugin), saved);
            var prefix = plugin.Plugin + "|outfit|";
            foreach (var pair in savedOptions.Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                AddInitialConfiguration(plugin, string.IsNullOrWhiteSpace(pair.Value.OutfitName) ? pair.Key[prefix.Length..] : pair.Value.OutfitName, pair.Value);
        }
        if (options.Count == 0) throw new ArgumentException("Select at least one outfit plugin before opening distribution setup.", nameof(plugins));
        BuildWindow(); ShowPage(0);
    }

    private void AddInitialConfiguration(PluginEntry plugin, string name, SavedDistributionOptions? saved)
    {
        var excluded = (saved?.ExcludedPieceFormIds ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var restored = RestoreRules(saved, out var missing);
        var config = new PluginPatchOptions
        {
            Source = plugin, OutfitName = name,
            WardrobeName = saved?.WardrobeName ?? "", WardrobeWeight = saved?.WardrobeWeight ?? 1,
            DistributeToNpcs = saved?.DistributeToNpcs ?? false,
            Chance = IsSkyPatcher ? "100" : saved?.Chance ?? defaultChance, Gender = saved?.Gender ?? "Any",
            Pieces = plugin.Pieces.Select(piece => ClonePiece(piece, !excluded.Contains(piece.FormId))).ToList(),
            Keywords = availableKeywords.ToList(),
            // A deliberately empty or stale saved setup must never silently become a broader suggestion.
            TargetRules = saved is not null ? restored : SuggestAudiences(plugin.Plugin)
        };
        if (config.DistributeToNpcs && missing > 0) { App.WriteDiagnostic(DiagnosticEvent.OutfitValidationFailed, level: DiagnosticLevel.Warning); restorationNotes[config] = $"{missing} saved NPC group(s) could not be restored completely. Their targets may have changed or been removed. Choose replacement NPC groups below."; }
        var required = OutfitGenderPolicy.RequiredGender(config.Pieces);
        if (config.DistributeToNpcs && required is "Female" or "Male" && config.Gender != "Any" && config.Gender != required)
            restorationNotes[config] = (restorationNotes.GetValueOrDefault(config) ?? "") + $" Saved {config.Gender} selection is incompatible with {required}-only outfit parts and has been corrected.";
        config.MerchantCategories = (saved?.MerchantCategories ?? []).Where(MerchantStockService.Categories.Contains).Distinct().ToList();
        config.IncludeMerchandiseWeapons = saved?.IncludeMerchandiseWeapons ?? true;
        config.MerchantTargets = merchantTargets.ToList();
        config.AllowLewdParts = saved?.AllowLewdParts ?? false;
        config.PartPreset = saved?.PartPreset ?? "Balanced";
        config.PreferredColor = saved?.PreferredColor ?? "";
        config.MatchVariants = saved?.MatchVariants ?? true;
        if (saved is null) OutfitPartPresets.Apply(config, "Balanced");
        else
        {
            foreach (var p in config.Pieces)
            {
                var choice = saved.PartChoices?.FirstOrDefault(item => item.FormId.Equals(p.FormId, StringComparison.OrdinalIgnoreCase));
                if (choice is not null) { p.PartMode = choice.Mode; p.OptionalChance = choice.Chance; }
                if (p.IsLewd && !config.AllowLewdParts) p.PartMode = "Excluded";
            }
        }
        var combinedAlternatives = OutfitPartSelection.CombinedAlternatives(config.Pieces);
        if (saved is not null && OutfitPartPresets.Names.Contains(config.PartPreset) &&
            (!config.MatchVariants || config.Pieces.Any(p => p.Include && combinedAlternatives.Contains(p.FormId))))
        {
            OutfitPartPresets.Apply(config, config.PartPreset);
            restorationNotes[config] = "The automatic preset now uses matching garment alternatives and prefers separate garments over combined Outfit versions. Review parts before generating; your NPC groups and overall chance are retained.";
        }
        if (saved is not null && (saved.PartChoices?.Count ?? 0) == 0 && config.DistributeToNpcs &&
            OutfitPartPresets.Errors(config).Any(error => error.StartsWith("Choose one alternative:", StringComparison.Ordinal)))
        {
            OutfitPartPresets.Apply(config, "Balanced");
            restorationNotes[config] = (restorationNotes.GetValueOrDefault(config) ?? "") + " Older setup combined incompatible body-slot alternatives. A matching Balanced set is selected for review; NPC targets and chance are retained. Review parts before generating.";
        }
        options.Add(config); pages.Add(new PageState(config, () => BuildPage(config)));
    }

    private bool IsSkyPatcher => framework.Equals("SkyPatcher", StringComparison.OrdinalIgnoreCase);

    private void BuildWindow()
    {
        UiTheme.Apply(this);
        Title = Loc.F($"Skyrim SPID Patch Generator · {framework} setup");
        Width = 1280; Height = 860; MinWidth = 980; MinHeight = 600;
        MaxHeight = Math.Max(600, SystemParameters.WorkArea.Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Closing += (_, _) => SaveAllPages();

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var stage = new TextBlock { Text = Loc.T("02  /  DISTRIBUTION"), Foreground = UiTheme.Brush("AccentBrush"), FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 5) };
        var heading = new StackPanel(); heading.Children.Add(stage);
        heading.Children.Add(new TextBlock { Text = Loc.T("Build your outfit distribution"), FontSize = 25, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(UiTheme.Caption(Loc.T("Choose NPC outfits, trader stock or both, then select parts. Only the plugins you chose are opened. Save & close keeps your setup; Review & generate builds the patch.")));
        var history = UiTheme.Button(Loc.T("Activity history")); DockPanel.SetDock(history, Dock.Right);
        history.Click += (_, _) => ActivityHistoryWindow.ShowFor(this);
        header.Children.Add(history); header.Children.Add(heading); root.Children.Add(header);

        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(228) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new DockPanel();
        var sidebarTitle = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        sidebarTitle.Children.Add(new TextBlock { Text = Loc.T("OUTFIT STEPS"), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Brush("MutedBrush") });
        sidebarTitle.Children.Add(UiTheme.Caption(Loc.T("Jump to any outfit to review or change it.")));
        DockPanel.SetDock(sidebarTitle, Dock.Top); sidebar.Children.Add(sidebarTitle);
        var sidebarNote = UiTheme.Caption(Loc.T("Adults with wearable bodies only. Groups and races come from the current NPC scan."));
        DockPanel.SetDock(sidebarNote, Dock.Bottom); sidebar.Children.Add(sidebarNote);
        outfitSteps = new ListBox { Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), SelectionMode = SelectionMode.Single };
        AutomationProperties.SetName(outfitSteps,Loc.T("Outfits in this patch setup"));
        outfitSteps.SelectionChanged += (_, _) =>
        {
            if (changingStep || outfitSteps.SelectedIndex < 0 || outfitSteps.SelectedIndex == currentIndex) return;
            CommitCurrentPage(); ShowPage(outfitSteps.SelectedIndex);
        };
        sidebar.Children.Add(outfitSteps); body.Children.Add(sidebar);
        pageHost = new ContentControl(); Grid.SetColumn(pageHost, 2); body.Children.Add(pageHost);
        Grid.SetRow(body, 1); root.Children.Add(body);

        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 22, 0) };
        stepLabel = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        stepProgress = new ProgressBar { Height = 5, Margin = new Thickness(0, 9, 0, 0), Maximum = 100 };
        status.Children.Add(stepLabel); status.Children.Add(stepProgress); footer.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = UiTheme.Button(Loc.T("Save & close")); cancel.MinWidth = 100; cancel.IsCancel = true;
        cancel.ToolTip = Loc.T("Save these outfit settings and return to the library. To create files, use Review & generate.");
        cancel.Click += (_, _) => DialogResult = false;
        backButton = UiTheme.Button(Loc.T("← Back")); backButton.MinWidth = 90;
        backButton.Click += (_, _) => { CommitCurrentPage(); ShowPage(currentIndex - 1); };
        skipButton = UiTheme.Button(Loc.T("Skip outfit")); skipButton.MinWidth = 100;
        skipButton.ToolTip = Loc.T("Leave this outfit out of this patch session. Saved choices and any existing patch for it are kept. Select it in the sidebar to include it again.");
        skipButton.Click += (_, _) =>
        {
            CommitCurrentPage();
            var key = GetSavedKey(pages[currentIndex].Config);
            if (skippedOutfits.Remove(key)) { ShowPage(currentIndex); return; }
            skippedOutfits.Add(key);
            ActivityHistory.Add("Skipped outfit: " + pages[currentIndex].Config.OutfitName + ". Existing patch retained.");
            if (Options.Count == 0) { DialogResult = false; return; }
            if (currentIndex < pages.Count - 1) ShowPage(currentIndex + 1);
            else CompleteSetup();
        };
        nextButton = UiTheme.Button(Loc.T("Next outfit →"),true); nextButton.MinWidth = 138; nextButton.IsDefault = true;
        nextButton.Click += (_, _) =>
        {
            CommitCurrentPage();
            if (!IsSkipped(pages[currentIndex].Config) && !pages[currentIndex].Validate()) return;
            SavePage(pages[currentIndex].Config);
            if (currentIndex < pages.Count - 1) { ShowPage(currentIndex + 1); return; }
            CompleteSetup();
        };
        buttons.Children.Add(cancel); buttons.Children.Add(backButton); buttons.Children.Add(skipButton); buttons.Children.Add(nextButton); Grid.SetColumn(buttons, 1); footer.Children.Add(buttons);
        Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
    }

    private PageState BuildPage(PluginPatchOptions config)
    {
        GroupBox? audienceBox = null;
        Expander? advancedTargets = null;
        string? lastActivityState = null;
        var content = new StackPanel { Margin = new Thickness(0, 0, 12, 8) };
        var title = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        title.Children.Add(new TextBlock { Text = config.OutfitName, FontSize = 21, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        title.Children.Add(UiTheme.Caption(config.Source.Plugin));
        var summary = new TextBlock { Foreground = UiTheme.Brush("AccentBrush"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        summary.ToolTip = Loc.T("NPC counts describe distinct base records in the scan. More than one spawned actor can use the same record.");
        title.Children.Add(summary); content.Children.Add(title);
        var targetWarning = new TextBlock { Foreground = UiTheme.Brush("WarningBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed };
        content.Children.Add(targetWarning);
        var feedback = new ValidationNotice();
        if (restorationNotes.TryGetValue(config, out var restoreNote)) { feedback.Text = restoreNote; feedback.Visibility = Visibility.Visible; }
        content.Children.Add(feedback);

        var chance = new ComboBox { Width = 140, IsEditable = !IsSkyPatcher, IsEnabled = !IsSkyPatcher, ItemsSource = new[] { "100", "80", "50", "25", "10", "5" }, Text = config.Chance, Margin = new Thickness(0, 5, 14, 5) };
        if (IsSkyPatcher) { chance.SelectedItem = "100"; chance.Text = "100"; }
        var gender = new ComboBox { Width = 150, ItemsSource = new[] { "Any", "Female", "Male" }, ItemTemplate = Loc.ChoiceTemplate(), SelectedItem = config.Gender, Margin = new Thickness(0, 5, 14, 5) };
        if (gender.SelectedItem is null) gender.SelectedIndex = 0;
        var settings = new StackPanel();
        var npcMode = new CheckBox { Content = Loc.T("Distribute outfits for NPCs to wear"), IsChecked = config.DistributeToNpcs, Margin = new Thickness(0, 0, 0, 8) };
        settings.Children.Add(npcMode);
        var poolButton = UiTheme.Button(string.IsNullOrEmpty(config.WardrobeName) ? Loc.T("Random outfit pools…") : Loc.T("Edit wardrobe: ") + config.WardrobeName);
        poolButton.HorizontalAlignment = HorizontalAlignment.Left;
        poolButton.IsEnabled = !IsSkyPatcher;
        poolButton.ToolTip = Loc.T("Choose several outfits for one shared NPC audience and chance. SPID rolls once, then Skyrim selects one complete set using your weights.");
        poolButton.Click += (_, _) => EditWardrobes();
        settings.Children.Add(poolButton);
        if (!string.IsNullOrEmpty(config.WardrobeName)) settings.Children.Add(UiTheme.Caption(Loc.F($"Random wardrobe: {config.WardrobeName} · weight {config.WardrobeWeight}. Overall chance, sex and NPC groups must match all its members. Use Edit wardrobe to change these together.")));
        var npcControls = new StackPanel(); settings.Children.Add(npcControls);
        npcControls.Children.Add(new WrapPanel { Children = { Labeled(IsSkyPatcher ? Loc.T("Assignment (%)") : Loc.T("Chance per NPC (%)"),chance), Labeled(Loc.T("NPC sex"),gender) } });
        var genderHint = UiTheme.Caption(""); npcControls.Children.Add(genderHint);
        var updatingGender = false;
        void SyncGender()
        {
            if (updatingGender || !config.DistributeToNpcs) return;
            updatingGender = true;
            try
            {
                var required = OutfitGenderPolicy.RequiredGender(config.Pieces);
                var previous = gender.SelectedItem?.ToString() ?? "Any";
                gender.IsEnabled = required is "Any" or "Conflict";
                if (required is "Female" or "Male") gender.SelectedItem = required;
                gender.ToolTip = required is "Female" or "Male" ? Loc.F($"Locked to {Loc.T(required)}: included parts only have worn models for that sex.") : Loc.T("Choose which NPC sex receives this outfit.");
                genderHint.Text = required == "Conflict" ? Loc.T("Incompatible parts: both female-only and male-only pieces are selected. Exclude one set before continuing.")
                    : required is "Female" or "Male" ? Loc.F($"{Loc.T(required)}-only parts detected. NPC sex is locked to {Loc.T(required)}.") + (previous != "Any" && previous != required ? Loc.T(" Your incompatible sex selection was corrected.") : "")
                    : config.Pieces.Where(piece => piece.Include).Any(piece => piece.SupportedGender == "Unknown") ? Loc.T("Some parts have unknown sex support. Check the outfit author's requirements; model paths cannot verify body or mesh compatibility.")
                    : Loc.T("Worn model paths are available for both sexes. Body and race compatibility still depend on the outfit.");
                genderHint.Foreground = UiTheme.Brush(required == "Any" ? "MutedBrush" : "WarningBrush");
                config.Gender = gender.SelectedItem?.ToString() ?? "Any";
            }
            finally { updatingGender = false; }
        }
        SyncGender();
        var chanceHint = UiTheme.Caption(""); npcControls.Children.Add(chanceHint);
        var sell = new CheckBox { Content = Loc.T("Sell selected parts through traders"), IsChecked = config.MerchantCategories.Count > 0, Margin = new Thickness(0, 10, 0, 5) };
        settings.Children.Add(UiTheme.Caption(Loc.T("Choose NPCs, traders, or both. Untick NPC distribution for shop stock only; no NPC group or chance is needed.")));
        var shopOptions = new StackPanel { Margin = new Thickness(23, 0, 0, 0) };
        var shopChecks = MerchantStockService.Categories.Select(category => (Category: category, Box: new CheckBox
        {
            Content = Loc.T(category) + " (" + merchantTargets.Count(target => target.Category == category) + Loc.T(" stock containers)"),
            IsChecked = config.MerchantCategories.Count == 0 ? merchantTargets.Any(target => target.Category == category) : config.MerchantCategories.Contains(category),
            IsEnabled = merchantTargets.Any(target => target.Category == category) || config.MerchantCategories.Contains(category)
        })).ToList();
        foreach (var (_, box) in shopChecks) shopOptions.Children.Add(box);
        var stockWeapons = new CheckBox { Content = Loc.F($"Include weapons from this plugin ({config.Source.Weapons.Count})"), IsChecked = config.IncludeMerchandiseWeapons, IsEnabled = config.Source.Weapons.Count > 0, Margin = new Thickness(0, 6, 0, 0) };
        shopOptions.Children.Add(stockWeapons);
        shopOptions.Children.Add(UiTheme.Caption(Loc.T("Requires SkyPatcher with container patching enabled, even with SPID NPC outfits. Adds one of each included item to shop stock; NPC chance does not apply. Weapons go only to blacksmiths/general goods, never clothing traders.")));
        shopOptions.Visibility = sell.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        settings.Children.Add(sell);
        settings.Children.Add(UiTheme.Caption(Loc.T("Only want traders to sell these items? Tick only ‘Sell selected parts through traders.’ Leave NPC distribution unticked. No NPC group or chance is required; SkyPatcher container patching is required.")));
        settings.Children.Add(shopOptions);
        content.Children.Add(new GroupBox { Header = Loc.T("1. Distribution"), Content = settings, Margin = new Thickness(0, 0, 0, 12) });

        var ruleList = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, Height = 150, SelectionMode = DataGridSelectionMode.Single };
        ruleList.Columns.Add(new DataGridTextColumn { Header = Loc.T("NPC group"), Binding = new Binding(nameof(AudienceRow.Group)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        ruleList.Columns.Add(new DataGridTextColumn { Header = Loc.T("Roles / races"), Binding = new Binding(nameof(AudienceRow.Details)), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
        ruleList.Columns.Add(new DataGridTextColumn { Header = Loc.T("NPCs"), Binding = new Binding(nameof(AudienceRow.Matches)), Width = 70 });
        var audiencesEmpty = UiTheme.Caption(Loc.T("No NPC groups added yet. Choose who should wear this outfit, then click Add NPC group."));
        var collection = new ComboBox { MinWidth = 250, MaxWidth = 340, ItemsSource = targeting.GetCollections(), ItemTemplate = Loc.ChoiceTemplate(), SelectedIndex = 0, Margin = new Thickness(0, 5, 12, 6) };
        var group = new ComboBox { MinWidth = 200, MaxWidth = 320, Margin = new Thickness(0, 5, 0, 6) };
        var groupText = new FrameworkElementFactory(typeof(TextBlock));
        groupText.SetBinding(TextBlock.TextProperty, new Binding { Converter = new GroupLabelConverter(label =>
        {
            var protectedCount = targeting.CountProtectedMembers(label, collection.SelectedItem?.ToString() ?? NpcTargetingService.OfficialCollection);
            return Loc.T(label) + (protectedCount > 0 ? " · " + Loc.T("⚠ Protected members") : label == NpcTargetingService.UniqueGroup ? " · " + Loc.T("⚠ Quest/script caution") : "");
        }) });
        group.ItemTemplate = new DataTemplate { VisualTree = groupText };
        var roleChoices = new ObservableCollection<SelectableChoice>(); var raceChoices = new ObservableCollection<SelectableChoice>();
        var roles = CheckList(roleChoices); var races = CheckList(raceChoices);
        var roleEmpty = UiTheme.Caption(Loc.T("No separate roles found for this group. All members will be included."));
        var raceEmpty = UiTheme.Caption(Loc.T("No usable race records found for this group."));
        var rolePanel = new StackPanel(); rolePanel.Children.Add(roleEmpty); rolePanel.Children.Add(roles);
        var racePanel = new StackPanel(); racePanel.Children.Add(raceEmpty); racePanel.Children.Add(races);
        var narrow = new Grid { Margin = new Thickness(0, 6, 0, 0) }; narrow.ColumnDefinitions.Add(new ColumnDefinition()); narrow.ColumnDefinitions.Add(new ColumnDefinition());
        var rolesBox = new GroupBox { Header = Loc.T("Roles · optional"), Content = rolePanel, Background = UiTheme.Brush("AppBackgroundBrush"), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10) };
        var racesBox = new GroupBox { Header = Loc.T("Races · optional"), Content = racePanel, Background = UiTheme.Brush("AppBackgroundBrush"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10) }; Grid.SetColumn(racesBox, 1);
        narrow.Children.Add(rolesBox); narrow.Children.Add(racesBox);
        var draftSummary = UiTheme.Caption(""); draftSummary.Margin = new Thickness(0, 10, 0, 8);
        var addAudience = UiTheme.Button(Loc.T("＋ Add NPC group"),true);
        addAudience.ToolTip = Loc.T("Add the selected NPC group (for example Bandits), including any role/race filters, as recipients of this outfit. This does not add new NPCs to the game.");
        var quickSearch = new TextBox { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 5, 10, 2), ToolTip = Loc.T("Filter preset names or source plugins, for example bandit, mage, wenches or FlowerGirls") };
        AutomationProperties.SetName(quickSearch,Loc.T("Find a distribution preset or NPC mod"));
        var quickPicker = new ComboBox { MinWidth = 300, MaxWidth = 640, MaxDropDownHeight = 420, IsTextSearchEnabled = true, DisplayMemberPath = nameof(QuickAudiencePreset.DisplayLabel), Margin = new Thickness(0, 5, 10, 6) };
        AutomationProperties.SetName(quickPicker,Loc.T("Common and installed mod NPC group presets"));
        var quickHeader = new FrameworkElementFactory(typeof(TextBlock));
        quickHeader.SetBinding(TextBlock.TextProperty, new Binding("Name")); quickHeader.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        quickHeader.SetValue(TextBlock.ForegroundProperty, UiTheme.Brush("AccentBrush")); quickHeader.SetValue(TextBlock.MarginProperty, new Thickness(8));
        quickPicker.GroupStyle.Add(new GroupStyle { HeaderTemplate = new DataTemplate { VisualTree = quickHeader } });
        string? quickGender = null;
        void RefreshQuickPresets()
        {
            if (quickGender == config.Gender) return;
            quickGender = config.Gender;
            var previous = quickPicker.SelectedItem as QuickAudiencePreset;
            var choices = commonPresets.GetPresets(config.Gender).ToList();
            var view = new ListCollectionView(choices); view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(QuickAudiencePreset.DisplayCategory)));
            view.Filter = item => item is QuickAudiencePreset preset && (string.IsNullOrWhiteSpace(quickSearch.Text) ||
                (preset.Label + " " + preset.Category + " " + preset.DisplayLabel + " " + preset.DisplayCategory).Contains(quickSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            quickPicker.ItemsSource = view;
            quickPicker.SelectedItem = previous is null ? null : choices.FirstOrDefault(item => item.Category == previous.Category && RuleSignature(item.Rule) == RuleSignature(previous.Rule));
        }
        var useQuickPreset = UiTheme.Button(Loc.T("Add preset NPC group")); useQuickPreset.IsEnabled = false;
        quickSearch.TextChanged += (_, _) => CollectionViewSource.GetDefaultView(quickPicker.ItemsSource)?.Refresh();
        quickPicker.SelectionChanged += (_, _) => useQuickPreset.IsEnabled = quickPicker.SelectedItem is QuickAudiencePreset;
        useQuickPreset.Click += (_, _) =>
        {
            if (quickPicker.SelectedItem is not QuickAudiencePreset preset) return;
            var source = preset.Rule;
            var rule = new DistributionTargetRule { Collection = source.Collection, Group = source.Group, Filters = source.Filters.Select(choice => CloneChoice(choice)).ToList() };
            targeting.Resolve(rule, config.Gender);
            if (rule.EstimatedNpcCount == 0) return;
            if (!config.TargetRules.Any(existing => RuleSignature(existing) == RuleSignature(rule))) config.TargetRules.Add(rule);
            feedback.Text = Loc.F($"Added {source.Group}. Review the NPC groups below; other groups are kept.");
            ActivityHistory.Add(config.OutfitName + ": added audience preset " + preset.Label);
            feedback.Visibility = Visibility.Visible; RefreshAudiences(); SaveCurrent();
        };
        var removeAudience = UiTheme.Button(Loc.T("Remove selected")); removeAudience.IsEnabled = false;
        ruleList.SelectionChanged += (_, _) => removeAudience.IsEnabled = ruleList.SelectedItem is AudienceRow;
        void CommitFields()
        {
            config.DistributeToNpcs = npcMode.IsChecked == true;
            npcControls.Visibility = config.DistributeToNpcs ? Visibility.Visible : Visibility.Collapsed;
            if (audienceBox is not null) audienceBox.Visibility = npcControls.Visibility;
            if (advancedTargets is not null) advancedTargets.IsEnabled = config.DistributeToNpcs;
            config.Chance = IsSkyPatcher ? "100" : chance.Text.Trim().TrimEnd('%').Trim();
            SyncGender();
            config.MerchantCategories = sell.IsChecked == true ? shopChecks.Where(item => item.Box.IsChecked == true).Select(item => item.Category).ToList() : [];
            config.IncludeMerchandiseWeapons = stockWeapons.IsChecked == true;
            stockWeapons.IsEnabled = config.Source.Weapons.Count > 0 && config.MerchantCategories.Any(category => category != MerchantStockService.Clothing);
        }
        void UpdateSummary()
        {
            CommitFields();
            var match = hasNpcScan ? config.TargetRules.SelectMany(rule => targeting.GetMatchingProfiles(rule, config.Gender)).DistinctBy(profile => profile.FormId, StringComparer.OrdinalIgnoreCase).Count() : -1;
            summary.Text = Loc.F($"{config.Pieces.Count(piece => piece.Include)} / {config.Pieces.Count} parts included   ·   ") +
                (!config.DistributeToNpcs ? config.MerchantCategories.Count > 0 ? Loc.T("Merchants only · no NPC outfits changed") : Loc.T("Choose NPCs, traders or both") : Loc.F($"{config.TargetRules.Count} NPC group(s)") + (match >= 0 ? Loc.F($"   ·   {match} matching NPCs") : "") + Loc.F($"   ·   {config.Chance}%")) +
                (config.MerchantCategories.Count > 0 ? Loc.F($"   ·   {config.MerchantCategories.Count} trader categories") : "");
            chanceHint.Text = IsSkyPatcher ? Loc.T("Every matched NPC receives this outfit. Use SPID for a random chance. Existing outfits are replaced.")
                : !string.IsNullOrEmpty(config.WardrobeName) ? Loc.F($"{config.Chance}% is the shared wardrobe coverage. An eligible NPC gets one complete set from ‘{config.WardrobeName}’, selected by relative weight. No daily rotation is scheduled.")
                : int.TryParse(config.Chance, out var probability) && probability is > 0 and <= 100
                    ? probability == 100 ? Loc.T("Every eligible NPC receives this outfit. Existing outfits are replaced.")
                        : 100 % probability == 0 ? Loc.F($"{probability}% gives this outfit to roughly 1 in {100 / probability} eligible NPCs. Existing outfits are replaced.")
                            : Loc.F($"{probability}% rolls independently for each eligible NPC. Existing outfits are replaced.")
                    : Loc.T("Enter a whole-number chance from 1 to 100.");
            if (outfitSteps is not null) RefreshSteps();
            var protectedCount = config.DistributeToNpcs ? config.TargetRules.SelectMany(rule => targeting.GetMatchingProfiles(rule, config.Gender)).Where(profile => profile.IsProtectedUniformed).DistinctBy(profile => profile.FormId).Count() : 0;
            var warnings = new List<string>();
            if (protectedCount > 0) warnings.Add($"⚠ Protected: {protectedCount} selected NPC(s) have uniform protection. This explicit faction selection can replace their uniforms. Guards remain excluded.");
            if (config.DistributeToNpcs && config.TargetRules.Any(rule => rule.Group == NpcTargetingService.UniqueGroup)) warnings.Add("⚠ " + NpcTargetingService.UniqueWarning);
            targetWarning.Text = string.Join("\n", warnings); targetWarning.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        string DescribeSetup() => config.OutfitName + ": " +
            (config.DistributeToNpcs ? $"NPC outfits on · {config.Chance}% · {config.Gender} · {config.TargetRules.Count} NPC group(s)" : "NPC outfits off") +
            (config.MerchantCategories.Count > 0 ? " · Shops: " + string.Join(", ", config.MerchantCategories) : " · Shop stock off") +
            $" · {config.Pieces.Count(piece => piece.Include)}/{config.Pieces.Count} parts · source weapons {(config.IncludeMerchandiseWeapons ? "included for eligible shops" : "excluded")}";
        void SaveCurrent()
        {
            CommitFields(); SavePage(config); RefreshAudiences(); RefreshQuickPresets();
            var state = DescribeSetup();
            if (state != lastActivityState) { lastActivityState = state; ActivityHistory.Add(state); }
        }
        void ModeChanged()
        {
            feedback.Visibility = Visibility.Collapsed;
            SaveCurrent();
            ActivityHistory.Add(config.OutfitName + (config.DistributeToNpcs ? ": NPC distribution enabled." : ": NPC distribution disabled; choose traders for merchants-only stock."));
        }
        npcMode.Checked += (_, _) => ModeChanged(); npcMode.Unchecked += (_, _) => ModeChanged();
        sell.Checked += (_, _) =>
        {
            shopOptions.Visibility = Visibility.Visible;
            SaveCurrent(); ActivityHistory.Add(config.OutfitName + ": trader stock enabled" + (!config.DistributeToNpcs ? " · merchants only; no NPC group required." : " alongside NPC outfits."));
        };
        sell.Unchecked += (_, _) => { shopOptions.Visibility = Visibility.Collapsed; SaveCurrent(); ActivityHistory.Add(config.OutfitName + ": trader stock disabled."); };
        foreach (var (category, box) in shopChecks)
        {
            box.Checked += (_, _) => { SaveCurrent(); ActivityHistory.Add(config.OutfitName + ": trader category selected · " + category); };
            box.Unchecked += (_, _) => { SaveCurrent(); ActivityHistory.Add(config.OutfitName + ": trader category removed · " + category); };
        }
        stockWeapons.Checked += (_, _) => SaveCurrent(); stockWeapons.Unchecked += (_, _) => SaveCurrent();
        void RefreshAudiences()
        {
            ruleList.ItemsSource = config.TargetRules.Select(rule =>
            {
                var matches = targeting.GetMatchingProfiles(rule, config.Gender);
                rule.ProtectedNpcCount = matches.Count(profile => profile.IsProtectedUniformed);
                return new AudienceRow(Loc.T(rule.Group) + (rule.ProtectedNpcCount > 0 ? " · " + Loc.T("⚠ Protected") : ""), AudienceDetails(rule), hasNpcScan ? matches.Count.ToString(CultureInfo.InvariantCulture) : "—", rule);
            }).ToList();
            audiencesEmpty.Visibility = config.TargetRules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ruleList.Visibility = config.TargetRules.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateSummary();
        }
        DistributionTargetRule DraftRule()
        {
            var draft = targeting.BuildRule(group.SelectedItem?.ToString() ?? "", collection.SelectedItem?.ToString() ?? NpcTargetingService.OfficialCollection);
            draft.Filters.AddRange(roleChoices.Where(item => item.Selected).SelectMany(item => item.Choices).Select(item => CloneChoice(item, "NPC role")));
            draft.Filters.AddRange(raceChoices.Where(item => item.Selected).SelectMany(item => item.Choices).Select(item => CloneChoice(item, "NPC race")));
            draft.NpcResolutionComplete = false;
            return draft;
        }
        void RefreshDraft()
        {
            var draft = DraftRule(); var matches = targeting.GetMatchingProfiles(draft, gender.SelectedItem?.ToString() ?? "Any");
            addAudience.IsEnabled = draft.Filters.Count > 0 && matches.Count > 0;
            draftSummary.Text = matches.Count == 0
                ? hasNpcScan ? Loc.T("No eligible NPCs match this selection. Try another group, role, race, or collection.") : Loc.T("NPC targets are unavailable. Rescan the deployed game Data folder to enable NPC group selection.")
                : Loc.F($"{matches.Count} matching NPCs · ") + string.Join(", ", matches.Take(4).Select(profile => string.IsNullOrWhiteSpace(profile.Name) ? profile.EditorId : profile.Name)) + (matches.Count > 4 ? ", …" : "");
            draftSummary.Foreground = UiTheme.Brush(matches.Count == 0 ? "WarningBrush" : "MutedBrush");
            if (matches.Any(profile => profile.IsProtectedUniformed)) { draftSummary.Text += Loc.T("\n⚠ Protected members: this explicit faction selection can replace their uniforms. Guards remain excluded."); draftSummary.Foreground = UiTheme.Brush("WarningBrush"); }
            if (draft.Group == NpcTargetingService.UniqueGroup) { draftSummary.Text += "\n⚠ " + NpcTargetingService.UniqueWarning; draftSummary.Foreground = UiTheme.Brush("WarningBrush"); }
        }
        void RefreshNarrowing()
        {
            var selectedCollection = collection.SelectedItem?.ToString() ?? NpcTargetingService.OfficialCollection;
            var selectedGroup = group.SelectedItem?.ToString() ?? "";
            rolesBox.Header = selectedGroup == NpcTargetingService.UniqueGroup ? Loc.T("Individual NPCs · optional") : Loc.T("Roles · optional");
            FillChecks(roleChoices, targeting.GetRoles(selectedCollection, selectedGroup), RefreshDraft);
            FillChecks(raceChoices, targeting.GetRaces(selectedCollection, selectedGroup), RefreshDraft);
            roleEmpty.Visibility = roleChoices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            raceEmpty.Visibility = raceChoices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            roles.Visibility = roleChoices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            races.Visibility = raceChoices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshDraft();
        }
        void RefreshGroups()
        {
            var previous = group.SelectedItem?.ToString();
            var groups = targeting.GetGroupLabels(collection.SelectedItem?.ToString() ?? NpcTargetingService.OfficialCollection);
            group.ItemsSource = groups;
            group.SelectedItem = groups.Contains(previous ?? "") ? previous : groups.FirstOrDefault();
            RefreshNarrowing();
        }
        collection.SelectionChanged += (_, _) => RefreshGroups();
        group.SelectionChanged += (_, _) => RefreshNarrowing();
        gender.SelectionChanged += (_, _) => { if (updatingGender) return; RefreshAudiences(); RefreshDraft(); SaveCurrent(); };
        chance.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateSummary()));
        chance.LostKeyboardFocus += (_, _) => SaveCurrent();
        addAudience.Click += (_, _) =>
        {
            var rule = DraftRule();
            if (targeting.CountMatches(rule, config.Gender) == 0) return;
            if (config.TargetRules.Any(existing => RuleSignature(existing) == RuleSignature(rule)))
            {
                feedback.Text = Loc.T("That NPC group is already included. Select another group or narrow the roles and races."); feedback.Visibility = Visibility.Visible; return;
            }
            config.TargetRules.Add(rule); feedback.Visibility = Visibility.Collapsed; RefreshAudiences(); SaveCurrent();
            ActivityHistory.Add(config.OutfitName + ": added audience " + rule.Group + " · " + targeting.CountMatches(rule, config.Gender) + " NPCs");
        };
        removeAudience.Click += (_, _) => { if (ruleList.SelectedItem is AudienceRow row) { config.TargetRules.Remove(row.Rule); ActivityHistory.Add(config.OutfitName + ": removed audience " + row.Rule.Group); } RefreshAudiences(); SaveCurrent(); };

        var audiencePanel = new StackPanel();
        audiencePanel.Children.Add(Labeled(Loc.T("Find a preset or NPC mod (optional)"),quickSearch));
        audiencePanel.Children.Add(Labeled(Loc.T("Quick distribution presets"),quickPicker));
        audiencePanel.Children.Add(useQuickPreset);
        audiencePanel.Children.Add(UiTheme.Caption(Loc.T("Grouped by common NPC groups, factions/guilds and installed NPC mods. Protected labels apply only to members already covered by uniform protection. Choosing their explicit faction can replace their uniforms. Guards remain excluded.")));
        audiencePanel.Children.Add(UiTheme.Caption(Loc.T("Pick the NPC collection, then a familiar group. Leave roles and races unchecked to include the whole group. Multiple checked roles or races are alternatives.")));
        audiencePanel.Children.Add(new WrapPanel { Children = { Labeled(Loc.T("NPC collection"),collection), Labeled(Loc.T("Group"),group) } });
        audiencePanel.Children.Add(narrow); audiencePanel.Children.Add(draftSummary);
        audiencePanel.Children.Add(new WrapPanel { Margin = new Thickness(0, 0, 0, 12), Children = { addAudience, removeAudience } });
        audiencePanel.Children.Add(audiencesEmpty); audiencePanel.Children.Add(ruleList);
        audiencePanel.Children.Add(UiTheme.Caption(Loc.T("NPCs matching any added group can receive the outfit. Within one group: group + any selected role + any selected race are required together.")));
        audienceBox = new GroupBox { Header = Loc.T("2. Who should wear it?"), Content = audiencePanel, Margin = new Thickness(0, 0, 0, 12) };
        content.Children.Add(audienceBox);

        var partsEditor = new OutfitPartsEditor(config, SaveCurrent);
        var pieceGrid = partsEditor.Grid;
        content.Children.Add(new GroupBox { Header = Loc.T("3. Outfit parts · presets"), Content = partsEditor, Margin = new Thickness(0, 0, 0, 12) });
        var tools = new StackPanel();
        var presetPicker = new ComboBox { Width = 230, ItemsSource = presets.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList(), Margin = new Thickness(0, 5, 8, 8) };
        var applyPreset = UiTheme.Button(Loc.T("Apply")); var savePresetButton = UiTheme.Button(Loc.T("Save NPC groups as preset…")); var deletePresetButton = UiTheme.Button(Loc.T("Delete preset"));
        applyPreset.Click += (_, _) =>
        {
            if (presetPicker.SelectedItem is not string name || !presets.TryGetValue(name, out var preset)) return;
            config.TargetRules = RestoreRules(preset, out var missing);
            config.Chance = IsSkyPatcher ? "100" : preset.Chance; config.Gender = preset.Gender;
            chance.Text = config.Chance;
            var required = OutfitGenderPolicy.RequiredGender(config.Pieces);
            var incompatible = required is "Female" or "Male" && preset.Gender != "Any" && preset.Gender != required;
            gender.SelectedItem = required is "Female" or "Male" ? required : config.Gender;
            if (incompatible) { App.WriteDiagnostic(DiagnosticEvent.OutfitSexMismatch, level: DiagnosticLevel.Warning); MessageBox.Show(this,Loc.F($"This preset requests {Loc.T(preset.Gender)} NPCs, but the outfit has {Loc.T(required)}-only parts. NPC sex has been kept at {Loc.T(required)}."),Loc.T("Outfit sex restriction"),MessageBoxButton.OK,MessageBoxImage.Warning); }
            feedback.Text = missing > 0 ? Loc.F($"{missing} preset NPC group(s) are unavailable in this scan. Choose replacement NPC groups.") : Loc.T("NPC group preset applied. Outfit parts are unchanged.");
            feedback.Visibility = Visibility.Visible; RefreshAudiences(); SaveCurrent();
        };
        savePresetButton.Click += (_, _) =>
        {
            CommitFields();
            if (config.TargetRules.Count == 0) { feedback.Text = Loc.T("Add an NPC group before saving a reusable preset."); feedback.Visibility = Visibility.Visible; return; }
            var name = TextPromptWindow.Show(this, "Save NPC group preset", "Name this NPC group setup:");
            if (string.IsNullOrWhiteSpace(name)) return;
            savePreset(name.Trim(), SaveConfiguration(config, false));
            presetPicker.ItemsSource = presets.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList(); presetPicker.SelectedItem = name.Trim();
        };
        deletePresetButton.Click += (_, _) =>
        {
            if (presetPicker.SelectedItem is not string name || MessageBox.Show(this,Loc.F($"Delete the reusable preset '{name}'?"),Loc.T("Delete preset"),MessageBoxButton.YesNo,MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            removePreset(name); presetPicker.ItemsSource = presets.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();
        };
        tools.Children.Add(new WrapPanel { Children = { Labeled(Loc.T("Reusable NPC group setup"),presetPicker), applyPreset, savePresetButton, deletePresetButton } });
        tools.Children.Add(UiTheme.Caption(Loc.T("Presets keep NPC groups, sex, and chance settings. Outfit parts stay specific to this outfit.")));
        var createVariant = UiTheme.Button(Loc.T("Create another outfit from this plugin…")); createVariant.HorizontalAlignment = HorizontalAlignment.Left;
        createVariant.Click += (_, _) =>
        {
            CommitFields(); pieceGrid.CommitEdit(DataGridEditingUnit.Cell, true); pieceGrid.CommitEdit(DataGridEditingUnit.Row, true); SaveCurrent();
            var name = TextPromptWindow.Show(this, "Create a separate outfit", "Name this outfit (for example, Dark or Archer):");
            if (string.IsNullOrWhiteSpace(name)) return;
            if (options.Any(item => item.Source.Plugin.Equals(config.Source.Plugin, StringComparison.OrdinalIgnoreCase) && item.OutfitName.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            { feedback.Text = Loc.T("That outfit name is already used in this plugin. Choose another name."); feedback.Visibility = Visibility.Visible; return; }
            var duplicate = CloneConfiguration(config, name.Trim()); var at = currentIndex + 1;
            options.Insert(at, duplicate); pages.Insert(at, new PageState(duplicate, () => BuildPage(duplicate))); ShowPage(at);
        };
        tools.Children.Add(createVariant);
        tools.Children.Add(UiTheme.Caption(Loc.T("Use a separate named outfit for another color or set with different parts or NPC groups of its own.")));
        if (!config.OutfitName.Equals(Path.GetFileNameWithoutExtension(config.Source.Plugin), StringComparison.OrdinalIgnoreCase))
        {
            var removeVariant = UiTheme.Button(Loc.T("Remove this saved setup")); removeVariant.HorizontalAlignment = HorizontalAlignment.Left;
            removeVariant.Click += (_, _) =>
            {
                if (MessageBox.Show(this,Loc.F($"Remove '{config.OutfitName}' from saved setup choices? Any generated outfit stays installed. Use My patch → Remove outfit to remove it from the generated patch."),Loc.T("Remove saved setup"),MessageBoxButton.YesNo,MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                var index = pages.FindIndex(page => ReferenceEquals(page.Config, config));
                options.Remove(config); pages.RemoveAt(index); removeSavedOptions(GetSavedKey(config));
                if (pages.Count == 0) { DialogResult = false; return; }
                ShowPage(Math.Clamp(index - 1, 0, pages.Count - 1));
            };
            tools.Children.Add(removeVariant);
        }
        advancedTargets = BuildAdvanced(config, RefreshAudiences, SaveCurrent, feedback);
        tools.Children.Add(advancedTargets);
        content.Children.Add(new Expander { Header = Loc.T("Presets, separate outfits & advanced targets"), Content = tools, Margin = new Thickness(0, 2, 0, 0) });

        RefreshGroups();
        if (config.TargetRules.FirstOrDefault() is { } initial)
        {
            if (targeting.GetCollections().Contains(initial.Collection)) collection.SelectedItem = initial.Collection;
            if (targeting.GetGroupLabels(collection.SelectedItem?.ToString() ?? NpcTargetingService.OfficialCollection).Contains(initial.Group)) group.SelectedItem = initial.Group;
        }
        RefreshAudiences(); RefreshQuickPresets();
        lastActivityState = DescribeSetup();
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false, PanningMode = PanningMode.VerticalOnly };
        UiTheme.EnableScrollChaining(scroll);
        bool Validate()
        {
            pieceGrid.CommitEdit(DataGridEditingUnit.Cell, true); pieceGrid.CommitEdit(DataGridEditingUnit.Row, true); CommitFields();
            partsEditor.ShowPartWarnings();
            var problems = new List<string>();
            if (config.DistributeToNpcs && !hasNpcScan) problems.Add("Rescan the deployed game Data folder before generating. A resolved NPC roster is required to exclude guards and uniformed protectors.");
            if (config.DistributeToNpcs && (!int.TryParse(config.Chance, NumberStyles.Integer, CultureInfo.InvariantCulture, out var probability) || probability is < 1 or > 100)) problems.Add("Enter a whole-number chance from 1 to 100.");
            if (!config.Pieces.Any(piece => piece.Include)) problems.Add("Include at least one outfit part.");
            problems.AddRange(OutfitPartPresets.Errors(config));
            if (config.DistributeToNpcs && OutfitGenderPolicy.ValidationIssue(config) is { } sexIssue) problems.Add(sexIssue);
            if (config.DistributeToNpcs && config.TargetRules.Count == 0) problems.Add("Choose who should wear this outfit and click Add NPC group, or untick NPC distribution for merchants-only stock.");
            if (!config.DistributeToNpcs && config.MerchantCategories.Count == 0) problems.Add("Tick trader sales and choose an available trader category, or enable NPC distribution and add an NPC group.");
            if (sell.IsChecked == true && config.MerchantCategories.Count == 0) problems.Add("Tick at least one trader category, or untick merchandise.");
            foreach (var category in config.MerchantCategories)
                if (!merchantTargets.Any(target => target.Category == category)) problems.Add($"No confirmed {category} stock containers found. Rescan or untick that category.");
            if (config.DistributeToNpcs && hasNpcScan && config.TargetRules.Any(rule => targeting.CountMatches(rule, config.Gender) == 0)) problems.Add("One or more saved groups match no eligible NPCs. Remove or replace those groups.");
            feedback.ShowMessages(problems,Loc.T("Before you continue"),OutfitMeshPolicy.Details(config));
            if (problems.Count > 0) { App.WriteDiagnostic(DiagnosticEvent.OutfitValidationFailed, level: DiagnosticLevel.Warning); scroll.ScrollToTop(); }
            UpdateSummary(); return problems.Count == 0;
        }
        return new PageState(config, scroll, () => { pieceGrid.CommitEdit(DataGridEditingUnit.Cell, true); pieceGrid.CommitEdit(DataGridEditingUnit.Row, true); CommitFields(); }, Validate);
    }

    private Expander BuildAdvanced(PluginPatchOptions config, Action refresh, Action save, ValidationNotice feedback)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(UiTheme.Caption(Loc.T("Search actual installed NPC targets. Select several records with Ctrl. Records in the same category are alternatives; different categories are required together.")));
        var search = new TextBox { Margin = new Thickness(0, 3, 0, 8) }; AutomationProperties.SetName(search,Loc.T("Search NPC targets"));
        var choices = new ListBox { Height = 185, SelectionMode = SelectionMode.Extended, DisplayMemberPath = nameof(AdvancedTargetChoice.Label) };
        search.TextChanged += (_, _) =>
        {
            if (advancedChoices is not null) choices.ItemsSource = advancedChoices.Where(item => item.Label.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToList();
        };
        var add = UiTheme.Button(Loc.T("Add selected NPC targets")); add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(0, 8, 0, 0);
        add.Click += (_, _) =>
        {
            var selected = choices.SelectedItems.Cast<AdvancedTargetChoice>().Select(item => CloneChoice(item.Choice)).ToList();
            if (selected.Count == 0) return;
            var rule = new DistributionTargetRule { Collection = NpcTargetingService.AllCollection, Group = "Custom audience", Filters = selected };
            if (hasNpcScan && targeting.CountMatches(rule, config.Gender) == 0) { App.WriteDiagnostic(DiagnosticEvent.AudienceValidationFailed, level: DiagnosticLevel.Warning); feedback.Text = Loc.T("Those combined targets match no eligible NPCs. Choose another combination."); feedback.Visibility = Visibility.Visible; return; }
            if (config.TargetRules.Any(existing => RuleSignature(existing) == RuleSignature(rule))) return;
            config.TargetRules.Add(rule); feedback.Visibility = Visibility.Collapsed; refresh(); save();
        };
        panel.Children.Add(search); panel.Children.Add(choices); panel.Children.Add(add);
        var browser = new Expander { Header = Loc.T("Advanced target browser"), Content = panel, Margin = new Thickness(0, 10, 0, 0) };
        browser.Expanded += (_, _) =>
        {
            advancedChoices ??= config.Keywords.Select(choice => new AdvancedTargetChoice($"{FriendlyLabel(choice)} · {choice.TargetKind} · {choice.Plugin}", choice))
                .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase).ToList();
            choices.ItemsSource = advancedChoices.Where(item => item.Label.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToList();
        };
        return browser;
    }

    private List<DistributionTargetRule> RestoreRules(SavedDistributionOptions? saved, out int missing)
    {
        missing = 0; if (saved is null) return [];
        var available = availableByKey;
        var restored = new List<DistributionTargetRule>();
        foreach (var savedRule in saved.TargetRules ?? [])
        {
            var collection = savedRule.Collection == "All installed mods" ? NpcTargetingService.AllCollection : savedRule.Collection;
            var knownGroup = hasNpcScan && targeting.GetGroupLabels(collection).Contains(savedRule.Group, StringComparer.OrdinalIgnoreCase);
            var savedFilters = knownGroup ? savedRule.Filters.Where(filter => !filter.Category.Equals("NPC audience", StringComparison.OrdinalIgnoreCase)).ToList() : savedRule.Filters;
            var resolved = savedFilters.Select(filter => (Saved: filter, Current: available.GetValueOrDefault(filter.TargetKind + "|0x" + filter.LocalId.ToString("X6") + "~" + filter.Plugin))).ToList();
            // Dropping just one required filter can widen an audience. Restore all filters or require user repair.
            if (resolved.Any(pair => pair.Current is null)) { missing++; continue; }
            var rule = knownGroup ? targeting.BuildRule(savedRule.Group, collection) : new DistributionTargetRule { Collection = collection, Group = savedRule.Group };
            rule.Filters.AddRange(resolved.Select(pair => CloneChoice(pair.Current!, pair.Saved.Category)));
            rule.NpcResolutionComplete = false;
            if (rule.Filters.Count == 0 || hasNpcScan && targeting.CountMatches(rule, saved.Gender) == 0) { missing++; continue; }
            restored.Add(rule);
        }
        return restored;
    }

    private List<DistributionTargetRule> SuggestAudiences(string plugin)
    {
        if (!hasNpcScan) return [];
        var titleWords = Words(Path.GetFileNameWithoutExtension(plugin));
        var suggestions = targeting.GetGroupLabels(NpcTargetingService.OfficialCollection)
            .Where(group => group != "All NPCs" && Words(group).Any(word => titleWords.Contains(word) || titleWords.Contains(word.TrimEnd('s')) || titleWords.Any(titleWord => titleWord.StartsWith(word.TrimEnd('s') + "core", StringComparison.OrdinalIgnoreCase))))
            .Select(group => targeting.BuildRule(group, NpcTargetingService.OfficialCollection)).Where(rule => rule.Filters.Count > 0).Take(3).ToList();
        return suggestions;
    }

    private void RefreshSteps()
    {
        if (outfitSteps is null) return;
        changingStep = true;
        outfitSteps.Items.Clear();
        for (var i = 0; i < options.Count; i++)
        {
            var config = options[i]; var ready = (config.DistributeToNpcs ? config.TargetRules.Count > 0 : config.MerchantCategories.Count > 0) && config.Pieces.Any(piece => piece.Include);
            var skipped = IsSkipped(config);
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = Loc.F($"{i + 1:00}   {config.OutfitName}"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            label.Children.Add(new TextBlock { Text = skipped ? Loc.T("Skipped · not included this session") : ready ? Loc.F($"{config.Pieces.Count(piece => piece.Include)} parts · ") + (config.DistributeToNpcs ? Loc.F($"{config.TargetRules.Count} NPC group(s)") : Loc.T("merchants only")) : Loc.T("Choose parts & recipients"), FontSize = 11, Foreground = UiTheme.Brush(!skipped && ready ? "AccentBrush" : "MutedBrush"), Margin = new Thickness(22, 5, 0, 0), TextWrapping = TextWrapping.Wrap });
            outfitSteps.Items.Add(new ListBoxItem { Content = label, ToolTip = config.Source.Plugin, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 5) });
        }
        outfitSteps.SelectedIndex = Math.Clamp(currentIndex, 0, options.Count - 1); changingStep = false;
    }

    private void ShowPage(int index)
    {
        currentIndex = Math.Clamp(index, 0, pages.Count - 1); pageHost.Content = pages[currentIndex].View;
        ActivityHistory.Add("Opened outfit setup: " + pages[currentIndex].Config.OutfitName);
        stepLabel.Text = Loc.F($"Outfit {currentIndex + 1} of {pages.Count} · {pages[currentIndex].Config.OutfitName}");
        stepProgress.Value = 100.0 * (currentIndex + 1) / pages.Count;
        backButton.IsEnabled = currentIndex > 0; nextButton.Content = currentIndex == pages.Count - 1 ? Loc.T("Review & generate →") : Loc.T("Next outfit →");
        skipButton.Content = IsSkipped(pages[currentIndex].Config) ? Loc.T("Include outfit") : Loc.T("Skip outfit");
        RefreshSteps(); outfitSteps.ScrollIntoView(outfitSteps.SelectedItem);
    }

    private bool IsSkipped(PluginPatchOptions config) => skippedOutfits.Contains(GetSavedKey(config));

    private void CompleteSetup()
    {
        if (Options.Count == 0) { DialogResult = false; return; }
        for (var i = 0; i < pages.Count; i++)
        {
            if (IsSkipped(pages[i].Config)) continue;
            pages[i].Commit();
            if (pages[i].Validate()) continue;
            ShowPage(i); return;
        }
        foreach (var config in Options.Where(c => c.DistributeToNpcs && !string.IsNullOrEmpty(c.WardrobeName)))
            foreach (var rule in config.TargetRules) targeting.Resolve(rule, config.Gender);
        var poolErrors = WardrobePools.ConfigurationErrors(Options, framework);
        if (poolErrors.Count > 0) { MessageBox.Show(this,string.Join("\n\n", poolErrors),Loc.T("Review random outfit pools"),MessageBoxButton.OK,MessageBoxImage.Warning); return; }
        DialogResult = true;
    }

    private void CommitCurrentPage() { if (pages.Count > 0) { pages[currentIndex].Commit(); SavePage(pages[currentIndex].Config); ActivityHistory.Add("Saved setup: " + pages[currentIndex].Config.OutfitName); } }
    private void SaveAllPages()
    {
        foreach (var page in pages) page.Commit();
        if (saveBatch is not null) saveBatch(pages.ToDictionary(page => GetSavedKey(page.Config), page => SaveConfiguration(page.Config, true), StringComparer.OrdinalIgnoreCase));
        else foreach (var page in pages) SavePage(page.Config);
    }
    private void SavePage(PluginPatchOptions config) => saveOptions(GetSavedKey(config), SaveConfiguration(config, true));
    private SavedDistributionOptions SaveConfiguration(PluginPatchOptions config, bool includePieces) => new()
    {
        WardrobeName = includePieces ? config.WardrobeName : "", WardrobeWeight = includePieces ? config.WardrobeWeight : 1,
        OutfitName = config.OutfitName, Chance = config.Chance, Gender = config.Gender,
        MatchVariants = includePieces && config.MatchVariants, PreferredColor = includePieces ? config.PreferredColor : "", PartPreset = includePieces ? config.PartPreset : "Custom", AllowLewdParts = includePieces && config.AllowLewdParts,
        PartChoices = includePieces ? config.Pieces.Select(p => new SavedPartChoice(p.FormId, p.PartMode, p.OptionalChance)).ToList() : [],
        DistributeToNpcs = config.DistributeToNpcs,
        MerchantCategories = config.MerchantCategories.ToList(), IncludeMerchandiseWeapons = config.IncludeMerchandiseWeapons,
        ExcludedPieceFormIds = includePieces ? config.Pieces.Where(piece => !piece.Include).Select(piece => piece.FormId).ToList() : [],
        TargetRules = config.TargetRules.Select(rule => new SavedDistributionTargetRule
        {
            Collection = rule.Collection, Group = rule.Group,
            // Named groups are rebuilt from each scan during restore. Persist only refinements;
            // serializing thousands of expanded audience IDs on every click stalls the UI.
            Filters = rule.Filters.Where(choice => choice.Category != "NPC audience" ||
                !targeting.GetGroupLabels(rule.Collection).Contains(rule.Group, StringComparer.OrdinalIgnoreCase))
                .Select(choice => new SavedTargetChoice { EditorId = choice.EditorId, Plugin = choice.Plugin, Category = choice.Category, TargetKind = choice.TargetKind, LocalId = choice.LocalId }).ToList()
        }).ToList()
    };

    private void EditWardrobes()
    {
        if (IsSkipped(pages[currentIndex].Config)) { MessageBox.Show(this,Loc.T("Include this outfit again before editing its random wardrobe."),Loc.T("Outfit skipped"),MessageBoxButton.OK,MessageBoxImage.Information); return; }
        foreach (var page in pages) page.Commit();
        foreach (var config in Options.Where(c => c.DistributeToNpcs)) foreach (var rule in config.TargetRules) targeting.Resolve(rule, config.Gender);
        var dialog = new WardrobePoolWindow(Options, pages[currentIndex].Config) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        // Old controls hold stale field values: discard lazy views before saving the changed models.
        pages.Clear(); foreach (var config in options) pages.Add(new PageState(config, () => BuildPage(config)));
        if (saveBatch is not null) saveBatch(options.ToDictionary(GetSavedKey, c => SaveConfiguration(c, true), StringComparer.OrdinalIgnoreCase));
        else foreach (var config in options) SavePage(config);
        ShowPage(currentIndex);
    }

    private static string GetSavedKey(PluginPatchOptions config) => config.OutfitName.Equals(Path.GetFileNameWithoutExtension(config.Source.Plugin), StringComparison.OrdinalIgnoreCase) ? config.Source.Plugin : config.Source.Plugin + "|outfit|" + config.OutfitName;
    private static WearableRecord ClonePiece(WearableRecord piece, bool include) => new() { Plugin = piece.Plugin, LocalId = piece.LocalId, EditorId = piece.EditorId, Name = piece.Name, Slots = piece.Slots, Category = piece.Category, SourcePath = piece.SourcePath, Include = include, SupportedGender = piece.SupportedGender, GenderEvidence = piece.GenderEvidence, WornModels = piece.WornModels.ToList(), MeshEvidence = piece.MeshEvidence, PartMode = include ? piece.PartMode == "Excluded" ? "Required" : piece.PartMode : "Excluded", OptionalChance = piece.OptionalChance };
    private static KeywordChoice CloneChoice(KeywordChoice choice, string? category = null) => new() { EditorId = choice.EditorId, Plugin = choice.Plugin, Category = category ?? choice.Category, TargetKind = choice.TargetKind, LocalId = choice.LocalId, Selected = choice.Selected, SuggestionReason = choice.SuggestionReason };
    private static PluginPatchOptions CloneConfiguration(PluginPatchOptions source, string name) => new() { MatchVariants = source.MatchVariants, PreferredColor = source.PreferredColor, PartPreset = source.PartPreset, AllowLewdParts = source.AllowLewdParts, Source = source.Source, OutfitName = name, DistributeToNpcs = source.DistributeToNpcs, Chance = source.Chance, Gender = source.Gender, MerchantCategories = source.MerchantCategories.ToList(), IncludeMerchandiseWeapons = source.IncludeMerchandiseWeapons, MerchantTargets = source.MerchantTargets.ToList(), Pieces = source.Pieces.Select(piece => ClonePiece(piece, piece.Include)).ToList(), Keywords = source.Keywords, TargetRules = source.TargetRules.Select(rule => new DistributionTargetRule { Collection = rule.Collection, Group = rule.Group, Filters = rule.Filters.Select(choice => CloneChoice(choice)).ToList() }).ToList() };
    private static string AudienceDetails(DistributionTargetRule rule)
    {
        var refinements = rule.Filters.Where(choice => choice.TargetKind != "Npc" || choice.Category == "NPC role").Select(FriendlyLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return rule.Collection + (refinements.Count > 0 ? " · " + string.Join(", ", refinements.Take(8)) + (refinements.Count > 8 ? ", …" : "") : " · All roles & races");
    }
    private static string RuleSignature(DistributionTargetRule rule) => rule.Collection.ToUpperInvariant() + "|" + string.Join(";", rule.Filters.Select(choice => choice.Category.ToUpperInvariant() + ":" + choice.FormId.ToUpperInvariant()).Order(StringComparer.Ordinal));
    private static string FriendlyLabel(KeywordChoice choice) => NpcTargetingService.FriendlyLabel(choice);
    private static HashSet<string> Words(string text) => Regex.Matches(Regex.Replace(text, "([a-z0-9])([A-Z])", "$1 $2"), "[A-Za-z]+").Select(match => match.Value.ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static FrameworkElement Labeled(string text, FrameworkElement control)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        panel.Children.Add(new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Brush("MutedBrush") }); panel.Children.Add(control);
        AutomationProperties.SetName(control,text); return panel;
    }
    private static ListBox CheckList(ObservableCollection<SelectableChoice> choices)
    {
        var list = new ListBox { ItemsSource = choices, MaxHeight = 150, MinHeight = 60, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
        var check = new FrameworkElementFactory(typeof(CheckBox)); check.SetBinding(ContentControl.ContentProperty, new Binding(nameof(SelectableChoice.Label))); check.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(SelectableChoice.Details))); check.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(SelectableChoice.Selected)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        list.ItemTemplate = new DataTemplate { VisualTree = check };
        return list;
    }
    private static void FillChecks(ObservableCollection<SelectableChoice> target, IEnumerable<KeywordChoice> choices, Action changed)
    {
        var selected = target.Where(item => item.Selected).SelectMany(item => item.Choices).Select(item => item.FormId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        target.Clear();
        foreach (var group in choices.DistinctBy(choice => choice.FormId, StringComparer.OrdinalIgnoreCase).GroupBy(FriendlyLabel, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var grouped = group.Select(choice => CloneChoice(choice)).ToList();
            var item = new SelectableChoice(grouped, group.Key, grouped.Any(choice => selected.Contains(choice.FormId)));
            item.PropertyChanged += (_, _) => changed(); target.Add(item);
        }
    }

    private sealed class PageState
    {
        public PluginPatchOptions Config { get; }
        private readonly Func<PageState>? create;
        private PageState? built;
        private readonly FrameworkElement? view;
        private readonly Action? commit;
        private readonly Func<bool>? validate;
        public PageState(PluginPatchOptions config, Func<PageState> create) { Config = config; this.create = create; }
        public PageState(PluginPatchOptions config, FrameworkElement view, Action commit, Func<bool> validate)
        { Config = config; this.view = view; this.commit = commit; this.validate = validate; }
        private PageState Ready => create is null ? this : built ??= create();
        public FrameworkElement View => Ready.view!;
        // Unvisited pages already have restored model data; saving must not construct their controls.
        public void Commit() { if (create is null) commit!(); else built?.Commit(); }
        public bool Validate() => Ready.validate!();
    }
    private sealed class GroupLabelConverter(Func<string, string> label) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string name ? Loc.T(label(name)) : value;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
    private sealed record AudienceRow(string Group, string Details, string Matches, DistributionTargetRule Rule);
    private sealed class SelectableChoice(List<KeywordChoice> choices, string label, bool selected) : INotifyPropertyChanged
    {
        private bool isSelected = selected;
        public List<KeywordChoice> Choices { get; } = choices;
        public string Label { get; } = Loc.T(label);
        public string Details => string.Join("\n", Choices.Select(choice => choice.EditorId + " · " + choice.Plugin));
        public bool Selected { get => isSelected; set { if (isSelected == value) return; isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
