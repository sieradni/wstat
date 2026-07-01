using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WinSize = System.Windows.Size;

namespace Wstat.Desktop.Views;

public class TimelineControl : Canvas
{
    private const double TopMargin = 32;
    private const double RowHeight = 34;
    private const double BarHeight = 26;

    private static readonly Dictionary<MediaColor, MediaBrush> _brushCache = [];
    private static readonly Typeface _typeface = new("Segoe UI");
    private static readonly MediaPen _markerPen;
    private static readonly MediaPen _nowLinePen;
    private static readonly MediaBrush _textBrush;
    private static readonly MediaBrush _zebraBrush;

    static TimelineControl()
    {
        _markerPen = new MediaPen(GetCachedBrush(MediaColor.FromRgb(0xDD, 0xDD, 0xDD)), 0.5);
        _markerPen.Freeze();

        _nowLinePen = new MediaPen(GetCachedBrush(MediaColor.FromArgb(0xCC, 0xE5, 0x39, 0x35)), 1.5);
        _nowLinePen.Freeze();

        _textBrush = GetCachedBrush(MediaColor.FromRgb(0x66, 0x66, 0x66));
        _zebraBrush = GetCachedBrush(MediaColor.FromArgb(0x08, 0x00, 0x00, 0x00));
    }

    private List<Models.TimelineEntry> _entries = [];
    private List<IGrouping<string, Models.TimelineEntry>> _groups = [];
    private double _hourWidth = 60;
    private DateTime? _firstDate;
    private List<(Rect Bounds, Models.TimelineEntry Entry)> _entryRects = [];

    public Models.TimelineEntry? GetEntryAt(System.Windows.Point pos)
    {
        foreach (var (bounds, entry) in _entryRects)
        {
            if (bounds.Contains(pos)) return entry;
        }
        return null;
    }

    public TimelineControl()
    {
        Background = System.Windows.Media.Brushes.Transparent;
        ToolTipService.SetInitialShowDelay(this, 0);
        ToolTipService.SetShowDuration(this, 30000);
    }

    public double HourWidth
    {
        get => _hourWidth;
        set
        {
            if (Math.Abs(_hourWidth - value) > 0.5)
            {
                _hourWidth = value;
                InvalidateVisual();
                InvalidateMeasure();
            }
        }
    }

    public List<IGrouping<string, Models.TimelineEntry>>? GetGroups() => _groups;

    public void Render(List<Models.TimelineEntry> entries)
    {
        _entries = entries;
        _groups = entries.GroupBy(e => e.AppName).ToList();
        _firstDate = entries.Count > 0 ? entries.Min(e => e.StartTime.Date) : null;
        ToolTip = null;
        InvalidateVisual();
        InvalidateMeasure();
    }

    protected override WinSize MeasureOverride(WinSize constraint)
    {
        var count = _groups.Count;
        var desiredW = 24 * _hourWidth + 16;
        var w = double.IsInfinity(constraint.Width) ? desiredW : Math.Max(constraint.Width, desiredW);
        var h = TopMargin + count * RowHeight + 8;
        return new WinSize(w, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        try
        {
            RenderCore(dc);
        }
        catch (Exception ex)
        {
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "wstat", "trace.log");
            try { System.IO.File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff} [TimelineControl] ERROR: {ex}\n"); }
            catch { }
        }
    }

    private void RenderCore(DrawingContext dc)
    {
        var timeWidth = Math.Max(ActualWidth, 24 * _hourWidth);
        var hourPx = timeWidth / 24.0;

        if (hourPx <= 0) return;

        DrawBackground(dc, _groups.Count);
        DrawHourMarkers(dc, hourPx);
        DrawNowLine(dc, hourPx);

        _entryRects.Clear();
        var row = 0;
        foreach (var group in _groups)
        {
            var y = TopMargin + row * RowHeight;
            var barY = y + (RowHeight - BarHeight) / 2;

            foreach (var entry in group)
            {
                var spanStart = entry.StartTime.TimeOfDay;
                var spanEnd = entry.EndTime.TimeOfDay;

                if (spanEnd == TimeSpan.Zero && entry.EndTime.Date > entry.StartTime.Date)
                    spanEnd = TimeSpan.FromHours(24);

                var left = spanStart.TotalHours * hourPx;
                var width = Math.Max((spanEnd - spanStart).TotalHours * hourPx, 2);

                if (left + width > 0)
                {
                    if (left < 0)
                    {
                        width -= -left;
                        left = 0;
                    }

                    if (width > 0)
                    {
                        var barRect = new System.Windows.Rect(left, barY, width, BarHeight);
                        dc.DrawRectangle(GetCachedBrush(entry.TitleColor), null, barRect);
                        _entryRects.Add((barRect, entry));
                        DrawWindowTitle(dc, entry, left, barY, width);
                    }
                }
            }

            row++;
        }
    }

    private static void DrawBackground(DrawingContext dc, int rowCount)
    {
        for (int i = 0; i < rowCount; i++)
        {
            if (i % 2 == 1)
            {
                var y = TopMargin + i * RowHeight;
                dc.DrawRectangle(_zebraBrush,
                    null, new System.Windows.Rect(0, y, 50000, RowHeight));
            }
        }
    }

    private void DrawHourMarkers(DrawingContext dc, double hourPx)
    {
        var bottom = ActualHeight > 0 ? ActualHeight : TopMargin + 8;

        for (int h = 0; h <= 24; h++)
        {
            if (h > 0 && h < 24 && h % 2 != 0) continue;

            var x = h * hourPx;
            dc.DrawLine(_markerPen, new System.Windows.Point(x, TopMargin - 4), new System.Windows.Point(x, bottom));

            if (h <= 24)
            {
                var label = h == 24 ? "24:00" : $"{h:D2}:00";
                var ft = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight, _typeface, 10, _textBrush, 1.25);
                dc.DrawText(ft, new System.Windows.Point(x - ft.Width / 2, 4));
            }
        }
    }

    private void DrawNowLine(DrawingContext dc, double hourPx)
    {
        var now = DateTime.Now;
        var todayStart = now.Date;

        if (_firstDate.HasValue && _firstDate.Value != todayStart) return;

        var x = (now - todayStart).TotalHours * hourPx;

        if (x >= 0 && x <= 24 * hourPx)
        {
            var bottom = ActualHeight > 0 ? ActualHeight : TopMargin + 8;
            dc.DrawLine(_nowLinePen, new System.Windows.Point(x, TopMargin - 4), new System.Windows.Point(x, bottom));
        }
    }

    private static void DrawWindowTitle(DrawingContext dc, Models.TimelineEntry entry, double left, double barY, double barWidth)
    {
        var title = entry.WindowTitle;
        if (string.IsNullOrEmpty(title)) return;

        var fontSize = Math.Min(11, Math.Max(7, barWidth / (title.Length * 0.6)));
        if (fontSize < 7) return;

        var textBrush = GetCachedBrush(Colors.White);
        var ft = new FormattedText(title, System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight, _typeface, fontSize, textBrush, 1.25);

        if (ft.Width > barWidth - 6)
        {
            var ellipsis = "\u2026";
            var dotWidth = new FormattedText(ellipsis, System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight, _typeface, fontSize, textBrush, 1.25).Width;
            var maxText = barWidth - 6 - dotWidth;

            int lo = 0, hi = title.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                var test = new FormattedText(title[..mid], System.Globalization.CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight, _typeface, fontSize, textBrush, 1.25);
                if (test.Width <= maxText)
                    lo = mid;
                else
                    hi = mid - 1;
            }

            if (lo > 0)
                ft = new FormattedText(title[..lo] + ellipsis, System.Globalization.CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight, _typeface, fontSize, textBrush, 1.25);
            else
                return;
        }

        dc.DrawText(ft, new System.Windows.Point(left + 3, barY + (BarHeight - ft.Height) / 2));
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
