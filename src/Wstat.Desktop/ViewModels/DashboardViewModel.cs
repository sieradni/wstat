using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    }

    public void RefreshSummary()
    {
        LoadApps();
        LoadUrls();
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

    private static BitmapSource? GetOrLoadIcon(string? processPath)
    {
        if (string.IsNullOrEmpty(processPath) || !File.Exists(processPath))
            return null;

        if (IconCache.TryGetValue(processPath, out var cached))
            return cached;

        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
            if (icon == null) return null;

            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            source.Freeze();
            IconCache[processPath] = source;
            return source;
        }
        catch
        {
            IconCache[processPath] = null;
            return null;
        }
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
