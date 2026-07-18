using System.IO;

namespace Wstat.Desktop.Common;

internal static class AppPaths
{
    private static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "wstat");

    static AppPaths()
    {
        Directory.CreateDirectory(BaseDir);
    }

    public static string DbPath => Path.Combine(BaseDir, "wstat.db");
    public static string LogPath => Path.Combine(BaseDir, "trace.log");
    public static string IconPath => Path.Combine(BaseDir, "app.ico");
    public static string SettingsPath => Path.Combine(BaseDir, "settings.json");
}
