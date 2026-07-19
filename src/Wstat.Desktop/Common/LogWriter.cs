using System.IO;

namespace Wstat.Desktop.Common;

internal static class LogWriter
{
    private const long MaxFileSize = 10 * 1024 * 1024;
    private const int MaxBackupFiles = 3;

    private static StreamWriter? _writer;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.LogPath)!);

        try
        {
            var logFile = new FileInfo(AppPaths.LogPath);
            if (logFile.Exists && logFile.Length >= MaxFileSize)
                RotateLogs();
        }
        catch { }

        var stream = new FileStream(AppPaths.LogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream) { AutoFlush = true };
        _initialized = true;
    }

    private static void RotateLogs()
    {
        for (int i = MaxBackupFiles - 1; i >= 1; i--)
        {
            var oldPath = AppPaths.LogPath + "." + i;
            var newPath = AppPaths.LogPath + "." + (i + 1);
            if (File.Exists(oldPath))
            {
                if (File.Exists(newPath))
                    File.Delete(newPath);
                File.Move(oldPath, newPath);
            }
        }

        if (File.Exists(AppPaths.LogPath + ".1"))
            File.Delete(AppPaths.LogPath + ".1");
        File.Move(AppPaths.LogPath, AppPaths.LogPath + ".1");
    }

    public static void Write(string message)
    {
        _writer?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
    }

    public static void Shutdown()
    {
        if (!_initialized) return;
        _writer?.Dispose();
        _writer = null;
        _initialized = false;
    }
}
