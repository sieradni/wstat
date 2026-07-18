using System.IO;
using Wstat.Desktop.Common;
using Wstat.Desktop.Models;
using Wstat.Desktop.Native;

namespace Wstat.Desktop.Services;

public class WindowTrackerService : IWindowTrackerService, IDisposable
{
    private readonly IDatabaseService _db;
    private readonly SettingsModel _settings;
    private readonly object _stateLock = new();
    private CancellationTokenSource? _cts;

    private ActivityRecord? _currentRecord;
    private string? _lastProcessPath;
    private string? _lastWindowTitle;
    private bool _wasIdle;
    private volatile bool _disposed;

    private string? _pendingProcessPath;
    private string? _pendingWindowTitle;
    private string? _pendingAppName;
    private int _pendingCount;

    private string? _latestBrowserUrl;
    private string? _latestBrowserTitle;
    private uint _lastTick;
    private int _consecutiveErrorCount;

    public event Action<ActivityRecord>? RecordUpdated;
    public event Action<bool>? IdleStateChanged;

    public ActivityRecord? CurrentRecord
    {
        get { lock (_stateLock) { return _currentRecord; } }
    }
    public bool IsIdle { get; private set; }

    public WindowTrackerService(IDatabaseService db, SettingsModel settings)
    {
        _db = db;
        _settings = settings;
    }

    public void Start()
    {
        _db.CloseOrphanedRecords();
        _cts = new CancellationTokenSource();
        _ = PollLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        lock (_stateLock) { CloseCurrentRecord(); }
    }

    public void SetBrowserTab(string url, string title)
    {
        ActivityRecord? updatedRecord = null;

        lock (_stateLock)
        {
            _latestBrowserUrl = url;
            _latestBrowserTitle = title;

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
                updatedRecord = _currentRecord;
                LogWriter.Write("[WindowTracker] STORED: " + title + " (" + url + ")");
            }
            else
            {
                LogWriter.Write("[WindowTracker] SKIPPED: curr=" + (_currentRecord?.AppName ?? "null") + ", idle=" + _wasIdle + ", url=" + url);
            }
        }

        if (updatedRecord != null)
            RecordUpdated?.Invoke(updatedRecord);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_disposed)
        {
            try
            {
                var hWnd = Win32Api.GetForegroundWindow();
                var processPath = Win32Api.GetForegroundProcessPath();
                var windowTitle = Win32Api.GetForegroundWindowTitle();
                var lastInputTick = Win32Api.GetLastInputTick();
                var nowTick = (uint)Environment.TickCount;
                var idleDuration = nowTick - lastInputTick;
                var isIdle = idleDuration > _settings.IdleThresholdMs;

                if (hWnd != IntPtr.Zero)
                {
                    if (_lastTick != 0)
                    {
                        var elapsed = nowTick - _lastTick;
                        if (elapsed > _settings.PollIntervalMs * 10)
                        {
                            lock (_stateLock)
                            {
                                CloseCurrentRecord();
                                LogWriter.Write("[WindowTracker] GAP detected: " + elapsed + "ms since last tick (sleep/resume/DST)");
                            }
                        }
                    }
                    _lastTick = nowTick;

                    if (!string.IsNullOrEmpty(processPath))
                    {
                        var appName = Path.GetFileName(processPath);
                        await ProcessTickAsync(processPath, windowTitle ?? "", appName, isIdle);
                    }
                    else
                    {
                        lock (_stateLock) { CloseCurrentRecord(); }
                    }
                }

                _consecutiveErrorCount = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _consecutiveErrorCount++;
                LogWriter.Write("[WindowTracker] Poll error (" + _consecutiveErrorCount + "): " + ex);
            }

            if (!_disposed && !ct.IsCancellationRequested)
            {
                try
                {
                    var delay = _consecutiveErrorCount > 0
                        ? Math.Min(_settings.PollIntervalMs * (1 << Math.Min(_consecutiveErrorCount, 4)), 30_000)
                        : _settings.PollIntervalMs;
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    internal Task ProcessTickAsync(string processPath, string windowTitle, string appName, bool isIdle)
    {
        ActivityRecord? updatedRecord = null;
        bool idleStateChanged = false;
        bool idleEntered = false;
        bool skipProcessing = false;

        lock (_stateLock)
        {
            if (isIdle != _wasIdle)
            {
                if (isIdle)
                {
                    CloseCurrentRecord();
                    IsIdle = true;
                    _wasIdle = true;
                    idleStateChanged = true;
                    idleEntered = true;
                    LogWriter.Write("[WindowTracker] IDLE enter");
                }
                else
                {
                    IsIdle = false;
                    _wasIdle = false;
                    idleStateChanged = true;
                    idleEntered = false;
                    updatedRecord = StartNewRecord(processPath, windowTitle, appName);
                    _lastProcessPath = processPath;
                    _lastWindowTitle = windowTitle;
                    LogWriter.Write("[WindowTracker] IDLE exit path=" + appName);
                }
                skipProcessing = true;
            }

            if (!skipProcessing && !isIdle)
            {
                var processChanged = processPath != _lastProcessPath;

                if (processChanged)
                {
                    if (_pendingProcessPath == null)
                    {
                        _pendingProcessPath = processPath;
                        _pendingWindowTitle = windowTitle;
                        _pendingAppName = appName;
                        _pendingCount = 1;
                        LogWriter.Write("[WindowTracker] DEBOUNCE first-sight path=" + appName);
                    }
                    else if (processPath == _pendingProcessPath &&
                             _pendingCount < 2)
                    {
                        _pendingCount++;
                        LogWriter.Write("[WindowTracker] DEBOUNCE increment path=" + appName + " count=" + _pendingCount);
                    }
                    else if (processPath != _pendingProcessPath)
                    {
                        LogWriter.Write("[WindowTracker] DEBOUNCE third-process pending=" + _pendingAppName + " now=" + appName);
                        CloseCurrentRecord();
                        _pendingProcessPath = null;
                        _pendingCount = 0;
                        updatedRecord = StartNewRecord(processPath, windowTitle, appName);
                        _lastProcessPath = processPath;
                        _lastWindowTitle = windowTitle;
                    }

                    if (_pendingProcessPath != null && _pendingCount >= 2)
                    {
                        LogWriter.Write("[WindowTracker] DEBOUNCE commit path=" + _pendingAppName);
                        CloseCurrentRecord();
                        updatedRecord = StartNewRecord(_pendingProcessPath, _pendingWindowTitle ?? "", _pendingAppName!);
                        _lastProcessPath = _pendingProcessPath;
                        _lastWindowTitle = _pendingWindowTitle ?? "";
                        _pendingProcessPath = null;
                        _pendingCount = 0;
                    }

                    if (_pendingProcessPath != null && _currentRecord != null)
                    {
                        _db.InsertOrUpdateActive(_currentRecord);
                        updatedRecord = _currentRecord;
                    }
                }
                else
                {
                    if (_pendingProcessPath != null)
                    {
                        LogWriter.Write("[WindowTracker] DEBOUNCE cancel path=" + _pendingAppName);
                        _pendingProcessPath = null;
                        _pendingCount = 0;
                    }

                    if (_currentRecord != null)
                    {
                        var titleChanged = windowTitle != _lastWindowTitle;
                        if (titleChanged)
                        {
                            _currentRecord.WindowTitle = windowTitle;
                            var isFirefox = appName.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase);
                            if (isFirefox && _latestBrowserUrl != null)
                            {
                                _currentRecord.BrowserUrl = _latestBrowserUrl;
                                _currentRecord.WindowTitle = _latestBrowserTitle ?? windowTitle;
                            }
                        }

                        if (titleChanged || _currentRecord.Id == 0)
                        {
                            _db.InsertOrUpdateActive(_currentRecord);
                        }
                        else
                        {
                            var elapsed = (int)(DateTime.Now - _currentRecord.StartTime).TotalSeconds;
                            if (elapsed != _currentRecord.DurationSeconds)
                            {
                                _db.InsertOrUpdateActive(_currentRecord);
                            }
                            else if (_currentRecord.Id != 0 && _latestBrowserUrl != null &&
                                     string.Equals(appName, "firefox.exe", StringComparison.OrdinalIgnoreCase))
                            {
                                _db.UpdateBrowserUrl(_currentRecord.Id, _latestBrowserUrl);
                            }
                        }
                        updatedRecord = _currentRecord;
                        _lastWindowTitle = windowTitle;
                    }
                    else if (_lastProcessPath != null)
                    {
                        updatedRecord = StartNewRecord(processPath, windowTitle, appName);
                        _lastWindowTitle = windowTitle;
                        LogWriter.Write("[WindowTracker] RECOVER restart=" + appName);
                    }
                }
            }
        }

        if (idleStateChanged)
            IdleStateChanged?.Invoke(idleEntered);

        if (updatedRecord != null)
            RecordUpdated?.Invoke(updatedRecord);

        return Task.CompletedTask;
    }

    private ActivityRecord? StartNewRecord(string processPath, string windowTitle, string appName)
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
        return _currentRecord;
    }

    private void CloseCurrentRecord()
    {
        if (_currentRecord == null) return;
        _db.CloseActive(_currentRecord);
        _currentRecord = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        lock (_stateLock) { CloseCurrentRecord(); }
    }
}
