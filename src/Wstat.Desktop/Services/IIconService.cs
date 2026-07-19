using System.Windows.Media.Imaging;
using MediaColor = System.Windows.Media.Color;

namespace Wstat.Desktop.Services;

public interface IIconService
{
    BitmapSource? GetIcon(string? processPath);
    Task<BitmapSource?> GetIconAsync(string? processPath);
    MediaColor GetOrAssignAppColor(string appName);
}
