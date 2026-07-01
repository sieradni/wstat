using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using WinSize = System.Windows.Size;

namespace Wstat.Desktop.Views;

public class TimelineLabelsControl : Canvas
{
    private const double TopMargin = 32;
    private const double RowHeight = 34;
    private const double LabelWidth = 140;

    private static readonly Dictionary<MediaColor, MediaBrush> _brushCache = [];
    private static readonly Typeface _typeface = new("Segoe UI");
    private static readonly MediaBrush _textBrush;
    private static readonly MediaBrush _zebraBrush;

    static TimelineLabelsControl()
    {
        _textBrush = GetCachedBrush(MediaColor.FromRgb(0x33, 0x33, 0x33));
        _zebraBrush = GetCachedBrush(MediaColor.FromArgb(0x08, 0x00, 0x00, 0x00));
    }

    private List<Models.TimelineEntry> _entries = [];
    private List<IGrouping<string, Models.TimelineEntry>> _groups = [];

    public void Render(List<Models.TimelineEntry> entries)
    {
        _entries = entries;
        _groups = entries.GroupBy(e => e.AppName).ToList();
        InvalidateVisual();
        InvalidateMeasure();
    }

    protected override WinSize MeasureOverride(WinSize constraint)
    {
        var count = _groups.Count;
        var h = TopMargin + count * RowHeight + 8;
        return new WinSize(LabelWidth, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        try { RenderCore(dc); } catch { }
    }

    private void RenderCore(DrawingContext dc)
    {
        var row = 0;
        foreach (var group in _groups)
        {
            var y = TopMargin + row * RowHeight;

            if (row % 2 == 1)
            {
                dc.DrawRectangle(_zebraBrush,
                    null, new System.Windows.Rect(0, y, LabelWidth, RowHeight));
            }

            DrawAppLabel(dc, group.Key, y);
            row++;
        }
    }

    private void DrawAppLabel(DrawingContext dc, string appName, double y)
    {
        var name = appName;

        var ft = new FormattedText(name, System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight, _typeface, 11, _textBrush, 1.25);

        var textX = 4.0;
        var textY = y + (RowHeight - ft.Height) / 2;

        try
        {
            var procPath = _entries
                .Where(e => string.Equals(e.AppName, appName, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.ProcessPath)
                .FirstOrDefault(p => !string.IsNullOrEmpty(p) && System.IO.File.Exists(p));

            if (procPath != null && ViewModels.DashboardViewModel.TryGetIcon(procPath, out var icon))
            {
                dc.DrawImage(icon, new System.Windows.Rect(4, y + (RowHeight - 16) / 2, 16, 16));
                textX = 24;
            }
        }
        catch { }

        if (ft.Width > LabelWidth - textX - 4)
        {
            var ellipsis = "\u2026";
            var dotWidth = new FormattedText(ellipsis, System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight, _typeface, 11, _textBrush, 1.25).Width;
            var maxText = LabelWidth - textX - 4 - dotWidth;

            int lo = 0, hi = name.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                var test = new FormattedText(name[..mid], System.Globalization.CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight, _typeface, 11, _textBrush, 1.25);
                if (test.Width <= maxText)
                    lo = mid;
                else
                    hi = mid - 1;
            }

            if (lo > 0)
                ft = new FormattedText(name[..lo] + ellipsis, System.Globalization.CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight, _typeface, 11, _textBrush, 1.25);
        }

        dc.DrawText(ft, new System.Windows.Point(textX, textY));
    }

    private static MediaBrush GetCachedBrush(MediaColor color)
    {
        lock (_brushCache)
        {
            if (!_brushCache.TryGetValue(color, out var brush))
            {
                brush = new SolidColorBrush(color);
                brush.Freeze();
                _brushCache[color] = brush;
            }
            return brush;
        }
    }
}
