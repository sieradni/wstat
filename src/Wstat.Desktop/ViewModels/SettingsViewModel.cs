using System.ComponentModel;
using System.Runtime.CompilerServices;
using Wstat.Desktop.Common;
using Wstat.Desktop.Models;
using Wstat.Desktop.Services;

namespace Wstat.Desktop.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsModel _settings;
    private readonly int _originalHttpPort;
    private readonly int _originalPollIntervalMs;
    private readonly int _originalIdleThresholdMs;
    private readonly bool _originalAutoStartup;

    private bool _autoStartup;
    private string _httpPort;
    private string _pollIntervalMs;
    private string _idleThresholdMs;
    private string? _errorMessage;

    public bool AutoStartup
    {
        get => _autoStartup;
        set
        {
            if (_autoStartup == value) return;
            _autoStartup = value;
            OnPropertyChanged();
        }
    }

    public string HttpPort
    {
        get => _httpPort;
        set
        {
            if (_httpPort == value) return;
            _httpPort = value;
            OnPropertyChanged();
        }
    }

    public string PollIntervalMs
    {
        get => _pollIntervalMs;
        set
        {
            if (_pollIntervalMs == value) return;
            _pollIntervalMs = value;
            OnPropertyChanged();
        }
    }

    public string IdleThresholdMs
    {
        get => _idleThresholdMs;
        set
        {
            if (_idleThresholdMs == value) return;
            _idleThresholdMs = value;
            OnPropertyChanged();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (_errorMessage == value) return;
            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    public bool HasChanges { get; private set; }
    public bool RestartRequired { get; private set; }

    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public Action? RequestClose { get; set; }

    public SettingsViewModel(SettingsModel settings)
    {
        _settings = settings;
        _originalHttpPort = settings.HttpPort;
        _originalPollIntervalMs = settings.PollIntervalMs;
        _originalIdleThresholdMs = settings.IdleThresholdMs;
        _originalAutoStartup = settings.AutoStartup;

        _autoStartup = settings.AutoStartup;
        _httpPort = settings.HttpPort.ToString();
        _pollIntervalMs = settings.PollIntervalMs.ToString();
        _idleThresholdMs = settings.IdleThresholdMs.ToString();
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke());
    }

    private void Save()
    {
        ErrorMessage = null;

        if (!int.TryParse(_httpPort, out var port) || port <= 0 || port > 65535)
        {
            ErrorMessage = "HTTP Port must be a number between 1 and 65535.";
            return;
        }

        if (!int.TryParse(_pollIntervalMs, out var poll) || poll < 500)
        {
            ErrorMessage = "Poll Interval must be at least 500ms.";
            return;
        }

        if (!int.TryParse(_idleThresholdMs, out var idle) || idle < 10000)
        {
            ErrorMessage = "Idle Threshold must be at least 10000ms.";
            return;
        }

        HasChanges = port != _originalHttpPort ||
                     poll != _originalPollIntervalMs ||
                     idle != _originalIdleThresholdMs ||
                     _autoStartup != _originalAutoStartup;

        RestartRequired = port != _originalHttpPort ||
                          poll != _originalPollIntervalMs ||
                          idle != _originalIdleThresholdMs;

        _settings.AutoStartup = _autoStartup;
        _settings.HttpPort = port;
        _settings.PollIntervalMs = poll;
        _settings.IdleThresholdMs = idle;

        if (_autoStartup)
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
                AutoStartupService.Enable(exePath);
        }
        else
        {
            AutoStartupService.Disable();
        }
        SettingsManager.Save(_settings);
        LogWriter.Write("[Settings] Saved: AutoStartup=" + _autoStartup + ", Port=" + _settings.HttpPort + ", Poll=" + _settings.PollIntervalMs + ", Idle=" + _settings.IdleThresholdMs);
        RequestClose?.Invoke();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
