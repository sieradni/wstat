using System.Windows;
using Wstat.Desktop.ViewModels;

namespace Wstat.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
        base.OnStateChanged(e);
    }
}
