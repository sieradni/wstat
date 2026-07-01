using Microsoft.Data.Sqlite;
using Wstat.Desktop.Common;
using Wstat.Desktop.Models;

namespace Wstat.Desktop.Services;

public class DatabaseService : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public DatabaseService()
    {
        _connection = new SqliteConnection($"Data Source={AppPaths.DbPath}");
        _connection.Open();
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var wal = _connection.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL;";
        wal.ExecuteNonQuery();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ActivityLog (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AppName TEXT NOT NULL,
                WindowTitle TEXT NOT NULL,
                BrowserUrl TEXT,
                ProcessPath TEXT,
                StartTime TEXT NOT NULL,
                EndTime TEXT,
                DurationSeconds INTEGER NOT NULL DEFAULT 0
            );
            """;
        cmd.ExecuteNonQuery();

        try
        {
            using var alter = _connection.CreateCommand();
            alter.CommandText = "ALTER TABLE ActivityLog ADD COLUMN ProcessPath TEXT;";
            alter.ExecuteNonQuery();
        }
        catch { }

        using var idx = _connection.CreateCommand();
        idx.CommandText = "CREATE INDEX IF NOT EXISTS IX_ActivityLog_StartTime ON ActivityLog(StartTime);";
        idx.ExecuteNonQuery();
    }

    public void InsertOrUpdateActive(ActivityRecord record)
    {
        _writeLock.Wait();
        try
        {
            using var cmd = _connection.CreateCommand();

            if (record.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO ActivityLog (AppName, WindowTitle, BrowserUrl, ProcessPath, StartTime, EndTime, DurationSeconds)
                    VALUES ($appName, $windowTitle, $browserUrl, $processPath, $startTime, NULL, 0);
                    SELECT last_insert_rowid();
                    """;
                cmd.Parameters.AddWithValue("$appName", record.AppName);
                cmd.Parameters.AddWithValue("$windowTitle", record.WindowTitle);
                cmd.Parameters.AddWithValue("$browserUrl", (object?)record.BrowserUrl ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$processPath", (object?)record.ProcessPath ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$startTime", record.StartTime.ToString("O"));

                var result = cmd.ExecuteScalar();
                record.Id = Convert.ToInt32(result);
            }
            else
            {
                var elapsed = (int)(DateTime.Now - record.StartTime).TotalSeconds;
                cmd.CommandText = """
                    UPDATE ActivityLog
                    SET DurationSeconds = $duration
                    WHERE Id = $id;
                    """;
                cmd.Parameters.AddWithValue("$duration", elapsed);
                cmd.Parameters.AddWithValue("$id", record.Id);
                cmd.ExecuteNonQuery();
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void CloseActive(ActivityRecord record)
    {
        if (record.Id == 0) return;

        record.EndTime = DateTime.Now;
        record.DurationSeconds = Math.Max(0, (int)(record.EndTime.Value - record.StartTime).TotalSeconds);

        _writeLock.Wait();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE ActivityLog
                SET EndTime = $endTime, DurationSeconds = $duration
                WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$endTime", record.EndTime.Value.ToString("O"));
            cmd.Parameters.AddWithValue("$duration", record.DurationSeconds);
            cmd.Parameters.AddWithValue("$id", record.Id);
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void CloseOrphanedRecords()
    {
        _writeLock.Wait();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE ActivityLog
                SET EndTime = $now,
                    DurationSeconds = CAST((julianday($now) - julianday(StartTime)) * 86400 AS INTEGER)
                WHERE EndTime IS NULL;
                """;
            cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void UpdateBrowserUrl(int recordId, string url)
    {
        _writeLock.Wait();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE ActivityLog SET BrowserUrl = $url WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$url", url);
            cmd.Parameters.AddWithValue("$id", recordId);
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public List<AppSummary> GetAppSummary(DateFilter filter)
    {
        var (start, end) = GetDateRange(filter);
        var results = new List<AppSummary>();

        _writeLock.Wait();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT a.AppName,
                       SUM(a.DurationSeconds) as TotalSeconds,
                       (SELECT ProcessPath FROM ActivityLog
                        WHERE AppName = a.AppName AND StartTime >= $start
                        ORDER BY StartTime DESC LIMIT 1) as ProcessPath
                FROM ActivityLog a
                WHERE a.StartTime >= $start
                AND ($end IS NULL OR a.StartTime < $end)
                AND (a.DurationSeconds > 0 OR a.EndTime IS NULL)
                GROUP BY a.AppName
                ORDER BY TotalSeconds DESC;
                """;
            cmd.Parameters.AddWithValue("$start", start.ToString("O"));
            cmd.Parameters.AddWithValue("$end", (object?)end?.ToString("O") ?? DBNull.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new AppSummary
                {
                    AppName = reader.GetString(0),
                    TotalSeconds = reader.GetInt64(1),
                    ProcessPath = reader.IsDBNull(2) ? null : reader.GetString(2)
                });
            }
        }
        finally
        {
            _writeLock.Release();
        }

        return results;
    }

    public List<UrlSummary> GetUrlSummary(DateFilter filter)
    {
        var (start, end) = GetDateRange(filter);
        var results = new List<UrlSummary>();

        _writeLock.Wait();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT a.BrowserUrl,
                       (SELECT WindowTitle FROM ActivityLog
                        WHERE BrowserUrl = a.BrowserUrl AND StartTime >= $start
                        ORDER BY StartTime DESC LIMIT 1) as WindowTitle,
                       COUNT(*) as VisitCount,
                       SUM(a.DurationSeconds) as TotalSeconds
                FROM ActivityLog a
                WHERE a.BrowserUrl IS NOT NULL
                AND a.BrowserUrl != ''
                AND a.StartTime >= $start
                AND ($end IS NULL OR a.StartTime < $end)
                AND (a.DurationSeconds > 0 OR a.EndTime IS NULL)
                GROUP BY a.BrowserUrl
                ORDER BY TotalSeconds DESC;
                """;
            cmd.Parameters.AddWithValue("$start", start.ToString("O"));
            cmd.Parameters.AddWithValue("$end", (object?)end?.ToString("O") ?? DBNull.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new UrlSummary
                {
                    Url = reader.GetString(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    VisitCount = reader.GetInt32(2),
                    TotalSeconds = reader.GetInt64(3)
                });
            }
        }
        finally
        {
            _writeLock.Release();
        }

        return results;
    }

    public List<TimelineEntry> GetTimeline(DateFilter filter)
    {
        var (start, end) = GetDateRange(filter);
        var results = new List<TimelineEntry>();

        _writeLock.Wait();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT AppName, WindowTitle, StartTime,
                       COALESCE(EndTime, $now) as EndTime,
                       DurationSeconds, ProcessPath
                FROM ActivityLog
                WHERE StartTime >= $start
                AND ($end IS NULL OR StartTime < $end)
                AND (DurationSeconds > 0 OR EndTime IS NULL)
                ORDER BY StartTime ASC;
                """;
            cmd.Parameters.AddWithValue("$start", start.ToString("O"));
            cmd.Parameters.AddWithValue("$end", (object?)end?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("O"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new TimelineEntry
                {
                    AppName = reader.GetString(0),
                    WindowTitle = reader.GetString(1),
                    StartTime = DateTime.Parse(reader.GetString(2)),
                    EndTime = DateTime.Parse(reader.GetString(3)),
                    DurationSeconds = reader.GetInt32(4),
                    ProcessPath = reader.IsDBNull(5) ? null : reader.GetString(5)
                });
            }
        }
        finally
        {
            _writeLock.Release();
        }

        return results;
    }

    internal static (DateTime start, DateTime? end) GetDateRange(DateFilter filter)
    {
        var now = DateTime.Now;
        return filter switch
        {
            DateFilter.Today => (now.Date, null),
            DateFilter.Yesterday => (now.Date.AddDays(-1), now.Date),
            DateFilter.Last7Days => (now.Date.AddDays(-6), now.Date.AddDays(1)),
            DateFilter.Last30Days => (now.Date.AddDays(-29), now.Date.AddDays(1)),
            _ => (now.Date, null)
        };
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
        _writeLock?.Dispose();
    }
}

public enum DateFilter
{
    Today,
    Yesterday,
    Last7Days,
    Last30Days
}
