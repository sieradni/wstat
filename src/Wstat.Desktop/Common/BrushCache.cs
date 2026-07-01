using WMedia = System.Windows.Media;

namespace Wstat.Desktop.Common;

internal static class BrushCache
{
    private static readonly Dictionary<WMedia.Color, WMedia.Brush> _cache = [];

    public static WMedia.Brush Get(WMedia.Color color)
    {
        lock (_cache)
        {
            if (!_cache.TryGetValue(color, out var brush))
            {
                brush = new WMedia.SolidColorBrush(color);
                brush.Freeze();
                _cache[color] = brush;
            }
            return brush;
        }
    }
}
