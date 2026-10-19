using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace WearableDistributor;

/// <summary>Uses the same embedded icon as the executable, with no external artwork dependency.</summary>
public sealed class StartupWindow : Window
{
    public StartupWindow()
    {
        UiTheme.Apply(this);
        Title = "Skyrim SPID Patch Generator";
        Width = 460; Height = 270; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        var logo = BitmapFrame.Create(new Uri("pack://application:,,,/assets/WearableDistributor.ico"));
        Icon = logo;
        var content = new StackPanel { Margin = new Thickness(32), VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new Image { Source = logo, Width = 80, Height = 80, Margin = new Thickness(0, 0, 0, 14) });
        content.Children.Add(new TextBlock { Text = "Skyrim SPID Patch Generator", FontSize = 22, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        var caption = UiTheme.Caption("Opening your outfit workspace…"); caption.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(caption);
        content.Children.Add(new ProgressBar { Height = 4, IsIndeterminate = true, Margin = new Thickness(0, 18, 0, 0) });
        Content = new Border { BorderThickness = new Thickness(1), BorderBrush = UiTheme.Brush("AccentBrush"), Child = content };
    }
}
