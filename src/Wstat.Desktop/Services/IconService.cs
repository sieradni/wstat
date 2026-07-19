using System.IO;
using System.Windows.Media.Imaging;
using Wstat.Desktop.Common;
using MediaColor = System.Windows.Media.Color;

namespace Wstat.Desktop.Services;

public class IconService : IIconService
{
    private sealed class IconCacheEntry
    {
        public BitmapSource? Icon;
        public LinkedListNode<string>? Node;
    }

    private readonly Dictionary<string, IconCacheEntry> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _iconAccessOrder = [];
    private const int MaxIconCacheSize = 64;

    private readonly Dictionary<string, MediaColor> _appColorCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly MediaColor[] TimelineColors =
    [
        MediaColor.FromRgb(0x42, 0x85, 0xF4),
        MediaColor.FromRgb(0xEA, 0x43, 0x35),
        MediaColor.FromRgb(0x34, 0xA8, 0x53),
        MediaColor.FromRgb(0xFB, 0xBC, 0x04),
        MediaColor.FromRgb(0xAB, 0x47, 0xBC),
        MediaColor.FromRgb(0x00, 0x96, 0x88),
        MediaColor.FromRgb(0xFF, 0x6F, 0x00),
        MediaColor.FromRgb(0x8E, 0x24, 0xAA),
        MediaColor.FromRgb(0x00, 0x89, 0x4B),
        MediaColor.FromRgb(0xE9, 0x1E, 0x63),
        MediaColor.FromRgb(0x00, 0x76, 0xD4),
        MediaColor.FromRgb(0x6D, 0x4C, 0x41),
    ];

    public Task<BitmapSource?> GetIconAsync(string? processPath)
    {
        return Task.Run(() => GetIcon(processPath));
    }

    public BitmapSource? GetIcon(string? processPath)
    {
        if (string.IsNullOrEmpty(processPath) || !File.Exists(processPath))
            return null;

        if (_iconCache.TryGetValue(processPath, out var entry) && entry.Icon != null)
        {
            TouchEntry(entry, processPath);
            return entry.Icon;
        }

        try
        {
            using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
            if (sysIcon == null) return null;

            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                sysIcon.Handle,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            source.Freeze();

            if (_iconCache.Count >= MaxIconCacheSize)
            {
                var oldestNode = _iconAccessOrder.First;
                if (oldestNode != null)
                {
                    _iconCache.Remove(oldestNode.Value);
                    _iconAccessOrder.RemoveFirst();
                }
            }

            var newEntry = new IconCacheEntry { Icon = source };
            newEntry.Node = _iconAccessOrder.AddLast(processPath);
            _iconCache[processPath] = newEntry;
            return source;
        }
        catch (Exception ex)
        {
            LogWriter.Write("[IconService] Extract error for " + processPath + ": " + ex.Message);

            if (!_iconCache.TryGetValue(processPath, out var failedEntry))
            {
                failedEntry = new IconCacheEntry();
                failedEntry.Node = _iconAccessOrder.AddLast(processPath);
                _iconCache[processPath] = failedEntry;
            }

            return null;
        }
    }

    public MediaColor GetOrAssignAppColor(string appName)
    {
        if (_appColorCache.TryGetValue(appName, out var color))
            return color;

        color = TimelineColors[_appColorCache.Count % TimelineColors.Length];
        _appColorCache[appName] = color;
        return color;
    }

    private void TouchEntry(IconCacheEntry entry, string processPath)
    {
        if (entry.Node != null)
            _iconAccessOrder.Remove(entry.Node);
        entry.Node = _iconAccessOrder.AddLast(processPath);
    }
}
