using Microsoft.Win32;
using Wstat.Desktop.Common;

namespace Wstat.Desktop.Services;

public static class AutoStartupService
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "wstat";

    public static void Enable(string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue(ValueName, executablePath);
            LogWriter.Write("[AutoStartup] Enabled: " + executablePath);
        }
        catch (Exception ex)
        {
            LogWriter.Write("[AutoStartup] Failed to enable: " + ex.Message);
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            LogWriter.Write("[AutoStartup] Disabled");
        }
        catch (Exception ex)
        {
            LogWriter.Write("[AutoStartup] Failed to disable: " + ex.Message);
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) != null;
        }
        catch
        {
            return false;
        }
    }
}
