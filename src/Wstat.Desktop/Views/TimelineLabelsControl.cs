using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using WinSize = System.Windows.Size;

namespace Wstat.Desktop.Views;

public class TimelineLabelsControl : Canvas
{
    private const double TopMargin = 32;
    private const double RowHeight = 34;
    private const double LabelWidth = 140;

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
                dc.DrawRectangle(new SolidColorBrush(MediaColor.FromArgb(0x08, 0x00, 0x00, 0x00)),
                    null, new System.Windows.Rect(0, y, LabelWidth, RowHeight));
            }

            DrawAppLabel(dc, group.Key, y);
            row++;
        }
    }

    private void DrawAppLabel(DrawingContext dc, string appName, double y)
    {
        var typeface = new Typeface("Segoe UI");
        var textBrush = new SolidColorBrush(MediaColor.FromRgb(0x33, 0x33, 0x33));

        var name = appName;

        var ft = new FormattedText(name, System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight, typeface, 11, textBrush, 1.25);

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
            var dotFmt = new FormattedText("...", System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight, typeface, 11, textBrush, 1.25);
            var maxText = LabelWidth - textX - 4 - dotFmt.Width;
            for (int i = name.Length - 1; i > 0; i--)
            {
                var test = new FormattedText(name[..i], System.Globalization.CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight, typeface, 11, textBrush, 1.25);
                if (test.Width <= maxText)
                {
                    ft = new FormattedText(name[..i] + "...", System.Globalization.CultureInfo.CurrentCulture,
                        System.Windows.FlowDirection.LeftToRight, typeface, 11, textBrush, 1.25);
                    break;
                }
            }
        }

        dc.DrawText(ft, new System.Windows.Point(textX, textY));
    }
}
