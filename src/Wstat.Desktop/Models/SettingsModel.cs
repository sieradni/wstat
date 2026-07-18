namespace Wstat.Desktop.Models;

public class SettingsModel
{
    public int HttpPort { get; set; } = 12345;
    public int PollIntervalMs { get; set; } = 2000;
    public int IdleThresholdMs { get; set; } = 300_000;
}
