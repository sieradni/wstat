using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;
using Wstat.Desktop.Models;
using Wstat.Desktop.Services;

namespace Wstat.Desktop.ViewModels;

public class DashboardViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly Dictionary<string, BitmapSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly DatabaseService _db;
    private readonly DispatcherTimer _refreshTimer;
    private DateFilter _selectedFilter = DateFilter.Today;
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
                LoadAll();
            }
        }
    }

    public RelayCommand FilterTodayCommand { get; }
    public RelayCommand FilterYesterdayCommand { get; }
    public RelayCommand FilterLast7DaysCommand { get; }
    public RelayCommand FilterLast30DaysCommand { get; }

    public event Action? TimelineUpdated;

    public DashboardViewModel(DatabaseService db)
    {
        _db = db;

        FilterTodayCommand = new RelayCommand(() => SelectedFilter = DateFilter.Today);
        FilterYesterdayCommand = new RelayCommand(() => SelectedFilter = DateFilter.Yesterday);
        FilterLast7DaysCommand = new RelayCommand(() => SelectedFilter = DateFilter.Last7Days);
        FilterLast30DaysCommand = new RelayCommand(() => SelectedFilter = DateFilter.Last30Days);

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
        LoadTimeline();
    }

    private void LoadApps()
    {
        var apps = _db.GetAppSummary(_selectedFilter);
        Applications.Clear();
        foreach (var app in apps)
        {
            app.Icon = GetOrLoadIcon(app.ProcessPath);
            Applications.Add(app);
        }
    }

    private void LoadUrls()
    {
        var urls = _db.GetUrlSummary(_selectedFilter);
        TopUrls.Clear();
        foreach (var url in urls)
            TopUrls.Add(url);
    }

    private void LoadTimeline()
    {
        var raw = _db.GetTimeline(_selectedFilter);
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
            IconCache[processPath] = source;
            icon = source;
            return true;
        }
        catch
        {
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
