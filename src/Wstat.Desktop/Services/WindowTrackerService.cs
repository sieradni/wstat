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

    private string? _pendingProcessPath;
    private string? _pendingWindowTitle;
    private string? _pendingAppName;
    private int _pendingCount;

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

            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            if (string.IsNullOrEmpty(processPath))
            {
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
                    File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} IDLE enter\n");
                }
                else
                {
                    IsIdle = false;
                    _wasIdle = false;
                    IdleStateChanged?.Invoke(false);
                    StartNewRecord(processPath, windowTitle ?? "", appName);
                    _lastProcessPath = processPath;
                    _lastWindowTitle = windowTitle ?? "";
                    File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} IDLE exit path={appName}\n");
                }
                return;
            }

            if (isIdle) return;

            var processChanged = processPath != _lastProcessPath;

            if (processChanged)
            {
                if (_pendingProcessPath == null)
                {
                    // First sighting of a new process — start debounce
                    _pendingProcessPath = processPath;
                    _pendingWindowTitle = windowTitle ?? "";
                    _pendingAppName = appName;
                    _pendingCount = 1;
                    File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} DEBOUNCE first-sight path={appName}\n");
                }
                else if (processPath == _pendingProcessPath &&
                         _pendingCount < 2)
                {
                    // Still the same new process, count up
                    _pendingCount++;
                    File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} DEBOUNCE increment path={appName} count={_pendingCount}\n");
                }
                else if (processPath != _pendingProcessPath)
                {
                    // A third process appeared before debounce settled
                    File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} DEBOUNCE third-process pending={_pendingAppName} now={appName}\n");
                    CloseCurrentRecord();
                    _pendingProcessPath = null;
                    _pendingCount = 0;
                    StartNewRecord(processPath, windowTitle ?? "", appName);
                    _lastProcessPath = processPath;
                    _lastWindowTitle = windowTitle ?? "";
                }

                // Check if we should commit the pending switch
                if (_pendingProcessPath != null && _pendingCount >= 2)
                {
                    File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} DEBOUNCE commit path={_pendingAppName}\n");
                    CloseCurrentRecord();
                    StartNewRecord(_pendingProcessPath, _pendingWindowTitle ?? "", _pendingAppName!);
                    _lastProcessPath = _pendingProcessPath;
                    _lastWindowTitle = _pendingWindowTitle ?? "";
                    _pendingProcessPath = null;
                    _pendingCount = 0;
                }

                // If pending is still active, update its window title
                if (_pendingProcessPath != null && _currentRecord != null)
                {
                    _db.InsertOrUpdateActive(_currentRecord);
                    RecordUpdated?.Invoke(_currentRecord);
                }
            }
            else
            {
                // No process change
                if (_pendingProcessPath != null)
                {
                    // We were about to switch but the user came back — cancel
                    File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} DEBOUNCE cancel path={_pendingAppName}\n");
                    _pendingProcessPath = null;
                    _pendingCount = 0;
                }

                if (_currentRecord != null)
                {
                    var titleChanged = (windowTitle ?? "") != _lastWindowTitle;
                    if (titleChanged)
                    {
                        _currentRecord.WindowTitle = windowTitle ?? "";
                        var isFirefox = appName.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase);
                        if (isFirefox && _latestBrowserUrl != null)
                        {
                            _currentRecord.BrowserUrl = _latestBrowserUrl;
                            _currentRecord.WindowTitle = _latestBrowserTitle ?? windowTitle ?? "";
                        }
                    }

                    _db.InsertOrUpdateActive(_currentRecord);
                    RecordUpdated?.Invoke(_currentRecord);
                    _lastWindowTitle = windowTitle ?? "";
                }
                else if (_lastProcessPath != null)
                {
                    StartNewRecord(processPath, windowTitle ?? "", appName);
                    _lastWindowTitle = windowTitle ?? "";
                    File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} RECOVER restart={appName}\n");
                }
            }

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
