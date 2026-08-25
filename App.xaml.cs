using System.Windows;
using System.Windows.Threading;

namespace CapsCaret;

public partial class App : System.Windows.Application
{
    private OverlayWindow? _overlay;
    private DispatcherTimer? _timer;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _enabled = true;
    private int? _lastCaretX;
    private int? _lastCaretY;
    
    private bool _wasCapsLockOn;
    
    private DateTime _lastCaretMovement = DateTime.UtcNow;

    private static readonly TimeSpan IndicatorDelay =
        TimeSpan.FromMilliseconds(220);
    
    private IntPtr _moveSizeHook = IntPtr.Zero;

    private NativeMethods.WinEventDelegate? _moveSizeDelegate;

    private bool _windowMoveInProgress;
    
    private AutomationCaretProvider? _automationCaret;
    
    private JavaCaretProvider? _javaCaret;
    
    private bool TryGetCaretPosition(
        out int x,
        out int y)
    {
        // 1. Классический Win32.
        if (NativeMethods.TryGetCaretPosition(
                out x,
                out y))
        {
            return true;
        }

        // 2. Java Access Bridge.
        //
        // Проверяем перед UIA специально:
        // Rider через UIA виден только как SunAwtFrame,
        // а JAB умеет получить настоящий focused Java component.
        if (_javaCaret is not null &&
            _javaCaret.TryGetCaretPosition(
                out x,
                out y))
        {
            return true;
        }

        // 3. Windows UI Automation.
        if (_automationCaret is null)
        {
            x = 0;
            y = 0;
            return false;
        }
        
        _automationCaret.RequestUpdate();

        return _automationCaret.TryGetLatestPosition(
            out x,
            out y
        );
    }
    private void Application_Startup(
        object sender,
        StartupEventArgs e
    )
    {
        _javaCaret = new JavaCaretProvider();
        _javaCaret.Initialize();
        _overlay = new OverlayWindow();
        _automationCaret =
            new AutomationCaretProvider();

        StartWindowMoveHook();

        CreateTrayIcon();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _timer.Tick += (_, _) => { UpdateIndicator(); };

        _timer.Start();
    }
    
    private void OnWindowMoveSizeEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime
    )
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_overlay is null)
                return;

            if (eventType ==
                NativeMethods.EventSystemMoveSizeStart)
            {
                _windowMoveInProgress = true;

                _lastCaretMovement = DateTime.UtcNow;

                _overlay.HideImmediately();

                return;
            }

            if (eventType ==
                NativeMethods.EventSystemMoveSizeEnd)
            {
                _windowMoveInProgress = false;

                // Положение caret изменилось вместе с окном,
                // но это не было редактированием текста.
                if (TryGetCaretPosition(
                        out var x,
                        out var y))
                {
                    _lastCaretX = x;
                    _lastCaretY = y;
                }

                // После отпускания окна даём обычную
                // UX-задержку перед возвращением плашки.
                _lastCaretMovement = DateTime.UtcNow;
            }
        });
    }
    private void StartWindowMoveHook()
    {
        // Delegate обязательно храним в поле.
        // Иначе GC может его удалить, пока native hook ещё работает.
        _moveSizeDelegate = OnWindowMoveSizeEvent;

        _moveSizeHook =
            NativeMethods.InstallMoveSizeHook(
                _moveSizeDelegate
            );
    }
    private void CreateTrayIcon()
    {
        var enabledItem = new System.Windows.Forms.ToolStripMenuItem
        {
            Text = "Enabled",
            Checked = true,
            CheckOnClick = true
        };

        enabledItem.CheckedChanged += (_, _) =>
        {
            _enabled = enabledItem.Checked;

            if (!_enabled)
            {
                _overlay.HideAnimated();
            }
        };

        var exitItem = new System.Windows.Forms.ToolStripMenuItem
        {
            Text = "Exit"
        };

        exitItem.Click += (_, _) => { ShutdownApplication(); };

        var menu = new System.Windows.Forms.ContextMenuStrip();

        menu.Items.Add(enabledItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new System.Windows.Forms.NotifyIcon
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
        System.Windows.Application.GetResourceStream(uri);

    if (resource is null)
        return System.Drawing.SystemIcons.Application;

    return new System.Drawing.Icon(resource.Stream);
}

    private void UpdateIndicator()
    {
        if (_overlay is null)
            return;

        if (!_enabled)
        {
            _overlay.HideAnimated();
            return;
        }
        
        if (_windowMoveInProgress)
        {
            _overlay.HideImmediately();
            return;
        }

        bool capsLockOn = NativeMethods.IsCapsLockOn();

        // Caps только что выключили
        if (!capsLockOn)
        {
            _wasCapsLockOn = false;

            _lastCaretX = null;
            _lastCaretY = null;

            _automationCaret?.Invalidate();

            _overlay.HideAnimated();
            return;
        }

        // Caps только что включили
        if (!_wasCapsLockOn)
        {
            if (!TryGetCaretPosition(
                    out var initialX,
                    out var initialY))
            {
                // UI Automation ещё получает свежий caret.
                // Старую позицию не показываем.
                _overlay.HideImmediately();
                return;
            }

            _wasCapsLockOn = true;

            _lastCaretX = initialX;
            _lastCaretY = initialY;

            _lastCaretMovement = DateTime.MinValue;

            _overlay.MoveNearCaret(initialX, initialY);
            _overlay.ShowAnimated();

            return;
        }

        // Дальше обычная логика работы
        if (NativeMethods.IsInputKeyHeld())
        {
            _lastCaretMovement = DateTime.UtcNow;
            _overlay.HideAnimated();
            return;
        }
        
        if (!TryGetCaretPosition(
                out var x,
                out var y))
        {
            _overlay.HideAnimated();
            return;
        }

        bool caretMoved =
            _lastCaretX != x ||
            _lastCaretY != y;

        if (caretMoved)
        {
            _lastCaretX = x;
            _lastCaretY = y;

            _lastCaretMovement = DateTime.UtcNow;

            _overlay.HideAnimated();

            return;
        }

        var timeSinceMovement =
            DateTime.UtcNow - _lastCaretMovement;

        if (timeSinceMovement < IndicatorDelay)
        {
            _overlay.HideAnimated();
            return;
        }

        _overlay.MoveNearCaret(x, y);
        _overlay.ShowAnimated();
    }

    private void ShutdownApplication()
    {
        _timer?.Stop();
        _automationCaret?.Dispose();
        _automationCaret = null;
        
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

        Shutdown();
    }
}