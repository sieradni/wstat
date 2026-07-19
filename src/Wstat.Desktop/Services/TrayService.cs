using System.Drawing;
using System.IO;
using System.Windows;
using Wstat.Desktop.Common;
using FontStyle = System.Drawing.FontStyle;
using Forms = System.Windows.Forms;

namespace Wstat.Desktop.Services;

internal sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Action _showMainWindow;
    private readonly Action _showSettings;
    private readonly Action _quit;
    private bool _disposed;

    public TrayService(MainWindow mainWindow, Action showMainWindow, Action showSettings, Action quit)
    {
        _showMainWindow = showMainWindow;
        _showSettings = showSettings;
        _quit = quit;

        var icon = CreateAndSaveIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = Constants.WindowTitle,
            Visible = true
        };

        mainWindow.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(AppPaths.IconPath));

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show Window", null, (_, _) => _showMainWindow());
        menu.Items.Add("Settings...", null, (_, _) => _showSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => _quit());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => _showMainWindow();
    }

    private static Icon CreateAndSaveIcon()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.FromArgb(0x20, 0x78, 0xD4));
            using var font = new Font("Segoe UI", 10, FontStyle.Bold);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }
}
