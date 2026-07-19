using FluentAssertions;
using NSubstitute;
using Xunit;
using Wstat.Desktop.Models;
using Wstat.Desktop.Native;
using Wstat.Desktop.Services;

#pragma warning disable CA2000

namespace Wstat.Desktop.Tests;

public class WindowTrackerServiceTests : IDisposable
{
    private readonly IDatabaseService _db = Substitute.For<IDatabaseService>();
    private readonly IWin32Api _win32 = Substitute.For<IWin32Api>();
    private readonly SettingsModel _settings = new();
    private readonly WindowTrackerService _sut;
    private readonly List<ActivityRecord> _recordUpdates = [];
    private readonly List<bool> _idleChanges = [];
    private int _nextId;

    public WindowTrackerServiceTests()
    {
        _sut = new WindowTrackerService(_db, _settings, _win32);
        _sut.RecordUpdated += r => _recordUpdates.Add(r);
        _sut.IdleStateChanged += i => _idleChanges.Add(i);

        _db.When(x => x.InsertOrUpdateActive(Arg.Any<ActivityRecord>()))
            .Do(ci =>
            {
                var r = ci.Arg<ActivityRecord>();
                if (r.Id == 0) r.Id = Interlocked.Increment(ref _nextId);
            });
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    [Fact]
    public void First_tick_starts_debounce_no_db_write()
    {
        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: false);

        _recordUpdates.Should().BeEmpty();
        _idleChanges.Should().BeEmpty();
    }

    [Fact]
    public void Second_tick_same_process_commits_new_record()
    {
        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: false);

        _db.Received(1).InsertOrUpdateActive(Arg.Is<ActivityRecord>(r => r.AppName == "app.exe"));
        _recordUpdates.Should().HaveCount(1);
        _recordUpdates[0].AppName.Should().Be("app.exe");
    }

    [Fact]
    public void Third_tick_same_process_updates_duration()
    {
        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: false);
        _recordUpdates.Clear();
        _db.ClearReceivedCalls();

        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: false);

        _recordUpdates.Should().ContainSingle();
        _recordUpdates[0].AppName.Should().Be("app.exe");
    }

    [Fact]
    public void Title_change_triggers_update()
    {
        _sut.ProcessTickAsync(@"C:\app.exe", "Old Title", "app.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\app.exe", "Old Title", "app.exe", isIdle: false);
        _recordUpdates.Clear();
        _db.ClearReceivedCalls();

        _sut.ProcessTickAsync(@"C:\app.exe", "New Title", "app.exe", isIdle: false);

        _db.Received(1).InsertOrUpdateActive(Arg.Is<ActivityRecord>(r => r.WindowTitle == "New Title"));
    }

    [Fact]
    public void Idle_enter_closes_record_and_fires_event()
    {
        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: false);
        _recordUpdates.Clear();
        _db.ClearReceivedCalls();

        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: true);

        _db.Received(1).CloseActive(Arg.Any<ActivityRecord>());
        _idleChanges.Should().ContainSingle().Which.Should().BeTrue();
    }

    [Fact]
    public void Idle_exit_starts_new_record()
    {
        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: true);
        _recordUpdates.Clear();
        _idleChanges.Clear();
        _db.ClearReceivedCalls();

        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: false);

        _db.Received(1).InsertOrUpdateActive(Arg.Any<ActivityRecord>());
        _idleChanges.Should().ContainSingle().Which.Should().BeFalse();
    }

    [Fact]
    public void Switch_process_starts_debounce_no_commit_yet()
    {
        _sut.ProcessTickAsync(@"C:\app1.exe", "App1", "app1.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\app1.exe", "App1", "app1.exe", isIdle: false);
        _db.ClearReceivedCalls();

        _sut.ProcessTickAsync(@"C:\app2.exe", "App2", "app2.exe", isIdle: false);

        _db.Received(1).InsertOrUpdateActive(Arg.Is<ActivityRecord>(r => r.AppName == "app1.exe"));
        _sut.CurrentRecord?.AppName.Should().Be("app1.exe");
    }

    [Fact]
    public void Debounce_commit_after_two_ticks_of_new_process()
    {
        _sut.ProcessTickAsync(@"C:\app1.exe", "App1", "app1.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\app1.exe", "App1", "app1.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\app2.exe", "App2", "app2.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\app2.exe", "App2", "app2.exe", isIdle: false);

        _recordUpdates.Should().HaveCount(3);
        _recordUpdates[2].AppName.Should().Be("app2.exe");
    }

    [Fact]
    public void Third_different_process_during_debounce_commits_immediately()
    {
        _sut.ProcessTickAsync(@"C:\app1.exe", "App1", "app1.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\app1.exe", "App1", "app1.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\app2.exe", "App2", "app2.exe", isIdle: false);
        _recordUpdates.Clear();

        _sut.ProcessTickAsync(@"C:\app3.exe", "App3", "app3.exe", isIdle: false);

        _recordUpdates.Should().ContainSingle();
        _recordUpdates[0].AppName.Should().Be("app3.exe");
    }

    [Fact]
    public void Firefox_browser_url_applied_on_new_record()
    {
        _sut.SetBrowserTab("https://example.com", "Example Page");

        _sut.ProcessTickAsync(@"C:\firefox.exe", "Mozilla Firefox", "firefox.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\firefox.exe", "Mozilla Firefox", "firefox.exe", isIdle: false);

        _recordUpdates.Should().ContainSingle();
        _recordUpdates[0].BrowserUrl.Should().Be("https://example.com");
        _recordUpdates[0].WindowTitle.Should().Be("Example Page");
    }

    [Fact]
    public void SetBrowserTab_updates_current_firefox_record()
    {
        _sut.ProcessTickAsync(@"C:\firefox.exe", "Mozilla Firefox", "firefox.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\firefox.exe", "Mozilla Firefox", "firefox.exe", isIdle: false);
        _recordUpdates.Clear();
        _db.ClearReceivedCalls();

        _sut.SetBrowserTab("https://example.com", "Example Page");

        _recordUpdates.Should().ContainSingle();
        _recordUpdates[0].BrowserUrl.Should().Be("https://example.com");
        _recordUpdates[0].WindowTitle.Should().Be("Example Page");
    }

    [Fact]
    public void Non_http_url_skipped_by_SetBrowserTab()
    {
        _sut.ProcessTickAsync(@"C:\firefox.exe", "Mozilla Firefox", "firefox.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\firefox.exe", "Mozilla Firefox", "firefox.exe", isIdle: false);
        _recordUpdates.Clear();

        _sut.SetBrowserTab("about:config", "Config");

        _recordUpdates.Should().BeEmpty();
    }

    [Fact]
    public void IsIdle_reflects_current_state()
    {
        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: true);

        _sut.IsIdle.Should().BeTrue();
    }

    [Fact]
    public void CurrentRecord_is_null_after_idle_close()
    {
        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: false);
        _sut.ProcessTickAsync(@"C:\app.exe", "Window", "app.exe", isIdle: true);

        _sut.CurrentRecord.Should().BeNull();
    }

}
