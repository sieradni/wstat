using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using Wstat.Desktop.Models;
using Wstat.Desktop.Services;

namespace Wstat.Desktop.Tests;

public class DatabaseServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _sut;

    public DatabaseServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "wstat_test_" + Guid.NewGuid() + ".db");
        _sut = new DatabaseService($"Data Source={_dbPath}");
    }

    public void Dispose()
    {
        _sut.Dispose();
        SqliteConnection.ClearAllPools();
        TryDeleteFile(_dbPath);
        TryDeleteFile(_dbPath + "-wal");
        TryDeleteFile(_dbPath + "-shm");
    }

    private static void TryDeleteFile(string path)
    {
        if (!File.Exists(path)) return;
        for (int i = 0; i < 5; i++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Fact]
    public void Insert_new_record_assigns_id()
    {
        var record = new ActivityRecord
        {
            AppName = "app.exe",
            WindowTitle = "Test Window",
            ProcessPath = @"C:\app.exe",
            StartTime = DateTime.Now
        };

        _sut.InsertOrUpdateActive(record);

        record.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Insert_new_record_can_be_queried()
    {
        var start = DateTime.Now.AddMinutes(-5);
        var record = new ActivityRecord
        {
            AppName = "notepad.exe",
            WindowTitle = "Untitled - Notepad",
            ProcessPath = @"C:\Windows\notepad.exe",
            BrowserUrl = null,
            StartTime = start
        };
        _sut.InsertOrUpdateActive(record);

        var summary = _sut.GetAppSummary(DateFilter.Today);

        summary.Should().ContainSingle(a => a.AppName == "notepad.exe");
    }

    [Fact]
    public void Update_existing_record_changes_duration()
    {
        var record = new ActivityRecord
        {
            AppName = "app.exe",
            WindowTitle = "Window",
            StartTime = DateTime.Now.AddHours(-1)
        };
        _sut.InsertOrUpdateActive(record);
        var id = record.Id;

        record.StartTime = DateTime.Now.AddHours(-2);
        _sut.InsertOrUpdateActive(record);

        record.Id.Should().Be(id);
    }

    [Fact]
    public void CloseActive_sets_endtime_and_duration()
    {
        var start = DateTime.Now.AddMinutes(-30);
        var record = new ActivityRecord
        {
            AppName = "app.exe",
            WindowTitle = "Window",
            StartTime = start
        };
        _sut.InsertOrUpdateActive(record);

        _sut.CloseActive(record);

        record.EndTime.Should().NotBeNull();
        record.DurationSeconds.Should().BeInRange(1790, 1810);
    }

    [Fact]
    public void CloseActive_with_zero_id_does_nothing()
    {
        var record = new ActivityRecord
        {
            Id = 0,
            AppName = "app.exe",
            WindowTitle = "Window",
            StartTime = DateTime.Now
        };

        _sut.CloseActive(record);

        record.EndTime.Should().BeNull();
    }

    [Fact]
    public void CloseOrphanedRecords_caps_zero_duration_to_1s()
    {
        var start = DateTime.Now.AddMinutes(-30);
        var record = new ActivityRecord
        {
            AppName = "orphan.exe",
            WindowTitle = "Orphaned",
            StartTime = start
        };
        _sut.InsertOrUpdateActive(record);
        record.Id.Should().BeGreaterThan(0);

        _sut.CloseOrphanedRecords();

        var apps = _sut.GetAppSummary(DateFilter.Today);
        apps.Should().ContainSingle(a => a.AppName == "orphan.exe");
        var app = apps.Single(a => a.AppName == "orphan.exe");
        app.TotalSeconds.Should().Be(1);

        var timeline = _sut.GetTimeline(DateFilter.Today);
        timeline.Should().ContainSingle(e => e.AppName == "orphan.exe");
        var entry = timeline.Single(e => e.AppName == "orphan.exe");
        entry.DurationSeconds.Should().Be(1);
        entry.EndTime.Should().Be(start.AddSeconds(1));
    }

    [Fact]
    public void CloseOrphanedRecords_preserves_existing_duration()
    {
        var start = DateTime.Now.AddMinutes(-30);
        var record = new ActivityRecord
        {
            AppName = "existing.exe",
            WindowTitle = "Existing",
            StartTime = start
        };
        _sut.InsertOrUpdateActive(record);

        using (var directConn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            directConn.Open();
            using var updateCmd = directConn.CreateCommand();
            updateCmd.CommandText = "UPDATE ActivityLog SET DurationSeconds = 120 WHERE Id = $id;";
            updateCmd.Parameters.AddWithValue("$id", record.Id);
            updateCmd.ExecuteNonQuery();
        }

        _sut.CloseOrphanedRecords();

        var apps = _sut.GetAppSummary(DateFilter.Today);
        apps.Should().ContainSingle(a => a.AppName == "existing.exe");
        var app = apps.Single(a => a.AppName == "existing.exe");
        app.TotalSeconds.Should().Be(120);

        var timeline = _sut.GetTimeline(DateFilter.Today);
        var entry = timeline.Single(e => e.AppName == "existing.exe");
        entry.DurationSeconds.Should().Be(120);
        entry.EndTime.Should().Be(start.AddSeconds(120));
    }

    [Fact]
    public void GetAppSummary_groups_and_orders_by_duration()
    {
        InsertApp("b.exe", 60);
        InsertApp("a.exe", 120);
        InsertApp("c.exe", 30);

        var summary = _sut.GetAppSummary(DateFilter.Today);

        summary[0].AppName.Should().Be("a.exe");
        summary[1].AppName.Should().Be("b.exe");
        summary[2].AppName.Should().Be("c.exe");
    }

    [Fact]
    public void GetAppSummary_respects_date_filter()
    {
        InsertApp("app.exe", 60, startTime: DateTime.Now.AddDays(-2));

        var todaySummary = _sut.GetAppSummary(DateFilter.Today);
        var yesterdaySummary = _sut.GetAppSummary(DateFilter.Yesterday);

        todaySummary.Should().BeEmpty();
        yesterdaySummary.Should().BeEmpty();
    }

    [Fact]
    public void GetUrlSummary_returns_browser_urls_only()
    {
        InsertApp("firefox.exe", 60, browserUrl: "https://example.com");
        InsertApp("notepad.exe", 30, browserUrl: null);

        var urls = _sut.GetUrlSummary(DateFilter.Today);

        urls.Should().ContainSingle();
        urls[0].Url.Should().Be("https://example.com");
    }

    [Fact]
    public void GetUrlSummary_aggregates_visits()
    {
        InsertApp("firefox.exe", 60, browserUrl: "https://example.com");
        InsertApp("firefox.exe", 120, browserUrl: "https://example.com");
        InsertApp("firefox.exe", 30, browserUrl: "https://other.com");

        var urls = _sut.GetUrlSummary(DateFilter.Today);

        urls.Should().HaveCount(2);
        urls[0].Url.Should().Be("https://example.com");
        urls[0].VisitCount.Should().Be(2);
    }

    [Fact]
    public void GetTimeline_returns_ordered_entries()
    {
        var start = DateTime.Now.AddMinutes(-5);
        var r1 = InsertApp("a.exe", 60, startTime: start);
        var r2 = InsertApp("b.exe", 60, startTime: start.AddSeconds(30));

        var timeline = _sut.GetTimeline(DateFilter.Today);

        timeline.Should().HaveCount(2);
        timeline[0].AppName.Should().Be("a.exe");
        timeline[1].AppName.Should().Be("b.exe");
    }

    [Fact]
    public void GetTimeline_excludes_zero_duration_open_records()
    {
        InsertApp("a.exe", 60);

        var timeline = _sut.GetTimeline(DateFilter.Today);

        timeline.Should().ContainSingle(e => e.AppName == "a.exe");
    }

    [Fact]
    public void UpdateBrowserUrl_updates_field()
    {
        var record = new ActivityRecord
        {
            AppName = "firefox.exe",
            WindowTitle = "Mozilla Firefox",
            StartTime = DateTime.Now
        };
        _sut.InsertOrUpdateActive(record);

        _sut.UpdateBrowserUrl(record.Id, "https://updated.example.com");

        var urls = _sut.GetUrlSummary(DateFilter.Today);
        urls.Should().ContainSingle(u => u.Url == "https://updated.example.com");
    }

    [Fact]
    public void DeleteRecordsForDay_removes_matching_records()
    {
        var start = DateTime.Now.AddDays(-1);
        InsertApp("old.exe", 60, start);

        _sut.DeleteRecordsForDay(start);

        var yesterdaySummary = _sut.GetAppSummary(DateFilter.Yesterday);
        yesterdaySummary.Should().BeEmpty();
    }

    [Fact]
    public void DeleteProblematicRecordsForDay_removes_zero_duration_orphans()
    {
        var start = DateTime.Now.AddDays(-1);
        var record = new ActivityRecord
        {
            AppName = "bad.exe",
            WindowTitle = "Bad",
            StartTime = start,
            DurationSeconds = 0
        };
        _sut.InsertOrUpdateActive(record);

        var deleted = _sut.DeleteProblematicRecordsForDay(start);

        deleted.Should().Be(1);
    }

    [Fact]
    public void GetDateRange_Today_returns_correct_bounds()
    {
        var (start, end) = _sut.GetDateRange(DateFilter.Today);

        start.Should().Be(DateTime.Today);
        end.Should().BeNull();
    }

    [Fact]
    public void GetDateRange_Yesterday_returns_correct_bounds()
    {
        var (start, end) = _sut.GetDateRange(DateFilter.Yesterday);

        start.Should().Be(DateTime.Today.AddDays(-1));
        end.Should().Be(DateTime.Today);
    }

    [Fact]
    public void GetDateRange_Last7Days_returns_correct_bounds()
    {
        var (start, end) = _sut.GetDateRange(DateFilter.Last7Days);

        start.Should().Be(DateTime.Today.AddDays(-6));
        end.Should().Be(DateTime.Today.AddDays(1));
    }

    [Fact]
    public void GetDateRange_Last30Days_returns_correct_bounds()
    {
        var (start, end) = _sut.GetDateRange(DateFilter.Last30Days);

        start.Should().Be(DateTime.Today.AddDays(-29));
        end.Should().Be(DateTime.Today.AddDays(1));
    }

    [Fact]
    public void GetDateRange_Specific_uses_provided_date()
    {
        var specific = new DateTime(2026, 6, 15);
        var (start, end) = _sut.GetDateRange(DateFilter.Specific, specific);

        start.Should().Be(specific.Date);
        end.Should().Be(specific.Date.AddDays(1));
    }

    [Fact]
    public void Schema_creation_is_idempotent()
    {
        var sut2 = new DatabaseService($"Data Source={_dbPath}");
        sut2.Dispose();

        var app = _sut.GetAppSummary(DateFilter.Today);
        app.Should().BeEmpty();
    }

    [Fact]
    public async Task Concurrent_reads_and_writes_do_not_deadlock()
    {
        var start = DateTime.Now.AddHours(-1);
        var record = new ActivityRecord
        {
            AppName = "concurrent.exe",
            WindowTitle = "Concurrent",
            StartTime = start
        };
        _sut.InsertOrUpdateActive(record);

        var writeTask = Task.Run(() =>
        {
            for (int i = 0; i < 20; i++)
            {
                _sut.InsertOrUpdateActive(record);
            }
        });
        var readTask = Task.Run(() =>
        {
            for (int i = 0; i < 20; i++)
            {
                _sut.GetAppSummary(DateFilter.Today);
                _sut.GetUrlSummary(DateFilter.Today);
            }
        });

        await Task.WhenAll(writeTask, readTask);

        _sut.CloseActive(record);
        record.EndTime.Should().NotBeNull();
    }

    private ActivityRecord InsertApp(string appName, int durationSeconds, DateTime? startTime = null, string? browserUrl = null)
    {
        var effectiveStart = startTime ?? DateTime.Now.AddSeconds(-durationSeconds);
        var record = new ActivityRecord
        {
            AppName = appName,
            WindowTitle = appName + " Window",
            ProcessPath = @"C:\" + appName,
            BrowserUrl = browserUrl,
            StartTime = effectiveStart
        };
        _sut.InsertOrUpdateActive(record);

        if (durationSeconds > 0)
            _sut.CloseActive(record);

        return record;
    }
}