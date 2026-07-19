using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Wstat.Desktop.Common;
using Wstat.Desktop.Native;
using Wstat.Desktop.ViewModels;

namespace Wstat.Desktop;

public partial class MainWindow : Window
{
    private readonly DashboardViewModel _viewModel;
    private double _hourWidth = 60;
    private bool _syncingScroll;
    private string? _lastPopupText;

    public MainWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.TimelineUpdated += OnTimelineUpdated;
        _viewModel.ApplicationsUpdated += OnApplicationsUpdated;
        OnTimelineUpdated();
        OnApplicationsUpdated();

        PreviewMouseMove += Window_PreviewMouseMove;
        Deactivated += (_, _) => timelinePopup.IsOpen = false;

        timelineControl.SelectionChanged += OnTimelineSelectionChanged;
        PreviewKeyDown += Window_PreviewKeyDown;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Api.WM_SHOW_APP)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnTimelineUpdated()
    {
        var entries = _viewModel.TimelineEntries;
        timelineControl.Render(entries);
        timelineLabels.Render(entries);
    }

    private void OnApplicationsUpdated()
    {
        appsBarControl.Render([.. _viewModel.Applications]);
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Delete && timelineControl.HasSelection)
        {
            DeleteSelected();
            e.Handled = true;
        }
    }

    private void OnTimelineSelectionChanged()
    {
        UpdateSelectionInfo();
    }

    private void UpdateSelectionInfo()
    {
        if (!timelineControl.HasSelection)
        {
            selectionInfoBar.Visibility = Visibility.Collapsed;
            return;
        }

        selectionInfoBar.Visibility = Visibility.Visible;

        var count = timelineControl.SelectedEntries.Count;
        selectionCountText.Text = count == 1
            ? "1 entry selected"
            : $"{count} entries selected";

        var (total, active) = timelineControl.GetSelectionInfo();
        selectionTotalTimeValue.Text = FormatDurationLong(total);
        selectionActiveTimeValue.Text = FormatDurationLong(active);
    }

    private static string FormatDurationLong(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
        if (ts.TotalMinutes >= 1)
            return $"{ts.Minutes}m {ts.Seconds}s";
        return $"{ts.Seconds}s";
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        timelineControl.ClearSelection();
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelected();
    }

    private void DeleteSelected()
    {
        if (!timelineControl.HasSelection) return;
        var selected = timelineControl.SelectedEntries.ToList();

        var count = selected.Count;
        var msg = count == 1
            ? $"Delete the selected activity record?"
            : $"Delete {count} selected activity records?\n\nThis cannot be undone.";

        var result = System.Windows.MessageBox.Show(
            msg,
            "Delete Records",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var ids = selected.Select(e => e.Id).ToList();
        _viewModel.DeleteEntries(ids);
    }

    private void Window_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (timelineControl.IsVisible == false || timelineControl.IsDragging)
        {
            timelinePopup.IsOpen = false;
            return;
        }

        var pos = e.GetPosition(timelineControl);

        if (pos.X < 0 || pos.X >= timelineControl.ActualWidth ||
            pos.Y < 32 || pos.Y < 0)
        {
            timelinePopup.IsOpen = false;
            return;
        }

        var entry = timelineControl.GetEntryAt(pos);
        if (entry != null)
        {
            var endStr = entry.EndTime == DateTime.MinValue ? "now" : entry.EndTime.ToString("HH:mm");
            var range = $"{entry.StartTime:HH:mm} - {endStr}";
            var dur = entry.DurationSeconds >= 3600
                ? $"{entry.DurationSeconds / 3600}h {(entry.DurationSeconds % 3600) / 60}m"
                : $"{entry.DurationSeconds / 60}m {entry.DurationSeconds % 60}s";
            var name = System.IO.Path.GetFileNameWithoutExtension(entry.AppName);
            if (string.IsNullOrEmpty(name)) name = entry.AppName;

            var text = $"{name}\n{entry.WindowTitle}\n{range}  ({dur})";
            if (text != _lastPopupText)
            {
                _lastPopupText = text;
                timelinePopupText.Text = text;
            }
            timelinePopup.IsOpen = true;
            return;
        }

        timelinePopup.IsOpen = false;
        _lastPopupText = null;
    }

    private void BarsScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_syncingScroll) return;
        _syncingScroll = true;
        labelsScroller.ScrollToVerticalOffset(barsScroller.VerticalOffset);
        _syncingScroll = false;
    }

    private void BarsScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            var mousePos = e.GetPosition(timelineControl);
            var cursorHour = mousePos.X / _hourWidth;

            var delta = e.Delta > 0 ? _hourWidth * 0.12 : -_hourWidth * 0.12;
            var newWidth = Math.Max(5, _hourWidth + delta);

            if (newWidth != _hourWidth)
            {
                _hourWidth = newWidth;
                timelineControl.HourWidth = _hourWidth;

                var newCursorX = cursorHour * _hourWidth;
                Dispatcher.BeginInvoke(() =>
                {
                    barsScroller.ScrollToHorizontalOffset(
                        Math.Max(0, barsScroller.HorizontalOffset + (newCursorX - mousePos.X)));
                });
            }
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            var vStep = e.Delta > 0 ? -barsScroller.ViewportHeight / 6 : barsScroller.ViewportHeight / 6;
            barsScroller.ScrollToVerticalOffset(barsScroller.VerticalOffset + vStep);
            e.Handled = true;
        }
        else
        {
            var hStep = e.Delta > 0 ? -barsScroller.ViewportWidth / 8 : barsScroller.ViewportWidth / 8;
            barsScroller.ScrollToHorizontalOffset(barsScroller.HorizontalOffset + hStep);
            e.Handled = true;
        }
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        timelinePopup.IsOpen = false;
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
