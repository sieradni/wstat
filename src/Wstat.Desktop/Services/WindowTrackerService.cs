using System.Diagnostics;
using System.IO;
using System.Timers;
using Wstat.Desktop.Models;
using Wstat.Desktop.Native;
using Timer = System.Timers.Timer;

namespace Wstat.Desktop.Services;

public class WindowTrackerService : IDisposable
{
    private const int PollIntervalMs = 2000;
    private const int IdleThresholdMs = 5 * 60 * 1000;

    private readonly DatabaseService _db;
    private readonly Timer _timer;

    private ActivityRecord? _currentRecord;
    private string? _lastProcessPath;
    private string? _lastWindowTitle;
    private bool _wasIdle;
    private bool _disposed;

    private string? _latestBrowserUrl;
    private string? _latestBrowserTitle;

    public event Action<ActivityRecord>? RecordUpdated;
    public event Action<bool>? IdleStateChanged;

    public ActivityRecord? CurrentRecord => _currentRecord;
    public bool IsIdle { get; private set; }

    public WindowTrackerService(DatabaseService db)
    {
        _db = db;
        _timer = new Timer(PollIntervalMs);
        _timer.Elapsed += OnTimerElapsed;
    }

    public void Start()
    {
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        CloseCurrentRecord();
    }

    private static readonly string LogPath = CreateLogPath();

    private static string CreateLogPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "wstat");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "trace.log");
    }

    public void SetBrowserTab(string url, string title)
    {
        _latestBrowserUrl = url;
        _latestBrowserTitle = title;

        // Only store http/https URLs
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_currentRecord != null && !_wasIdle &&
            string.Equals(_currentRecord.AppName, "firefox.exe", StringComparison.OrdinalIgnoreCase))
        {
            _currentRecord.BrowserUrl = url;
            _currentRecord.WindowTitle = title;
            if (_currentRecord.Id != 0)
            {
                _db.UpdateBrowserUrl(_currentRecord.Id, url);
            }
            RecordUpdated?.Invoke(_currentRecord);
            File.AppendAllText(LogPath, $"{DateTime.Now:O} STORED: {title} ({url})\n");
        }
        else
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:O} SKIPPED: curr={_currentRecord?.AppName}, idle={_wasIdle}, url={url}\n");
        }
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_disposed) return;

        try
        {
            var hWnd = Win32Api.GetForegroundWindow();
            var processPath = Win32Api.GetForegroundProcessPath();
            var windowTitle = Win32Api.GetForegroundWindowTitle();
            var lastInputTick = Win32Api.GetLastInputTick();
            var nowTick = (uint)Environment.TickCount;
            var idleDuration = nowTick - lastInputTick;
            var isIdle = idleDuration > IdleThresholdMs;

            if (hWnd == IntPtr.Zero || string.IsNullOrEmpty(processPath))
            {
                if (_currentRecord != null)
                    CloseCurrentRecord();
                return;
            }

            var appName = Path.GetFileName(processPath);

            if (isIdle != _wasIdle)
            {
                if (isIdle)
                {
                    CloseCurrentRecord();
                    IsIdle = true;
                    _wasIdle = true;
                    IdleStateChanged?.Invoke(true);
                }
                else
                {
                    IsIdle = false;
                    _wasIdle = false;
                    IdleStateChanged?.Invoke(false);
                    StartNewRecord(processPath, windowTitle ?? "", appName);
                }
                return;
            }

            if (isIdle) return;

            var hasChanged = processPath != _lastProcessPath ||
                             (windowTitle ?? "") != _lastWindowTitle;

            if (hasChanged)
            {
                CloseCurrentRecord();
                StartNewRecord(processPath, windowTitle ?? "", appName);
            }
            else if (_currentRecord != null)
            {
                _db.InsertOrUpdateActive(_currentRecord);
                RecordUpdated?.Invoke(_currentRecord);
            }

            _lastProcessPath = processPath;
            _lastWindowTitle = windowTitle;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowTracker] Error: {ex.Message}");
        }
    }

    private void StartNewRecord(string processPath, string windowTitle, string appName)
    {
        var appNameLower = appName.ToLowerInvariant();
        var isFirefox = appNameLower == "firefox.exe";

        _currentRecord = new ActivityRecord
        {
            AppName = appName,
            WindowTitle = isFirefox && !string.IsNullOrEmpty(_latestBrowserTitle)
                ? _latestBrowserTitle
                : windowTitle,
            BrowserUrl = isFirefox ? _latestBrowserUrl : null,
            ProcessPath = processPath,
            StartTime = DateTime.Now
        };

        _db.InsertOrUpdateActive(_currentRecord);
        RecordUpdated?.Invoke(_currentRecord);
    }

    private void CloseCurrentRecord()
    {
        if (_currentRecord == null) return;
        _db.CloseActive(_currentRecord);
        RecordUpdated?.Invoke(_currentRecord);
        _currentRecord = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _timer.Dispose();
        _db.Dispose();
    }
}
