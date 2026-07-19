namespace Wstat.Desktop.Common;

internal static class Constants
{
    public const string WindowTitle = "wstat \u2014 Screen Time Tracker";
    public const string MutexName = @"Local\Wstat_Desktop_App";
    public const int DefaultHttpPort = 12345;
    public const int DefaultPollIntervalMs = 2000;
    public const int DefaultIdleThresholdMs = 300_000;

    public const string TabEndpoint = "/tab";
    public const string LocalhostPrefix = "http://127.0.0.1:";
    public const string StatusOk = "ok";
    public const string StatusBadRequest = "bad request";
    public const string StatusNotFound = "not found";
    public const string FirefoxExe = "firefox.exe";
    public const string IsoFormat = "O";
}
