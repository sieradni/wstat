using System.IO;
using FluentAssertions;
using Xunit;
using Wstat.Desktop.Common;
using Wstat.Desktop.Models;
using Wstat.Desktop.ViewModels;

namespace Wstat.Desktop.Tests;

[Collection("AppPaths")]
public class SettingsViewModelTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "wstat_test_" + Guid.NewGuid());

    public SettingsViewModelTests()
    {
        AppPaths.BaseDir = _tempDir;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Save_valid_values_clears_error()
    {
        var settings = new SettingsModel();
        var vm = new SettingsViewModel(settings);

        vm.SaveCommand.Execute(null);

        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Save_invalid_port_sets_error()
    {
        var settings = new SettingsModel();
        var vm = new SettingsViewModel(settings);
        vm.HttpPort = "99999";

        vm.SaveCommand.Execute(null);

        vm.ErrorMessage.Should().NotBeNull();
        vm.ErrorMessage.Should().Contain("Port");
    }

    [Fact]
    public void Save_non_numeric_port_sets_error()
    {
        var settings = new SettingsModel();
        var vm = new SettingsViewModel(settings);
        vm.HttpPort = "abc";

        vm.SaveCommand.Execute(null);

        vm.ErrorMessage.Should().NotBeNull();
    }

    [Fact]
    public void Save_invalid_poll_interval_sets_error()
    {
        var settings = new SettingsModel();
        var vm = new SettingsViewModel(settings);
        vm.PollIntervalMs = "100";

        vm.SaveCommand.Execute(null);

        vm.ErrorMessage.Should().NotBeNull();
        vm.ErrorMessage.Should().Contain("Poll");
    }

    [Fact]
    public void Save_invalid_idle_threshold_sets_error()
    {
        var settings = new SettingsModel();
        var vm = new SettingsViewModel(settings);
        vm.IdleThresholdMs = "500";

        vm.SaveCommand.Execute(null);

        vm.ErrorMessage.Should().NotBeNull();
        vm.ErrorMessage.Should().Contain("Idle");
    }

    [Fact]
    public void Save_restart_required_when_port_changes()
    {
        var settings = new SettingsModel { HttpPort = 12345 };
        var vm = new SettingsViewModel(settings);
        vm.HttpPort = "9090";

        vm.SaveCommand.Execute(null);

        vm.RestartRequired.Should().BeTrue();
    }

    [Fact]
    public void Save_restart_not_required_when_only_auto_startup_changes()
    {
        var settings = new SettingsModel();
        var vm = new SettingsViewModel(settings);
        vm.AutoStartup = true;

        vm.SaveCommand.Execute(null);

        vm.RestartRequired.Should().BeFalse();
    }

    [Fact]
    public void Save_has_changes_true_when_value_modified()
    {
        var settings = new SettingsModel();
        var vm = new SettingsViewModel(settings);
        vm.HttpPort = "9090";

        vm.SaveCommand.Execute(null);

        vm.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void Save_has_changes_false_when_no_modifications()
    {
        var settings = new SettingsModel();
        var vm = new SettingsViewModel(settings);

        vm.SaveCommand.Execute(null);

        vm.HasChanges.Should().BeFalse();
    }
}
