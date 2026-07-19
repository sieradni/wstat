using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using WinSize = System.Windows.Size;
using Microsoft.Extensions.DependencyInjection;
using Wstat.Desktop.Common;
using Wstat.Desktop.Services;

namespace Wstat.Desktop.Views;

public class TimelineLabelsControl : Canvas
{
    private const double TopMargin = 32;
    private const double RowHeight = 34;
    private const double LabelWidth = 140;

    private static readonly Typeface _typeface = new("Segoe UI");
    private static readonly MediaBrush _textBrush;
    private static readonly MediaBrush _zebraBrush;

    static TimelineLabelsControl()
    {
        _textBrush = BrushCache.Get(MediaColor.FromRgb(0x33, 0x33, 0x33));
        _zebraBrush = BrushCache.Get(MediaColor.FromArgb(0x08, 0x00, 0x00, 0x00));
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
        try { RenderCore(dc); }
        catch (Exception ex) { LogWriter.Write("[TimelineLabels] Render error: " + ex.Message); }
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

            DrawAppLabel(dc, group.Key, y, group.ToList());
            row++;
        }
    }

    private void DrawAppLabel(DrawingContext dc, string appName, double y, List<Models.TimelineEntry> groupEntries)
    {
        var name = appName;
        var textX = 4.0;

        try
        {
            var procPath = groupEntries
                .Select(e => e.ProcessPath)
                .FirstOrDefault(p => !string.IsNullOrEmpty(p) && System.IO.File.Exists(p));

            if (procPath != null)
            {
                var iconService = App.ServiceProvider?.GetService(typeof(IIconService)) as IIconService;
                var icon = iconService?.GetIcon(procPath);
                if (icon != null)
                {
                    dc.DrawImage(icon, new System.Windows.Rect(4, y + (RowHeight - 16) / 2, 16, 16));
                    textX = 24;
                }
            }
        }
        catch (Exception ex) { LogWriter.Write("[TimelineLabels] Icon error: " + ex.Message); }

        var availableWidth = LabelWidth - textX - 4;
        if (availableWidth <= 0) return;

        var ft = TextHelper.BuildTruncatedText(name, availableWidth, _typeface, 11, _textBrush, 1.25);
        var textY = y + (RowHeight - ft.Height) / 2;

        dc.DrawText(ft, new System.Windows.Point(textX, textY));
    }
}
