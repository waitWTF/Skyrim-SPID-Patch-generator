using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WearableDistributor;

/// <summary>Visible progress while scan-owned audience data is prepared off the UI thread.</summary>
public sealed class StartupWindow : Window
{
    private readonly TextBlock stage;
    private readonly TextBlock elapsed;
    private readonly Stopwatch clock = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public StartupWindow(string heading = "Skyrim SPID Patch Generator")
    {
        UiTheme.Apply(this);
        Title = heading;
        Width = 520; Height = 340; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/" + typeof(StartupWindow).Assembly.GetName().Name + ";component/assets/WearableDistributor.ico"));
        var content = new StackPanel { Margin = new Thickness(28), VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new LoadingIcon { Width = 112, Height = 112, Margin = new Thickness(0, 0, 0, 12) });
        content.Children.Add(new TextBlock { Text = heading, FontSize = 21, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        stage = UiTheme.Caption("Opening your workspace…"); stage.HorizontalAlignment = HorizontalAlignment.Center;
        stage.TextWrapping = TextWrapping.Wrap; stage.TextAlignment = TextAlignment.Center; stage.MinHeight = 38;
        content.Children.Add(stage);
        elapsed = UiTheme.Caption("Preparing…"); elapsed.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(elapsed);
        content.Children.Add(new ProgressBar { Height = 4, IsIndeterminate = true, Margin = new Thickness(0, 12, 0, 0) });
        Content = new Border { BorderThickness = new Thickness(1), BorderBrush = UiTheme.Brush("AccentBrush"), Child = content };
        timer.Tick += (_, _) => elapsed.Text = "Working · " + clock.Elapsed.ToString(@"mm\:ss") + " elapsed";
        Loaded += (_, _) => { clock.Start(); timer.Start(); };
        Closed += (_, _) => { timer.Stop(); clock.Stop(); };
    }
    public void SetStage(string message) => stage.Text = message;
}
