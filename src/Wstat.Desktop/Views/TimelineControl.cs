using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WinSize = System.Windows.Size;
using Wstat.Desktop.Common;

namespace Wstat.Desktop.Views;

public class TimelineControl : Canvas
{
    private const double TopMargin = 32;
    private const double RowHeight = 34;
    private const double BarHeight = 26;
    private const double DragThreshold = 4;

    private static readonly Typeface _typeface = new("Segoe UI");
    private static readonly MediaPen _markerPen;
    private static readonly MediaPen _minorPen;
    private static readonly MediaPen _daySepPen;
    private static readonly MediaPen _nowLinePen;
    private static readonly MediaPen _selectRectPen;
    private static readonly MediaBrush _textBrush;
    private static readonly MediaBrush _zebraBrush;
    private static readonly MediaBrush _selectFillBrush;
    private static readonly MediaBrush _selectOverlayBrush;

    static TimelineControl()
    {
        _markerPen = new MediaPen(BrushCache.Get(MediaColor.FromRgb(0xDD, 0xDD, 0xDD)), 0.5);
        _markerPen.Freeze();

        _minorPen = new MediaPen(BrushCache.Get(MediaColor.FromRgb(0xEE, 0xEE, 0xEE)), 0.5);
        _minorPen.Freeze();

        _daySepPen = new MediaPen(BrushCache.Get(MediaColor.FromRgb(0xBB, 0xBB, 0xBB)), 1);
        _daySepPen.Freeze();

        _nowLinePen = new MediaPen(BrushCache.Get(MediaColor.FromArgb(0xCC, 0xE5, 0x39, 0x35)), 1.5);
        _nowLinePen.Freeze();

        _textBrush = BrushCache.Get(MediaColor.FromRgb(0x66, 0x66, 0x66));
        _zebraBrush = BrushCache.Get(MediaColor.FromArgb(0x08, 0x00, 0x00, 0x00));

        _selectFillBrush = BrushCache.Get(MediaColor.FromArgb(0x20, 0x42, 0x85, 0xF4));
        _selectOverlayBrush = BrushCache.Get(MediaColor.FromArgb(0x50, 0x42, 0x85, 0xF4));

        var selectPen = new MediaPen(BrushCache.Get(MediaColor.FromArgb(0x80, 0x42, 0x85, 0xF4)), 1);
        selectPen.Freeze();
        _selectRectPen = selectPen;
    }

    private List<Models.TimelineEntry> _entries = [];
    private List<IGrouping<string, Models.TimelineEntry>> _groups = [];
    private double _hourWidth = 60;
    private DateTime _overallStart;
    private double _totalHours = 24;
    private List<(Rect Bounds, Models.TimelineEntry Entry)> _entryRects = [];

    private readonly HashSet<Models.TimelineEntry> _selectedEntries = [];
    private bool _isDragging;
    private System.Windows.Point _dragStartPoint;
    private System.Windows.Point _dragEndPoint;
    private bool _hasMovedPastThreshold;

    public bool IsDragging => _isDragging;

    public IReadOnlyCollection<Models.TimelineEntry> SelectedEntries => _selectedEntries;

    public bool HasSelection => _selectedEntries.Count > 0;

    public event Action? SelectionChanged;

    public Models.TimelineEntry? GetEntryAt(System.Windows.Point pos)
    {
        foreach (var (bounds, entry) in _entryRects)
        {
            if (bounds.Contains(pos)) return entry;
        }
        return null;
    }

    public List<Models.TimelineEntry> GetEntriesInRect(System.Windows.Rect rect)
    {
        var result = new List<Models.TimelineEntry>();
        foreach (var (bounds, entry) in _entryRects)
        {
            if (bounds.IntersectsWith(rect))
                result.Add(entry);
        }
        return result;
    }

    public void ClearSelection()
    {
        if (_selectedEntries.Count == 0) return;
        _selectedEntries.Clear();
        InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    public (TimeSpan totalTime, TimeSpan activeTime) GetSelectionInfo()
    {
        if (_selectedEntries.Count == 0)
            return (TimeSpan.Zero, TimeSpan.Zero);

        var minStart = _selectedEntries.Min(e => e.StartTime);
        var maxEnd = _selectedEntries.Max(e => e.EndTime);
        var total = maxEnd - minStart;
        var active = TimeSpan.FromSeconds(_selectedEntries.Sum(e => e.DurationSeconds));
        return (total, active);
    }

    public TimelineControl()
    {
        Background = System.Windows.Media.Brushes.Transparent;
        ToolTipService.SetInitialShowDelay(this, 0);
        ToolTipService.SetShowDuration(this, 30000);
        Focusable = true;
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

        if (entries.Count > 0)
        {
            _overallStart = entries.Min(e => e.StartTime);
            var endMax = entries.Max(e => e.EndTime);
            _totalHours = Math.Max((endMax - _overallStart).TotalHours, 24);
        }
        else
        {
            _overallStart = DateTime.Today;
            _totalHours = 24;
        }

        ToolTip = null;
        _selectedEntries.Clear();
        InvalidateVisual();
        InvalidateMeasure();
    }

    protected override WinSize MeasureOverride(WinSize constraint)
    {
        var count = _groups.Count;
        var desiredW = _totalHours * _hourWidth + 16;
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
            LogWriter.Write("[TimelineControl] ERROR: " + ex);
        }
    }

    private void RenderCore(DrawingContext dc)
    {
        var totalHoursNum = Math.Max(_totalHours, 24);
        var timeWidth = Math.Max(ActualWidth, totalHoursNum * _hourWidth);
        var hourPx = timeWidth / totalHoursNum;

        if (hourPx <= 0) return;

        var labelCache = new Dictionary<string, FormattedText>();

        DrawBackground(dc, _groups.Count);
        DrawDaySeparators(dc, hourPx, totalHoursNum, labelCache);
        DrawHourMarkers(dc, hourPx, totalHoursNum, labelCache);
        DrawNowLine(dc, hourPx);

        _entryRects.Clear();
        var row = 0;
        foreach (var group in _groups)
        {
            var y = TopMargin + row * RowHeight;
            var barY = y + (RowHeight - BarHeight) / 2;

            foreach (var entry in group)
            {
                var startOff = (entry.StartTime - _overallStart).TotalHours;
                var endOff = (entry.EndTime - _overallStart).TotalHours;

                if (endOff < 0 || startOff > totalHoursNum) continue;

                if (startOff < 0) startOff = 0;
                if (endOff > totalHoursNum) endOff = totalHoursNum;

                var left = startOff * hourPx;
                var width = Math.Max((endOff - startOff) * hourPx, 2);

                if (width <= 0) continue;

                var barRect = new System.Windows.Rect(left, barY, width, BarHeight);
                dc.DrawRectangle(BrushCache.Get(entry.TitleColor), null, barRect);
                _entryRects.Add((barRect, entry));

                if (_selectedEntries.Contains(entry))
                {
                    dc.DrawRectangle(_selectOverlayBrush, null, barRect);
                }

                if (width > 20)
                    DrawWindowTitle(dc, entry, left, barY, width);
            }

            row++;
        }

        if (_isDragging && _hasMovedPastThreshold)
        {
            DrawSelectionRect(dc);
        }
    }

    private void DrawSelectionRect(DrawingContext dc)
    {
        var x = Math.Min(_dragStartPoint.X, _dragEndPoint.X);
        var y = Math.Min(_dragStartPoint.Y, _dragEndPoint.Y);
        var w = Math.Abs(_dragEndPoint.X - _dragStartPoint.X);
        var h = Math.Abs(_dragEndPoint.Y - _dragStartPoint.Y);

        if (w < 1 && h < 1) return;

        dc.DrawRectangle(_selectFillBrush, _selectRectPen, new System.Windows.Rect(x, y, w, h));
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        CaptureMouse();

        var pos = e.GetPosition(this);
        _dragStartPoint = pos;
        _dragEndPoint = pos;
        _isDragging = true;
        _hasMovedPastThreshold = false;

        var clickedEntry = GetEntryAt(pos);
        var isCtrl = Keyboard.Modifiers == ModifierKeys.Control;

        if (isCtrl && clickedEntry != null)
        {
            if (!_selectedEntries.Remove(clickedEntry))
                _selectedEntries.Add(clickedEntry);
            SelectionChanged?.Invoke();
        }
        else if (clickedEntry != null)
        {
            if (_selectedEntries.Count != 1 || !_selectedEntries.Contains(clickedEntry))
            {
                _selectedEntries.Clear();
                _selectedEntries.Add(clickedEntry);
                SelectionChanged?.Invoke();
            }
        }
        else if (!isCtrl)
        {
            if (_selectedEntries.Count > 0)
            {
                _selectedEntries.Clear();
                SelectionChanged?.Invoke();
            }
        }

        InvalidateVisual();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isDragging) return;

        var pos = e.GetPosition(this);
        _dragEndPoint = pos;

        var dx = _dragEndPoint.X - _dragStartPoint.X;
        var dy = _dragEndPoint.Y - _dragStartPoint.Y;
        var distSq = dx * dx + dy * dy;

        if (!_hasMovedPastThreshold && distSq > DragThreshold * DragThreshold)
        {
            _hasMovedPastThreshold = true;
        }

        if (_hasMovedPastThreshold)
        {
            UpdateRubberBandSelection();
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        _isDragging = false;
        _hasMovedPastThreshold = false;
        InvalidateVisual();
    }

    protected override void OnMouseRightButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        Focus();

        var pos = e.GetPosition(this);
        var clickedEntry = GetEntryAt(pos);

        if (clickedEntry != null && !_selectedEntries.Contains(clickedEntry))
        {
            _selectedEntries.Clear();
            _selectedEntries.Add(clickedEntry);
            SelectionChanged?.Invoke();
            InvalidateVisual();
        }
    }

    private void UpdateRubberBandSelection()
    {
        var x = Math.Min(_dragStartPoint.X, _dragEndPoint.X);
        var y = Math.Min(_dragStartPoint.Y, _dragEndPoint.Y);
        var w = Math.Abs(_dragEndPoint.X - _dragStartPoint.X);
        var h = Math.Abs(_dragEndPoint.Y - _dragStartPoint.Y);

        var rect = new System.Windows.Rect(x, y, w, h);
        var isCtrl = Keyboard.Modifiers == ModifierKeys.Control;
        var intersecting = GetEntriesInRect(rect);

        if (isCtrl)
        {
            foreach (var entry in intersecting)
                _selectedEntries.Add(entry);
        }
        else
        {
            if (_selectedEntries.Count != intersecting.Count ||
                !intersecting.All(e => _selectedEntries.Contains(e)))
            {
                _selectedEntries.Clear();
                foreach (var entry in intersecting)
                    _selectedEntries.Add(entry);
            }
        }

        SelectionChanged?.Invoke();
    }

    private void DrawBackground(DrawingContext dc, int rowCount)
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

    private void DrawDaySeparators(DrawingContext dc, double hourPx, double totalHoursNum, Dictionary<string, FormattedText> cache)
    {
        if (_totalHours <= 24) return;

        var firstMidnight = (_overallStart.Date.AddDays(1) - _overallStart).TotalHours;
        if (firstMidnight <= 0 || firstMidnight >= _totalHours) return;

        var bottom = ActualHeight > 0 ? ActualHeight : TopMargin + 8;
        var dayIndex = 1;

        for (var h = firstMidnight; h < _totalHours; h += 24)
        {
            var x = h * hourPx;
            dc.DrawLine(_daySepPen, new System.Windows.Point(x, TopMargin - 4), new System.Windows.Point(x, bottom));

            var dayDate = _overallStart.Date.AddDays(dayIndex);
            var key = "_day_" + dayDate.ToString("yyyyMMdd");
            if (!cache.TryGetValue(key, out var ft))
            {
                var label = dayDate.ToString("ddd M/d");
                ft = new FormattedText(label, CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight, _typeface, 9, _textBrush, 1.25);
                cache[key] = ft;
            }
            dc.DrawText(ft, new System.Windows.Point(x + 3, TopMargin - 16));

            dayIndex++;
        }
    }

    private void DrawHourMarkers(DrawingContext dc, double hourPx, double totalHoursNum, Dictionary<string, FormattedText> cache)
    {
        var bottom = ActualHeight > 0 ? ActualHeight : TopMargin + 8;

        double majorInterval;
        double minorInterval;

        if (hourPx > 80)
        {
            majorInterval = 1;
            minorInterval = _totalHours > 48 ? 0 : 0.25;
        }
        else if (hourPx > 40)
        {
            majorInterval = 2;
            minorInterval = _totalHours > 48 ? 0 : 0.5;
        }
        else if (hourPx > 20)
        {
            majorInterval = 2;
            minorInterval = _totalHours > 48 ? 0 : 1;
        }
        else
        {
            majorInterval = 2;
            minorInterval = 0;
        }

        if (minorInterval > 0)
        {
            for (double m = 0; m <= totalHoursNum; m += minorInterval)
            {
                var mRounded = Math.Round(m / minorInterval) * minorInterval;
                if (Math.Abs(mRounded % majorInterval) < 0.01) continue;

                var mx = mRounded * hourPx;
                dc.DrawLine(_minorPen, new System.Windows.Point(mx, TopMargin - 4), new System.Windows.Point(mx, bottom));
            }
        }

        for (double h = 0; h <= totalHoursNum; h += majorInterval)
        {
            var hRounded = Math.Round(h / majorInterval) * majorInterval;
            var x = hRounded * hourPx;
            dc.DrawLine(_markerPen, new System.Windows.Point(x, TopMargin - 4), new System.Windows.Point(x, bottom));

            var totalHoursInt = (int)Math.Round(hRounded);

            string key;
            if (totalHoursNum > 24 && totalHoursInt % 24 == 0)
            {
                var dayDate = _overallStart.Date.AddDays(totalHoursInt / 24);
                key = "_daylabel_" + dayDate.ToString("yyyyMMdd");
            }
            else
            {
                key = "_hour_" + (totalHoursInt % 24);
            }

            if (!cache.TryGetValue(key, out var ft))
            {
                string label;
                if (totalHoursNum > 24 && totalHoursInt % 24 == 0)
                {
                    var dayDate = _overallStart.Date.AddDays(totalHoursInt / 24);
                    label = dayDate.ToString("ddd M/d");
                }
                else
                {
                    var hh = totalHoursInt % 24;
                    label = $"{hh:D2}:00";
                }

                ft = new FormattedText(label, CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight, _typeface, 10, _textBrush, 1.25);
                cache[key] = ft;
            }

            dc.DrawText(ft, new System.Windows.Point(x - ft.Width / 2, 4));
        }
    }

    private void DrawNowLine(DrawingContext dc, double hourPx)
    {
        var now = DateTime.Now;
        if (now < _overallStart || now > _overallStart + TimeSpan.FromHours(_totalHours)) return;

        var x = (now - _overallStart).TotalHours * hourPx;
        if (x < 0) return;

        var totalHoursNum = Math.Max(_totalHours, 24);
        if (x > totalHoursNum * hourPx) return;

        var bottom = ActualHeight > 0 ? ActualHeight : TopMargin + 8;
        dc.DrawLine(_nowLinePen, new System.Windows.Point(x, TopMargin - 4), new System.Windows.Point(x, bottom));
    }

    private static void DrawWindowTitle(DrawingContext dc, Models.TimelineEntry entry, double left, double barY, double barWidth)
    {
        var title = entry.WindowTitle;
        if (string.IsNullOrEmpty(title)) return;

        var fontSize = Math.Min(11, Math.Max(7, barWidth / (title.Length * 0.6)));
        if (fontSize < 7) return;

        var textBrush = BrushCache.Get(Colors.White);
        var availableWidth = barWidth - 6;

        if (availableWidth <= 0) return;

        var ft = TextHelper.BuildTruncatedText(title, availableWidth, _typeface, fontSize, textBrush, 1.25);

        if (ft.Width > availableWidth) return;

        dc.DrawText(ft, new System.Windows.Point(left + 3, barY + (BarHeight - ft.Height) / 2));
    }
}
