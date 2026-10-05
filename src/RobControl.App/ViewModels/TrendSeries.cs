using RobControl.Core.Trending;

namespace RobControl.App.ViewModels;

/// <summary>
/// One line on the chart: a signal on a robot, and its points in the window. Immutable - a refresh
/// replaces it, which is what makes the chart redraw.
/// </summary>
public sealed record TrendSeries(string Label, bool IsDigital, int ColourIndex, IReadOnlyList<TrendPoint> Points)
{
    public double? Last => Points.Count == 0 ? null : Points[^1].Value;
}
