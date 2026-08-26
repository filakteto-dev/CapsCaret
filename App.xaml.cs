using System;
using System.Windows;
using System.Windows.Threading;

namespace CapsCaret;

public partial class App : System.Windows.Application
{
    private OverlayWindow? _overlay;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    private bool _enabled = true;

    // Standard Windows move / resize events.
    private IntPtr _moveSizeHook = IntPtr.Zero;
    private NativeMethods.WinEventDelegate? _moveSizeDelegate;
    private bool _windowMoveInProgress;

    // Mouse geometry fallback.
    // No polling: remember the foreground window rectangle on mouse-down
    // and compare it once on mouse-up.
    private bool _mouseGeometryTracking;
    private IntPtr _mouseDownWindow = IntPtr.Zero;
    private NativeMethods.WindowBounds? _mouseDownBounds;

    // Caret providers.
    private AutomationCaretProvider? _automationCaret;
    private JavaCaretProvider? _javaCaret;

    // Global keyboard / mouse events.
    private GlobalInputHook? _inputHook;

    // One-shot UX delay after ordinary input.
    private DispatcherTimer? _idleTimer;

    private bool _capsLockOn;

    // Invalidates stale asynchronous UI Automation results.
    private long _stateVersion;

    private static readonly TimeSpan IndicatorDelay =
        TimeSpan.FromMilliseconds(220);

    private void Application_Startup(
        object sender,
        StartupEventArgs e)
    {
        _overlay = new OverlayWindow();

        StartWindowMoveHook();

        _javaCaret = new JavaCaretProvider();
        _javaCaret.Initialize();

        _automationCaret =
            new AutomationCaretProvider();

        _automationCaret.ResultUpdated +=
            OnAutomationResultUpdated;

        CreateTrayIcon();

        _capsLockOn =
            NativeMethods.IsCapsLockOn();

        _idleTimer = new DispatcherTimer
        {
            Interval = IndicatorDelay
        };

        _idleTimer.Tick +=
            OnIdleTimerTick;

        _inputHook =
            new GlobalInputHook();

        _inputHook.InputActivity +=
            OnGlobalInputActivity;

        _inputHook.CapsLockPressed +=
            OnGlobalCapsLockPressed;

        _inputHook.Start();

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

        _overlay?.HideAnimated();

        bool mouseButtonDown =
            _inputHook?.IsMouseButtonDown == true;

        if (mouseButtonDown)
        {
            StartMouseGeometryTracking();
            return;
        }

        if (_mouseGeometryTracking)
        {
            bool geometryChanged =
                FinishMouseGeometryTracking();

            if (geometryChanged)
            {
                RefreshAfterWindowGeometryChange();
                return;
            }
        }

        if (_windowMoveInProgress)
            return;

        if (_inputHook?.IsInputActive == true)
            return;

        RestartIdleTimer();
    }

    private void HandleCapsLockPressed()
    {
        _stateVersion++;

        _idleTimer?.Stop();

        _capsLockOn = !_capsLockOn;

        if (!_enabled)
        {
            _overlay?.HideAnimated();
            return;
        }

        if (!_capsLockOn)
        {
            _overlay?.HideAnimated();
            return;
        }

        RefreshIndicatorPosition();
    }

    // ------------------------------------------------------------
    // MOUSE GEOMETRY FALLBACK
    // ------------------------------------------------------------

    private void StartMouseGeometryTracking()
    {
        if (_mouseGeometryTracking)
            return;

        _mouseGeometryTracking = true;
        _mouseDownWindow = IntPtr.Zero;
        _mouseDownBounds = null;

        if (NativeMethods.TryGetForegroundRootBounds(
                out var rootHwnd,
                out var bounds))
        {
            _mouseDownWindow = rootHwnd;
            _mouseDownBounds = bounds;
        }
    }

    private bool FinishMouseGeometryTracking()
    {
        bool changed = false;

        if (_mouseDownWindow != IntPtr.Zero &&
            _mouseDownBounds is not null &&
            NativeMethods.TryGetForegroundRootBounds(
                out var rootHwnd,
                out var bounds) &&
            rootHwnd == _mouseDownWindow &&
            bounds != _mouseDownBounds.Value)
        {
            changed = true;
        }

        _mouseGeometryTracking = false;
        _mouseDownWindow = IntPtr.Zero;
        _mouseDownBounds = null;

        return changed;
    }

    private void RefreshAfterWindowGeometryChange()
    {
        _stateVersion++;

        _idleTimer?.Stop();

        // Geometry reported by accessibility providers can lag behind
        // the actual window while it is being moved/resized. Hide the
        // old indicator immediately, then let the normal idle delay
        // request a fresh caret position once the window has settled.
        _overlay?.HideImmediately();

        RestartIdleTimer();
    }

    // ------------------------------------------------------------
    // IDLE TIMER
    // ------------------------------------------------------------

    private void OnIdleTimerTick(
        object? sender,
        EventArgs e)
    {
        _idleTimer?.Stop();

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

        long version =
            _stateVersion;

        int x;
        int y;

        // 1. Microsoft Active Accessibility.
        //
        // Chromium exposes a useful caret here. This path is deliberately
        // separate from the classic Win32 caret.
        if (NativeMethods.TryGetAccessibleCaretPosition(
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

        // 2. Java Access Bridge.
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

        // 3. UI Automation.
        //
        // The provider first uses the managed TextPattern path and can
        // fall back internally to native TextPattern2 caret geometry
        // for empty text controls.
        //
        // There is intentionally no classic Win32 fallback.
        _automationCaret?.RequestUpdate(
            version
        );
    }

    private void OnAutomationResultUpdated(
        long requestVersion,
        AutomationCaretResult result)
    {
        if (Dispatcher.HasShutdownStarted)
            return;

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (requestVersion != _stateVersion)
                    return;

                switch (result.Kind)
                {
                    case AutomationCaretResultKind.Success:
                        ShowIndicatorAt(
                            requestVersion,
                            result.X,
                            result.Y
                        );
                        break;

                    case AutomationCaretResultKind.TextControlWithoutCaret:
                        // A text control exists, but it does not currently
                        // expose trustworthy caret geometry.
                        //
                        // Do NOT fall back to classic Win32 here.
                        break;

                    case AutomationCaretResultKind.Unsupported:
                        // No reliable caret provider for this control.
                        // Do not fall back to classic Win32:
                        // Telegram can expose misleading native caret coordinates.
                        break;
                }
            })
        );
    }

    private void ShowIndicatorAt(
        long version,
        int x,
        int y)
    {
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

        _overlay.MoveNearCaret(
            x,
            y
        );

        _overlay.ShowAnimated();
    }

    // ------------------------------------------------------------
    // STANDARD WINDOW MOVE / RESIZE
    // ------------------------------------------------------------

    private void StartWindowMoveHook()
    {
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

                    _overlay.HideImmediately();

                    return;
                }

                if (eventType ==
                    NativeMethods.EventSystemMoveSizeEnd)
                {
                    _windowMoveInProgress = false;

                    RefreshAfterWindowGeometryChange();
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

            _capsLockOn =
                NativeMethods.IsCapsLockOn();

            if (_capsLockOn)
            {
                RefreshIndicatorPosition();
            }
        };

        var startupItem =
            new System.Windows.Forms.ToolStripMenuItem
            {
                Text = "Start with Windows",
                Checked = StartupManager.IsEnabled(),
                CheckOnClick = true
            };

        startupItem.CheckedChanged += (_, _) =>
        {
            StartupManager.SetEnabled(
                startupItem.Checked
            );
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
        menu.Items.Add(startupItem);

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

        using var stream =
            resource.Stream;

        using var icon =
            new System.Drawing.Icon(stream);

        return (System.Drawing.Icon)
            icon.Clone();
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
            _automationCaret.ResultUpdated -=
                OnAutomationResultUpdated;

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
