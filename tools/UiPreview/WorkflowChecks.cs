using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using WearableDistributor;

namespace WearableDistributor.UiPreview;

internal static class WorkflowChecks
{
    private static int checks;
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);checks++;Console.WriteLine("PASS UI "+message);}
    private static IEnumerable<DependencyObject> Tree(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Tree(child))yield return nested;}}
    private static T Field<T>(object obj,string name)=>(T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(obj)!;
    private static object? Call(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic,null,args.Select(a=>a.GetType()).ToArray(),null)?.Invoke(obj,args);
    private static void Click(Window window,string label){var button=Tree(window).OfType<Button>().Single(b=>b.Content?.ToString()==label);button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));window.UpdateLayout();}
    private static int BuiltPages(PatchOptionsWindow window)=>Field<System.Collections.IEnumerable>(window,"pages").Cast<object>().Count(page=>page.GetType().GetField("built",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(page) is not null);
    private static void Frame(){var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(90)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    public static void Run(List<PluginEntry> plugins,List<KeywordChoice> choices,List<NpcTargetProfile> profiles)
    {
        var splash=new StartupWindow();splash.Show();Frame();Check(Tree(splash).OfType<LoadingIcon>().Any(),"animated icon constructs and loads without a cast failure");splash.Close();
        var timer=Stopwatch.StartNew();var preparation=new AudiencePreparation(choices,profiles);timer.Stop();Console.WriteLine("Audience preparation: "+timer.Elapsed.TotalMilliseconds.ToString("F1")+" ms (synthetic roster)");
        foreach(var piece in plugins[0].Pieces)piece.SupportedGender="Female";
        var saved=new Dictionary<string,SavedDistributionOptions>(StringComparer.OrdinalIgnoreCase);var skipped=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shops=new[]{new MerchantTarget("Skyrim.esm",0x100,"Smith",MerchantStockService.Blacksmiths),new MerchantTarget("Skyrim.esm",0x200,"General",MerchantStockService.GeneralGoods),new MerchantTarget("Skyrim.esm",0x300,"Clothes",MerchantStockService.Clothing)};
        PatchOptionsWindow Open(){var window=new PatchOptionsWindow(plugins,choices,"25","SPID",saved,(key,value)=>saved[key]=value,npcProfiles:profiles,skippedOutfits:skipped,merchantTargets:shops,preparation:preparation);window.Show();window.UpdateLayout();return window;}
        var wizard=Open();Check(BuiltPages(wizard)==1,"only the current outfit builds controls");Check(ReferenceEquals(Field<NpcTargetingService>(wizard,"targeting"),preparation.Targeting),"wizard reuses prepared targeting service");
        var npc=Tree(wizard).OfType<CheckBox>().Single(box=>box.Content?.ToString()=="Distribute outfits for NPCs to wear");timer.Restart();npc.IsChecked=true;wizard.UpdateLayout();timer.Stop();Console.WriteLine("NPC toggle: "+timer.Elapsed.TotalMilliseconds.ToString("F1")+" ms (synthetic roster)");
        Check(wizard.Options[0].DistributeToNpcs && wizard.Options[0].Gender=="Female","NPC toggle enforces female-only outfit sex");
        Click(wizard,"＋ Add NPC group");Check(wizard.Options[0].TargetRules.Count==1,"Add audience stores exactly one audience");
        var sell=Tree(wizard).OfType<CheckBox>().Single(box=>box.Content?.ToString()=="Sell selected parts through traders");sell.IsChecked=true;npc.IsChecked=false;wizard.UpdateLayout();
        Check(!wizard.Options[0].DistributeToNpcs&&wizard.Options[0].MerchantCategories.Count==3,"trader checkbox alone enables merchant-only output");
        var optional=wizard.Options[0].Pieces.First(piece=>piece.PartMode=="Optional");optional.OptionalChance=80;
        Click(wizard,"Next outfit →");Check(BuiltPages(wizard)==2,"Next constructs only the next outfit");
        Click(wizard,"Skip outfit");Check(wizard.SkippedOptions.Count==1 && wizard.Options.Count==2,"Skip omits one outfit and preserves the others");
        wizard.Close();Check(saved.Count==3,"closing saves visited and unvisited configurations");
        var reopened=Open();Check(BuiltPages(reopened)==1,"reopening does not eagerly build every saved page");
        Check(!reopened.Options[0].DistributeToNpcs && reopened.Options[0].MerchantCategories.Count==3,"merchant-only destination survives reopening");
        Check(reopened.Options[0].Pieces.Any(piece=>piece.PartMode=="Optional"&&piece.OptionalChance==80),"optional part percentage survives reopening");
        Check(reopened.Options[0].MatchVariants && saved[plugins[0].Plugin].MatchVariants,"matching-variant choice survives saving and reopening");
        Check(reopened.Options[0].TargetRules.Count==1 && reopened.SkippedOptions.Count==1,"audience and session skip survive a return to setup");reopened.Close();
        var allSaved=new Dictionary<string,SavedDistributionOptions>();var untouched=new PatchOptionsWindow(plugins,choices,"25","SPID",allSaved,(key,value)=>allSaved[key]=value,npcProfiles:profiles,merchantTargets:shops,preparation:preparation);untouched.Show();untouched.UpdateLayout();untouched.Close();
        Check(BuiltPages(untouched)==1 && allSaved.Count==3,"saving unvisited pages does not construct their controls");
        var main=new MainWindow();var scan=Field<List<PluginEntry>>(main,"scannedPlugins");scan.AddRange(new[]{new PluginEntry{Plugin="Checked.esp",Selected=true},new PluginEntry{Plugin="Highlighted.esp",Selected=false},new PluginEntry{Plugin="Neither.esp",Selected=false}});
        typeof(MainWindow).GetMethod("RefreshPluginList",BindingFlags.Instance|BindingFlags.NonPublic,null,Type.EmptyTypes,null)!.Invoke(main,null);
        var grid=(DataGrid)main.FindName("PluginsGrid");grid.SelectedItems.Add(scan[1]);var targets=(List<PluginEntry>)typeof(MainWindow).GetMethod("GetActionTargets",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,null)!;
        Check(targets.Count==2 && targets.Contains(scan[0])&&targets.Contains(scan[1]),"bulk actions include both checked and highlighted rows");
        typeof(MainWindow).GetMethod("IgnorePlugin_Click",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,[main,new RoutedEventArgs()]);
        Check(grid.Items.Count==1 && ((PluginEntry)grid.Items[0]).Plugin=="Neither.esp","hide acts on those exact two rows");
        ((TabControl)main.FindName("LibraryViews")).SelectedIndex=1;Check(grid.Items.Count==2,"hidden tab contains the hidden rows");grid.SelectedItems.Clear();grid.SelectedItems.Add(scan[0]);grid.SelectedItems.Add(scan[1]);
        typeof(MainWindow).GetMethod("RestorePlugin_Click",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,[main,new RoutedEventArgs()]);
        ((TabControl)main.FindName("LibraryViews")).SelectedIndex=0;Check(grid.Items.Count==3,"unhide restores selected rows without losing the other row");
        Check(!new PluginEntry{Plugin="New outfit.esp"}.Selected,"new outfit plugins are unchecked by default");
        scan[0].Selected=true;scan[1].Selected=false;scan[2].Selected=false;
        typeof(MainWindow).GetMethod("RefreshPluginList",BindingFlags.Instance|BindingFlags.NonPublic,null,Type.EmptyTypes,null)!.Invoke(main,null);
        var batch=(List<PluginEntry>)typeof(MainWindow).GetMethod("GetCheckedConfigurationTargets",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,null)!;
        Check(batch.Count==1 && batch[0]==scan[0],"batch configuration uses only explicit checks, never highlighted rows");
        ((TextBox)main.FindName("SearchBox")).Text="Highlighted";grid.SelectedItem=scan[1];
        Check(((TextBlock)main.FindName("SelectionCountText")).Text.Contains("outside this view"),"search reports checked outfits outside the visible results");
        var rowTargets=(List<PluginEntry>)typeof(MainWindow).GetMethod("GetContextConfigurationTargets",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,null)!;
        Check(rowTargets.Count==1 && rowTargets[0]==scan[1],"configure this outfit ignores checked outfits outside search");
        for(var i=0;i<500;i++)scan.Add(new PluginEntry{Plugin="Other"+i+".esp",Selected=true});
        rowTargets=(List<PluginEntry>)typeof(MainWindow).GetMethod("GetContextConfigurationTargets",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,null)!;
        Check(rowTargets.Count==1 && rowTargets[0]==scan[1],"single-row configure ignores 500 other checked plugins");
        Check(grid.ContextMenu.Items.OfType<MenuItem>().Any(item=>item.Header?.ToString()=="Configure this outfit…"),"right-click menu exposes configure this outfit");
        var oneSaved=new Dictionary<string,SavedDistributionOptions>();
        var one=new PatchOptionsWindow(new[]{plugins[0]},choices,"25","SPID",oneSaved,(key,value)=>oneSaved[key]=value,npcProfiles:profiles,merchantTargets:shops,preparation:preparation);
        Exception? modalFailure=null;
        one.Loaded+=(_,_)=>one.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
        {
            try
            {
                Check(one.Options.Count==1 && BuiltPages(one)==1,"single-outfit setup builds exactly one outfit page");
                Check(Tree(one).OfType<Button>().Any(button=>button.Content?.ToString()=="Review & generate →"),"single outfit clearly offers review and generate");
                Tree(one).OfType<CheckBox>().Single(box=>box.Content?.ToString()=="Distribute outfits for NPCs to wear").IsChecked=true;one.UpdateLayout();
                Check(Tree(one).OfType<Button>().Any(button=>button.Content?.ToString()=="＋ Add NPC group"),"NPC group action uses plain wording");
                Click(one,"Save & close");
            }
            catch(Exception failure){modalFailure=failure;one.Close();}
        }));
        var modalResult=one.ShowDialog();if(modalFailure is not null)throw modalFailure;
        Check(modalResult==false && oneSaved.ContainsKey(plugins[0].Plugin),"Save and close persists setup without approving generation");
        Check(oneSaved.Count==1,"single-outfit save does not write settings for other plugins");
        var assetsFolder = Path.Combine(Path.GetTempPath(), "SSPG-feedback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(assetsFolder);
        var broken = new PluginEntry { Plugin = "ExampleMissingMeshes.esp", MeshAssets = new MeshAssetIndex(assetsFolder) };
        broken.MeshAssets.RefreshArchives();
        broken.Pieces = Enumerable.Range(0, 5).Select(i => new WearableRecord { Plugin = broken.Plugin, LocalId = (uint)(0x800 + i), Name = "Example part " + i, Slots = "Unnamed" + (44 + i), WornModels = [new("", "example\\part" + i + ".nif", false, false)] }).ToList();
        var feedbackWindow = new PatchOptionsWindow([broken], choices, "25", "SPID", new Dictionary<string, SavedDistributionOptions>(), (_, _) => {}, npcProfiles: profiles);
        feedbackWindow.Show(); feedbackWindow.UpdateLayout();
        var notices = Tree(feedbackWindow).OfType<Border>().Where(b => b.GetType().Name == "ValidationNotice").ToList();
        var messages = notices.SelectMany(Tree).OfType<TextBlock>().Where(t => t.Text.Contains("Worn meshes unavailable")).ToList();
        Check(messages.Count > 0 && messages.All(t => t.FontSize >= 14 && t.LineHeight >= 22 && !t.Text.Contains(".nif")), "mesh errors use readable summary text without a wall of file paths");
        var details = Tree(feedbackWindow).OfType<Expander>().Single(e => e.Header?.ToString() == "Show affected parts and file details");
        Check(!details.IsExpanded, "mesh file details start collapsed"); details.IsExpanded = true; feedbackWindow.UpdateLayout();
        Check(((TextBox)details.Content).IsReadOnly && ((TextBox)details.Content).Text.Contains("part4.nif"), "expanded file details are complete and copyable");
        if (Environment.GetEnvironmentVariable("SSPG_UI_FEEDBACK_CAPTURE") is string capture && capture.Length > 0)
        {
            notices.Last().BringIntoView(); Frame(); feedbackWindow.UpdateLayout();
            typeof(Program).GetMethod("RenderPreview", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [feedbackWindow, capture]);
        }
        feedbackWindow.Close();
        main.Close();
        Console.WriteLine("UI workflow checks: "+checks+" passed.");
    }
}
