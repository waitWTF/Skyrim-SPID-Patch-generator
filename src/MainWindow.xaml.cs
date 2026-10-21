using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;

namespace WearableDistributor;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<PluginEntry> visiblePlugins = [];
    private readonly ObservableCollection<PatchInventoryRow> inventoryRows = [];
    private readonly List<PluginEntry> scannedPlugins = [];
    private readonly List<KeywordChoice> availableKeywords = [];
    private readonly List<MerchantTarget> merchantTargets = [];
    private readonly Dictionary<string, NpcTargetProfile> npcProfiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> patchSearchRoots = [];
    private readonly List<string> discoveredPatchFiles = [];
    private readonly List<PluginEntry> recoveredPatchedSources = [];
    private int inventoryRefreshVersion;
    private List<(string Path, string Line)> scannedDistributionRules = [];
    private readonly string preferencesFile = Path.Combine(BuildInfo.SettingsRoot, "settings.json");
    private AppPreferences preferences = new();
    private string dataPath = "";
    private bool activePluginFilterApplied;
    private CancellationTokenSource? scanCancellation;
    private bool isGenerating;
    private bool isPreparingSetup;
    private bool isConfiguring;
    private PluginEntry? contextPlugin;
    private AudiencePreparation? audiencePreparation;
    private bool isDiscoveringLocations;
    private string lastScanReport = "No scan report yet.";
    private bool settingsRecoveryRequired;
    private bool editingPatchedOutfit;
    private IReadOnlyDictionary<string, IReadOnlyList<string>>? requestedRepatchOutfits;
    private bool reuseSavedRepatch;

    public MainWindow()
    {
        InitializeComponent();
        Title += " — " + BuildInfo.Label;
        BuildVersionText.Text = BuildInfo.Label;
        ExperimentalHeaderText.Text = BuildInfo.Label + " · OUTFITS & MERCHANDISE";
        var statusDescriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        EventHandler statusChanged = (_, _) => ActivityHistory.Add(StatusText.Text);
        statusDescriptor.AddValueChanged(StatusText, statusChanged);
        Closed += (_, _) => statusDescriptor.RemoveValueChanged(StatusText, statusChanged);
        ActivityHistory.Add("App opened. Activity history is local to this session.");
        UiTheme.Apply(this);
        PluginsGrid.ItemsSource = visiblePlugins;
        PatchInventoryGrid.ItemsSource = inventoryRows;
        MainTabs.SelectionChanged += (_, _) =>
        {
            OutfitsNavButton.IsChecked = MainTabs.SelectedIndex == 0;
            InventoryNavButton.IsChecked = MainTabs.SelectedIndex == 1;
            SettingsNavButton.IsChecked = MainTabs.SelectedIndex == 2;
        };
        PluginsGrid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateSelectionSummary));
        PluginsGrid.SelectionChanged += (_, _) => { contextPlugin = null; UpdateSelectionSummary(); };
        Closed += (_, _) => scanCancellation?.Cancel();
        LoadPreferences();
        DataPathBox.Text = preferences.DataPath;
        BaseGameDataPathBox.Text = preferences.BaseGameDataPath;
        OutputPathBox.Text = preferences.OutputPath;
        PluginListPathBox.Text = preferences.PluginListPath;
        DefaultFrameworkBox.SelectedIndex = preferences.Framework == "SkyPatcher" ? 1 : 0;
        DefaultChanceBox.SelectedIndex = ChanceIndex(preferences.DefaultChance);
        IncludeAccessoriesBox.IsChecked = preferences.IncludeAccessories;
        ExclusionsBox.Text = string.Join(Environment.NewLine, preferences.AdditionalExclusions);
        dataPath = preferences.DataPath;
        ActivePathText.Text = string.IsNullOrWhiteSpace(dataPath) ? "Choose your Skyrim Data or mod staging folder in Settings." : dataPath;
        UpdateSelectionSummary();
        ContentRendered += StartWorkspace;
    }

    private void StartWorkspace(object? sender, EventArgs e)
    {
        ContentRendered -= StartWorkspace;
        // The workspace and its loading indicator must render before discovery or scanning starts.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            if (Environment.GetEnvironmentVariable("WEARABLE_DISTRIBUTOR_NO_AUTOSCAN") == "1" || settingsRecoveryRequired) { RefreshInventory(); return; }
            if (Directory.Exists(dataPath) && (!ScanLocationDiscovery.IsMo2Attached() || GameDataLocator.IsGameData(dataPath))) ScanFolder();
            else FindData_Click(this, new RoutedEventArgs());
        }));
    }

    private void LoadPreferences()
    {
        try
        {
            preferences = PreferencesService.Load(preferencesFile);
        }
        catch (Exception ex)
        {
            App.WriteDiagnostic(DiagnosticEvent.PreferencesLoadFailed, ex);
            preferences = new AppPreferences();
            settingsRecoveryRequired = true;
            StatusText.Text = "Saved settings could not be read. The original file has been preserved. Import a backup or explicitly save new preferences.";
            lastScanReport = $"Settings recovery required: {ex.Message}\nOriginal file: {preferencesFile}";
            ScanDiagnosticsBox.Text = lastScanReport;
        }
    }

    private void SavePreferences()
    {
        if (settingsRecoveryRequired) return;
        try { PreferencesService.Save(preferencesFile, preferences); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.WriteDiagnostic(DiagnosticEvent.PreferencesSaveFailed, ex);
            StatusText.Text = $"The app could not save settings: {ex.Message}";
        }
    }

    private bool TryFindDataFolder(out string path, AppPreferences? settings = null)
    {
        settings ??= preferences;
        var contextual = GameDataLocator.Find(settings.DataPath, settings.PluginListPath, settings.BaseGameDataPath);
        if (contextual is not null) { path = contextual; return true; }
        var roots = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "Skyrim Special Edition", "Data"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps", "common", "Skyrim Special Edition", "Data"),
            @"C:\GOG Games\Skyrim Special Edition\Data",
            @"H:\SteamLibrary\steamapps\common\Skyrim Special Edition\Data"
        };
        var steamRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        try
        {
            steamRoot = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string ?? steamRoot;
            foreach (var key in new[] { @"HKEY_LOCAL_MACHINE\SOFTWARE\Bethesda Softworks\Skyrim Special Edition", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Bethesda Softworks\Skyrim Special Edition" })
                if (Registry.GetValue(key, "Installed Path", null) is string installed) roots.Add(Path.Combine(installed, "Data"));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { App.WriteDiagnostic(DiagnosticEvent.RegistryDiscoveryFailed, ex, DiagnosticLevel.Warning); }
        roots.Add(Path.Combine(steamRoot, "steamapps", "common", "Skyrim Special Edition", "Data"));
        var steamLibraries = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        try { if (File.Exists(steamLibraries))
            foreach (Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(steamLibraries), "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                roots.Add(Path.Combine(match.Groups[1].Value.Replace("\\\\", "\\"), "steamapps", "common", "Skyrim Special Edition", "Data"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.WriteDiagnostic(DiagnosticEvent.SteamDiscoveryFailed, ex, DiagnosticLevel.Warning); }
        path = roots.FirstOrDefault(GameDataLocator.IsGameData) ?? "";
        return !string.IsNullOrEmpty(path);
    }

    private void BrowseData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose Skyrim Data or Vortex staging folder" };
        if (dialog.ShowDialog() != true) return;
        DataPathBox.Text = dialog.FolderName;
    }

    private void BrowsePluginList_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose the active profile plugin list", Filter = "Plugin lists (plugins.txt;loadorder.txt)|plugins.txt;loadorder.txt|Text files (*.txt)|*.txt|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true) PluginListPathBox.Text = dialog.FileName;
    }

    private void BrowseBaseGameData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the game Data folder containing Skyrim.esm" };
        if (dialog.ShowDialog(this) == true) BaseGameDataPathBox.Text = dialog.FolderName;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        if (scanCancellation != null || isGenerating || isPreparingSetup || isConfiguring) return;
        var dialog = new OpenFolderDialog { Title = "Choose your generated mod folder", InitialDirectory = Directory.Exists(preferences.OutputPath) ? preferences.OutputPath : "" };
        if (dialog.ShowDialog(this) != true) return;
        preferences.OutputPath = dialog.FolderName; OutputPathBox.Text = dialog.FolderName;
        SavePreferences(); RefreshInventory();
        StatusText.Text = "Output folder selected. The patch library now shows generated outfits in this folder.";
    }

    private async void FindData_Click(object sender, RoutedEventArgs e)
    {
        if (scanCancellation != null || isGenerating || isPreparingSetup || isConfiguring || isDiscoveringLocations) return;
        isDiscoveringLocations = true;
        ScanButton.IsEnabled = false; GenerateButton.IsEnabled = false;
        ActionProgress.Visibility = Visibility.Visible; ActionProgress.IsIndeterminate = true;
        StatusText.Text = "Finding MO2 instances, game Data and profile plugin visibility…";
        ScanLocation? selected = null;
        try
        {
            var snapshot = PreferencesService.Clone(preferences);
            snapshot.DataPath = DataPathBox.Text.Trim(); snapshot.PluginListPath = PluginListPathBox.Text.Trim();
            snapshot.BaseGameDataPath = BaseGameDataPathBox.Text.Trim();
            var locations = await Task.Run(() =>
            {
                TryFindDataFolder(out var fallback, snapshot);
                return ScanLocationDiscovery.Discover(snapshot, fallback);
            });
            var dialog = new ScanLocationWindow(locations) { Owner = this };
            if (dialog.ShowDialog() == true) selected = dialog.SelectedLocation;
            if (selected == null) { StatusText.Text = "Folder detection canceled. You can still browse manually in Settings."; return; }
            DataPathBox.Text = preferences.DataPath = dataPath = selected.DataPath;
            // Use the game Data as the scan root, never an MO2 mods folder with ambiguous winners.
            BaseGameDataPathBox.Text = preferences.BaseGameDataPath = "";
            PluginListPathBox.Text = preferences.PluginListPath = selected.ProfilePath;
            scannedPlugins.Clear(); availableKeywords.Clear(); npcProfiles.Clear(); merchantTargets.Clear(); scannedDistributionRules.Clear();
            audiencePreparation = null; patchSearchRoots.Clear(); discoveredPatchFiles.Clear(); recoveredPatchedSources.Clear();
            RefreshPluginList();
            SavePreferences();
            lastScanReport = "Detected scan location:\n" + selected.Explanation;
            ScanDiagnosticsBox.Text = lastScanReport;
        }
        catch (Exception ex) { App.WriteDiagnostic(DiagnosticEvent.FolderDiscoveryFailed, ex); StatusText.Text = "Folder detection failed: " + ex.Message + ". Browse manually in Settings."; }
        finally
        {
            isDiscoveringLocations = false; ScanButton.IsEnabled = true;
            ActionProgress.IsIndeterminate = false; ActionProgress.Visibility = Visibility.Collapsed;
            UpdateSelectionSummary();
        }
        if (selected != null) ScanFolder();
    }

    private void SavePreferences_Click(object sender, RoutedEventArgs e)
    {
        if (scanCancellation != null || isGenerating || isPreparingSetup || isConfiguring) return;
        settingsRecoveryRequired = false;
        preferences.DataPath = DataPathBox.Text.Trim();
        preferences.BaseGameDataPath = BaseGameDataPathBox.Text.Trim();
        preferences.PluginListPath = PluginListPathBox.Text.Trim();
        preferences.OutputPath = OutputPathBox.Text.Trim();
        dataPath = preferences.DataPath;
        RefreshInventory();
        preferences.Framework = DefaultFrameworkBox.SelectedIndex == 1 ? "SkyPatcher" : "SPID";
        preferences.DefaultChance = ChanceFromIndex(DefaultChanceBox.SelectedIndex);
        preferences.IncludeAccessories = IncludeAccessoriesBox.IsChecked == true;
        preferences.AdditionalExclusions = ExclusionsBox.Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        SavePreferences();
        StatusText.Text = "Settings saved.";
        if (Directory.Exists(dataPath)) ScanFolder();
    }

    private void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (scanCancellation != null || isGenerating || isPreparingSetup || isConfiguring) return;
        dataPath = DataPathBox.Text.Trim();
        preferences.BaseGameDataPath = BaseGameDataPathBox.Text.Trim();
        preferences.PluginListPath = PluginListPathBox.Text.Trim();
        if (!Directory.Exists(dataPath))
        {
            MainTabs.SelectedIndex = 2;
            StatusText.Text = "Choose a valid Skyrim Data folder in Settings first.";
            return;
        }
        preferences.DataPath = dataPath;
        SavePreferences();
        ScanFolder();
    }

    private async void ScanFolder()
    {
        if (scanCancellation != null || isGenerating || isPreparingSetup || isConfiguring || isDiscoveringLocations) return;
        var cancellation = new CancellationTokenSource();
        scanCancellation = cancellation;
        var requestedRoot = dataPath;
        var snapshot = PreferencesService.Clone(preferences);
        ScanButton.IsEnabled = false;
        CancelScanButton.Visibility = Visibility.Visible;
        GenerateButton.IsEnabled = false;
        ActionProgress.Visibility = Visibility.Visible;
        ActionProgress.IsIndeterminate = true;
        ActivePathText.Text = requestedRoot;
        var progress = new Progress<ScanProgress>(state =>
        {
            if (!ReferenceEquals(scanCancellation, cancellation)) return;
            ActionProgress.IsIndeterminate = state.Total == 0;
            ActionProgress.Value = state.Percent;
            StatusText.Text = state.Total > 0 ? $"{state.Stage}… {state.Completed}/{state.Total} · {state.CurrentPlugin}" : state.Stage + "…";
        });
        try
        {
            var result = await Task.Run(() =>
            {
                TryFindDataFolder(out var baseline, snapshot);
                return new WearableScanService().Scan(requestedRoot, snapshot, baseline, progress, cancellation.Token);
            }, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            audiencePreparation = null; // All cached audience lists belong to the previous successful scan.
            scannedPlugins.Clear(); scannedPlugins.AddRange(result.Plugins);
            patchSearchRoots.Clear(); patchSearchRoots.AddRange(result.PatchSearchRoots);
            discoveredPatchFiles.Clear(); discoveredPatchFiles.AddRange(result.PatchFiles);
            if (!string.IsNullOrWhiteSpace(result.ScanRoot) && !result.ScanRoot.Equals(dataPath, StringComparison.OrdinalIgnoreCase))
            {
                DataPathBox.Text = preferences.DataPath = dataPath = result.ScanRoot;
                ActivePathText.Text = result.ScanRoot; SavePreferences();
            }
            availableKeywords.Clear(); availableKeywords.AddRange(result.Targets);
            merchantTargets.Clear(); merchantTargets.AddRange(result.Merchants);
            npcProfiles.Clear(); foreach (var npc in result.Npcs) npcProfiles[npc.FormId] = npc;
            scannedDistributionRules = result.DistributionRules;
            activePluginFilterApplied = result.HasProfileLoadOrder;
            lastScanReport = result.CreateReport(); ScanDiagnosticsBox.Text = lastScanReport;
            RefreshInventory(false); RefreshPluginList();
            StatusText.Text = $"Scan complete: {result.Plugins.Count(item => !item.IsFiltered)} equipment-only candidates · {result.Npcs.Count(npc => npc.CanReceiveWearables)} eligible NPC bases · {result.Plugins.Count(item => item.IsFiltered)} mixed/hidden candidates." +
                (result.Diagnostics.Any(line => line.StartsWith("Cannot") || line.StartsWith("Missing") || line.StartsWith("Duplicate")) ? " Review scan diagnostics in Settings." : "");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scan canceled. Your previous scan results and patch are preserved.";
        }
        catch (Exception ex)
        {
            App.WriteDiagnostic(DiagnosticEvent.ScanFailed, ex);
            lastScanReport = $"Scan failed: {ex.Message}\nFolder: {requestedRoot}";
            ScanDiagnosticsBox.Text = lastScanReport;
            StatusText.Text = $"Scan failed: {ex.Message}. Previous results are preserved.";
        }
        finally
        {
            scanCancellation = null; cancellation.Dispose();
            ScanButton.IsEnabled = true; CancelScanButton.Visibility = Visibility.Collapsed;
            ActionProgress.IsIndeterminate = false; ActionProgress.Visibility = Visibility.Collapsed;
            UpdateSelectionSummary();
        }
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e)
    {
        scanCancellation?.Cancel();
        StatusText.Text = "Canceling scan after the current plugin finishes…";
    }
    private void NavigateOutfits_Click(object sender, RoutedEventArgs e) { if (MainTabs != null) MainTabs.SelectedIndex = 0; }
    private void NavigateInventory_Click(object sender, RoutedEventArgs e) { if (MainTabs != null) MainTabs.SelectedIndex = 1; }
    private void NavigateSettings_Click(object sender, RoutedEventArgs e) { if (MainTabs != null) MainTabs.SelectedIndex = 2; }
    private void UpdateSelectionSummary()
    {
        if (SelectionCountText == null) return;
        var count = scannedPlugins.Count(IsSelectedForPatch);
        var outsideView = scannedPlugins.Count(plugin => IsSelectedForPatch(plugin) && !visiblePlugins.Contains(plugin));
        SelectionCountText.Text = $"{count} outfit{(count == 1 ? "" : "s")} checked" + (outsideView > 0 ? $" · {outsideView} outside this view" : "");
        GenerateButton.Content = $"Configure checked ({count}) →";
        GenerateButton.ToolTip = $"Open exactly {count} checked outfit plugin(s). Searching does not clear checks." +
            (outsideView > 0 ? $" {outsideView} checked plugin(s) are outside the current search/tab." : "") +
            " To open one outfit regardless of other checks, right-click its row → Configure this outfit.";
        if (ActionTargetsText is not null) ActionTargetsText.Text = $"{GetActionTargets().Count} visible action targets";
        GenerateButton.IsEnabled = scanCancellation == null && !isGenerating && !isPreparingSetup && !isConfiguring && !isDiscoveringLocations && count > 0;
    }
    private bool IsSelectedForPatch(PluginEntry plugin) => plugin.Selected && plugin.CanPatch && (editingPatchedOutfit ||
        !preferences.IgnoredPlugins.Contains(plugin.Plugin) && (!plugin.IsFiltered || preferences.IncludedPlugins.Contains(plugin.Plugin)));

    private void PluginSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: PluginEntry plugin })
        {
            if (plugin.Selected)
            {
                preferences.DisabledPlugins.Remove(plugin.Plugin);
                if (plugin.IsFiltered) preferences.IncludedPlugins.Add(plugin.Plugin);
            }
            else preferences.DisabledPlugins.Add(plugin.Plugin);
            SavePreferences();
            ActivityHistory.Add((plugin.Selected ? "Checked for setup/actions: " : "Unchecked: ") + plugin.Plugin);
        }
        UpdateSelectionSummary();
    }

    private void PluginRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Keep a multi-selection when its member is clicked; target a new row when it isn't selected.
        var current = e.OriginalSource as DependencyObject;
        while (current != null && current is not DataGridRow) current = current is System.Windows.Media.Visual || current is System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        if (current is DataGridRow row)
        {
            if (!row.IsSelected) { PluginsGrid.SelectedItems.Clear(); row.IsSelected = true; }
            contextPlugin = row.DataContext as PluginEntry;
        }
        else contextPlugin = null;
    }

    private List<PluginEntry> GetCheckedConfigurationTargets() => scannedPlugins.Where(IsSelectedForPatch).ToList();

    private List<PluginEntry> GetContextConfigurationTargets()
    {
        var plugin = contextPlugin ?? PluginsGrid.SelectedItem as PluginEntry;
        return plugin is { CanPatch: true, SourceDetected: true } ? [plugin] : [];
    }

    private async void ConfigureContext_Click(object sender, RoutedEventArgs e)
        => await ConfigureAndGenerateAsync(GetContextConfigurationTargets());
    private void ExportScanReport_Click(object sender, RoutedEventArgs e)
    {
        ShowErrorReports_Click(sender, e);
    }

    private void ShowErrorReports_Click(object sender, RoutedEventArgs e) => new SupportReportWindow { Owner = this }.ShowDialog();
    private void ShowActivity_Click(object sender, RoutedEventArgs e) => ActivityHistoryWindow.ShowFor(this);
    private void ActionScopeChanged(object sender, SelectionChangedEventArgs e) { if (PluginsGrid is not null) UpdateSelectionSummary(); }

    private List<PluginEntry> GetActionTargets()
    {
        var highlighted = PluginsGrid.SelectedItems.Cast<PluginEntry>().ToHashSet();
        return visiblePlugins.Where(plugin => ActionScopeBox?.SelectedIndex switch
        {
            1 => plugin.Selected,
            2 => highlighted.Contains(plugin),
            _ => plugin.Selected || highlighted.Contains(plugin)
        }).ToList();
    }
    private void RecordListAction(string action, IReadOnlyCollection<PluginEntry> targets)
    {
        StatusText.Text = targets.Count == 0 ? "No matching action targets. Check a row or highlight it, then try again." :
            $"{action} {targets.Count} outfit plugin(s): " + string.Join(", ", targets.Take(15).Select(plugin => plugin.Plugin)) + (targets.Count > 15 ? ", …" : "");
    }

    private void RefreshPluginList(object? sender = null, RoutedEventArgs? e = null)
    {
        RefreshPluginList();
        if (sender is CheckBox box) ActivityHistory.Add((box.IsChecked == true ? "Showing: " : "Hiding view: ") + box.Content);
    }

    private void LibraryViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, LibraryViews) && PluginsGrid is not null) RefreshPluginList();
    }
    private void RefreshPluginList()
    {
        if (SearchBox is null || PluginsGrid is null || LibraryViews is null) return;
        var query = SearchBox.Text.Trim();
        var view = LibraryViews.SelectedIndex;
        var all = scannedPlugins.Concat(recoveredPatchedSources).Where(plugin => !WearableFilterService.IsOfficialContent(plugin.Plugin)).ToList();
        bool Included(PluginEntry item) => !preferences.IgnoredPlugins.Contains(item.Plugin) && (!item.IsFiltered || preferences.IncludedPlugins.Contains(item.Plugin));
        visiblePlugins.Clear();
        foreach (var plugin in all.Where(item => view == 2 ? item.HasGeneratedPatch : view == 1 ? !Included(item) : item.SourceDetected && Included(item))
                     .Where(item => string.IsNullOrWhiteSpace(query) || item.Plugin.Contains(query, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item => item.Plugin, StringComparer.OrdinalIgnoreCase)) visiblePlugins.Add(plugin);
        EquipmentViewTab.Header = $"Armor & clothing ({all.Count(item => item.SourceDetected && Included(item))})";
        MixedViewTab.Header = $"Mixed / hidden ({all.Count(item => !Included(item))})";
        PatchedViewTab.Header = $"Already patched ({all.Count(item => item.HasGeneratedPatch)})";
        LibraryViewHint.Text = view == 2 ? "Source mods confirmed in discovered generated ESPs. The note shows each patch location; activation in a profile is not established." :
            view == 1 ? "Mixed-content or manually hidden plugins. Use Enable selected / Unhide selected to include them; official Skyrim/DLC/Creation Club files are never selectable." :
            "Armor/clothing plugins, their equipment support records and any bundled weapons. Already patched outfits remain visible here. Mixed mods you explicitly include also appear here.";
        UpdateSelectionSummary();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshPluginList();

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var plugin in visiblePlugins.Where(item => item.CanPatch)) SetMark(plugin, true);
        SavePreferences(); RecordListAction("Checked", visiblePlugins.Where(plugin => plugin.Selected).ToList());
        PluginsGrid.Items.Refresh();
        UpdateSelectionSummary();
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var plugin in visiblePlugins) SetMark(plugin, false);
        PluginsGrid.SelectedItems.Clear();
        SavePreferences(); RecordListAction("Unchecked", visiblePlugins.ToList());
        PluginsGrid.Items.Refresh();
        UpdateSelectionSummary();
    }

    private void InvertSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var plugin in visiblePlugins.Where(item => item.CanPatch)) SetMark(plugin, !plugin.Selected);
        SavePreferences(); RecordListAction("Inverted checks for", visiblePlugins.Where(plugin => plugin.CanPatch).ToList());
        PluginsGrid.Items.Refresh();
        UpdateSelectionSummary();
    }

    private void EnableSelected_Click(object sender, RoutedEventArgs e) => SetSelectedRows(true);
    private void DisableSelected_Click(object sender, RoutedEventArgs e) => SetSelectedRows(false);

    private void IncludeFiltered_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetActionTargets().Where(candidate => candidate.IsFiltered && candidate.CanPatch).ToList();
        foreach (var plugin in targets)
        {
            preferences.IncludedPlugins.Add(plugin.Plugin);
            preferences.IgnoredPlugins.Remove(plugin.Plugin);
            preferences.DisabledPlugins.Remove(plugin.Plugin);
            plugin.Selected = plugin.CanPatch;
        }
        SavePreferences();
        RefreshPluginList();
        RecordListAction("Included filtered candidates:", targets);
    }

    private void SetMark(PluginEntry plugin, bool marked)
    {
        plugin.Selected = marked;
        if (marked) { preferences.DisabledPlugins.Remove(plugin.Plugin); if (plugin.IsFiltered) preferences.IncludedPlugins.Add(plugin.Plugin); }
        else preferences.DisabledPlugins.Add(plugin.Plugin);
    }

    private void SetSelectedRows(bool enabled)
    {
        var targets = GetActionTargets().Where(plugin => !enabled || plugin.CanPatch).ToList();
        foreach (var plugin in targets)
            {
                if (enabled) { preferences.IgnoredPlugins.Remove(plugin.Plugin); if (plugin.HasGeneratedPatch) preferences.UnhiddenPlugins.Add(plugin.Plugin); }
                SetMark(plugin, enabled);
            }
        SavePreferences();
        RefreshPluginList();
        UpdateSelectionSummary();
        RecordListAction(enabled ? "Enabled for patch setup:" : "Disabled for patch setup:", targets);
    }

    private void IgnorePlugin_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetActionTargets().Where(plugin => !preferences.IgnoredPlugins.Contains(plugin.Plugin)).ToList();
        foreach (var plugin in targets)
        {
            preferences.IgnoredPlugins.Add(plugin.Plugin);
            preferences.UnhiddenPlugins.Remove(plugin.Plugin);
            plugin.Selected = false;
        }
        SavePreferences();
        RefreshPluginList();
        RecordListAction("Hidden (restore through Show my exclusions):", targets);
    }

    private void RestorePlugin_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetActionTargets().Where(plugin => preferences.IgnoredPlugins.Contains(plugin.Plugin) || plugin.IsFiltered || plugin.HasGeneratedPatch).ToList();
        foreach (var plugin in targets)
        {
            preferences.IgnoredPlugins.Remove(plugin.Plugin);
            preferences.UnhiddenPlugins.Add(plugin.Plugin);
            if (plugin.IsFiltered) preferences.IncludedPlugins.Add(plugin.Plugin);
            plugin.Selected = plugin.CanPatch && !preferences.DisabledPlugins.Contains(plugin.Plugin) && (!plugin.IsFiltered || preferences.IncludedPlugins.Contains(plugin.Plugin));
        }
        SavePreferences();
        RefreshPluginList();
        RecordListAction("Unhidden:", targets);
    }

    private async Task PrepareAudienceLists(int outfitCount)
    {
        if (audiencePreparation is not null) return;
        var keywords = availableKeywords.ToArray();
        var population = npcProfiles.Values.ToArray();
        isPreparingSetup = true;
        var loading = new StartupWindow($"Preparing {outfitCount} selected outfit plugin(s)") { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        loading.SetStage("Preparing NPC groups and presets once for this scan. Other outfits are not being opened…");
        var progress = new Progress<string>(message =>
        {
            if (!isPreparingSetup) return;
            loading.SetStage(message); StatusText.Text = message;
        });
        try
        {
            IsEnabled = false;
            ActionProgress.Visibility = Visibility.Visible; ActionProgress.IsIndeterminate = true;
            loading.Show();
            // No WPF controls are created on the worker. Later outfits reuse these scan-owned lists.
            audiencePreparation = await Task.Run(() => new AudiencePreparation(keywords, population, progress));
            StatusText.Text = "Audience lists ready · cached until the next successful scan.";
        }
        finally
        {
            isPreparingSetup = false;
            loading.Close(); IsEnabled = true;
            ActionProgress.IsIndeterminate = false; ActionProgress.Visibility = Visibility.Collapsed;
            UpdateSelectionSummary();
        }
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        PluginsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        PluginsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        await ConfigureAndGenerateAsync(GetCheckedConfigurationTargets());
    }

    private async Task ConfigureAndGenerateAsync(List<PluginEntry> selected)
    {
        if (scanCancellation != null || isGenerating || isPreparingSetup || isConfiguring || isDiscoveringLocations) return;
        if (selected.Count == 0) { StatusText.Text = "Tick outfit boxes for a batch, or right-click an available outfit → Configure this outfit."; return; }
        isConfiguring = true;
        UpdateSelectionSummary();
        ActivityHistory.Add("Configuring only: " + string.Join(", ", selected.Select(plugin => plugin.Plugin)));
        try { await ConfigureCoreAsync(selected); }
        finally { isConfiguring = false; UpdateSelectionSummary(); }
    }

    private async Task ConfigureCoreAsync(List<PluginEntry> selected)
    {
        var outputDirectory = Directory.Exists(preferences.OutputPath) ? preferences.OutputPath : "";
        var requestedOutfits = requestedRepatchOutfits;
        var preferSavedSetup = reuseSavedRepatch;
        var framework = preferences.Framework;
        PatchOptionsWindow dialog;
        var skippedOutfits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        PatchOptionsWindow CreateSetup() => new PatchOptionsWindow(selected, availableKeywords, preferences.DefaultChance, preferences.Framework, preferences.DistributionSettings, (plugin, settings) =>
        {
            preferences.DistributionSettings[plugin] = settings;
            SavePreferences();
        }, preferences.DistributionPresets, (name, settings) =>
        {
            preferences.DistributionPresets[name] = settings;
            SavePreferences();
        }, key =>
        {
            preferences.DistributionSettings.Remove(key);
            SavePreferences();
        }, name =>
        {
            preferences.DistributionPresets.Remove(name);
            SavePreferences();
        }, npcProfiles: npcProfiles.Values, skippedOutfits: skippedOutfits, merchantTargets: merchantTargets, requestedOutfits: requestedOutfits,
        saveBatch: saved => { foreach (var pair in saved) preferences.DistributionSettings[pair.Key] = pair.Value; SavePreferences(); }, preparation: audiencePreparation) { Owner = this };

        try
        {
          await PrepareAudienceLists(selected.Count);
          while (true)
          {
            dialog = CreateSetup();
            var usingSavedSetup = preferSavedSetup && dialog.CanReviewSavedSetup();
            if (usingSavedSetup) dialog.Close();
            if (!usingSavedSetup && dialog.ShowDialog() != true)
            {
                if (dialog.Options.Count == 0) StatusText.Text = "All outfits skipped. Your existing patch has not been changed.";
                else StatusText.Text = $"Saved setup for {selected.Count} outfit plugin(s). Reopen Configure and choose Review & generate to build the patch.";
                return;
            }
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                var outputPicker = new OpenFolderDialog
                {
                    Title = "Choose the output folder for your generated mod",
                    InitialDirectory = Directory.Exists(preferences.OutputPath) ? preferences.OutputPath : dataPath
                };
                if (outputPicker.ShowDialog(this) != true) return;
                outputDirectory = outputPicker.FolderName;
            }
            var targeting = audiencePreparation?.Targeting ?? new NpcTargetingService(availableKeywords, npcProfiles.Values);
            foreach (var config in dialog.Options.Where(config => config.DistributeToNpcs))
                foreach (var rule in config.TargetRules) targeting.Resolve(rule, config.Gender);
            var review = new PatchReviewWindow(dialog.Options, CountMatchingNpcs, (rule, owner) => CountOverlappingRules(rule, owner, dialog.Options), CountInstalledRuleOverlaps, framework, outputDirectory) { Owner = this };
            if (review.ShowDialog() == true) break;
            preferSavedSetup = false;
            // WPF windows cannot be shown again after closing. Saved setup is restored in a fresh instance.
          }
        }
        catch (Exception failure)
        {
            App.WriteDiagnostic(DiagnosticEvent.PatchSetupFailed, failure);
            MessageBox.Show(this, "Patch setup could not be completed. Your existing patch was not changed.\n\n" + failure.Message +
                "\n\nUse Error reports → Save report to share technical details (APP-1418).", "Patch setup failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(dataPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(outputDirectory, "Skyrim.esm")) &&
            MessageBox.Show(this, "This is Skyrim's Data folder. A separate Vortex staging mod folder or MO2 mod folder is safer: direct writes may be tracked as external changes by Vortex. Continue writing here?", "Output directly in Skyrim Data", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            isGenerating = true;
            ScanButton.IsEnabled = false; GenerateButton.IsEnabled = false;
            preferences.OutputPath = outputDirectory;
            OutputPathBox.Text = outputDirectory;
            SavePreferences();
            ActionProgress.Visibility = Visibility.Visible;
            ActionProgress.IsIndeterminate = true;
            StatusText.Text = $"Generating {framework} outfit records and NPC distributions…";
            var generationProgress = new Progress<string>(message => StatusText.Text = message);
            var result = await Task.Run(() => PatchGenerator.Generate(dialog.Options, outputDirectory, framework, dialog.SkippedOptions, generationProgress));
            preferences.BackupOutputPath = outputDirectory;
            SavePreferences();
            ActionProgress.IsIndeterminate = false;
            ActionProgress.Visibility = Visibility.Collapsed;
            StatusText.Text = $"Updated {result.UpdatedOutfitCount} outfit(s), kept {result.PreservedOutfitCount} unchanged · {result.DistributionCount} NPC rule(s) · {result.MerchantDistributionCount} shop-stock rule(s) in the complete patch.";
            RefreshInventory();
            MainTabs.SelectedIndex = 1;
            var runtimeNote = !dialog.Options.Any(config => config.DistributeToNpcs) && result.DistributionCount == 0
                ? "Merchants-only output: install/enable SkyPatcher with container patching enabled. No NPC outfits are changed, and SPID is not required for these shop rules."
                : result.Framework == "SPID"
                ? "Install/enable SPID and enable WearableDistributor.esp together with the _DISTR.ini. SPID honors the configured chance."
                : "Install/enable SkyPatcher and enable WearableDistributor.esp with the generated npc INI. SkyPatcher outfit rules apply to every matching NPC and cannot use a random chance.";
            if (dialog.Options.Any(config => config.MerchantCategories.Count > 0)) runtimeNote += "\nShop stock also requires the generated SkyPatcher container INI and iEnableContainerPatching=1.";
            var masters = result.Masters.Count > 0 ? $"\n\nRequired source plugins: {string.Join(", ", result.Masters)}" : "";
            var distributionDetails = result.DistributionCount == 0 ? "NPC assignment: none (shop-stock output)." : "NPC rules: " + result.DistributionPath;
            if (result.MerchantDistributionPath is not null) distributionDetails += "\nShop-stock rules: " + result.MerchantDistributionPath;
            MessageBox.Show($"Patch generated and validated.\n\nPlugin: {result.PluginPath}\n{distributionDetails}{masters}\n\n{runtimeNote}\n\nFor Mod Organizer 2, install the selected output folder as a mod and enable it in the profile.", "Patch ready", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.WriteDiagnostic(DiagnosticEvent.PatchGenerationFailed, ex);
            ActionProgress.IsIndeterminate = false;
            ActionProgress.Visibility = Visibility.Collapsed;
            StatusText.Text = "Patch generation failed; no validated patch was produced.";
            MessageBox.Show($"Patch generation failed: {ex.Message}\n\nError APP-1400 was recorded. Use Error reports → Save report to send technical details without personal paths or mod lists.", "Patch generation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { isGenerating = false; ScanButton.IsEnabled = true; UpdateSelectionSummary(); }
    }

    private int CountMatchingNpcs(DistributionTargetRule rule)
    {
        if (rule.NpcResolutionComplete) return rule.ResolvedNpcFormIds.Count;
        return npcProfiles.Values.Count(profile => NpcTargetingService.Matches(rule, profile));
    }

    private int CountOverlappingRules(DistributionTargetRule rule, PluginPatchOptions owner, IEnumerable<PluginPatchOptions> current)
    {
        var targeting = audiencePreparation?.Targeting ?? new NpcTargetingService(availableKeywords, npcProfiles.Values);
        var matches = rule.NpcResolutionComplete ? rule.ResolvedNpcFormIds.ToHashSet(StringComparer.OrdinalIgnoreCase) : targeting.GetMatchingProfiles(rule, owner.Gender).Select(profile => profile.FormId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (matches.Count == 0) return 0;
        return current.Where(config => config.DistributeToNpcs && !ReferenceEquals(config, owner)).Sum(config => config.TargetRules.Count(other =>
            matches.Overlaps(other.NpcResolutionComplete ? other.ResolvedNpcFormIds : targeting.GetMatchingProfiles(other, config.Gender).Select(profile => profile.FormId))));
    }

    private int CountInstalledRuleOverlaps(DistributionTargetRule rule)
    {
        var categories = rule.Filters.GroupBy(filter => filter.Category, StringComparer.OrdinalIgnoreCase).ToList();
        if (categories.Count == 0) return 0;
        return scannedDistributionRules.Count(entry =>
        {
            if (Path.GetFileName(entry.Path).Equals(PatchGenerator.OutputIniName, StringComparison.OrdinalIgnoreCase) ||
                entry.Path.Contains(Path.Combine("WearableDistributor", "WearableDistributor.ini"), StringComparison.OrdinalIgnoreCase)) return false;
            return categories.All(group => group.Any(filter => entry.Line.Contains(filter.EditorId, StringComparison.OrdinalIgnoreCase) || entry.Line.Contains(filter.FormId, StringComparison.OrdinalIgnoreCase)));
        });
    }

    private void RefreshInventory_Click(object sender, RoutedEventArgs e) => RefreshInventory();

    private async void RefreshInventory(bool rediscover = true)
    {
        var refresh = ++inventoryRefreshVersion;
        ScanSummaryText.Text = "Reading generated patch files across detected mod locations…";
        var roots = (rediscover ? patchSearchRoots.Append(dataPath) : discoveredPatchFiles).Concat(preferences.PatchSearchFiles).Append(preferences.OutputPath).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        var snapshot = PreferencesService.Clone(preferences);
        var scannedSnapshot = scannedPlugins.ToList();
        var ownProgress = scanCancellation is null && !isGenerating && !isPreparingSetup && !isDiscoveringLocations;
        if (ownProgress) { ActionProgress.Visibility = Visibility.Visible; ActionProgress.IsIndeterminate = true; }
        try
        {
            var detected = await Task.Run(() => DetectedPatchReader.Read(PatchFileDiscovery.Find(rediscover ? roots.Concat(ScanLocationDiscovery.FindVortexStagingRoots(snapshot)) : roots), scannedSnapshot));
            if (refresh != inventoryRefreshVersion || Dispatcher.HasShutdownStarted) return;
            inventoryRows.Clear(); recoveredPatchedSources.Clear();
            var bySource = detected.GroupBy(item => item.Entry.SourcePlugin, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            foreach (var plugin in scannedPlugins)
            {
                var wasPatched = plugin.HasGeneratedPatch;
                var entries = bySource.GetValueOrDefault(plugin.Plugin) ?? [];
                plugin.HasGeneratedPatch = entries.Any(item => item.PresentInPlugin);
                plugin.PatchLocations = string.Join("; ", entries.Where(item => item.PresentInPlugin).Select(item => item.PatchPlugin).Distinct(StringComparer.OrdinalIgnoreCase));
                plugin.PatchStatus = entries.Count == 0 ? "Not patched" : entries.All(item => item.PresentInPlugin && item.Health == "Files found") ? "Patched" : "Needs review";
                if (plugin.HasGeneratedPatch && !wasPatched && !preferences.UnhiddenPlugins.Contains(plugin.Plugin)) plugin.Selected = false;
            }
            foreach (var group in bySource.Where(pair => !scannedPlugins.Any(plugin => plugin.Plugin.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))))
                if (group.Value.Any(item => item.PresentInPlugin)) recoveredPatchedSources.Add(new PluginEntry { Plugin = group.Key, SourceDetected = false, Selected = false, HasGeneratedPatch = true,
                    PatchStatus = "Source not scanned", ValidationIssue = "Patch found, but this source plugin is not visible in the current source scan.",
                    PatchLocations = string.Join("; ", group.Value.Select(item => item.PatchPlugin).Distinct(StringComparer.OrdinalIgnoreCase)) });
            foreach (var found in detected.OrderBy(item => item.Entry.SourcePlugin, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Directory, StringComparer.OrdinalIgnoreCase))
            {
                var entry = found.Entry;
                inventoryRows.Add(new PatchInventoryRow {
                    OutputDirectory = found.Directory, PatchPlugin = found.PatchPlugin, CanManage = found.CanManage,
                    Health = found.Health, SourcePlugin = string.IsNullOrWhiteSpace(entry.OutfitName) ? entry.SourcePlugin : entry.SourcePlugin + " · " + entry.OutfitName,
                    IncludedPieces = entry.IncludedPieces.Count.ToString(),
                    Audiences = !entry.DistributeToNpcs ? "Merchants only" : (string.IsNullOrEmpty(entry.WardrobeName) ? "" : "Wardrobe: " + entry.WardrobeName + " · ") + (entry.MerchantStock.Count > 0 ? "Shops + " : "") + string.Join(", ", entry.Audiences.Select(audience => audience.Split(':', 2)[0]).Distinct(StringComparer.OrdinalIgnoreCase)),
                    Chance = found.Health == "Recovered from ESP" ? "Unknown" : !entry.DistributeToNpcs ? "N/A (shops)" : entry.Framework == "SPID" ? $"{entry.Chance}%" : "100% (SkyPatcher)",
                    Gender = found.Health == "Recovered from ESP" ? "Unknown" : entry.DistributeToNpcs ? entry.Gender : "N/A (shops)", LastGenerated = entry.GeneratedAtUtc.ToLocalTime().ToString("g"), Entry = entry,
                    Details = "Patch file: " + found.PatchPlugin + "\n" + (found.CanManage ? "Managed release output: editable in this app." : "Read-only detected output: experimental or recovered patches are not edited by this release.") +
                        "\n\nStatus: " + found.Health + "\n" + found.Explanation + "\n\nSource plugin: " + entry.SourcePlugin + "\nGenerated outfit: " + entry.OutfitEditorId +
                        (string.IsNullOrEmpty(entry.WardrobeName) ? "" : $"\nRandom wardrobe: {entry.WardrobeName}\nRelative weight: {entry.WardrobeWeight}\nShared coverage: {entry.Chance}%\nOne complete weighted outfit; no daily rotation.") +
                        "\nFramework: " + entry.Framework + "\nDistribution file: " + entry.DistributionPath + "\n\nPart preset: " + entry.PartPreset + "\n" + string.Join("\n", entry.PartChoices.Select(part => part.FormId + " · " + part.Mode + (part.Mode == "Optional" ? " " + part.Chance + "%" : ""))) + "\n\nIncluded pieces:\n• " + string.Join("\n• ", entry.IncludedPieces) +
                        "\n\nAudiences:\n• " + string.Join("\n• ", entry.Audiences) + "\n\n" + MerchantStockService.Describe(entry)
                });
            }
            PluginsGrid.Items.Refresh();
            var patchedCount = bySource.Count(pair => pair.Value.Any(item => item.PresentInPlugin) && !WearableFilterService.IsOfficialContent(pair.Key));
            ScanSummaryText.Text = $"{scannedPlugins.Count} source plugin candidates · {patchedCount} source mods found in generated ESPs · {detected.Select(item => item.PatchPlugin).Distinct(StringComparer.OrdinalIgnoreCase).Count()} detected patch files";
            InventoryDetailsBox.Text = detected.Count == 0 ? "No generated patch contents found in the scanned mod locations. Use Find patch files to add an ESP, _DISTR.ini or .patch.json from another profile/mod folder. The output destination does not control this list." :
                "Select an entry to see its actual generated patch file and source equipment. The overview combines files from discovered mod locations and manually added patch files. Multiple profiles can contain the same source outfit; activation is not established.";
            RefreshPluginList();
        }
        catch (Exception failure)
        {
            App.WriteDiagnostic(DiagnosticEvent.PatchLibraryReadFailed, failure);
            if (refresh == inventoryRefreshVersion) InventoryDetailsBox.Text = "Patch discovery could not finish. Existing files were preserved. Use Error reports for technical details or Find patch files to inspect a specific output.";
        }
        finally
        {
            if (ownProgress && refresh == inventoryRefreshVersion && scanCancellation is null && !isGenerating && !isPreparingSetup && !isDiscoveringLocations)
            { ActionProgress.IsIndeterminate = false; ActionProgress.Visibility = Visibility.Collapsed; }
        }
    }

    private void FindPatchFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Select generated patch files from any profile/mod folder", Multiselect = true,
            Filter = "Generated patch files (*.esp;*_DISTR.ini;*.patch.json)|*.esp;*_DISTR.ini;*.patch.json" };
        if (picker.ShowDialog(this) != true) return;
        foreach (var file in picker.FileNames.Where(DetectedPatchReader.IsGeneratedFile)) preferences.PatchSearchFiles.Add(file);
        SavePreferences(); RefreshInventory();
    }

    private bool UseSelectedManagedLibrary()
    {
        if (PatchInventoryGrid.SelectedItem is not PatchInventoryRow row) return true;
        if (!row.CanManage)
        {
            MessageBox.Show(this, "This experimental or recovered patch can be inspected, but cannot be edited safely by this release. Choose a managed release patch or generate into a new output folder.", "Read-only patch", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (!preferences.OutputPath.Equals(row.OutputDirectory, StringComparison.OrdinalIgnoreCase))
        { OutputPathBox.Text = preferences.OutputPath = row.OutputDirectory; SavePreferences(); }
        return true;
    }

    private void InventorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PatchInventoryGrid.SelectedItem is PatchInventoryRow row) InventoryDetailsBox.Text = row.Details;
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        var path = (PatchInventoryGrid.SelectedItem as PatchInventoryRow)?.OutputDirectory ?? preferences.OutputPath;
        if (!Directory.Exists(path)) { MessageBox.Show(this, "Choose an output folder by generating a patch first.", "Output folder not set", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { App.WriteDiagnostic(DiagnosticEvent.OutputFolderOpenFailed, ex); MessageBox.Show(this, ex.Message, "Could not open output folder", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void EditInventoryEntry_Click(object sender, RoutedEventArgs e)
    {
        if (!UseSelectedManagedLibrary()) return;
        if (PatchInventoryGrid.SelectedItem is not PatchInventoryRow row) { MessageBox.Show(this, "Select a generated outfit first.", "No outfit selected", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var plugin = scannedPlugins.FirstOrDefault(item => item.Plugin.Equals(row.Entry.SourcePlugin, StringComparison.OrdinalIgnoreCase));
        if (plugin is null)
        {
            MessageBox.Show(this, "That source plugin is not in the current scan. Check the scan folder and optional active profile plugin list, then scan again.", "Source plugin not found", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        foreach (var item in scannedPlugins) item.Selected = ReferenceEquals(item, plugin);
        PluginsGrid.Items.Refresh();
        MainTabs.SelectedIndex = 0;
        requestedRepatchOutfits = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        { [plugin.Plugin] = new[] { string.IsNullOrWhiteSpace(row.Entry.OutfitName) ? Path.GetFileNameWithoutExtension(plugin.Plugin) : row.Entry.OutfitName } };
        editingPatchedOutfit = true;
        try { await ConfigureAndGenerateAsync(GetCheckedConfigurationTargets()); }
        finally { editingPatchedOutfit = false; requestedRepatchOutfits = null; }
    }

    private async void RepatchAll_Click(object sender, RoutedEventArgs e)
    {
        if (!UseSelectedManagedLibrary()) return;
        if (scanCancellation != null || isGenerating || isPreparingSetup || isConfiguring) return;
        if (inventoryRows.Count == 0) { StatusText.Text = "Choose an existing patch folder in Settings first."; return; }
        var entries = inventoryRows.Where(row => row.CanManage && row.OutputDirectory.Equals(preferences.OutputPath, StringComparison.OrdinalIgnoreCase)).Select(row => row.Entry).ToList();
        if (entries.Count == 0) { StatusText.Text = "Select an editable managed patch entry first."; return; }
        var sources = entries.Select(entry => entry.SourcePlugin).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = sources.Where(source => !scannedPlugins.Any(plugin => plugin.CanPatch && plugin.Plugin.Equals(source, StringComparison.OrdinalIgnoreCase))).ToList();
        if (missing.Count > 0)
        {
            MessageBox.Show(this, "Scan the correct profile and restore these source plugins before repatching everything:\n\n" + string.Join("\n", missing), "Source plugins unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        foreach (var plugin in scannedPlugins) plugin.Selected = sources.Contains(plugin.Plugin);
        requestedRepatchOutfits = entries.GroupBy(entry => entry.SourcePlugin, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(entry => string.IsNullOrWhiteSpace(entry.OutfitName) ? Path.GetFileNameWithoutExtension(entry.SourcePlugin) : entry.OutfitName).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        editingPatchedOutfit = true;
        reuseSavedRepatch = true;
        try { await ConfigureAndGenerateAsync(GetCheckedConfigurationTargets()); }
        finally { editingPatchedOutfit = false; requestedRepatchOutfits = null; reuseSavedRepatch = false; }
    }

    private void RemoveInventoryEntry_Click(object sender, RoutedEventArgs e)
    {
        if (!UseSelectedManagedLibrary()) return;
        if (PatchInventoryGrid.SelectedItem is not PatchInventoryRow row) { MessageBox.Show(this, "Select a generated outfit first.", "No outfit selected", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (string.IsNullOrWhiteSpace(row.Entry.OutfitEditorId)) { MessageBox.Show(this, "This is a legacy patch without an inventory record. It can be inspected, but cannot be safely removed by source plugin.", "Legacy patch", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (MessageBox.Show(this, $"Remove {row.SourcePlugin} from the generated patch? A backup will be saved first.", "Remove generated outfit", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { PatchInventoryService.Remove(preferences.OutputPath, row.Entry); RefreshInventory(); StatusText.Text = $"Removed {row.SourcePlugin} from the generated patch."; }
        catch (Exception ex) { App.WriteDiagnostic(DiagnosticEvent.PatchRemoveFailed, ex); MessageBox.Show(this, ex.Message, "Could not remove outfit", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!UseSelectedManagedLibrary()) return;
        if (string.IsNullOrWhiteSpace(preferences.OutputPath)) { MessageBox.Show(this, "No output folder has been selected yet.", "Backup unavailable", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (MessageBox.Show(this, "Restore the most recent saved patch files for this output folder? Current files will be replaced.", "Restore previous patch", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { PatchInventoryService.RestoreLastBackup(preferences.OutputPath); RefreshInventory(); StatusText.Text = "Previous generated patch restored from backup."; }
        catch (Exception ex) { App.WriteDiagnostic(DiagnosticEvent.PatchRestoreFailed, ex); MessageBox.Show(this, ex.Message, "Could not restore backup", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (!UseSelectedManagedLibrary()) return;
        if (string.IsNullOrWhiteSpace(preferences.OutputPath)) { MessageBox.Show(this, "Generate a patch first.", "Nothing to export", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var dialog = new SaveFileDialog { Filter = "JSON report (*.json)|*.json", FileName = "SkyrimSPIDPatchGenerator-patch-report.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { PatchInventoryService.ExportReport(preferences.OutputPath, dialog.FileName); StatusText.Text = "Patch report exported."; }
        catch (Exception ex) { App.WriteDiagnostic(DiagnosticEvent.PatchReportExportFailed, ex); MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ExportModArchive_Click(object sender, RoutedEventArgs e)
    {
        if (!UseSelectedManagedLibrary()) return;
        if (scanCancellation != null || isGenerating || isPreparingSetup || isConfiguring) return;
        if (!Directory.Exists(preferences.OutputPath)) { MessageBox.Show(this, "Choose a generated mod folder in Settings first.", "Output folder missing", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var dialog = new SaveFileDialog { Filter = "Installable mod (*.zip)|*.zip", FileName = "SkyrimSPIDPatchGenerator-Generated-Mod.zip" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            isGenerating = true; ScanButton.IsEnabled = false; GenerateButton.IsEnabled = false;
            ActionProgress.Visibility = Visibility.Visible; ActionProgress.IsIndeterminate = true;
            StatusText.Text = "Validating and packaging your generated mod…";
            var folder = preferences.OutputPath;
            await Task.Run(() => PatchPackageService.Export(folder, dialog.FileName));
            StatusText.Text = "Installable mod ZIP ready. Install it through Vortex or MO2, enable the mod and its plugin, then deploy if using Vortex.";
        }
        catch (Exception ex) { App.WriteDiagnostic(DiagnosticEvent.PatchPackageFailed, ex); MessageBox.Show(this, ex.Message, "Could not package mod", MessageBoxButton.OK, MessageBoxImage.Error); StatusText.Text = "Mod packaging failed; your generated patch is unchanged."; }
        finally { isGenerating = false; ScanButton.IsEnabled = true; ActionProgress.IsIndeterminate = false; ActionProgress.Visibility = Visibility.Collapsed; UpdateSelectionSummary(); }
    }

    private void ExportPreferences_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Skyrim SPID Patch Generator settings (*.json)|*.json", FileName = "SkyrimSPIDPatchGenerator-settings.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { PreferencesService.Save(dialog.FileName, PreferencesService.Clone(preferences)); StatusText.Text = "Preferences and reusable presets exported."; }
        catch (Exception ex) { App.WriteDiagnostic(DiagnosticEvent.PreferencesExportFailed, ex); MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ImportPreferences_Click(object sender, RoutedEventArgs e)
    {
        if (scanCancellation != null || isGenerating || isPreparingSetup || isConfiguring) return;
        var dialog = new OpenFileDialog { Filter = "Skyrim SPID Patch Generator settings (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            preferences = PreferencesService.Load(dialog.FileName);
            settingsRecoveryRequired = false;
            DataPathBox.Text = preferences.DataPath; dataPath = preferences.DataPath;
            BaseGameDataPathBox.Text = preferences.BaseGameDataPath;
            OutputPathBox.Text = preferences.OutputPath;
            PluginListPathBox.Text = preferences.PluginListPath;
            DefaultFrameworkBox.SelectedIndex = preferences.Framework == "SkyPatcher" ? 1 : 0;
            DefaultChanceBox.SelectedIndex = ChanceIndex(preferences.DefaultChance);
            IncludeAccessoriesBox.IsChecked = preferences.IncludeAccessories;
            ExclusionsBox.Text = string.Join(Environment.NewLine, preferences.AdditionalExclusions ?? []);
            SavePreferences(); RefreshInventory();
            if (Directory.Exists(dataPath)) ScanFolder();
            StatusText.Text = "Preferences and reusable presets imported.";
        }
        catch (Exception ex) { App.WriteDiagnostic(DiagnosticEvent.PreferencesImportFailed, ex); MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private static int ChanceIndex(string value) => value switch { "100" => 0, "80" => 1, "50" => 2, "25" => 3, "10" => 4, "5" => 5, _ => 3 };
    private static string ChanceFromIndex(int index) => index switch { 0 => "100", 1 => "80", 2 => "50", 4 => "10", 5 => "5", _ => "25" };
}

