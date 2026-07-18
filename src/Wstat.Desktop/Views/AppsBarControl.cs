using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WMedia = System.Windows.Media;
using WinSize = System.Windows.Size;
using Wstat.Desktop.Common;

namespace Wstat.Desktop.Views;

public class AppsBarControl : Canvas
{
    private const double RowHeight = 30;
    private const double BarHeight = 20;
    private const double IconSize = 16;
    private const double IconArea = 24;
    private const double NameAreaRatio = 0.32;
    private const double RightMargin = 80;

    private static readonly WMedia.Typeface _typeface = new("Segoe UI");
    private static readonly WMedia.Brush _barBrush;
    private static readonly WMedia.Brush _textBrush;
    private static readonly WMedia.Brush _valueBrush;
    private static readonly WMedia.Brush _zebraBrush;

    static AppsBarControl()
    {
        _barBrush = BrushCache.Get(WMedia.Color.FromRgb(0x42, 0x85, 0xF4));
        _textBrush = BrushCache.Get(WMedia.Color.FromRgb(0x33, 0x33, 0x33));
        _valueBrush = BrushCache.Get(WMedia.Color.FromRgb(0x66, 0x66, 0x66));
        _zebraBrush = BrushCache.Get(WMedia.Color.FromArgb(0x08, 0x00, 0x00, 0x00));
    }

    private List<Models.AppSummary> _apps = [];
    private long _maxSeconds = 1;

    public void Render(List<Models.AppSummary> apps)
    {
        _apps = apps;
        _maxSeconds = apps.Count > 0 ? apps.Max(a => Math.Max(a.TotalSeconds, 1)) : 1;
        InvalidateVisual();
        InvalidateMeasure();
    }

    protected override WinSize MeasureOverride(WinSize constraint)
    {
        return new WinSize(constraint.Width, _apps.Count * RowHeight + 8);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        try { RenderCore(dc); }
        catch (Exception ex) { LogWriter.Write("[AppsBar] Render error: " + ex.Message); }
    }

    private void RenderCore(DrawingContext dc)
    {
        var nameAreaWidth = ActualWidth * NameAreaRatio;
        var barAreaWidth = Math.Max(ActualWidth - IconArea - nameAreaWidth - RightMargin, 40);

        for (int i = 0; i < _apps.Count; i++)
        {
            var app = _apps[i];
            var y = 4 + i * RowHeight;

            if (i % 2 == 1)
                dc.DrawRectangle(_zebraBrush, null, new Rect(0, y, ActualWidth, RowHeight));

            var iconY = y + (RowHeight - IconSize) / 2;
            if (app.Icon != null)
                dc.DrawImage(app.Icon, new Rect(4, iconY, IconSize, IconSize));

            var name = Path.GetFileNameWithoutExtension(app.AppName);
            if (string.IsNullOrEmpty(name)) name = app.AppName;

            var labelFt = TextHelper.BuildTruncatedText(name, nameAreaWidth - 4, _typeface, 11, _textBrush, 1.25);
            dc.DrawText(labelFt, new System.Windows.Point(IconArea + 2, y + (RowHeight - labelFt.Height) / 2));

            var fraction = (double)app.TotalSeconds / _maxSeconds;
            var barWidth = fraction * barAreaWidth;
            if (barWidth > 2)
            {
                var barX = IconArea + nameAreaWidth;
                var barY = y + (RowHeight - BarHeight) / 2;
                dc.DrawRectangle(_barBrush, null, new Rect(barX, barY, barWidth, BarHeight));

                var valueFt = new WMedia.FormattedText(
                    app.DisplayTime,
                    System.Globalization.CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    _typeface, 10, _valueBrush, 1.25);
                dc.DrawText(valueFt, new System.Windows.Point(barX + barWidth + 6, y + (RowHeight - valueFt.Height) / 2));
            }
        }
    }
}
