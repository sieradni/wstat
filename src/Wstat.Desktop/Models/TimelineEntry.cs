using MediaColor = System.Windows.Media.Color;

namespace Wstat.Desktop.Models;

public class TimelineEntry
{
    public string AppName { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public string? ProcessPath { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int DurationSeconds { get; set; }
    public MediaColor AppColor { get; set; }
    public MediaColor TitleColor { get; set; }
    public int RowIndex { get; set; }
}
