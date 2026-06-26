using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Wstat.Desktop.Native;

internal static class Win32Api
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageName(
        IntPtr hProcess, uint dwFlags,
        StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    public static string? GetForegroundWindowTitle()
    {
        var hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return null;

        var sb = new StringBuilder(256);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string? GetForegroundProcessPath()
    {
        var hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return null;

        GetWindowThreadProcessId(hWnd, out var pid);
        if (pid == 0) return null;

        var hProcess = Process.GetProcessById((int)pid).Handle;
        var sb = new StringBuilder(260);
        var size = (uint)sb.Capacity;

        if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
            return sb.ToString();

        return null;
    }

    public static uint GetLastInputTick()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (GetLastInputInfo(ref lii))
            return lii.dwTime;
        return 0;
    }
}
