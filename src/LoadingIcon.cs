using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace WearableDistributor;

/// <summary>The app's vector icon, with light travelling from the garment along each distribution branch.</summary>
public sealed class LoadingIcon : Viewbox
{
    private readonly List<DependencyObject> animated = [];
    private readonly List<Action> start = [];
    public LoadingIcon()
    {
        var ink = new SolidColorBrush(Color.FromRgb(0x23, 0x36, 0x2d)); ink.Freeze();
        var gold = new SolidColorBrush(Color.FromRgb(0xcd, 0xa9, 0x5c)); gold.Freeze();
        var cream = new SolidColorBrush(Color.FromRgb(0xf6, 0xef, 0xd8)); cream.Freeze();
        var glow = new SolidColorBrush(Color.FromRgb(0x64, 0xed, 0xc7)); glow.Freeze();
        var canvas = new Canvas { Width = 512, Height = 512 };
        var disc = new Ellipse { Width = 456, Height = 456, Fill = ink, Stroke = gold, StrokeThickness = 11 }; Canvas.SetLeft(disc, 28); Canvas.SetTop(disc, 28); canvas.Children.Add(disc);
        canvas.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M256,308 L256,361 L150,361 L150,404 M256,361 L256,404 M256,361 L362,361 L362,404"), Stroke = gold, StrokeThickness = 13, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        canvas.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M178,150 L216,122 L242,107 L256,125 L270,107 L296,122 L334,150 L310,196 L291,181 L299,289 L215,289 L223,181 L204,196 Z"), Fill = cream });
        canvas.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M242,107 L256,125 L270,107 L263,139 L256,146 L249,139 Z"), Fill = ink });
        canvas.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M256,147 L256,268 M226,194 L286,194"), Stroke = gold, StrokeThickness = 7 });
        var heart = new Ellipse { Width = 24, Height = 24, Fill = gold }; Canvas.SetLeft(heart, 244); Canvas.SetTop(heart, 173); canvas.Children.Add(heart);
        var pulse = new Ellipse { Width = 42, Height = 42, Stroke = glow, StrokeThickness = 5, Opacity = .3 }; Canvas.SetLeft(pulse, 235); Canvas.SetTop(pulse, 164); canvas.Children.Add(pulse);
        animated.Add(pulse);
        start.Add(() => pulse.BeginAnimation(OpacityProperty, new DoubleAnimation(.2, 1, TimeSpan.FromSeconds(.65)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }));
        var endpoints = new[] { 150.0, 256.0, 362.0 };
        for (var i = 0; i < endpoints.Length; i++)
        {
            var x = endpoints[i]; var delay = TimeSpan.FromSeconds(i * .22);
            var node = new Ellipse { Width = 46, Height = 46, Fill = gold }; Canvas.SetLeft(node, x - 23); Canvas.SetTop(node, 401); canvas.Children.Add(node);
            var bead = new Ellipse { Width = 20, Height = 20, Fill = glow, Opacity = .95 }; Canvas.SetLeft(bead, -10); Canvas.SetTop(bead, -10);
            var move = new TranslateTransform(256, 280); bead.RenderTransform = move; canvas.Children.Add(bead); animated.Add(move);
            // Geometry.Parse may return StreamGeometry, which cannot be cast to PathGeometry.
            // Build the animation's required geometry type directly.
            var route = new PathGeometry();
            var branch = new PathFigure { StartPoint = new Point(256, 280), IsClosed = false };
            branch.Segments.Add(new LineSegment(new Point(256, 361), true));
            branch.Segments.Add(new LineSegment(new Point(x, 361), true));
            branch.Segments.Add(new LineSegment(new Point(x, 424), true));
            route.Figures.Add(branch); route.Freeze();
            start.Add(() =>
            {
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimationUsingPath { PathGeometry = route, Source = PathAnimationSource.X, Duration = TimeSpan.FromSeconds(1.5), BeginTime = delay, RepeatBehavior = RepeatBehavior.Forever });
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimationUsingPath { PathGeometry = route, Source = PathAnimationSource.Y, Duration = TimeSpan.FromSeconds(1.5), BeginTime = delay, RepeatBehavior = RepeatBehavior.Forever });
            });
        }
        Child = canvas; Stretch = Stretch.Uniform;
        Loaded += (_, _) => Animate(); IsVisibleChanged += (_, _) => Animate(); Unloaded += (_, _) => Stop();
        System.Windows.Automation.AutomationProperties.SetName(this, "Loading: distributing outfit parts");
    }
    private void Animate()
    {
        Stop();
        if (!IsVisible || !SystemParameters.ClientAreaAnimation) return;
        try { foreach (var action in start) action(); }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException)
        {
            // Decorative animation must not prevent startup or patch setup from opening.
            Stop();
            App.WriteDiagnostic(DiagnosticEvent.UiIconLoadFailed, failure, DiagnosticLevel.Warning);
        }
    }
    private void Stop() { foreach (var target in animated) { if (target is TranslateTransform t) { t.BeginAnimation(TranslateTransform.XProperty, null); t.BeginAnimation(TranslateTransform.YProperty, null); } else if (target is Ellipse e) e.BeginAnimation(OpacityProperty, null); } }
}
