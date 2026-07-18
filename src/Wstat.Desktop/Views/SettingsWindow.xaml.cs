using System.Windows;
using Wstat.Desktop.ViewModels;

namespace Wstat.Desktop.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.RequestClose = () =>
        {
            DialogResult = true;
            Close();
        };
    }
}
