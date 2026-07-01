using System.IO;

namespace Wstat.Desktop.Common;

internal static class LogWriter
{
    private static StreamWriter? _writer;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.LogPath)!);
        var stream = new FileStream(AppPaths.LogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream) { AutoFlush = true };
        _initialized = true;
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
