using Wstat.Desktop.Models;

namespace Wstat.Desktop.Services;

public interface IWindowTrackerService
{
    ActivityRecord? CurrentRecord { get; }
    bool IsIdle { get; }

    event Action<ActivityRecord>? RecordUpdated;
    event Action<bool>? IdleStateChanged;

    void Start();
    void Stop();
    void SetBrowserTab(string url, string title);
}
