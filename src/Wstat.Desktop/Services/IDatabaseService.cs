using Wstat.Desktop.Models;

namespace Wstat.Desktop.Services;

public interface IDatabaseService
{
    void InsertOrUpdateActive(ActivityRecord record);
    void CloseActive(ActivityRecord record);
    void CloseOrphanedRecords();
    void UpdateBrowserUrl(int recordId, string url);
    List<AppSummary> GetAppSummary(DateFilter filter, DateTime? specificDate = null);
    List<UrlSummary> GetUrlSummary(DateFilter filter, DateTime? specificDate = null);
    List<TimelineEntry> GetTimeline(DateFilter filter, DateTime? specificDate = null);
    int DeleteRecordsForDay(DateTime day);
    int DeleteProblematicRecordsForDay(DateTime day);
}
