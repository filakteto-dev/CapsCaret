using System;
using System.Windows;
using System.Windows.Threading;

namespace CapsCaret;

public partial class App : Application
{
    private OverlayWindow? _overlay;
    private DispatcherTimer? _timer;

    private void Application_Startup(
        object sender,
        StartupEventArgs e
    )
    {
        _overlay = new OverlayWindow();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };

        _timer.Tick += (_, _) =>
        {
            UpdateIndicator();
        };

        _timer.Start();
    }

    private void UpdateIndicator()
    {
        if (_overlay is null)
            return;

        if (!NativeMethods.IsCapsLockOn())
        {
            _overlay.Hide();
            return;
        }

        if (!NativeMethods.TryGetCaretPosition(
                out var x,
                out var y))
        {
            _overlay.Hide();
            return;
        }

        if (!_overlay.IsVisible)
            _overlay.Show();

        _overlay.MoveNearCaret(x, y);
    }
}