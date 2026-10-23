using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace Recess.App.Breaks;

/// <summary>The built-in "Default" theme: the break's name, a countdown ring, and a suggestion.</summary>
internal sealed class DefaultThemeView : Grid
{
    private const double RingSize = 300;
    private const double RingWidth = 14;

    private readonly Path arc;
    private readonly TextBlock time;

    public DefaultThemeView(string breakName, Color color, string message)
    {
        var font = new FontFamily("Segoe UI Variable Display, Segoe UI");
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 140),
        };

        stack.Children.Add(new TextBlock
        {
            Text = breakName.ToUpperInvariant(),
            FontFamily = font,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(178, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var ring = new Grid { Width = RingSize, Height = RingSize, Margin = new Thickness(0, 28, 0, 28) };
        ring.Children.Add(new Ellipse
        {
            Stroke = new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)),
            StrokeThickness = RingWidth,
            Margin = new Thickness(RingWidth / 2),
        });
        arc = new Path
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = RingWidth,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        ring.Children.Add(arc);
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        time = new TextBlock
        {
            FontFamily = font,
            FontSize = 72,
            FontWeight = FontWeights.Light,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        center.Children.Add(time);
        center.Children.Add(new TextBlock
        {
            Text = "remaining",
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromArgb(153, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        ring.Children.Add(center);
        stack.Children.Add(ring);

        stack.Children.Add(new TextBlock
        {
            Text = message,
            FontFamily = font,
            FontSize = 24,
            Foreground = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        Children.Add(stack);
    }

    /// <summary>Shows the time left; <paramref name="left"/> runs from 1 (just started) to 0.</summary>
    public void Update(string countdown, double left)
    {
        time.Text = countdown;
        arc.Data = Arc(Math.Clamp(left, 0, 1));
    }

    // The arc starts at the top and runs clockwise for the part of the break still to go.
    private static Geometry Arc(double fraction)
    {
        var r = (RingSize - RingWidth) / 2;
        var c = RingSize / 2;
        if (fraction >= 0.9999)
        {
            return new EllipseGeometry(new Point(c, c), r, r);
        }
        if (fraction <= 0)
        {
            return Geometry.Empty;
        }
        var angle = fraction * 2 * Math.PI;
        var start = new Point(c, c - r);
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        return new PathGeometry([figure]);
    }
}
