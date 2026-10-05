using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using RobControl.App.ViewModels;
using RobControl.Core.Trending;

namespace RobControl.App.Views;

/// <summary>
/// A plain time-series chart, drawn by hand rather than taken from a package (CLAUDE.md: every
/// dependency has to be justified to plant IT, and a line chart is a few hundred lines).
///
/// <para>Analog signals share the upper plot and its value axis. Digital signals (DI, DO and the other
/// bit I/O) get a lane each underneath, drawn as steps - a 0/1 line on the same axis as a counter at
/// 1287 would be invisible. Values hold until the next stored sample, because the recorder only stores
/// changes: the line is a step, never an interpolation between two samples.</para>
///
/// <para>Colours come from the theme tokens, so it follows the appearance dialog like everything else.</para>
/// </summary>
public sealed class TrendChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IEnumerable), typeof(TrendChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSeriesChanged));

    public static readonly DependencyProperty FromProperty = DependencyProperty.Register(
        nameof(From), typeof(DateTimeOffset), typeof(TrendChart),
        new FrameworkPropertyMetadata(default(DateTimeOffset), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ToProperty = DependencyProperty.Register(
        nameof(To), typeof(DateTimeOffset), typeof(TrendChart),
        new FrameworkPropertyMetadata(default(DateTimeOffset), FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly string[] Palette = ["accent", "info", "good", "bad", "warn", "txt-1"];

    private const double LaneHeight = 22;
    private const double AxisWidth = 64;
    private const double TimeAxisHeight = 20;
    private const double LegendRow = 18;

    public TrendChart()
    {
        // Redraw on a theme change: a brush looked up by name in OnRender does not track the swap.
        IsVisibleChanged += (_, _) => InvalidateVisual();
    }

    public IEnumerable? Series
    {
        get => (IEnumerable?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public DateTimeOffset From
    {
        get => (DateTimeOffset)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public DateTimeOffset To
    {
        get => (DateTimeOffset)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        ArgumentNullException.ThrowIfNull(dc);
        Brush background = Find("bg-0");
        Brush grid = Find("line-soft");
        Brush text = Find("txt-2");
        dc.DrawRectangle(background, null, new Rect(RenderSize));

        List<TrendSeries> all = Series?.OfType<TrendSeries>().ToList() ?? [];
        if (all.Count == 0 || To <= From || ActualWidth < AxisWidth + 50 || ActualHeight < 80)
        {
            DrawText(dc, "Nothing to plot yet. Add signals, then Start recording.", new Point(12, 12), text, 12);
            return;
        }

        List<TrendSeries> analog = [.. all.Where(s => !s.IsDigital)];
        List<TrendSeries> digital = [.. all.Where(s => s.IsDigital)];

        double legendHeight = Math.Ceiling(all.Count / 3.0) * LegendRow + 6;
        double left = AxisWidth;
        double right = ActualWidth - 10;
        double top = legendHeight;
        double lanes = digital.Count * LaneHeight;
        double bottom = ActualHeight - TimeAxisHeight;
        double plotBottom = analog.Count > 0 ? bottom - lanes - (digital.Count > 0 ? 8 : 0) : top;

        DrawLegend(dc, all, text);
        double X(DateTimeOffset t) => left + ((t - From).TotalMilliseconds / (To - From).TotalMilliseconds * (right - left));

        // Time axis: five ticks.
        for (int i = 0; i <= 4; i++)
        {
            DateTimeOffset t = From + ((To - From) * i / 4);
            double x = X(t);
            dc.DrawLine(new Pen(grid, 1), new Point(x, top), new Point(x, bottom));
            string label = (To - From) > TimeSpan.FromDays(1)
                ? t.ToLocalTime().ToString("ddd HH:mm", CultureInfo.CurrentCulture)
                : t.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            DrawText(dc, label, new Point(Math.Min(x - 20, right - 60), bottom + 3), text, 10);
        }

        if (analog.Count > 0 && plotBottom - top > 30)
        {
            List<double> values = [.. analog.SelectMany(s => s.Points).Select(p => p.Value)];
            double min = values.Count == 0 ? 0 : values.Min();
            double max = values.Count == 0 ? 1 : values.Max();
            if (max - min < 1e-9)
            {
                min -= 1;
                max += 1;
            }

            double pad = (max - min) * 0.08;
            min -= pad;
            max += pad;
            double Y(double v) => plotBottom - ((v - min) / (max - min) * (plotBottom - top));

            for (int i = 0; i <= 4; i++)
            {
                double v = min + ((max - min) * i / 4);
                double y = Y(v);
                dc.DrawLine(new Pen(grid, 1), new Point(left, y), new Point(right, y));
                DrawText(dc, v.ToString("G5", CultureInfo.CurrentCulture), new Point(4, y - 7), text, 10);
            }

            foreach (TrendSeries series in analog)
            {
                DrawSteps(dc, series, X, Y, right);
            }
        }

        for (int i = 0; i < digital.Count; i++)
        {
            double laneTop = bottom - lanes + (i * LaneHeight) + 3;
            double laneBottom = laneTop + LaneHeight - 6;
            dc.DrawLine(new Pen(grid, 1), new Point(left, laneBottom), new Point(right, laneBottom));
            DrawText(dc, Shorten(digital[i].Label, 10), new Point(4, laneTop + 2), text, 10);
            DrawSteps(dc, digital[i], X, v => v != 0 ? laneTop : laneBottom, right);
        }
    }

    private void DrawSteps(DrawingContext dc, TrendSeries series, Func<DateTimeOffset, double> x, Func<double, double> y, double right)
    {
        if (series.Points.Count == 0)
        {
            return;
        }

        var pen = new Pen(Colour(series.ColourIndex), 1.6);
        pen.Freeze();
        var geometry = new StreamGeometry();
        using (StreamGeometryContext g = geometry.Open())
        {
            double startX = Math.Max(x(series.Points[0].Utc), x(From));
            g.BeginFigure(new Point(startX, y(series.Points[0].Value)), false, false);
            for (int i = 1; i < series.Points.Count; i++)
            {
                double px = Math.Max(x(series.Points[i].Utc), startX);
                g.LineTo(new Point(px, y(series.Points[i - 1].Value)), true, false);
                g.LineTo(new Point(px, y(series.Points[i].Value)), true, false);
            }

            // Hold the last value to the right edge: no sample since means no change since.
            g.LineTo(new Point(right, y(series.Points[^1].Value)), true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private void DrawLegend(DrawingContext dc, List<TrendSeries> all, Brush text)
    {
        double column = Math.Max(160, (ActualWidth - AxisWidth) / 3);
        for (int i = 0; i < all.Count; i++)
        {
            double x = AxisWidth + ((i % 3) * column);
            double y = 4 + ((i / 3) * LegendRow);
            dc.DrawRectangle(Colour(all[i].ColourIndex), null, new Rect(x, y + 5, 12, 3));
            string last = all[i].Last is { } v
                ? " = " + (all[i].IsDigital ? (v != 0 ? "ON" : "OFF") : v.ToString("G6", CultureInfo.CurrentCulture))
                : string.Empty;
            DrawText(dc, Shorten(all[i].Label + last, 34), new Point(x + 16, y), text, 11);
        }
    }

    private Brush Colour(int index) => Find(Palette[index % Palette.Length]);

    private Brush Find(string token) => TryFindResource(token) as Brush ?? SystemColors.ControlTextBrush;

    private void DrawText(DrawingContext dc, string value, Point at, Brush brush, double size)
    {
        var formatted = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, at);
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static void OnSeriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (TrendChart)d;
        if (e.OldValue is INotifyCollectionChanged old)
        {
            old.CollectionChanged -= chart.OnCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged now)
        {
            now.CollectionChanged += chart.OnCollectionChanged;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
