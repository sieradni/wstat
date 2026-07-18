using System.IO;
using FluentAssertions;
using Xunit;
using Wstat.Desktop.Common;
using Wstat.Desktop.Services;

namespace Wstat.Desktop.Tests;

public class SettingsManagerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "wstat_test_" + Guid.NewGuid());

    public SettingsManagerTests()
    {
        AppPaths.BaseDir = _tempDir;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Load_returns_defaults_when_no_file()
    {
        var settings = SettingsManager.Load();

        settings.HttpPort.Should().Be(12345);
        settings.PollIntervalMs.Should().Be(2000);
        settings.IdleThresholdMs.Should().Be(300_000);
        settings.AutoStartup.Should().BeFalse();
    }

    [Fact]
    public void Save_creates_file()
    {
        var settings = new Wstat.Desktop.Models.SettingsModel
        {
            HttpPort = 9999,
            PollIntervalMs = 5000,
            IdleThresholdMs = 600_000,
            AutoStartup = true
        };

        SettingsManager.Save(settings);

        File.Exists(AppPaths.SettingsPath).Should().BeTrue();
    }

    [Fact]
    public void Round_trip_preserves_values()
    {
        var original = new Wstat.Desktop.Models.SettingsModel
        {
            HttpPort = 9999,
            PollIntervalMs = 5000,
            IdleThresholdMs = 600_000,
            AutoStartup = true
        };

        SettingsManager.Save(original);
        var loaded = SettingsManager.Load();

        loaded.HttpPort.Should().Be(9999);
        loaded.PollIntervalMs.Should().Be(5000);
        loaded.IdleThresholdMs.Should().Be(600_000);
        loaded.AutoStartup.Should().BeTrue();
    }

    [Fact]
    public void Load_corrupt_file_returns_defaults()
    {
        File.WriteAllText(AppPaths.SettingsPath, "not valid json {{{");

        var settings = SettingsManager.Load();

        settings.HttpPort.Should().Be(12345);
    }

    [Fact]
    public void Save_then_load_customizes_only_some_values()
    {
        var original = new Wstat.Desktop.Models.SettingsModel
        {
            HttpPort = 8080
        };

        SettingsManager.Save(original);
        var loaded = SettingsManager.Load();

        loaded.HttpPort.Should().Be(8080);
        loaded.PollIntervalMs.Should().Be(2000);
        loaded.IdleThresholdMs.Should().Be(300_000);
        loaded.AutoStartup.Should().BeFalse();
    }
}
