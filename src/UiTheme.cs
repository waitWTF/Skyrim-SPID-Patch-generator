using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace WearableDistributor;

internal static class UiTheme
{
    public static Brush Brush(string key) => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    public static void Apply(Window window)
    {
        window.SetResourceReference(Control.BackgroundProperty, "AppBackgroundBrush");
        window.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        window.FontFamily = new FontFamily("Segoe UI");
        window.FontSize = 13;
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
        window.SourceInitialized += (_, _) =>
        {
            var enabled = 1;
            try { DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref enabled, sizeof(int)); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        };
        try { window.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/SkyrimSPIDPatchGenerator;component/assets/WearableDistributor.ico", UriKind.Absolute)); }
        catch (IOException) { /* The executable icon remains available if the resource is missing. */ }
    }

    public static TextBlock Caption(string text) => new()
    {
        Text = text, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap,
        FontSize = 12, Margin = new Thickness(0, 4, 0, 8)
    };

    public static Button Button(string text, bool primary = false) => new()
    {
        Content = text,
        Style = Application.Current?.TryFindResource(primary ? "PrimaryButtonStyle" : "SecondaryButtonStyle") as Style
    };

    public static Border Card(UIElement content) => new()
    {
        Child = content, Background = Brush("SurfaceBrush"), BorderBrush = Brush("BorderBrush"),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(16)
    };

    public static void EnableScrollChaining(ScrollViewer outer)
    {
        outer.PreviewMouseWheel += (_, args) =>
        {
            var source = args.OriginalSource as DependencyObject;
            ScrollViewer? inner = null;
            while (source is not null && !ReferenceEquals(source, outer))
            {
                if (source is ScrollViewer viewer) { inner = viewer; break; }
                source = source is Visual or Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            }
            if (inner is not null && inner.ScrollableHeight > 0 &&
                (args.Delta > 0 && inner.VerticalOffset > 0 || args.Delta < 0 && inner.VerticalOffset < inner.ScrollableHeight - 0.5)) return;
            // Nested checklists and grids otherwise swallow wheel input at their edges.
            var lines = SystemParameters.WheelScrollLines;
            var amount = lines < 0 ? outer.ViewportHeight : Math.Max(1, lines) * 16.0;
            outer.ScrollToVerticalOffset(outer.VerticalOffset - args.Delta / 120.0 * amount);
            args.Handled = true;
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
