using System.Globalization;
using Microsoft.Data.Sqlite;
using Wstat.Desktop.Common;
using Wstat.Desktop.Models;

namespace Wstat.Desktop.Services;

public class DatabaseService : IDatabaseService, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ReaderWriterLockSlim _rwLock = new();
    private readonly IClock _clock;

    public DatabaseService(IClock clock)
        : this($"Data Source={AppPaths.DbPath}", clock)
    {
    }

    internal DatabaseService(string connectionString)
        : this(connectionString, new SystemClock())
    {
    }

    internal DatabaseService(string connectionString, IClock clock)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        _clock = clock;
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
        catch
        {
            LogWriter.Write("[Schema] Column ProcessPath already exists (migration skipped)");
        }

        using var idx1 = _connection.CreateCommand();
        idx1.CommandText = "CREATE INDEX IF NOT EXISTS IX_ActivityLog_StartTime ON ActivityLog(StartTime);";
        idx1.ExecuteNonQuery();

        using var idx2 = _connection.CreateCommand();
        idx2.CommandText = "CREATE INDEX IF NOT EXISTS IX_ActivityLog_AppName ON ActivityLog(AppName, StartTime);";
        idx2.ExecuteNonQuery();

        using var idx3 = _connection.CreateCommand();
        idx3.CommandText = "CREATE INDEX IF NOT EXISTS IX_ActivityLog_BrowserUrl ON ActivityLog(BrowserUrl, StartTime);";
        idx3.ExecuteNonQuery();
    }

    public void InsertOrUpdateActive(ActivityRecord record)
    {
        _rwLock.EnterWriteLock();
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
                cmd.Parameters.AddWithValue("$startTime", record.StartTime.ToString(Constants.IsoFormat));

                var result = cmd.ExecuteScalar();
                record.Id = Convert.ToInt32(result);
            }
            else
            {
                var elapsed = (int)(_clock.Now - record.StartTime).TotalSeconds;
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
            _rwLock.ExitWriteLock();
        }
    }

    public void CloseActive(ActivityRecord record)
    {
        if (record.Id == 0) return;

        record.EndTime = _clock.Now;
        record.DurationSeconds = Math.Max(0, (int)(record.EndTime.Value - record.StartTime).TotalSeconds);

        _rwLock.EnterWriteLock();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE ActivityLog
                SET EndTime = $endTime, DurationSeconds = $duration
                WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$endTime", record.EndTime.Value.ToString(Constants.IsoFormat));
            cmd.Parameters.AddWithValue("$duration", record.DurationSeconds);
            cmd.Parameters.AddWithValue("$id", record.Id);
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public void CloseOrphanedRecords()
    {
        _rwLock.EnterWriteLock();
        try
        {
            var orphans = new List<(int id, DateTime startTime, int durationSeconds)>();
            using (var selectCmd = _connection.CreateCommand())
            {
                selectCmd.CommandText = "SELECT Id, StartTime, DurationSeconds FROM ActivityLog WHERE EndTime IS NULL;";
                using var reader = selectCmd.ExecuteReader();
                while (reader.Read())
                {
                    orphans.Add((
                        reader.GetInt32(0),
                        DateTime.ParseExact(reader.GetString(1), Constants.IsoFormat, CultureInfo.InvariantCulture),
                        reader.GetInt32(2)
                    ));
                }
            }

            foreach (var (id, startTime, durationSeconds) in orphans)
            {
                var finalDuration = durationSeconds > 0 ? durationSeconds : 1;
                var endTime = startTime.AddSeconds(finalDuration);

                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "UPDATE ActivityLog SET EndTime = $endTime, DurationSeconds = $duration WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$endTime", endTime.ToString(Constants.IsoFormat));
                cmd.Parameters.AddWithValue("$duration", finalDuration);
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public int DeleteRecordsForDay(DateTime day)
    {
        var start = day.Date;
        var end = start.AddDays(1);

        _rwLock.EnterWriteLock();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                DELETE FROM ActivityLog
                WHERE StartTime >= $start AND StartTime < $end;
                """;
            cmd.Parameters.AddWithValue("$start", start.ToString(Constants.IsoFormat));
            cmd.Parameters.AddWithValue("$end", end.ToString(Constants.IsoFormat));
            return cmd.ExecuteNonQuery();
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public int DeleteProblematicRecordsForDay(DateTime day)
    {
        var start = day.Date;
        var end = start.AddDays(1);
        var cutoff = _clock.Now.AddHours(-1);

        _rwLock.EnterWriteLock();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                DELETE FROM ActivityLog
                WHERE StartTime >= $start AND StartTime < $end
                AND (DurationSeconds <= 0 OR (EndTime IS NULL AND StartTime < $cutoff));
                """;
            cmd.Parameters.AddWithValue("$start", start.ToString(Constants.IsoFormat));
            cmd.Parameters.AddWithValue("$end", end.ToString(Constants.IsoFormat));
            cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString(Constants.IsoFormat));
            return cmd.ExecuteNonQuery();
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public void UpdateBrowserUrl(int recordId, string url)
    {
        _rwLock.EnterWriteLock();
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
            _rwLock.ExitWriteLock();
        }
    }

    public List<AppSummary> GetAppSummary(DateFilter filter, DateTime? specificDate = null)
    {
        var (start, end) = GetDateRange(filter, specificDate);
        var results = new List<AppSummary>();

        _rwLock.EnterReadLock();
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
            cmd.Parameters.AddWithValue("$start", start.ToString(Constants.IsoFormat));
            cmd.Parameters.AddWithValue("$end", (object?)end?.ToString(Constants.IsoFormat) ?? DBNull.Value);

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
            _rwLock.ExitReadLock();
        }

        return results;
    }

    public List<UrlSummary> GetUrlSummary(DateFilter filter, DateTime? specificDate = null)
    {
        var (start, end) = GetDateRange(filter, specificDate);
        var results = new List<UrlSummary>();

        _rwLock.EnterReadLock();
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
            cmd.Parameters.AddWithValue("$start", start.ToString(Constants.IsoFormat));
            cmd.Parameters.AddWithValue("$end", (object?)end?.ToString(Constants.IsoFormat) ?? DBNull.Value);

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
            _rwLock.ExitReadLock();
        }

        return results;
    }

    public List<TimelineEntry> GetTimeline(DateFilter filter, DateTime? specificDate = null)
    {
        var (start, end) = GetDateRange(filter, specificDate);
        var results = new List<TimelineEntry>();

        _rwLock.EnterReadLock();
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
            cmd.Parameters.AddWithValue("$start", start.ToString(Constants.IsoFormat));
            cmd.Parameters.AddWithValue("$end", (object?)end?.ToString(Constants.IsoFormat) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", _clock.Now.ToString(Constants.IsoFormat));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new TimelineEntry
                {
                    AppName = reader.GetString(0),
                    WindowTitle = reader.GetString(1),
                    StartTime = DateTime.ParseExact(reader.GetString(2), Constants.IsoFormat, CultureInfo.InvariantCulture),
                    EndTime = DateTime.ParseExact(reader.GetString(3), Constants.IsoFormat, CultureInfo.InvariantCulture),
                    DurationSeconds = reader.GetInt32(4),
                    ProcessPath = reader.IsDBNull(5) ? null : reader.GetString(5)
                });
            }
        }
        finally
        {
            _rwLock.ExitReadLock();
        }

        return results;
    }

    internal (DateTime start, DateTime? end) GetDateRange(DateFilter filter, DateTime? specificDate = null)
    {
        var now = _clock.Now;
        return filter switch
        {
            DateFilter.Today => (now.Date, null),
            DateFilter.Yesterday => (now.Date.AddDays(-1), now.Date),
            DateFilter.Last7Days => (now.Date.AddDays(-6), now.Date.AddDays(1)),
            DateFilter.Last30Days => (now.Date.AddDays(-29), now.Date.AddDays(1)),
            DateFilter.Specific => specificDate.HasValue
                ? (specificDate.Value.Date, specificDate.Value.Date.AddDays(1))
                : (now.Date, null),
            _ => (now.Date, null)
        };
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
        _rwLock?.Dispose();
    }
}

public enum DateFilter
{
    Today,
    Yesterday,
    Last7Days,
    Last30Days,
    Specific
}
