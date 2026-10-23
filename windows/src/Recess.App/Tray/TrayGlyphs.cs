using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Recess.Core;

namespace Recess.App.Tray;

/// <summary>
/// The tray icon's pictures, drawn here so they're crisp at every size and match the
/// taskbar's light or dark colour. Each glyph is drawn in a 16-unit box and scaled.
/// </summary>
internal static class TrayGlyphs
{
    /// <summary>True when the taskbar is light, so glyphs should be dark.</summary>
    public static bool LightTaskbar
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
    }

    public static Color Ink => LightTaskbar ? Color.FromRgb(28, 28, 30) : Colors.White;

    public static Color ToColor(Rgba c) => Color.FromArgb(
        (byte)Math.Round(Math.Clamp(c.A, 0, 1) * 255), (byte)Math.Round(Math.Clamp(c.R, 0, 1) * 255),
        (byte)Math.Round(Math.Clamp(c.G, 0, 1) * 255), (byte)Math.Round(Math.Clamp(c.B, 0, 1) * 255));

    /// <summary>A line drawing for one of the icon choices.</summary>
    public static void DrawGlyph(DrawingContext dc, double size, StatusIcon icon, Color ink)
    {
        dc.PushTransform(new ScaleTransform(size / 16, size / 16));
        var pen = new Pen(new SolidColorBrush(ink), 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var fill = new SolidColorBrush(ink);
        switch (icon)
        {
            case StatusIcon.PalmTree:
                Stroke(dc, pen, "M 8.5 15 C 8.5 11 8 8 7 5.5");
                Stroke(dc, pen, "M 7 5.5 C 5 3.5 2.5 4 1.5 6");
                Stroke(dc, pen, "M 7 5.5 C 8 3 11 2.5 13 4");
                Stroke(dc, pen, "M 7 5.5 C 4.5 5.5 3 7.5 3 9.5");
                Stroke(dc, pen, "M 7 5.5 C 9.5 5 12 6.5 13 9");
                Stroke(dc, pen, "M 4 15 L 13 15");
                break;
            case StatusIcon.BeachUmbrella:
                Stroke(dc, pen, "M 2 8 A 6 6 0 0 1 14 8 Z");
                Stroke(dc, pen, "M 8 8 L 8 14.5");
                Stroke(dc, pen, "M 3 14.5 L 13 14.5");
                break;
            case StatusIcon.SunHorizon:
                Stroke(dc, pen, "M 4 10 A 4 4 0 0 1 12 10");
                Stroke(dc, pen, "M 1.5 10.5 L 14.5 10.5");
                Stroke(dc, pen, "M 4.5 13.5 L 11.5 13.5");
                Stroke(dc, pen, "M 8 2 L 8 3.5 M 2.8 4.3 L 3.9 5.4 M 13.2 4.3 L 12.1 5.4");
                break;
            case StatusIcon.Waves:
                Stroke(dc, pen, "M 1.5 5 Q 3.5 3 5.5 5 T 9.5 5 T 13.5 5 T 15 4.5");
                Stroke(dc, pen, "M 1.5 9 Q 3.5 7 5.5 9 T 9.5 9 T 13.5 9 T 15 8.5");
                Stroke(dc, pen, "M 1.5 13 Q 3.5 11 5.5 13 T 9.5 13 T 13.5 13 T 15 12.5");
                break;
            case StatusIcon.Cardio:
                dc.DrawEllipse(fill, null, new Point(10, 2.6), 1.6, 1.6);
                Stroke(dc, pen, "M 9.2 5.5 L 7.5 9.5 L 10 11.5 L 9.5 15");
                Stroke(dc, pen, "M 7.5 9.5 L 4.5 12 L 2 11.5");
                Stroke(dc, pen, "M 9 6 L 6 6.5 L 4.5 8.5 M 9 6 L 11 8.5 L 13.5 8");
                break;
            case StatusIcon.Strength:
                Stroke(dc, pen, "M 4 8 L 12 8");
                dc.DrawRoundedRectangle(fill, null, new Rect(2.2, 4.5, 2, 7), 0.6, 0.6);
                dc.DrawRoundedRectangle(fill, null, new Rect(11.8, 4.5, 2, 7), 0.6, 0.6);
                dc.DrawRoundedRectangle(fill, null, new Rect(0.6, 6, 1.4, 4), 0.5, 0.5);
                dc.DrawRoundedRectangle(fill, null, new Rect(14, 6, 1.4, 4), 0.5, 0.5);
                break;
            case StatusIcon.MindBody:
                dc.DrawEllipse(fill, null, new Point(8, 2.8), 1.7, 1.7);
                Stroke(dc, pen, "M 8 5.5 L 8 10");
                Stroke(dc, pen, "M 8 6.5 C 6 8 4.5 9 3 8.5 M 8 6.5 C 10 8 11.5 9 13 8.5");
                Stroke(dc, pen, "M 2.5 13.5 C 5 11 7 11.5 8 12.5 C 9 11.5 11 11 13.5 13.5");
                break;
            default: // Cup and saucer
                Stroke(dc, pen, "M 3 6.5 L 12 6.5 L 11 11.5 C 10.8 12.6 10 13 9 13 L 6 13 C 5 13 4.2 12.6 4 11.5 Z");
                Stroke(dc, pen, "M 11.8 7.5 C 14 7.3 14.6 10 11.3 10.4");
                Stroke(dc, pen, "M 1.5 14.8 L 13.5 14.8");
                Stroke(dc, pen, "M 6 1.5 C 5.3 2.5 6.7 3 6 4.3 M 9 1.5 C 8.3 2.5 9.7 3 9 4.3");
                break;
        }
        dc.Pop();
    }

    private static void Stroke(DrawingContext dc, Pen pen, string path) => dc.DrawGeometry(null, pen, Geometry.Parse(path));

    public static void DrawDot(DrawingContext dc, double size, Color color) =>
        dc.DrawEllipse(new SolidColorBrush(color), null, new Point(size / 2, size / 2), size * 0.32, size * 0.32);

    /// <summary>Today's postponed, skipped and done breaks as a pie, or the break's colour when there are none yet.</summary>
    public static void DrawPie(DrawingContext dc, double size, Break b)
    {
        var c = b.State.Counts;
        (int Count, Color Color)[] values =
        [
            (c.TodayPostpone, Color.FromRgb(255, 159, 10)),
            (c.TodaySkip, Color.FromRgb(255, 69, 58)),
            (c.TodayDone, Color.FromRgb(48, 209, 88)),
        ];
        var center = new Point(size / 2, size / 2);
        var r = size / 2 - 1;
        var total = values.Sum(v => v.Count);
        if (total == 0)
        {
            dc.DrawEllipse(new SolidColorBrush(ToColor(b.Color)), null, center, r, r);
            return;
        }
        var angle = -Math.PI / 2;
        foreach (var (count, color) in values.Where(v => v.Count > 0))
        {
            var sweep = (double)count / total * 2 * Math.PI;
            if (count == total)
            {
                dc.DrawEllipse(new SolidColorBrush(color), null, center, r, r);
                break;
            }
            var start = new Point(center.X + r * Math.Cos(angle), center.Y + r * Math.Sin(angle));
            var end = new Point(center.X + r * Math.Cos(angle + sweep), center.Y + r * Math.Sin(angle + sweep));
            var figure = new PathFigure { StartPoint = center, IsClosed = true };
            figure.Segments.Add(new LineSegment(start, false));
            figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, sweep > Math.PI, SweepDirection.Clockwise, false));
            dc.DrawGeometry(new SolidColorBrush(color), null, new PathGeometry([figure]));
            angle += sweep;
        }
    }

    /// <summary>Short text (e.g. "12", "2h") filling the icon, for the countdown style.</summary>
    public static void DrawText(DrawingContext dc, double size, string text, Color ink, double pixelsPerDip)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Condensed);
        var fontSize = size * 0.95;
        FormattedText Measure() => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, fontSize,
            new SolidColorBrush(ink), pixelsPerDip);
        var formatted = Measure();
        while (formatted.Width > size && fontSize > 4)
        {
            fontSize -= 0.5;
            formatted = Measure();
        }
        dc.DrawText(formatted, new Point((size - formatted.Width) / 2, (size - formatted.Height) / 2));
    }

    /// <summary>Two bars, for when every break is paused.</summary>
    public static void DrawPaused(DrawingContext dc, double size, Color ink)
    {
        var brush = new SolidColorBrush(ink);
        var w = size * 0.18;
        dc.DrawRoundedRectangle(brush, null, new Rect(size * 0.26, size * 0.2, w, size * 0.6), w / 3, w / 3);
        dc.DrawRoundedRectangle(brush, null, new Rect(size * 0.56, size * 0.2, w, size * 0.6), w / 3, w / 3);
    }
}
