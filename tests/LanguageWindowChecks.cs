using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using WearableDistributor;

/// <summary>Opt-in desktop tests of our own WPF windows; never load a real modlist or user settings.</summary>
public static class LanguageWindowChecks
{
    public static void Run(string parent, bool visualPreview = false)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SkyrimSPIDPatchGenerator;component/UiTheme.xaml") });
            app.DispatcherUnhandledException += (_, e) => { failure = e.Exception; e.Handled = true; app.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); };
            app.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await RunWindows(parent, app, visualPreview); }
                catch (Exception error) { failure = error; }
                finally
                {
                    foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close();
                    app.Shutdown();
                    if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(3))) throw new TimeoutException("Language window checks exceeded three minutes.");
        if (failure is not null) throw new InvalidOperationException("Language window regression failed", failure);
    }

    private static async Task RunWindows(string parent, Application app, bool visualPreview)
    {
        var count = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            count++; Console.WriteLine("PASS language UI: " + description);
        }
        var settings = Path.Combine(Environment.GetEnvironmentVariable("WEARABLE_DISTRIBUTOR_SETTINGS_DIR")!, "settings.json");
        var heading = new TextBlock { Style = (Style)app.FindResource("SectionHeaderStyle") };
        Check(((SolidColorBrush)heading.Foreground).Color == ((SolidColorBrush)app.FindResource("TextBrush")).Color, "Section headings retain readable contrast in the dark theme");
        var preferences = new AppPreferences { Language = "en", DataPath = Path.Combine(parent, "saved-game-data"), OutputPath = Path.Combine(parent, "saved-output"), AdditionalExclusions = ["fixture exclusion"] };
        PreferencesService.Save(settings, preferences);
        Loc.Initialize("en");
        var current = new MainWindow();
        // Only bypass unrelated startup discovery for this fixture. Real selection/change events run normally.
        var startup = (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), current, Method("StartWorkspace", typeof(object), typeof(EventArgs)));
        current.ContentRendered -= startup;
        var source = new PluginEntry { Plugin = "Language Fixture 装备.esp", Selected = true, Pieces = [new() { Plugin = "Language Fixture 装备.esp", LocalId = 0x800, Name = "Fixture Robe", Slots = "Body" }] };
        var other = new PluginEntry { Plugin = "Language Other.esp", Selected = false, Pieces = [new() { Plugin = "Language Other.esp", LocalId = 0x800, Name = "Fixture Boots", Slots = "Feet" }] };
        Field<List<PluginEntry>>(current, "scannedPlugins").AddRange([source, other]);
        var keyword = new KeywordChoice { Plugin = "Skyrim.esm", LocalId = 0x1234, EditorId = "BanditFaction", TargetKind = "Faction", Category = "Faction" };
        Field<List<KeywordChoice>>(current, "availableKeywords").Add(keyword);
        var profile = new NpcTargetProfile { FormId = "0x001234~Skyrim.esm", Plugin = "Skyrim.esm", Name = "Fixture NPC", Gender = "Female", IsHumanoid = true, IsEncounterable = true, TargetForms = [keyword.FormId] };
        Field<Dictionary<string, NpcTargetProfile>>(current, "npcProfiles")[profile.FormId] = profile;
        var preparation = new AudiencePreparation([keyword], [profile]);
        Set(current, "audiencePreparation", preparation);
        Method("RefreshPluginList").Invoke(current, null);
        Control<TextBox>(current, "SearchBox").Text = "Language";
        Control<TabControl>(current, "MainTabs").SelectedIndex = 2;
        Control<ComboBox>(current, "ActionScopeBox").SelectedIndex = 1;
        Control<TextBox>(current, "OutputPathBox").Text = "UNSAVED output field";
        Control<TextBox>(current, "DataPathBox").Text = "UNSAVED data field";
        Control<TextBox>(current, "ExclusionsBox").Text = "UNSAVED exclusion";
        Control<CheckBox>(current, "IncludeAccessoriesBox").IsChecked = true;
        var grid = Control<DataGrid>(current, "PluginsGrid");
        grid.SelectedItems.Add(other);
        grid.Columns[0].Width = 111;
        CollectionViewSource.GetDefaultView(grid.ItemsSource).SortDescriptions.Add(new SortDescription(nameof(PluginEntry.Plugin), ListSortDirection.Descending));
        app.MainWindow = current;
        current.Show();
        await WaitUntil(() => current.IsLoaded, "Initial fixture window did not load");
        await Task.Delay(100);
        var storedOutput = PreferencesService.Load(settings).OutputPath;
        var storedExclusion = PreferencesService.Load(settings).AdditionalExclusions.Single();
        var initialScanVersion = Field<int>(current, "inventoryRefreshVersion");
        var smokeLanguages = new HashSet<string>();

        async Task<MainWindow> Change(string code)
        {
            var previous = current;
            Control<ComboBox>(previous, "LanguageBox").SelectedValue = code;
            await WaitUntil(() => app.MainWindow is MainWindow next && !ReferenceEquals(previous, next) && next.IsLoaded && !previous.IsVisible, "Language did not apply automatically: " + code);
            current = (MainWindow)app.MainWindow;
            await Task.Delay(30);
            Check(Loc.Preference == code && PreferencesService.Load(settings).Language == code, code + " applies and persists without Save/restart");
            Check(!ReferenceEquals(previous, current) && app.Windows.OfType<StartupWindow>().Count() == 0, code + " completes loading and closes the old workspace/icon");
            Check(Field<List<PluginEntry>>(current, "scannedPlugins").SequenceEqual(new[] { source, other }) && source.Selected && !other.Selected, code + " preserves exact scanned objects and outfit checks");
            Check(ReferenceEquals(Field<AudiencePreparation>(current, "audiencePreparation"), preparation) && Field<int>(current, "inventoryRefreshVersion") == initialScanVersion && Field<int>(current, "activeInventoryReads") == 0, code + " reuses NPC preparation and performs no rescan/discovery");
            Check(Control<TextBox>(current, "DataPathBox").Text == "UNSAVED data field" && Control<TextBox>(current, "OutputPathBox").Text == "UNSAVED output field" && Control<TextBox>(current, "ExclusionsBox").Text == "UNSAVED exclusion" && Control<CheckBox>(current, "IncludeAccessoriesBox").IsChecked == true, code + " keeps unsaved Settings fields");
            Check(PreferencesService.Load(settings).OutputPath == storedOutput && PreferencesService.Load(settings).AdditionalExclusions.Single() == storedExclusion, code + " does not save unrelated unfinished Settings fields");
            Check(Control<TextBox>(current, "SearchBox").Text == "Language" && Control<TabControl>(current, "MainTabs").SelectedIndex == 2 && Control<ComboBox>(current, "ActionScopeBox").SelectedIndex == 1, code + " preserves search, open tab and action scope");
            var nextGrid = Control<DataGrid>(current, "PluginsGrid");
            Check(nextGrid.SelectedItems.Contains(other) && nextGrid.Columns[0].Width.Value == 111 && CollectionViewSource.GetDefaultView(nextGrid.ItemsSource).SortDescriptions.Count == 1, code + " preserves highlighted rows, widths and sort order");
            Check(Control<TextBlock>(current, "SelectionCountText").Text.StartsWith(Loc.F($"{1} outfit checked"), StringComparison.Ordinal), code + " refreshes dynamic selection text");
            Check(Control<TextBox>(current, "ScanDiagnosticsBox").Text == Loc.T("No scan report yet."), code + " refreshes the empty scan-diagnostics message");
            if (smokeLanguages.Add(Loc.ActiveLanguage))
            {
                var captured = new Dictionary<string, SavedDistributionOptions>();
                var wizard = new PatchOptionsWindow([source], [keyword], "25", "SPID", captured, (key, value) => captured[key] = value,
                    npcProfiles: [profile], preparation: preparation) { Owner = current };
                wizard.Show();
                await Task.Delay(60);
                var npcMode = Descendants(wizard).OfType<CheckBox>().Single(box => Equals(box.Content, Loc.T("Distribute outfits for NPCs to wear")));
                npcMode.IsChecked = true;
                await Task.Delay(40);
                wizard.UpdateLayout();
                var gender = Descendants(wizard).OfType<ComboBox>().Single(box => box.Items.Cast<object>().SequenceEqual(new object[] { "Any", "Female", "Male" }));
                gender.SelectedItem = "Female";
                var groups = Descendants(wizard).OfType<ComboBox>().Single(box => box.ItemsSource is IEnumerable<string> values && values.Contains("Bandits"));
                groups.SelectedItem = "Bandits";
                var add = Descendants(wizard).OfType<Button>().Single(button => Equals(button.Content, Loc.T("＋ Add NPC group")));
                Check(add.IsEnabled, code + " wizard resolves the untranslated Bandits group with the translated UI");
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var config = wizard.Options.Single();
                Check(config.Gender == "Female" && config.TargetRules.Single().Group == "Bandits" && config.TargetRules.Single().Filters.Any(filter => filter.FormId == keyword.FormId), code + " wizard stores correct NPC sex and original target identifiers");
                Check(captured.Values.Single().Gender == "Female" && captured.Values.Single().TargetRules.Single().Group == "Bandits", code + " wizard saves translated selections as invariant rules");
                var review = new PatchReviewWindow([config], _ => 1, (_, _) => 0, _ => 0, "SPID") { Owner = wizard };
                review.Show();
                await Task.Delay(40);
                Check(review.IsLoaded && review.Title == Loc.T("Skyrim SPID Patch Generator · Review patch"), code + " renders the translated review window");
                review.Close();
                wizard.Close();
            }
            return current;
        }
        foreach (var code in new[] { "de", "fr", "es", "zh-Hans", "en", "de", "zh-Hans", "auto", "en" }) await Change(code);

        Set(current, "activeInventoryReads", 1);
        Control<ComboBox>(current, "LanguageBox").SelectedValue = "de";
        await Task.Delay(150);
        Check(ReferenceEquals(app.MainWindow, current) && app.Windows.OfType<StartupWindow>().Any(), "Busy work keeps its workspace and displays the animated loading window");
        Control<ComboBox>(current, "LanguageBox").SelectedValue = "fr";
        Set(current, "activeInventoryReads", 0);
        var queued = current;
        await WaitUntil(() => app.MainWindow != queued && !queued.IsVisible, "Queued language change did not finish");
        current = (MainWindow)app.MainWindow;
        Check(Loc.Preference == "fr" && PreferencesService.Load(settings).Language == "fr", "Rapid changes during busy work apply the last requested language");

        Set(current, "activeInventoryReads", 1);
        Control<ComboBox>(current, "LanguageBox").SelectedValue = "de";
        await Task.Delay(100);
        Control<ComboBox>(current, "LanguageBox").SelectedValue = "fr";
        Set(current, "activeInventoryReads", 0);
        await WaitUntil(() => !Field<bool>(current, "changingLanguage"), "Canceled language request did not settle");
        Check(ReferenceEquals(app.MainWindow, current) && !app.Windows.OfType<StartupWindow>().Any() && Loc.Preference == "fr", "Returning to the current language cancels a pending switch cleanly");

        var savedBeforeRecovery = File.ReadAllBytes(settings);
        Set(current, "settingsRecoveryRequired", true);
        var recovering = current;
        Control<ComboBox>(current, "LanguageBox").SelectedValue = "es";
        await WaitUntil(() => app.MainWindow != recovering && !recovering.IsVisible, "Recovery session language did not change");
        current = (MainWindow)app.MainWindow;
        Check(Loc.Preference == "es" && savedBeforeRecovery.SequenceEqual(File.ReadAllBytes(settings)) && Control<TextBlock>(current, "StatusText").Text == Loc.T("Language changed for this session, but could not be saved. Your original settings file was kept."), "Settings recovery allows session translation without overwriting the protected file");
        Set(current, "settingsRecoveryRequired", false);

        var lockedBefore = File.ReadAllBytes(settings);
        var lockedWindow = current;
        using (var locked = new FileStream(settings, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Control<ComboBox>(current, "LanguageBox").SelectedValue = "de";
            await WaitUntil(() => app.MainWindow != lockedWindow && !lockedWindow.IsVisible, "Locked settings session language did not change");
            current = (MainWindow)app.MainWindow;
            Check(Loc.Preference == "de" && Control<TextBlock>(current, "StatusText").Text == Loc.T("Language changed for this session, but could not be saved. Your original settings file was kept."), "A locked settings file produces a readable warning while the language still changes");
        }
        Check(lockedBefore.SequenceEqual(File.ReadAllBytes(settings)), "Failed automatic save leaves original settings bytes intact");
        await Change("zh-Hans");
        if (visualPreview)
        {
            Console.WriteLine("VISUAL PREVIEW: Chinese language fixture is available for 30 seconds.");
            await Task.Delay(30_000);
        }
        Set(current, "activeInventoryReads", 1);
        var beforeClose = File.ReadAllBytes(settings);
        Control<ComboBox>(current, "LanguageBox").SelectedValue = "fr";
        await Task.Delay(100);
        current.Close();
        await Task.Delay(150);
        Check(beforeClose.SequenceEqual(File.ReadAllBytes(settings)) && !app.Windows.OfType<MainWindow>().Any() && !app.Windows.OfType<StartupWindow>().Any(), "Closing while a language request waits cancels it without writing settings or leaking a loader");
        Console.WriteLine("Language window checks passed: " + count);
    }

    private static async Task WaitUntil(Func<bool> predicate, string message)
    {
        var clock = Stopwatch.StartNew();
        while (!predicate())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException(message);
            await Task.Delay(20);
        }
    }
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(window)!;
    private static void Set(MainWindow window, string name, object value) => typeof(MainWindow).GetField(name, Private)!.SetValue(window, value);
    private static MethodInfo Method(string name, params Type[] types) => typeof(MainWindow).GetMethod(name, Private, types)!;
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
