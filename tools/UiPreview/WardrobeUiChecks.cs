using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WearableDistributor;

namespace WearableDistributor.UiPreview;

internal static class WardrobeUiChecks
{
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Tree(child)) yield return nested; }
    }
    internal static List<PluginPatchOptions> Fixtures()
    {
        return new[] { "Mage wardrobe example.esp", "Rogue wardrobe example.esp", "Adventurer wardrobe example.esp" }.Select((plugin, i) =>
        {
            var piece = new WearableRecord { Plugin = plugin, LocalId = 0x800, Name = "Example garment", Slots = "Body", SupportedGender = "Female" };
            return new PluginPatchOptions { Source = new() { Plugin = plugin, Pieces = [piece] }, Pieces = [piece], OutfitName = System.IO.Path.GetFileNameWithoutExtension(plugin),
                Gender = "Female", Chance = "100", WardrobeName = "Wenches wardrobe", WardrobeWeight = i == 2 ? 2 : 1,
                TargetRules = [new() { Collection = "Immersive Wenches.esp", Group = "Wenches", NpcResolutionComplete = true,
                    ResolvedNpcFormIds = ["0x000800~Immersive Wenches.esp"], Filters = [new() { Plugin = "Immersive Wenches.esp", LocalId = 0x800, TargetKind = "Npc", Category = "NPC audience" }] }] };
        }).ToList();
    }
    internal static void Run()
    {
        var count = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); count++; Console.WriteLine("PASS wardrobe UI: " + message); }
        void Click(Window window, string label) => Tree(window).OfType<Button>().Single(b => b.Content?.ToString() == label).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var options = Fixtures();
        foreach (var config in options) config.WardrobeName = "";
        options[1].DistributeToNpcs = false;
        var window = new WardrobePoolWindow(options, options[0]);
        Exception? failure = null;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                window.UpdateLayout();
                var grid = Tree(window).OfType<DataGrid>().Single();
                Check(grid.Items.Count == 3, "editor lists only the three opened outfit setups");
                Click(window, "Select all");
                var name = Tree(window).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Wardrobe name"); name.Text = "Shared test wardrobe";
                var chance = Tree(window).OfType<ComboBox>().Single(t => AutomationProperties.GetName(t) == "Wardrobe overall chance percent"); chance.Text = "80";
                var last = grid.Items[2]; last.GetType().GetProperty("Weight")!.SetValue(last, 2);
                Check(grid.Items.Cast<object>().Select(o => o.GetType().GetProperty("Share")!.GetValue(o)?.ToString()).SequenceEqual(new[] { "25%", "25%", "50%" }), "live weight estimates show 25%, 25%, 50% before applying");
                Click(window, "Apply wardrobe");
            }
            catch (Exception e) { failure = e; window.Close(); }
        }));
        Check(window.ShowDialog() == true, "Apply wardrobe confirms the selected pool");
        if (failure is not null) throw failure;
        Check(options.All(c => c.WardrobeName == "Shared test wardrobe" && c.Chance == "80" && c.Gender == "Female" && c.DistributeToNpcs), "apply shares name, coverage, sex and NPC mode across checked members");
        Check(!ReferenceEquals(options[0].TargetRules, options[1].TargetRules), "members receive independent audience snapshots");
        var remove = new WardrobePoolWindow(options, options[0]);
        remove.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                Click(remove, "None"); var row = Tree(remove).OfType<DataGrid>().Single().Items[0]; row.GetType().GetProperty("Include")!.SetValue(row, true); Click(remove, "Remove checked from pools");
            }
            catch (Exception e) { failure = e; remove.Close(); }
        }));
        Check(remove.ShowDialog() == true, "remove checked has an explicit apply action"); if (failure is not null) throw failure;
        Check(options[0].WardrobeName == "" && options[1].WardrobeName == "Shared test wardrobe", "removal affects only checked rows");
        Check(options[0].Chance == "80" && options[0].TargetRules.Count == 1, "leaving a pool preserves individual NPC configuration");
        var cancel = new WardrobePoolWindow(options, options[1]);
        cancel.Dispatcher.BeginInvoke(new Action(() => { Click(cancel, "None"); Click(cancel, "Cancel"); }));
        Check(cancel.ShowDialog() == false && options[1].WardrobeName == "Shared test wardrobe", "Cancel does not change pool membership");
        var fixtures = Fixtures();
        var npc = new NpcTargetProfile { FormId = "0x000800~Immersive Wenches.esp", Plugin = "Immersive Wenches.esp", Name = "Wench", EditorId = "Wench", Gender = "Female", IsEncounterable = true, IsHumanoid = true };
        var choice = new KeywordChoice { Plugin = npc.Plugin, LocalId = 0x800, EditorId = "Wench", TargetKind = "Npc", Category = "NPC audience" };
        var saved = fixtures.ToDictionary(c => c.Source.Plugin, c => new SavedDistributionOptions { OutfitName = c.OutfitName, WardrobeName = "Saved wardrobe", WardrobeWeight = c.WardrobeWeight,
            DistributeToNpcs = true, Gender = "Female", Chance = "100", TargetRules = [new() { Collection = npc.Plugin, Group = "Wenches", Filters = [new() { Plugin = choice.Plugin, LocalId = choice.LocalId, EditorId = choice.EditorId, Category = choice.Category, TargetKind = choice.TargetKind }] }] });
        PatchOptionsWindow Open() => new(fixtures.Select(c => c.Source), [choice], "25", "SPID", saved, (key, value) => saved[key] = value, npcProfiles: [npc]);
        var wizard = Open(); wizard.Show(); wizard.UpdateLayout();
        wizard.Dispatcher.BeginInvoke(new Action(() =>
        {
            var editor = Application.Current.Windows.OfType<WardrobePoolWindow>().Single();
            try { Tree(editor).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Wardrobe overall chance percent").Text = "80"; Click(editor, "Apply wardrobe"); }
            catch (Exception error) { failure = error; editor.Close(); }
        }));
        Click(wizard, "Edit wardrobe: Saved wardrobe"); if (failure is not null) throw failure;
        Check(wizard.Options.All(c => c.Chance == "80"), "editing from the actual wizard updates every pool model");
        wizard.Close();
        Check(saved.Values.All(s => s.Chance == "80" && s.WardrobeName == "Saved wardrobe"), "stale page controls cannot overwrite a shared pool edit on close");
        var reopened = Open(); reopened.Dispatcher.BeginInvoke(new Action(() =>
        {
            try { Click(reopened, "Next outfit →"); Click(reopened, "Next outfit →"); Click(reopened, "Review & generate →"); }
            catch (Exception error) { failure = error; reopened.Close(); }
        }));
        Check(reopened.ShowDialog() == true, "a saved wardrobe can be reopened and reviewed without reapplying its setup"); if (failure is not null) throw failure;
        Check(reopened.Options.All(c => c.TargetRules.All(r => r.NpcResolutionComplete)), "review refreshes saved audience snapshots before checking shared scope");
        Console.WriteLine($"Wardrobe UI checks: {count} passed.");
    }
}
