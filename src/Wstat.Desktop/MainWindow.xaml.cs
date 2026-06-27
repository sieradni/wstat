using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        OnTimelineUpdated();

        PreviewMouseMove += Window_PreviewMouseMove;
        Deactivated += (_, _) => timelinePopup.IsOpen = false;
    }

    private void OnTimelineUpdated()
    {
        var entries = _viewModel.TimelineEntries;
        timelineControl.Render(entries);
        timelineLabels.Render(entries);
    }

    private void Window_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var groups = timelineControl.GetGroups();
        if (timelineControl.IsVisible == false || groups == null || groups.Count == 0)
            return;

        var pos = e.GetPosition(timelineControl);

        if (pos.X < 0 || pos.X >= timelineControl.ActualWidth ||
            pos.Y < 32 || pos.Y < 0)
        {
            timelinePopup.IsOpen = false;
            return;
        }

        var row = (int)((pos.Y - 32) / 34);

        if (row < 0 || row >= groups.Count)
        {
            timelinePopup.IsOpen = false;
            return;
        }

        var hourPx = Math.Max(timelineControl.ActualWidth, 24 * _hourWidth) / 24.0;
        if (hourPx <= 0)
        {
            timelinePopup.IsOpen = false;
            return;
        }

        var barY = 32 + row * 34 + (34 - 26) / 2.0;

        foreach (var entry in groups[row])
        {
            var spanStart = entry.StartTime.TimeOfDay;
            var spanEnd = entry.EndTime.TimeOfDay;

            if (spanEnd == TimeSpan.Zero && entry.EndTime.Date > entry.StartTime.Date)
                spanEnd = TimeSpan.FromHours(24);

            var left = spanStart.TotalHours * hourPx;
            var width = Math.Max((spanEnd - spanStart).TotalHours * hourPx, 2);

            if (pos.X >= left && pos.X <= left + width && pos.Y >= barY && pos.Y <= barY + 26)
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
            var viewportCenter = barsScroller.HorizontalOffset + barsScroller.ViewportWidth / 2;
            var centerHour = viewportCenter / _hourWidth;

            var delta = e.Delta > 0 ? _hourWidth * 0.12 : -_hourWidth * 0.12;
            var newWidth = Math.Max(5, _hourWidth + delta);

            if (newWidth != _hourWidth)
            {
                _hourWidth = newWidth;
                timelineControl.HourWidth = _hourWidth;

                var newCenterX = centerHour * _hourWidth;
                Dispatcher.BeginInvoke(() =>
                {
                    barsScroller.ScrollToHorizontalOffset(Math.Max(0, newCenterX - barsScroller.ViewportWidth / 2));
                });
            }
            e.Handled = true;
        }
        else
        {
            var step = e.Delta > 0 ? -120.0 : 120.0;
            barsScroller.ScrollToHorizontalOffset(barsScroller.HorizontalOffset + step);
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
