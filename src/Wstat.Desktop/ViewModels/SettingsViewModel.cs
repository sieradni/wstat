using System.ComponentModel;
using System.Runtime.CompilerServices;
using Wstat.Desktop.Common;
using Wstat.Desktop.Models;
using Wstat.Desktop.Services;

namespace Wstat.Desktop.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsModel _settings;
    private bool _autoStartup;

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

    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public Action? RequestClose { get; set; }

    public SettingsViewModel(SettingsModel settings)
    {
        _settings = settings;
        _autoStartup = settings.AutoStartup;
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke());
    }

    private void Save()
    {
        _settings.AutoStartup = _autoStartup;
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
        LogWriter.Write("[Settings] Saved: AutoStartup=" + _autoStartup);
        RequestClose?.Invoke();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
