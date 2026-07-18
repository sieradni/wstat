using FluentAssertions;
using NSubstitute;
using Xunit;
using Wstat.Desktop.Models;
using Wstat.Desktop.Services;
using Wstat.Desktop.ViewModels;

namespace Wstat.Desktop.Tests;

public class DashboardViewModelTests
{
    private readonly IDatabaseService _db = Substitute.For<IDatabaseService>();

    public DashboardViewModelTests()
    {
        _db.GetAppSummary(Arg.Any<DateFilter>(), Arg.Any<DateTime?>())
            .Returns([]);
        _db.GetUrlSummary(Arg.Any<DateFilter>(), Arg.Any<DateTime?>())
            .Returns([]);
        _db.GetTimeline(Arg.Any<DateFilter>(), Arg.Any<DateTime?>())
            .Returns([]);
    }

    [Fact]
    public void Constructor_loads_today_data()
    {
        DashboardViewModel vm = null!;
        StaRunner.Run(() =>
        {
            vm = new DashboardViewModel(_db);
        });

        _db.Received(1).GetAppSummary(DateFilter.Today, Arg.Any<DateTime?>());
        _db.Received(1).GetUrlSummary(DateFilter.Today, Arg.Any<DateTime?>());
        _db.Received(1).GetTimeline(DateFilter.Today, Arg.Any<DateTime?>());
    }

    [Fact]
    public void FilterTodayCommand_loads_today()
    {
        DashboardViewModel vm = null!;
        StaRunner.Run(() =>
        {
            vm = new DashboardViewModel(_db);
            vm.FilterYesterdayCommand.Execute(null);
            _db.ClearReceivedCalls();
            vm.FilterTodayCommand.Execute(null);
        });

        _db.Received(1).GetAppSummary(DateFilter.Today, Arg.Any<DateTime?>());
    }

    [Fact]
    public void FilterYesterdayCommand_loads_yesterday()
    {
        DashboardViewModel vm = null!;
        StaRunner.Run(() =>
        {
            vm = new DashboardViewModel(_db);
            _db.ClearReceivedCalls();
            vm.FilterYesterdayCommand.Execute(null);
        });

        _db.Received(1).GetAppSummary(DateFilter.Yesterday, Arg.Any<DateTime?>());
    }

    [Fact]
    public void FilterLast7DaysCommand_loads_last7()
    {
        DashboardViewModel vm = null!;
        StaRunner.Run(() =>
        {
            vm = new DashboardViewModel(_db);
            _db.ClearReceivedCalls();
            vm.FilterLast7DaysCommand.Execute(null);
        });

        _db.Received(1).GetAppSummary(DateFilter.Last7Days, Arg.Any<DateTime?>());
    }

    [Fact]
    public void FilterLast30DaysCommand_loads_last30()
    {
        DashboardViewModel vm = null!;
        StaRunner.Run(() =>
        {
            vm = new DashboardViewModel(_db);
            _db.ClearReceivedCalls();
            vm.FilterLast30DaysCommand.Execute(null);
        });

        _db.Received(1).GetAppSummary(DateFilter.Last30Days, Arg.Any<DateTime?>());
    }

    [Fact]
    public void FilterSpecificCommand_loads_with_specific_date()
    {
        var specificDate = new DateTime(2026, 6, 15);
        DashboardViewModel vm = null!;
        StaRunner.Run(() =>
        {
            vm = new DashboardViewModel(_db);
            vm.SpecificDate = specificDate;
            _db.ClearReceivedCalls();
            vm.FilterSpecificCommand.Execute(null);
        });

        _db.Received(1).GetAppSummary(DateFilter.Specific, specificDate);
    }

    [Fact]
    public void IsTodaySelected_initial_state()
    {
        DashboardViewModel vm = null!;
        StaRunner.Run(() =>
        {
            vm = new DashboardViewModel(_db);
        });

        vm.IsTodaySelected.Should().BeTrue();
        vm.IsYesterdaySelected.Should().BeFalse();
        vm.IsLast7DaysSelected.Should().BeFalse();
        vm.IsLast30DaysSelected.Should().BeFalse();
        vm.IsSpecificSelected.Should().BeFalse();
    }

    [Fact]
    public void RefreshSummary_loads_apps_and_urls()
    {
        DashboardViewModel vm = null!;
        StaRunner.Run(() =>
        {
            vm = new DashboardViewModel(_db);
            _db.ClearReceivedCalls();
            vm.RefreshSummary();
        });

        _db.Received(1).GetAppSummary(Arg.Any<DateFilter>(), Arg.Any<DateTime?>());
        _db.Received(1).GetUrlSummary(Arg.Any<DateFilter>(), Arg.Any<DateTime?>());
    }
}
