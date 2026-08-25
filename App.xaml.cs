using System;
using System.Windows;
using System.Windows.Threading;

namespace CapsCaret;

public partial class App : System.Windows.Application
{
    private OverlayWindow? _overlay;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    private bool _enabled = true;

    // Window move / resize
    private IntPtr _moveSizeHook = IntPtr.Zero;
    private NativeMethods.WinEventDelegate? _moveSizeDelegate;
    private bool _windowMoveInProgress;

    // Caret providers
    private AutomationCaretProvider? _automationCaret;
    private JavaCaretProvider? _javaCaret;

    // Global keyboard / mouse events
    private GlobalInputHook? _inputHook;

    // One-shot UX delay after input
    private DispatcherTimer? _idleTimer;

    private bool _capsLockOn;

    // Invalidate stale asynchronous UI Automation results.
    private long _stateVersion;

    private static readonly TimeSpan IndicatorDelay =
        TimeSpan.FromMilliseconds(220);

    private void Application_Startup(
        object sender,
        StartupEventArgs e)
    {
        // Overlay
        _overlay = new OverlayWindow();

        // Win32 move / resize events
        StartWindowMoveHook();

        // Java / JetBrains caret provider
        _javaCaret = new JavaCaretProvider();
        _javaCaret.Initialize();

        // UI Automation provider
        _automationCaret = new AutomationCaretProvider();
        _automationCaret.PositionUpdated +=
            OnAutomationPositionUpdated;

        // Tray
        CreateTrayIcon();

        // Initial Caps state
        _capsLockOn =
            NativeMethods.IsCapsLockOn();

        // This timer is NOT polling.
        // It fires only once after 220 ms of inactivity.
        _idleTimer = new DispatcherTimer
        {
            Interval = IndicatorDelay
        };

        _idleTimer.Tick += OnIdleTimerTick;

        // Keyboard / mouse events
        _inputHook = new GlobalInputHook();

        _inputHook.InputActivity +=
            OnGlobalInputActivity;

        _inputHook.CapsLockPressed +=
            OnGlobalCapsLockPressed;

        _inputHook.Start();

        // If Caps was already enabled when CapsCaret started,
        // show the indicator after the normal idle delay.
        RestartIdleTimer();
    }

    // ------------------------------------------------------------
    // GLOBAL INPUT
    // ------------------------------------------------------------

    private void OnGlobalInputActivity()
    {
        Dispatcher.BeginInvoke(
            new Action(HandleInputActivity)
        );
    }

    private void OnGlobalCapsLockPressed()
    {
        Dispatcher.BeginInvoke(
            new Action(HandleCapsLockPressed)
        );
    }

    private void HandleInputActivity()
    {
        _stateVersion++;

        _idleTimer?.Stop();

        // Any real interaction hides the indicator.
        _overlay?.HideAnimated();

        // If a key/button is still physically held,
        // do NOT start the 220 ms timer yet.
        //
        // This fixes Backspace/Space/letters autorepeat.
        if (_inputHook?.IsInputActive == true)
            return;

        // Last key/button was released.
        RestartIdleTimer();
    }

    private void HandleCapsLockPressed()
    {
        _stateVersion++;

        _idleTimer?.Stop();

        // The low-level hook fires immediately on Caps key-down.
        // Toggle our cached state instead of waiting for polling.
        _capsLockOn = !_capsLockOn;

        if (!_enabled)
        {
            _overlay?.HideAnimated();
            return;
        }

        if (!_capsLockOn)
        {
            // Caps OFF → disappear immediately.
            _overlay?.HideAnimated();
            return;
        }

        // Caps ON → show immediately.
        // No 220 ms delay here.
        RefreshIndicatorPosition();
    }

    // ------------------------------------------------------------
    // IDLE TIMER
    // ------------------------------------------------------------

    private void OnIdleTimerTick(
        object? sender,
        EventArgs e)
    {
        _idleTimer?.Stop();

        // Occasionally synchronize with the actual Windows state.
        // This handles Caps changes made by something other than
        // our physical keyboard hook.
        bool actualCapsState =
            NativeMethods.IsCapsLockOn();

        if (actualCapsState != _capsLockOn)
        {
            _capsLockOn = actualCapsState;
            _stateVersion++;
        }

        if (!_capsLockOn)
        {
            _overlay?.HideAnimated();
            return;
        }

        RefreshIndicatorPosition();
    }

    private void RestartIdleTimer()
    {
        if (_idleTimer is null)
            return;

        _idleTimer.Stop();

        if (!_enabled)
            return;

        if (!_capsLockOn)
            return;

        if (_windowMoveInProgress)
            return;

        if (_inputHook?.IsInputActive == true)
            return;

        _idleTimer.Start();
    }

    // ------------------------------------------------------------
    // CARET POSITION
    // ------------------------------------------------------------

    private void RefreshIndicatorPosition()
    {
        if (!_enabled)
            return;

        if (!_capsLockOn)
            return;

        if (_windowMoveInProgress)
            return;

        if (_inputHook?.IsInputActive == true)
            return;

        long version = _stateVersion;

        // 1. Classic Win32 caret.
        if (NativeMethods.TryGetCaretPosition(
                out var x,
                out var y))
        {
            ShowIndicatorAt(
                version,
                x,
                y
            );

            return;
        }

        // 2. Java Access Bridge:
        // Rider / IntelliJ / other JetBrains IDEs.
        if (_javaCaret is not null &&
            _javaCaret.TryGetCaretPosition(
                out x,
                out y))
        {
            ShowIndicatorAt(
                version,
                x,
                y
            );

            return;
        }

        // 3. UI Automation:
        // Chrome, ChatGPT, XAML, etc.
        //
        // This runs asynchronously on its own worker thread.
        _automationCaret?.RequestUpdate(version);
    }

    private void OnAutomationPositionUpdated(
        long requestVersion,
        int x,
        int y)
    {
        if (Dispatcher.HasShutdownStarted)
            return;

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                ShowIndicatorAt(
                    requestVersion,
                    x,
                    y
                );
            })
        );
    }

    private void ShowIndicatorAt(
        long version,
        int x,
        int y)
    {
        // UIA might return a result from an old request.
        // Ignore it if anything changed since then.
        if (version != _stateVersion)
            return;

        if (!_enabled)
            return;

        if (!_capsLockOn)
            return;

        if (_windowMoveInProgress)
            return;

        if (_inputHook?.IsInputActive == true)
            return;

        if (_overlay is null)
            return;

        _overlay.MoveNearCaret(x, y);
        _overlay.ShowAnimated();
    }

    // ------------------------------------------------------------
    // WINDOW MOVE / RESIZE
    // ------------------------------------------------------------

    private void StartWindowMoveHook()
    {
        // Store delegate in a field so GC cannot collect it
        // while native Windows code is still using it.
        _moveSizeDelegate =
            OnWindowMoveSizeEvent;

        _moveSizeHook =
            NativeMethods.InstallMoveSizeHook(
                _moveSizeDelegate
            );
    }

    private void OnWindowMoveSizeEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime)
    {
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (_overlay is null)
                    return;

                if (eventType ==
                    NativeMethods.EventSystemMoveSizeStart)
                {
                    _windowMoveInProgress = true;

                    _stateVersion++;

                    _idleTimer?.Stop();

                    // No fade while moving a window.
                    _overlay.HideImmediately();

                    return;
                }

                if (eventType ==
                    NativeMethods.EventSystemMoveSizeEnd)
                {
                    _windowMoveInProgress = false;

                    _stateVersion++;

                    // Wait 220 ms after dropping the window.
                    RestartIdleTimer();
                }
            })
        );
    }

    // ------------------------------------------------------------
    // TRAY
    // ------------------------------------------------------------

    private void CreateTrayIcon()
    {
        var enabledItem =
            new System.Windows.Forms.ToolStripMenuItem
            {
                Text = "Enabled",
                Checked = true,
                CheckOnClick = true
            };

        enabledItem.CheckedChanged += (_, _) =>
        {
            _enabled = enabledItem.Checked;

            _stateVersion++;

            _idleTimer?.Stop();

            if (!_enabled)
            {
                _overlay?.HideAnimated();
                return;
            }

            // Re-sync in case Caps changed while disabled.
            _capsLockOn =
                NativeMethods.IsCapsLockOn();

            if (_capsLockOn)
            {
                RefreshIndicatorPosition();
            }
        };

        var exitItem =
            new System.Windows.Forms.ToolStripMenuItem
            {
                Text = "Exit"
            };

        exitItem.Click += (_, _) =>
        {
            ShutdownApplication();
        };

        var menu =
            new System.Windows.Forms.ContextMenuStrip();

        menu.Items.Add(enabledItem);
        menu.Items.Add(
            new System.Windows.Forms.ToolStripSeparator()
        );
        menu.Items.Add(exitItem);

        _trayIcon =
            new System.Windows.Forms.NotifyIcon
            {
                Text = "CapsCaret",
                Icon = LoadTrayIcon(),
                Visible = true,
                ContextMenuStrip = menu
            };
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var uri = new Uri(
            "pack://application:,,,/Assets/CapsCaret.ico",
            UriKind.Absolute
        );

        var resource =
            System.Windows.Application
                .GetResourceStream(uri);

        if (resource is null)
        {
            return System.Drawing
                .SystemIcons.Application;
        }

        // Clone the icon so it does not depend
        // on the resource stream remaining open.
        using var stream = resource.Stream;
        using var icon =
            new System.Drawing.Icon(stream);

        return (System.Drawing.Icon)icon.Clone();
    }

    // ------------------------------------------------------------
    // SHUTDOWN
    // ------------------------------------------------------------

    private void ShutdownApplication()
    {
        _idleTimer?.Stop();
        _idleTimer = null;

        if (_inputHook is not null)
        {
            _inputHook.InputActivity -=
                OnGlobalInputActivity;

            _inputHook.CapsLockPressed -=
                OnGlobalCapsLockPressed;

            _inputHook.Dispose();
            _inputHook = null;
        }

        if (_automationCaret is not null)
        {
            _automationCaret.PositionUpdated -=
                OnAutomationPositionUpdated;

            _automationCaret.Dispose();
            _automationCaret = null;
        }

        _javaCaret = null;

        if (_moveSizeHook != IntPtr.Zero)
        {
            NativeMethods.RemoveWinEventHook(
                _moveSizeHook
            );

            _moveSizeHook = IntPtr.Zero;
        }

        _moveSizeDelegate = null;

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _overlay?.Close();
        _overlay = null;

        Shutdown();
    }
}