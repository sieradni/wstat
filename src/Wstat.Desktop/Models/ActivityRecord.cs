using System.Windows.Media.Imaging;
using Wstat.Desktop.Common;

namespace Wstat.Desktop.Models;

public class ActivityRecord
{
    public int Id { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public string? BrowserUrl { get; set; }
    public string? ProcessPath { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int DurationSeconds { get; set; }

    public string DisplayDuration =>
        EndTime.HasValue
            ? Formatting.FormatDuration(DurationSeconds)
            : Formatting.FormatDuration((int)(DateTime.Now - StartTime).TotalSeconds);

    public bool IsActive => EndTime == null;
}

public class AppSummary
{
    public string AppName { get; set; } = string.Empty;
    public string? ProcessPath { get; set; }
    public long TotalSeconds { get; set; }
    public BitmapSource? Icon { get; set; }
    public string DisplayTime => Formatting.FormatDuration(TotalSeconds);
}

public class UrlSummary
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int VisitCount { get; set; }
    public long TotalSeconds { get; set; }
    public string DisplayTime => Formatting.FormatDuration(TotalSeconds);
}
