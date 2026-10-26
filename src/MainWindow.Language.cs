using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace WearableDistributor;

public partial class MainWindow
{
    private bool suppressLanguageSelection, changingLanguage, languageWindowClosing;
    private string? pendingLanguage;
    private int activeInventoryReads, languagePatchedSourceCount, languagePatchFileCount;

    private void LanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressLanguageSelection || !IsLoaded || LanguageBox.SelectedValue is not string code) return;
        QueueLanguageChange(code);
    }

    private async void QueueLanguageChange(string code)
    {
        pendingLanguage = Loc.NormalizePreference(code);
        if (changingLanguage || pendingLanguage == Loc.Preference) return;
        changingLanguage = true;
        var oldCode = Loc.Preference;
        var oldSavedCode = preferences.Language;
        var persisted = false;
        var committed = false;
        MainWindow? replacement = null;
        StartupWindow? loading = null;
        try
        {
            loading = new StartupWindow("Changing interface language…") { Owner = this, ShowActivated = false };
            loading.SetStage("Updating the interface. Your current choices will be kept.");
            loading.Show();
            // Allow the existing animated icon to paint before any UI reconstruction.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            // Never recreate a window that still owns an active scan, patch write or inventory callback.
            // While waiting, the owner stays usable, including Cancel scan; rapid selections keep the last choice.
            while (scanCancellation is not null || isGenerating || isPreparingSetup || isConfiguring || isDiscoveringLocations || activeInventoryReads > 0)
            {
                if (languageWindowClosing || !IsLoaded) return;
                loading.SetStage("Waiting for the current action to finish before changing language…");
                await Task.Delay(75);
            }
            if (languageWindowClosing || !IsLoaded) return;
            code = pendingLanguage ?? oldCode;
            if (code == oldCode) return;
            IsEnabled = false;
            loading.SetStage("Updating the interface. Your current choices will be kept.");

            // Persist only the language in the preferences model. Unsaved folder/default text boxes
            // are copied below, not silently applied or saved as a side effect of this dropdown.
            var snapshot = PreferencesService.Clone(preferences);
            snapshot.Language = code;
            if (!settingsRecoveryRequired)
            {
                try
                {
                    await Task.Run(() => PreferencesService.Save(preferencesFile, snapshot));
                    persisted = true;
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                { App.WriteDiagnostic(DiagnosticEvent.PreferencesSaveFailed, failure); }
            }
            if (languageWindowClosing || !IsLoaded) return;
            preferences.Language = code;
            Loc.Initialize(code);
            replacement = new MainWindow(this);
            replacement.StatusText.Text = Loc.T(persisted
                ? "Language changed and saved automatically."
                : "Language changed for this session, but could not be saved. Your original settings file was kept.");
            var app = Application.Current;
            app.MainWindow = replacement; // Closing the old main window must not shut down the application.
            replacement.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);

            var reopenActivity = OwnedWindows.OfType<ActivityHistoryWindow>().Any();
            if (loading.IsVisible) loading.Close();
            committed = true;
            Close();
            replacement.Activate();
            ActivityHistory.Add(Loc.T("Interface language changed."));
            if (reopenActivity) ActivityHistoryWindow.ShowFor(replacement);
        }
        catch (Exception failure)
        {
            App.WriteDiagnostic(DiagnosticEvent.UiBindingFailed, failure);
            if (!committed && !languageWindowClosing)
            {
                Application.Current.MainWindow = this;
                if (replacement is not null) replacement.Close();
                preferences.Language = oldSavedCode;
                Loc.Initialize(oldCode);
                if (persisted)
                {
                    try { await Task.Run(() => PreferencesService.Save(preferencesFile, PreferencesService.Clone(preferences))); }
                    catch (Exception saveFailure) when (saveFailure is IOException or UnauthorizedAccessException)
                    { App.WriteDiagnostic(DiagnosticEvent.PreferencesSaveFailed, saveFailure); }
                }
                suppressLanguageSelection = true;
                LanguageBox.SelectedValue = oldCode;
                suppressLanguageSelection = false;
                StatusText.Text = Loc.T("The language change could not finish. Your current workspace has been kept. Try again or open Error reports.");
            }
        }
        finally
        {
            if (loading?.IsVisible == true) loading.Close();
            if (!languageWindowClosing) IsEnabled = true;
            changingLanguage = false;
        }
    }

    private void CopyLanguageSession(MainWindow previous)
    {
        preferences = previous.preferences;
        settingsRecoveryRequired = previous.settingsRecoveryRequired;
        dataPath = previous.dataPath;
        activePluginFilterApplied = previous.activePluginFilterApplied;
        scannedPlugins.AddRange(previous.scannedPlugins);
        availableKeywords.AddRange(previous.availableKeywords);
        merchantTargets.AddRange(previous.merchantTargets);
        foreach (var entry in previous.npcProfiles) npcProfiles[entry.Key] = entry.Value;
        patchSearchRoots.AddRange(previous.patchSearchRoots);
        discoveredPatchFiles.AddRange(previous.discoveredPatchFiles);
        recoveredPatchedSources.AddRange(previous.recoveredPatchedSources);
        foreach (var row in previous.inventoryRows) inventoryRows.Add(row);
        scannedDistributionRules = previous.scannedDistributionRules;
        audiencePreparation = previous.audiencePreparation;
        lastScanReport = previous.lastScanReport;
        editingPatchedOutfit = previous.editingPatchedOutfit;
        requestedRepatchOutfits = previous.requestedRepatchOutfits;
        reuseSavedRepatch = previous.reuseSavedRepatch;
        languagePatchedSourceCount = previous.languagePatchedSourceCount;
        languagePatchFileCount = previous.languagePatchFileCount;
    }

    private void RestoreLanguageWorkspace(MainWindow previous)
    {
        dataPath = previous.dataPath;
        DataPathBox.Text = previous.DataPathBox.Text;
        BaseGameDataPathBox.Text = previous.BaseGameDataPathBox.Text;
        OutputPathBox.Text = previous.OutputPathBox.Text;
        PluginListPathBox.Text = previous.PluginListPathBox.Text;
        DefaultFrameworkBox.SelectedIndex = previous.DefaultFrameworkBox.SelectedIndex;
        DefaultChanceBox.SelectedIndex = previous.DefaultChanceBox.SelectedIndex;
        IncludeAccessoriesBox.IsChecked = previous.IncludeAccessoriesBox.IsChecked;
        ExclusionsBox.Text = previous.ExclusionsBox.Text;
        ScanDiagnosticsBox.Text = lastScanReport == "No scan report yet." ? Loc.T("No scan report yet.") : previous.ScanDiagnosticsBox.Text;
        ActivePathText.Text = string.IsNullOrWhiteSpace(dataPath) ? Loc.T("Choose your Skyrim Data or mod staging folder in Settings.") : previous.ActivePathText.Text;
        SearchBox.Text = previous.SearchBox.Text;
        LibraryViews.SelectedIndex = previous.LibraryViews.SelectedIndex;
        ActionScopeBox.SelectedIndex = previous.ActionScopeBox.SelectedIndex;
        MainTabs.SelectedIndex = previous.MainTabs.SelectedIndex;
        RefreshPluginList();
        foreach (var item in previous.PluginsGrid.SelectedItems.Cast<PluginEntry>())
            if (visiblePlugins.Contains(item)) PluginsGrid.SelectedItems.Add(item);
        PatchInventoryGrid.SelectedItem = previous.PatchInventoryGrid.SelectedItem;
        if (PatchInventoryGrid.SelectedItem is null) InventoryDetailsBox.Text = Loc.T("Select an outfit above to see its pieces, audience, required plugins and generated files.");
        CopyGridLayout(previous.PluginsGrid, PluginsGrid);
        CopyGridLayout(previous.PatchInventoryGrid, PatchInventoryGrid);
        ScanSummaryText.Text = previous.ScanSummaryText.Text;
        RenderLanguageScanSummary();
        var bounds = previous.WindowState == WindowState.Normal ? new Rect(previous.Left, previous.Top, previous.ActualWidth, previous.ActualHeight) : previous.RestoreBounds;
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (!bounds.IsEmpty) { Left = bounds.Left; Top = bounds.Top; Width = Math.Max(MinWidth, bounds.Width); Height = Math.Max(MinHeight, bounds.Height); }
        var oldSettingsScroll = (previous.MainTabs.Items[2] as TabItem)?.Content as ScrollViewer;
        var settingsOffset = oldSettingsScroll?.VerticalOffset ?? 0;
        var previousState = previous.WindowState;
        Loaded += (_, _) =>
        {
            // Capture values only: retaining the old Window here would retain every previous language UI.
            WindowState = previousState;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                ((MainTabs.Items[2] as TabItem)?.Content as ScrollViewer)?.ScrollToVerticalOffset(settingsOffset);
            }));
        };
    }

    private void RenderLanguageScanSummary()
    {
        if (scannedPlugins.Count > 0 || inventoryRows.Count > 0)
            ScanSummaryText.Text = Loc.F($"{scannedPlugins.Count} source plugin candidates · {languagePatchedSourceCount} source mods found in generated ESPs · {languagePatchFileCount} detected patch files");
    }

    private static void CopyGridLayout(DataGrid source, DataGrid target)
    {
        for (var i = 0; i < Math.Min(source.Columns.Count, target.Columns.Count); i++)
        { target.Columns[i].Width = source.Columns[i].Width; target.Columns[i].SortDirection = source.Columns[i].SortDirection; }
        var sourceView = CollectionViewSource.GetDefaultView(source.ItemsSource);
        var targetView = CollectionViewSource.GetDefaultView(target.ItemsSource);
        using (targetView.DeferRefresh())
            foreach (var sort in sourceView.SortDescriptions) targetView.SortDescriptions.Add(sort);
        // Keep the same place in long outfit/patch libraries after the window is rebuilt.
        var scroll = FindGridScroll(source);
        if (scroll is null) return;
        var horizontal = scroll.HorizontalOffset;
        var vertical = scroll.VerticalOffset;
        target.Loaded += (_, _) => target.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var updated = FindGridScroll(target);
            updated?.ScrollToHorizontalOffset(horizontal);
            updated?.ScrollToVerticalOffset(vertical);
        }));
    }

    private static ScrollViewer? FindGridScroll(DependencyObject element)
    {
        if (element is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (FindGridScroll(VisualTreeHelper.GetChild(element, i)) is { } found) return found;
        return null;
    }
}
