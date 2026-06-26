using System.Drawing;
using System.Windows;
using H.NotifyIcon;
using Wstat.Desktop.Services;
using Wstat.Desktop.ViewModels;

namespace Wstat.Desktop;

public partial class App : Application
{
    private DatabaseService? _db;
    private WindowTrackerService? _tracker;
    private LocalHttpServer? _httpServer;
    private MainWindow? _mainWindow;
    private TaskbarIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _db = new DatabaseService();
        _tracker = new WindowTrackerService(_db);
        _httpServer = new LocalHttpServer(_tracker);

        _tracker.Start();
        _httpServer.Start();

        var viewModel = new DashboardViewModel(_db);
        _mainWindow = new MainWindow(viewModel);

        CreateTrayIcon();

        _mainWindow.Show();
    }

    private void CreateTrayIcon()
    {
        var icon = GenerateIcon();

        _trayIcon = new TaskbarIcon
        {
            Icon = icon,
            ToolTipText = "wstat — Screen Time Tracker",
            MenuActivation = H.NotifyIcon.Core.PopupActivationMode.LeftOrRightClick
        };

        var showItem = new System.Windows.Controls.MenuItem
        {
            Header = "Show Window"
        };
        showItem.Click += (_, _) => ShowMainWindow();

        var quitItem = new System.Windows.Controls.MenuItem
        {
            Header = "Quit"
        };
        quitItem.Click += (_, _) => QuitApp();

        var contextMenu = new System.Windows.Controls.ContextMenu();
        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(quitItem);
        _trayIcon.ContextMenu = contextMenu;

        _trayIcon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();
    }

    private static Icon GenerateIcon()
    {
        var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(0x20, 0x78, 0xD4));
            g.FillRectangle(brush, 0, 0, 16, 16);
            using var font = new Font(new FontFamily("Segoe UI"), 10, System.Drawing.FontStyle.Bold);
            g.DrawString("W", font, Brushes.White, 2, 1);
        }

        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null) return;

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void QuitApp()
    {
        _tracker?.Stop();
        _httpServer?.Stop();
        _trayIcon?.Dispose();
        _db?.Dispose();
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tracker?.Stop();
        _httpServer?.Stop();
        _trayIcon?.Dispose();
        _db?.Dispose();
        base.OnExit(e);
    }
}
