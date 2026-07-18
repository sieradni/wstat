namespace Wstat.Desktop.Common;

internal static class Formatting
{
    public static string FormatDuration(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes}m"
            : $"{ts.Minutes}m {ts.Seconds}s";
    }
}
