using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Wstat.Desktop.Common;
using Wstat.Desktop.Models;
using Wstat.Desktop.Services;
using Wstat.Desktop.ViewModels;
using Wstat.Desktop.Views;

namespace Wstat.Desktop;

public partial class App : System.Windows.Application
{
    private static Mutex? _instanceMutex;
    internal static ServiceProvider? ServiceProvider { get; private set; }
    private ServiceProvider? _serviceProvider;
    private IWindowTrackerService? _tracker;
    private ILocalHttpServer? _httpServer;
    private MainWindow? _mainWindow;
    private TrayService? _trayService;
    private DashboardViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LogWriter.Initialize();

        bool createdNew;
        _instanceMutex = new Mutex(true, Constants.MutexName, out createdNew);

        if (!createdNew)
        {
            _instanceMutex = null;
            Native.Win32Api.ActivateExistingInstance();
            Current.Shutdown();
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            LogWriter.Write("[FATAL] Unhandled: " + ex?.GetType().Name + ": " + ex?.Message);
            if (ex != null)
            {
                try { System.IO.File.AppendAllText(AppPaths.LogPath, ex.StackTrace + "\n"); }
                catch { }
            }
        };

        DispatcherUnhandledException += (_, args) =>
        {
            LogWriter.Write("[FATAL] Dispatcher: " + args.Exception.GetType().Name + ": " + args.Exception.Message);
            try { System.IO.File.AppendAllText(AppPaths.LogPath, args.Exception.StackTrace + "\n"); }
            catch { }
            args.Handled = true;
        };

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnded += OnSessionEnded;

        var settings = SettingsManager.Load();

        if (settings.AutoStartup)
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
                AutoStartupService.Enable(exePath);
        }

        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IWindowTrackerService, WindowTrackerService>();
        services.AddSingleton<ILocalHttpServer, LocalHttpServer>();
        services.AddSingleton<IIconService, IconService>();
        services.AddSingleton<DashboardViewModel>();
        _serviceProvider = services.BuildServiceProvider();
        ServiceProvider = _serviceProvider;

        _tracker = _serviceProvider.GetRequiredService<IWindowTrackerService>();
        _httpServer = _serviceProvider.GetRequiredService<ILocalHttpServer>();
        _viewModel = _serviceProvider.GetRequiredService<DashboardViewModel>();

        _tracker.Start();
        _httpServer.Start();

        if (!_httpServer.IsRunning)
        {
            LogWriter.Write("[App] HTTP server failed to start on port " + settings.HttpPort);
        }

        _mainWindow = new MainWindow(_viewModel);

        _tracker.RecordUpdated += _ =>
        {
            Dispatcher.Invoke(() => _viewModel?.RefreshSummary());
        };

        _trayService = new TrayService(_mainWindow, ShowMainWindow, ShowSettingsWindow, QuitApp);

        _mainWindow.Show();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                LogWriter.Write("[App] System suspend detected");
                _tracker?.ForceCloseCurrentRecord();
                break;
            case PowerModes.Resume:
                LogWriter.Write("[App] System resume detected");
                break;
        }
    }

    private void OnSessionEnded(object sender, SessionEndedEventArgs e)
    {
        LogWriter.Write("[App] Session ended (shutdown/logoff): " + e.Reason);
        _tracker?.ForceCloseCurrentRecord();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null) return;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ShowSettingsWindow()
    {
        ShowMainWindow();
        _viewModel?.ShowSettingsCommand?.Execute(null);
    }

    private void QuitApp()
    {
        _trayService?.Dispose();
        _tracker?.Stop();
        _httpServer?.Stop();
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        _serviceProvider?.Dispose();
        LogWriter.Shutdown();
        if (_instanceMutex != null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
        base.OnExit(e);
    }
}
