using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using Wstat.Desktop.Models;
using Wstat.Desktop.Services;

namespace Wstat.Desktop.ViewModels;

public class DashboardViewModel : INotifyPropertyChanged
{
    private readonly DatabaseService _db;
    private DateFilter _selectedFilter = DateFilter.Today;

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
                LoadData();
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

        LoadData();
    }

    public void LoadData()
    {
        var apps = _db.GetAppSummary(_selectedFilter);
        Applications.Clear();
        foreach (var app in apps)
            Applications.Add(app);

        var urls = _db.GetUrlSummary(_selectedFilter);
        TopUrls.Clear();
        foreach (var url in urls)
            TopUrls.Add(url);

        OnPropertyChanged(nameof(Applications));
        OnPropertyChanged(nameof(TopUrls));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
