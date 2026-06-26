using System.IO;
using Microsoft.Data.Sqlite;
using Wstat.Desktop.Models;

namespace Wstat.Desktop.Services;

public class DatabaseService : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _connectionString;

    public DatabaseService()
    {
        var dbDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "wstat");

        Directory.CreateDirectory(dbDir);

        var dbPath = Path.Combine(dbDir, "wstat.db");
        _connectionString = $"Data Source={dbPath}";
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ActivityLog (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AppName TEXT NOT NULL,
                WindowTitle TEXT NOT NULL,
                BrowserUrl TEXT,
                StartTime TEXT NOT NULL,
                EndTime TEXT,
                DurationSeconds INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS IX_ActivityLog_StartTime ON ActivityLog(StartTime);
            """;
        cmd.ExecuteNonQuery();
    }

    public void InsertOrUpdateActive(ActivityRecord record)
    {
        using var cmd = _connection.CreateCommand();

        if (record.Id == 0)
        {
            cmd.CommandText = """
                INSERT INTO ActivityLog (AppName, WindowTitle, BrowserUrl, StartTime, EndTime, DurationSeconds)
                VALUES ($appName, $windowTitle, $browserUrl, $startTime, NULL, 0);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$appName", record.AppName);
            cmd.Parameters.AddWithValue("$windowTitle", record.WindowTitle);
            cmd.Parameters.AddWithValue("$browserUrl", (object?)record.BrowserUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$startTime", record.StartTime.ToString("O"));

            var result = cmd.ExecuteScalar();
            record.Id = Convert.ToInt32(result);
        }
        else
        {
            var elapsed = (int)(DateTime.Now - record.StartTime).TotalSeconds;
            cmd.CommandText = """
                UPDATE ActivityLog
                SET EndTime = $endTime, DurationSeconds = $duration
                WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$endTime", DateTime.Now.ToString("O"));
            cmd.Parameters.AddWithValue("$duration", elapsed);
            cmd.Parameters.AddWithValue("$id", record.Id);
            cmd.ExecuteNonQuery();
        }
    }

    public void CloseActive(ActivityRecord record)
    {
        if (record.Id == 0) return;

        record.EndTime = DateTime.Now;
        record.DurationSeconds = (int)(record.EndTime.Value - record.StartTime).TotalSeconds;

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

    public void UpdateBrowserUrl(int recordId, string url)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE ActivityLog SET BrowserUrl = $url WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$id", recordId);
        cmd.ExecuteNonQuery();
    }

    public List<AppSummary> GetAppSummary(DateFilter filter)
    {
        var (start, end) = GetDateRange(filter);
        var results = new List<AppSummary>();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT AppName, SUM(DurationSeconds) as TotalSeconds
            FROM ActivityLog
            WHERE StartTime >= $start
            AND ($end IS NULL OR StartTime < $end)
            GROUP BY AppName
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
                TotalSeconds = reader.GetInt64(1)
            });
        }

        return results;
    }

    public List<UrlSummary> GetUrlSummary(DateFilter filter)
    {
        var (start, end) = GetDateRange(filter);
        var results = new List<UrlSummary>();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT BrowserUrl, WindowTitle, COUNT(*) as VisitCount, SUM(DurationSeconds) as TotalSeconds
            FROM ActivityLog
            WHERE BrowserUrl IS NOT NULL
            AND BrowserUrl != ''
            AND StartTime >= $start
            AND ($end IS NULL OR StartTime < $end)
            GROUP BY BrowserUrl
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

        return results;
    }

    private static (DateTime start, DateTime? end) GetDateRange(DateFilter filter)
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
    }
}

public enum DateFilter
{
    Today,
    Yesterday,
    Last7Days,
    Last30Days
}
