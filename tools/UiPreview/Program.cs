using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using WearableDistributor;

namespace WearableDistributor.UiPreview;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "scan") return AuditScan(args);
        var settings = Path.Combine(AppContext.BaseDirectory, "preview-settings");
        Directory.CreateDirectory(settings);
        Environment.SetEnvironmentVariable("WEARABLE_DISTRIBUTOR_SETTINGS_DIR", settings);
        Environment.SetEnvironmentVariable("WEARABLE_DISTRIBUTOR_NO_AUTOSCAN", "1");
        var app = new App();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/SkyrimSPIDPatchGenerator;component/UiTheme.xaml", UriKind.Relative)
        });
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var mode = args.FirstOrDefault() ?? "wizard";
        var (plugins, choices, profiles) = Sample();
        var saved = new Dictionary<string, SavedDistributionOptions>(StringComparer.OrdinalIgnoreCase);
        var documentation = args.Contains("--docs");
        var demoMerchants = new[] { new MerchantTarget("Skyrim.esm", 0x100, "DemoSmithStock", MerchantStockService.Blacksmiths), new MerchantTarget("Skyrim.esm", 0x101, "DemoGeneralStock", MerchantStockService.GeneralGoods), new MerchantTarget("Skyrim.esm", 0x102, "DemoClothingStock", MerchantStockService.Clothing) };
        if (documentation)
            foreach (var plugin in plugins) plugin.Weapons = [new WearableRecord { Plugin = plugin.Plugin, LocalId = 0x900, Name = "Demo sword", Category = "Weapon" }];
        if (documentation)
            foreach (var plugin in plugins)
                saved[plugin.Plugin] = new SavedDistributionOptions
                {
                    Chance = "25", Gender = "Any",
                    MerchantCategories = mode == "merchandise" ? MerchantStockService.Categories.ToList() : [],
                    ExcludedPieceFormIds = plugin.Pieces.Skip(6).Select(piece => piece.FormId).ToList(),
                    TargetRules = [new SavedDistributionTargetRule { Collection = NpcTargetingService.OfficialCollection, Group = "Bandits",
                        Filters = [new SavedTargetChoice { EditorId = choices[1].EditorId, Plugin = choices[1].Plugin, LocalId = choices[1].LocalId, TargetKind = "Class", Category = choices[1].Category }] }]
                };
        Window window = mode == "cover" ? DocumentationCover()
            : mode == "main" || mode == "settings" || mode == "inventory" ? new MainWindow()
            : mode == "review" ? new PatchReviewWindow(plugins.Select(plugin => new PluginPatchOptions
                { Source = plugin, OutfitName = Path.GetFileNameWithoutExtension(plugin.Plugin), Pieces = documentation ? plugin.Pieces.Take(6).ToList() : plugin.Pieces,
                  TargetRules = [new DistributionTargetRule { Collection = NpcTargetingService.OfficialCollection, Group = "Bandits", Filters = [choices[0]] }] }),
                  _ => 12, (_, _) => 1, _ => 0, "SPID")
            : new PatchOptionsWindow(plugins, choices, "25", "SPID", saved, (key, value) => saved[key] = value,
                npcProfiles: profiles, merchantTargets: documentation ? demoMerchants : null);
        if (mode == "settings" && window.FindName("MainTabs") is TabControl settingsTabs) settingsTabs.SelectedIndex = 2;
        if (args.Contains("--small")) { window.Width = window.MinWidth; window.Height = window.MinHeight; }
        window.Closed += (_, _) => app.Shutdown();
        window.Title += " [synthetic UI preview]";
        window.Loaded += (_, _) =>
        {
            if (!args.Contains("--check") && !args.Contains("--render")) return;
            if (documentation && window is MainWindow) PopulateDocumentationPage(window, mode, plugins);
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try
                {
                    window.UpdateLayout();
                    if (documentation && mode == "parts")
                    {
                        Descendants(window).OfType<ScrollViewer>().First(viewer => !viewer.CanContentScroll && viewer.Content is StackPanel).ScrollToBottom();
                        window.UpdateLayout();
                    }
                    if (args.Contains("--check"))
                    {
                        CheckLayout(window);
                        Console.WriteLine("UI layout check passed: " + mode + " " + window.ActualWidth + "x" + window.ActualHeight);
                    }
                    var renderIndex = Array.IndexOf(args, "--render");
                    if (renderIndex >= 0)
                    {
                        if (renderIndex + 1 >= args.Length) throw new ArgumentException("Supply a PNG path after --render.");
                        RenderPreview(window, args[renderIndex + 1]);
                    }
                    app.Shutdown(0);
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
            }));
        };
        app.Dispatcher.BeginInvoke(new Action(() => window.ShowDialog()));
        return app.Run();
    }

    // Only the documentation renderer uses these fixtures; no installed mods or generated patches are read.
    private static void PopulateDocumentationPage(Window window, string mode, List<PluginEntry> plugins)
    {
        if (window.FindName("MainTabs") is TabControl tabs) tabs.SelectedIndex = mode == "inventory" ? 1 : mode == "settings" ? 2 : 0;
        void Label(string name, string text) { if (window.FindName(name) is TextBlock block) block.Text = text; }
        Label("ActivePathText", "Demo profile · synthetic outfit records");
        Label("StatusText", "Documentation demo · no installed mods or output files are changed.");
        Label("ScanSummaryText", "3 demo outfit plugins · 54 wearable records · 2 patched");
        Label("SelectionCountText", "3 outfits selected");
        foreach (var name in new[] { "DataPathBox", "OutputPathBox", "PluginListPathBox" })
            if (window.FindName(name) is TextBox path) path.Text = name == "OutputPathBox" ? @"D:\Modding\Skyrim SPID Patch Generator Output" : name == "PluginListPathBox" ? @"D:\Modding\Profiles\Demo\plugins.txt" : @"D:\Games\Skyrim Special Edition\Data";
        if (window.FindName("PluginsGrid") is DataGrid grid)
        {
            plugins[0].PatchStatus = "Patched"; plugins[1].PatchStatus = "Patched";
            grid.ItemsSource = plugins;
        }
        if (window.FindName("GenerateButton") is Button generate) generate.IsEnabled = true;
        if (mode != "inventory" || window.FindName("PatchInventoryGrid") is not DataGrid inventory) return;
        inventory.ItemsSource = plugins.Take(2).Select(plugin => new PatchInventoryRow
        {
            SourcePlugin = plugin.Plugin, IncludedPieces = "6", Health = "Demo: healthy", Chance = "25%", Gender = "Any",
            Audiences = "Bandits · Archers", Entry = new PatchManifestEntry { SourcePlugin = plugin.Plugin, Framework = "SPID" },
            Details = "SYNTHETIC DOCUMENTATION EXAMPLE\n\nOutfit: " + Path.GetFileNameWithoutExtension(plugin.Plugin) +
                "\nEngine: SPID\nAudience: Skyrim + official DLC / Bandits / Archers\nChance: 25% per matching NPC\nNPC sex: Any\n\nIncluded parts: Tunic (Dark), Gloves (Dark), Boots (Dark), Hood (Dark), Belt (Dark), Cloak (Dark)" +
                "\n\nGenerated files: WearableDistributor.esp + WearableDistributor_DISTR.ini\nThe real view also lists required plugins and file-health details."
        }).ToList();
        inventory.SelectedIndex = 0;
    }

    private static Window DocumentationCover()
    {
        var background = new Grid { Width = 1600, Height = 520, Background = new LinearGradientBrush(Color.FromRgb(15, 23, 31), Color.FromRgb(27, 58, 51), 20) };
        var content = new Grid { Margin = new Thickness(65, 45, 65, 40) };
        content.ColumnDefinitions.Add(new ColumnDefinition()); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(265) });
        var copy = new StackPanel();
        copy.Children.Add(new TextBlock { Text = "SKYRIM SE / AE     ·     WINDOWS DESKTOP APP     ·     0.4.1 PREVIEW", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Application.Current.FindResource("AccentBrush") });
        copy.Children.Add(new TextBlock { Text = "Skyrim SPID Patch Generator", FontSize = 66, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 15, 0, 4), Foreground = Brushes.White });
        copy.Children.Add(new TextBlock { Text = "Your outfit mods. Your NPCs. One guided patch.", FontSize = 26, Foreground = new SolidColorBrush(Color.FromRgb(189, 207, 211)), Margin = new Thickness(0, 0, 0, 30) });
        var steps = new WrapPanel();
        foreach (var (number, title, subtitle) in new[] { ("01", "Choose outfits", "Scan installed plugins"), ("02", "Choose who wears them", "Groups, roles, races and parts"), ("03", "Generate your patch", "SPID or SkyPatcher") })
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = number, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = (Brush)Application.Current.FindResource("AccentBrush") });
            panel.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 6, 0, 4) });
            panel.Children.Add(new TextBlock { Text = subtitle, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(172, 192, 201)) });
            steps.Children.Add(new Border { Child = panel, Padding = new Thickness(20, 14, 20, 14), MinWidth = 260, Margin = new Thickness(0, 0, 14, 0), Background = new SolidColorBrush(Color.FromArgb(170, 18, 33, 43)), BorderBrush = new SolidColorBrush(Color.FromRgb(53, 83, 85)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) });
        }
        copy.Children.Add(steps);
        copy.Children.Add(new TextBlock { Text = "OPEN SOURCE  /  AI-ASSISTED DEVELOPMENT  /  COMMUNITY CONTRIBUTIONS WELCOME", FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(154, 180, 185)), Margin = new Thickness(0, 30, 0, 0) });
        content.Children.Add(copy);
        var gold = new SolidColorBrush(Color.FromRgb(205, 169, 92));
        var green = new SolidColorBrush(Color.FromRgb(35, 54, 45));
        var ivory = new SolidColorBrush(Color.FromRgb(246, 239, 216));
        var logo = new DrawingGroup();
        logo.Children.Add(new GeometryDrawing(green, new Pen(gold, 11), new EllipseGeometry(new Point(256, 256), 228, 228)));
        logo.Children.Add(new GeometryDrawing(null, new Pen(gold, 13) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Geometry.Parse("M256,308 V361 H150 V404 M256,361 V404 M256,361 H362 V404")));
        foreach (var x in new[] { 150, 256, 362 }) logo.Children.Add(new GeometryDrawing(gold, null, new EllipseGeometry(new Point(x, 424), 23, 23)));
        logo.Children.Add(new GeometryDrawing(ivory, null, Geometry.Parse("M178,150 L216,122 L242,107 L256,125 L270,107 L296,122 L334,150 L310,196 L291,181 L299,289 L215,289 L223,181 L204,196 Z")));
        logo.Children.Add(new GeometryDrawing(green, null, Geometry.Parse("M242,107 L256,125 L270,107 L263,139 L256,146 L249,139 Z")));
        logo.Children.Add(new GeometryDrawing(null, new Pen(gold, 7), Geometry.Parse("M256,147 V268 M226,194 H286")));
        logo.Children.Add(new GeometryDrawing(gold, null, new EllipseGeometry(new Point(256, 185), 12, 12)));
        var icon = new Image { Width = 230, Height = 230, VerticalAlignment = VerticalAlignment.Center, Source = new DrawingImage(logo) };
        Grid.SetColumn(icon, 1); content.Children.Add(icon); background.Children.Add(content);
        return new Window { Content = background, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterScreen, Title = "Skyrim SPID Patch Generator documentation cover" };
    }

    private static void RenderPreview(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        var origin = VisualTreeHelper.GetOffset(root);
        var padding = root.Width == 1600 ? 0 : 20;
        var width = root.ActualWidth + padding * 2;
        var height = root.ActualHeight + padding * 2;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle((Brush)Application.Current.FindResource("AppBackgroundBrush"), null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(root) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(origin.X, origin.Y, root.ActualWidth, root.ActualHeight) }, null, new Rect(padding, padding, root.ActualWidth, root.ActualHeight));
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path); encoder.Save(stream);
        Console.WriteLine("Synthetic UI preview exported: " + Path.GetFullPath(path));
    }

    private static int AuditScan(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("Usage: scan <Data folder> <plugins.txt> <report.txt>"); return 2; }
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            var result = new WearableScanService().Scan(args[1], new AppPreferences { DataPath = args[1], PluginListPath = args[2] }, null, null, cancellation.Token);
            var targeting = new NpcTargetingService(result.Targets, result.Npcs);
            var report = result.CreateReport() + "\n\nAUDIENCE AUDIT\n" + string.Join("\n", targeting.GetCollections().Select(collection =>
                collection + ": " + string.Join(", ", targeting.GetGroupLabels(collection).Select(group => group + "=" + targeting.CountMatches(targeting.BuildRule(group, collection))))));
            var targetNames = result.Targets.DistinctBy(choice => choice.FormId, StringComparer.OrdinalIgnoreCase).ToDictionary(choice => choice.FormId, choice => choice.EditorId, StringComparer.OrdinalIgnoreCase);
            report += "\n\nMOD NPC EVIDENCE (read-only audit samples)\n" + string.Join("\n", result.Npcs.Where(npc => npc.CanReceiveWearables && npc.Plugin.Contains("Wenches", StringComparison.OrdinalIgnoreCase)).Take(30)
                .Select(npc => npc.FormId + " | " + npc.EditorId + " | " + npc.Name + " | " + npc.Gender + " | " + string.Join(", ", npc.TargetForms.Select(id => targetNames.GetValueOrDefault(id, id)))));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);
            File.WriteAllText(args[3], report);
            Console.WriteLine($"Read-only scan finished in {stopwatch.Elapsed.TotalSeconds:F1}s: {result.LoadOrder.Count} plugins; {result.Plugins.Count} wearable candidates; {result.Npcs.Count(npc => npc.CanReceiveWearables)} eligible NPC bases; {result.Diagnostics.Count} diagnostic lines.");
            Console.WriteLine("Report: " + Path.GetFullPath(args[3]));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("Read-only audit failed: " + ex.Message); return 1; }
    }

    private static void CheckLayout(Window window)
    {
        var buttons = Descendants(window).OfType<Button>().Where(button => button.IsVisible).ToList();
        if (buttons.Count < 2) throw new InvalidOperationException("The expected navigation controls did not render.");
        var navigation = buttons.Where(button => (button.Content?.ToString() ?? "").Contains("Next") ||
            (button.Content?.ToString() ?? "").Contains("Review") || (button.Content?.ToString() ?? "").Contains("Generate") ||
            (button.Content?.ToString() ?? "").Contains("Configure outfits") || (button.Content?.ToString() ?? "").Contains("Back")).ToList();
        if (navigation.Count == 0) throw new InvalidOperationException("No primary workflow control is visible.");
        foreach (var button in navigation)
        {
            var origin = button.TransformToAncestor(window).Transform(new Point());
            if (button.ActualHeight < 20 || origin.Y + button.ActualHeight > window.ActualHeight - 4 || origin.X < 0 || origin.X + button.ActualWidth > window.ActualWidth)
                throw new InvalidOperationException("Workflow control is outside the viewport: " + button.Content);
        }
        if (window is PatchOptionsWindow && !Descendants(window).OfType<ScrollViewer>().Any(viewer => viewer.ScrollableHeight > 0))
            throw new InvalidOperationException("Tall outfit page is not scrollable at the minimum window size.");
        if (window is MainWindow) CheckMainPageAutomation(window);
    }

    private static void CheckMainPageAutomation(Window window)
    {
        if (window.FindName("MainTabs") is not TabControl tabs)
            throw new InvalidOperationException("Main navigation pages are missing.");
        var peer = UIElementAutomationPeer.CreatePeerForElement(tabs)
            ?? throw new InvalidOperationException("Main navigation has no automation peer.");
        var expectedControl = tabs.SelectedIndex == 2 ? "DataPathBox" : "SearchBox";
        var pending = new Stack<AutomationPeer>();
        var visited = new HashSet<AutomationPeer>();
        pending.Push(peer);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current)) continue;
            if (current.GetAutomationId() == expectedControl && current.IsControlElement()) return;
            foreach (var child in current.GetChildren() ?? []) pending.Push(child);
        }
        throw new InvalidOperationException("Visible page content is missing from UI Automation: " + expectedControl);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static (List<PluginEntry>, List<KeywordChoice>, List<NpcTargetProfile>) Sample()
    {
        var choices = new List<KeywordChoice>
        {
            new() { EditorId = "BanditFaction", Plugin = "Skyrim.esm", LocalId = 1, TargetKind = "Faction", Category = "Faction" },
            new() { EditorId = "EncClassBanditMissile", Plugin = "Skyrim.esm", LocalId = 2, TargetKind = "Class", Category = "Occupation / class" },
            new() { EditorId = "EncClassBanditMelee", Plugin = "Skyrim.esm", LocalId = 3, TargetKind = "Class", Category = "Occupation / class" },
            new() { EditorId = "NordRace", Plugin = "Skyrim.esm", LocalId = 4, TargetKind = "Race", Category = "Race" },
            new() { EditorId = "BretonRace", Plugin = "Skyrim.esm", LocalId = 5, TargetKind = "Race", Category = "Race" },
            new() { EditorId = "JobFarmerFaction", Plugin = "Skyrim.esm", LocalId = 6, TargetKind = "Faction", Category = "Faction" },
            new() { EditorId = "JobFarmer", Plugin = "Skyrim.esm", LocalId = 7, TargetKind = "Keyword", Category = "Occupation" }
        };
        var profiles = Enumerable.Range(1, 18).Select(index => new NpcTargetProfile
        {
            FormId = $"0x{0x1000 + index:X6}~Skyrim.esm", Name = index < 13 ? "Bandit " + index : "Farmer " + index,
            Plugin = "Skyrim.esm", WinningPlugin = "Skyrim.esm", Gender = index % 2 == 0 ? "Female" : "Male",
            IsHumanoid = true, IsEncounterable = true, EncounterEvidence = "Synthetic UI preview",
            TargetForms = (index < 13 ? new[] { choices[0].FormId, choices[index % 2 == 0 ? 1 : 2].FormId, choices[index % 2 == 0 ? 3 : 4].FormId }
                : new[] { choices[5].FormId, choices[6].FormId, choices[3].FormId }).ToHashSet(StringComparer.OrdinalIgnoreCase)
        }).ToList();
        var plugins = new[] { "Adventurers Armor.esp", "Magecore.esp", "Lunar Claw.esp" }.Select(name => new PluginEntry
        {
            Plugin = name, SourcePath = "UI preview / " + name,
            Pieces = Enumerable.Range(0, 18).Select(index => new WearableRecord
            {
                Plugin = name, LocalId = (uint)(0x800 + index), Include = true,
                Name = new[] { "Tunic", "Gloves", "Boots", "Hood", "Belt", "Cloak" }[index % 6] + " (" + new[] { "Dark", "Light", "Red" }[index / 6] + ")",
                Slots = new[] { "Body", "Hands", "Feet", "Hair", "Pelvis", "Back" }[index % 6]
            }).ToList()
        }).ToList();
        return (plugins, choices, profiles);
    }
}
