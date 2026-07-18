using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;
using Wstat.Desktop.Common;
using Wstat.Desktop.Models;
using Wstat.Desktop.Services;
using Wstat.Desktop.Views;

namespace Wstat.Desktop.ViewModels;

public class DashboardViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly Dictionary<string, BitmapSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxIconCacheSize = 64;

    private readonly IDatabaseService _db;
    private readonly SettingsModel _settings;
    private readonly DispatcherTimer _refreshTimer;
    private DateFilter _selectedFilter = DateFilter.Today;
    private DateTime _specificDate = DateTime.Now.AddDays(-1);
    private bool _disposed;

    public ObservableCollection<AppSummary> Applications { get; } = new();
    public ObservableCollection<UrlSummary> TopUrls { get; } = new();
    public List<TimelineEntry> TimelineEntries { get; private set; } = [];

    public DateFilter SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (_selectedFilter != value)
            {
                _selectedFilter = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTodaySelected));
                OnPropertyChanged(nameof(IsYesterdaySelected));
                OnPropertyChanged(nameof(IsLast7DaysSelected));
                OnPropertyChanged(nameof(IsLast30DaysSelected));
                OnPropertyChanged(nameof(IsSpecificSelected));
                OnPropertyChanged(nameof(IsNotSpecificSelected));
                LoadAll();
            }
        }
    }

    public DateTime SpecificDate
    {
        get => _specificDate;
        set
        {
            if (_specificDate != value)
            {
                _specificDate = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsTodaySelected => _selectedFilter == DateFilter.Today;
    public bool IsYesterdaySelected => _selectedFilter == DateFilter.Yesterday;
    public bool IsLast7DaysSelected => _selectedFilter == DateFilter.Last7Days;
    public bool IsLast30DaysSelected => _selectedFilter == DateFilter.Last30Days;
    public bool IsSpecificSelected => _selectedFilter == DateFilter.Specific;
    public bool IsNotSpecificSelected => _selectedFilter != DateFilter.Specific;

    public RelayCommand FilterTodayCommand { get; }
    public RelayCommand FilterYesterdayCommand { get; }
    public RelayCommand FilterLast7DaysCommand { get; }
    public RelayCommand FilterLast30DaysCommand { get; }
    public RelayCommand FilterSpecificCommand { get; }
    public RelayCommand ClearDayCommand { get; }
    public RelayCommand ClearProblematicCommand { get; }
    public RelayCommand ExportCsvCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }

    public event Action? TimelineUpdated;
    public event Action? ApplicationsUpdated;

    public DashboardViewModel(IDatabaseService db, SettingsModel settings)
    {
        _db = db;
        _settings = settings;

        FilterTodayCommand = new RelayCommand(() => SelectedFilter = DateFilter.Today);
        FilterYesterdayCommand = new RelayCommand(() => SelectedFilter = DateFilter.Yesterday);
        FilterLast7DaysCommand = new RelayCommand(() => SelectedFilter = DateFilter.Last7Days);
        FilterLast30DaysCommand = new RelayCommand(() => SelectedFilter = DateFilter.Last30Days);
        FilterSpecificCommand = new RelayCommand(() => SelectedFilter = DateFilter.Specific);
        ClearDayCommand = new RelayCommand(ClearDay);
        ClearProblematicCommand = new RelayCommand(ClearProblematic);
        ExportCsvCommand = new RelayCommand(ExportCsv);
        ShowSettingsCommand = new RelayCommand(ShowSettings);

        BindingOperations.EnableCollectionSynchronization(Applications, new object());
        BindingOperations.EnableCollectionSynchronization(TopUrls, new object());

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += (_, _) => RefreshSummary();
        _refreshTimer.Start();

        LoadAll();
    }

    public void LoadAll()
    {
        LoadApps();
        LoadUrls();
        LoadTimeline();
    }

    public void RefreshSummary()
    {
        LoadApps();
        LoadUrls();
        if (_selectedFilter is DateFilter.Today or DateFilter.Specific)
            LoadTimeline();
    }

    private DateTime? SpecificDateOrNull => _selectedFilter == DateFilter.Specific ? _specificDate : null;

    private void LoadApps()
    {
        var apps = _db.GetAppSummary(_selectedFilter, SpecificDateOrNull);
        Applications.Clear();
        foreach (var app in apps)
        {
            app.Icon = GetOrLoadIcon(app.ProcessPath);
            Applications.Add(app);
        }
        ApplicationsUpdated?.Invoke();
    }

    private void LoadUrls()
    {
        var urls = _db.GetUrlSummary(_selectedFilter, SpecificDateOrNull);
        TopUrls.Clear();
        foreach (var url in urls)
            TopUrls.Add(url);
    }

    private void LoadTimeline()
    {
        var raw = _db.GetTimeline(_selectedFilter, SpecificDateOrNull);
        var colorIndex = new Dictionary<string, MediaColor>(StringComparer.OrdinalIgnoreCase);
        var paletteIdx = 0;

        foreach (var entry in raw)
        {
            if (!colorIndex.TryGetValue(entry.AppName, out var color))
            {
                color = TimelineColors[paletteIdx % TimelineColors.Length];
                colorIndex[entry.AppName] = color;
                paletteIdx++;
            }
            entry.AppColor = color;
            entry.TitleColor = Lighten(color, 0.15);
        }

        TimelineEntries = raw;
        TimelineUpdated?.Invoke();
    }

    public static bool TryGetIcon(string? processPath, out BitmapSource? icon)
    {
        icon = null;
        if (string.IsNullOrEmpty(processPath) || !File.Exists(processPath))
            return false;

        if (IconCache.TryGetValue(processPath, out var cached))
        {
            icon = cached;
            return cached != null;
        }

        try
        {
            using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
            if (sysIcon == null) return false;

            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                sysIcon.Handle,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            source.Freeze();
            if (IconCache.Count >= MaxIconCacheSize)
                IconCache.Clear();
            IconCache[processPath] = source;
            icon = source;
            return true;
        }
        catch (Exception ex)
        {
            LogWriter.Write("[Icon] Extract error for " + processPath + ": " + ex.Message);
            IconCache[processPath] = null;
            return false;
        }
    }

    private static BitmapSource? GetOrLoadIcon(string? processPath)
    {
        TryGetIcon(processPath, out var icon);
        return icon;
    }

    private static MediaColor Lighten(MediaColor color, double amount)
    {
        return MediaColor.FromRgb(
            (byte)Math.Min(255, color.R + 255 * amount),
            (byte)Math.Min(255, color.G + 255 * amount),
            (byte)Math.Min(255, color.B + 255 * amount));
    }

    private static readonly MediaColor[] TimelineColors =
    [
        MediaColor.FromRgb(0x42, 0x85, 0xF4),
        MediaColor.FromRgb(0xEA, 0x43, 0x35),
        MediaColor.FromRgb(0x34, 0xA8, 0x53),
        MediaColor.FromRgb(0xFB, 0xBC, 0x04),
        MediaColor.FromRgb(0xAB, 0x47, 0xBC),
        MediaColor.FromRgb(0x00, 0x96, 0x88),
        MediaColor.FromRgb(0xFF, 0x6F, 0x00),
        MediaColor.FromRgb(0x8E, 0x24, 0xAA),
        MediaColor.FromRgb(0x00, 0x89, 0x4B),
        MediaColor.FromRgb(0xE9, 0x1E, 0x63),
        MediaColor.FromRgb(0x00, 0x76, 0xD4),
        MediaColor.FromRgb(0x6D, 0x4C, 0x41),
    ];

    private void ClearDay()
    {
        if (_selectedFilter != DateFilter.Specific) return;

        var result = System.Windows.MessageBox.Show(
            $"Delete ALL activity records for {_specificDate:yyyy-MM-dd}?\nThis cannot be undone.",
            "Clear Day",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var count = _db.DeleteRecordsForDay(_specificDate);
        LogWriter.Write($"[Clear] Deleted {count} records for {_specificDate:yyyy-MM-dd}");
        LoadAll();
    }

    private void ClearProblematic()
    {
        if (_selectedFilter != DateFilter.Specific) return;

        var result = System.Windows.MessageBox.Show(
            $"Delete problematic records (DurationSeconds <= 0 or orphaned) for {_specificDate:yyyy-MM-dd}?",
            "Clear Problematic Records",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var count = _db.DeleteProblematicRecordsForDay(_specificDate);
        LogWriter.Write($"[Clear] Deleted {count} problematic records for {_specificDate:yyyy-MM-dd}");
        LoadAll();
    }

    private void ShowSettings()
    {
        var vm = new SettingsViewModel(_settings);
        var window = new SettingsWindow(vm);
        window.Owner = System.Windows.Application.Current.MainWindow;
        window.ShowDialog();
    }

    private void ExportCsv()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            DefaultExt = ".csv",
            FileName = $"wstat-export-{DateTime.Now:yyyy-MM-dd}.csv"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Type,Name,TimeSeconds,DisplayTime,Details,StartTime,EndTime,ProcessPath");

            foreach (var app in Applications)
            {
                var name = CsvEscape(app.AppName);
                sb.AppendLine($"App,{name},{app.TotalSeconds},{app.DisplayTime},,,");
            }

            foreach (var url in TopUrls)
            {
                var urlVal = CsvEscape(url.Url);
                var titleVal = CsvEscape(url.Title);
                sb.AppendLine($"Url,{urlVal},{url.TotalSeconds},{url.DisplayTime},{titleVal},,");
            }

            foreach (var entry in TimelineEntries)
            {
                var appName = CsvEscape(entry.AppName);
                var windowTitle = CsvEscape(entry.WindowTitle);
                var processPath = CsvEscape(entry.ProcessPath ?? "");
                var displayTime = Formatting.FormatDuration(entry.DurationSeconds);
                sb.AppendLine($"Timeline,{appName},{entry.DurationSeconds},{displayTime},{windowTitle},{entry.StartTime:O},{entry.EndTime:O},{processPath}");
            }

            File.WriteAllText(dialog.FileName, sb.ToString());
            LogWriter.Write("[Export] Saved CSV to " + dialog.FileName);
        }
        catch (Exception ex)
        {
            LogWriter.Write("[Export] Error: " + ex.Message);
        }
    }

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer?.Stop();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
