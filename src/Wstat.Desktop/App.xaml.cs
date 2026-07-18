using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Wstat.Desktop.Common;
using Wstat.Desktop.Models;
using Wstat.Desktop.Services;
using Wstat.Desktop.ViewModels;
using Wstat.Desktop.Views;
using Forms = System.Windows.Forms;

namespace Wstat.Desktop;

public partial class App : System.Windows.Application
{
    private static Mutex? _instanceMutex;
    private ServiceProvider? _serviceProvider;
    private IWindowTrackerService? _tracker;
    private ILocalHttpServer? _httpServer;
    private MainWindow? _mainWindow;
    private Forms.NotifyIcon? _trayIcon;
    private DashboardViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LogWriter.Initialize();

        const string mutexName = "Local\\Wstat_Desktop_App";
        bool createdNew;
        _instanceMutex = new Mutex(true, mutexName, out createdNew);

        if (!createdNew)
        {
            _instanceMutex = null;
            Native.Win32Api.ActivateExistingInstance("wstat \u2014 Screen Time Tracker");
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

        var settings = SettingsManager.Load();

        if (settings.AutoStartup)
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
                AutoStartupService.Enable(exePath);
        }

        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IWindowTrackerService, WindowTrackerService>();
        services.AddSingleton<ILocalHttpServer, LocalHttpServer>();
        services.AddSingleton<DashboardViewModel>();
        _serviceProvider = services.BuildServiceProvider();

        _tracker = _serviceProvider.GetRequiredService<IWindowTrackerService>();
        _httpServer = _serviceProvider.GetRequiredService<ILocalHttpServer>();
        _viewModel = _serviceProvider.GetRequiredService<DashboardViewModel>();

        _tracker.Start();
        _httpServer.Start();

        _mainWindow = new MainWindow(_viewModel);

        _tracker.RecordUpdated += _ =>
        {
            Dispatcher.Invoke(() => _viewModel?.RefreshSummary());
        };

        CreateTrayIcon();

        _mainWindow.Show();
    }

    private void CreateTrayIcon()
    {
        var icon = CreateAndSaveIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "wstat \u2014 Screen Time Tracker",
            Visible = true
        };

        _mainWindow!.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(AppPaths.IconPath));

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show Window", null, (_, _) => ShowMainWindow());
        menu.Items.Add("Settings...", null, (_, _) => ShowSettingsWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitApp());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private static Icon CreateAndSaveIcon()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.FromArgb(0x20, 0x78, 0xD4));
            using var font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold);
            g.DrawString("W", font, Brushes.White, 3, 2);
        }

        using var ms = new MemoryStream();
        WriteIco(bitmap, ms);
        ms.Position = 0;
        var icon = new Icon(ms);

        using (var fs = new FileStream(AppPaths.IconPath, FileMode.Create, FileAccess.Write))
        {
            ms.Position = 0;
            ms.CopyTo(fs);
        }

        return icon;
    }

    private static void WriteIco(Bitmap bitmap, Stream output)
    {
        int w = bitmap.Width, h = bitmap.Height;
        int xorSize = w * h * 4;
        int andRowBytes = ((w + 31) / 32) * 4;
        int andSize = andRowBytes * h;
        int dataSize = 40 + xorSize + andSize;

        output.WriteByte(0); output.WriteByte(0);
        output.WriteByte(1); output.WriteByte(0);
        output.WriteByte(1); output.WriteByte(0);

        output.WriteByte((byte)(w == 256 ? 0 : w));
        output.WriteByte((byte)(h == 256 ? 0 : h));
        output.WriteByte(0);
        output.WriteByte(0);
        WriteLe16(output, 1);
        WriteLe16(output, 32);
        WriteLe32(output, dataSize);
        WriteLe32(output, 22);

        WriteLe32(output, 40);
        WriteLe32(output, w);
        WriteLe32(output, h * 2);
        WriteLe16(output, 1);
        WriteLe16(output, 32);
        WriteLe32(output, 0);
        WriteLe32(output, xorSize + andSize);
        WriteLe32(output, 0);
        WriteLe32(output, 0);
        WriteLe32(output, 0);
        WriteLe32(output, 0);

        for (int y = h - 1; y >= 0; y--)
            for (int x = 0; x < w; x++)
            {
                var px = bitmap.GetPixel(x, y);
                output.WriteByte(px.B);
                output.WriteByte(px.G);
                output.WriteByte(px.R);
                output.WriteByte(px.A);
            }

        output.Write(new byte[andSize], 0, andSize);
    }

    private static void WriteLe16(Stream s, ushort v)
    {
        s.WriteByte((byte)v);
        s.WriteByte((byte)(v >> 8));
    }

    private static void WriteLe32(Stream s, int v)
    {
        s.WriteByte((byte)v);
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 24));
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
        if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); }
        _tracker?.Stop();
        _httpServer?.Stop();
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
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
