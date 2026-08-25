using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CapsCaret;

public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;

    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    
    private bool _targetVisible;
    private int _animationVersion;
    
    public void HideImmediately()
    {
        _targetVisible = false;

        // Инвалидируем незавершённые callbacks анимации.
        _animationVersion++;

        BeginAnimation(
            OpacityProperty,
            null
        );

        IndicatorScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            null
        );

        IndicatorScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            null
        );

        Opacity = 0;

        IndicatorScale.ScaleX = 1;
        IndicatorScale.ScaleY = 1;

        Hide();
    }
    public void ShowAnimated()
    {
        if (_targetVisible)
            return;

        _targetVisible = true;

        int version = ++_animationVersion;

        if (!IsVisible)
        {
            Opacity = 0;

            IndicatorScale.ScaleX = 0.96;
            IndicatorScale.ScaleY = 0.96;

            Show();
        }

        var fade = new DoubleAnimation
        {
            From = Opacity,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(70)
        };

        var scaleX = new DoubleAnimation
        {
            From = IndicatorScale.ScaleX,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(70)
        };

        var scaleY = new DoubleAnimation
        {
            From = IndicatorScale.ScaleY,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(70)
        };

        fade.Completed += (_, _) =>
        {
            if (version != _animationVersion)
                return;

            Opacity = 1;
        };

        BeginAnimation(
            OpacityProperty,
            fade,
            HandoffBehavior.SnapshotAndReplace
        );

        IndicatorScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            scaleX,
            HandoffBehavior.SnapshotAndReplace
        );

        IndicatorScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            scaleY,
            HandoffBehavior.SnapshotAndReplace
        );
    }
    
    public void HideAnimated()
    {
        if (!_targetVisible)
            return;

        _targetVisible = false;

        int version = ++_animationVersion;

        var fade = new DoubleAnimation
        {
            From = Opacity,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(45)
        };

        var scaleX = new DoubleAnimation
        {
            From = IndicatorScale.ScaleX,
            To = 0.97,
            Duration = TimeSpan.FromMilliseconds(45)
        };

        var scaleY = new DoubleAnimation
        {
            From = IndicatorScale.ScaleY,
            To = 0.97,
            Duration = TimeSpan.FromMilliseconds(45)
        };
        
        fade.Completed += (_, _) =>
        {
            if (version != _animationVersion)
                return;

            Opacity = 0;
            Hide();
        };

        BeginAnimation(
            OpacityProperty,
            fade,
            HandoffBehavior.SnapshotAndReplace
        );

        IndicatorScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            scaleX,
            HandoffBehavior.SnapshotAndReplace
        );

        IndicatorScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            scaleY,
            HandoffBehavior.SnapshotAndReplace
        );
    }
    
    private void ApplySystemAppearance()
    {
        var accent = SystemParameters.WindowGlassColor;

        if (accent.A < 50)
        {
            accent = System.Windows.Media.Color.FromRgb(
                45,
                114,
                217
            );
        }

        var background =
            System.Windows.Media.Color.FromArgb(
                235,
                accent.R,
                accent.G,
                accent.B
            );

        IndicatorBackground.Background =
            new System.Windows.Media.SolidColorBrush(background);

        var brightness =
            accent.R * 0.299 +
            accent.G * 0.587 +
            accent.B * 0.114;

        CapsIcon.Fill =
            brightness > 165
                ? System.Windows.Media.Brushes.Black
                : System.Windows.Media.Brushes.White;
    }
    
    public OverlayWindow()
    {
        InitializeComponent();

        ApplySystemAppearance();

        SystemParameters.StaticPropertyChanged += (_, _) =>
        {
            ApplySystemAppearance();
        };

        SourceInitialized += (_, _) =>
        {
            MakeClickThrough();
        };
    }

    public void MoveNearCaret(int x, int y)
    {
        var helper = new WindowInteropHelper(this);

        var hwnd = helper.Handle;

        if (hwnd == IntPtr.Zero)
        {
            hwnd = helper.EnsureHandle();
        }

        var dpi = VisualTreeHelper.GetDpi(this);

        var overlayWidth =
            (int)Math.Round(Width * dpi.DpiScaleX);

        var gap =
            (int)Math.Round(4 * dpi.DpiScaleY);

        SetWindowPos(
            hwnd,
            HWND_TOPMOST,

            x - overlayWidth / 2,
            y + gap,

            0,
            0,

            SWP_NOSIZE | SWP_NOACTIVATE
        );
    }

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();

        style |=
            WS_EX_TRANSPARENT |
            WS_EX_TOOLWINDOW |
            WS_EX_NOACTIVATE;

        SetWindowLongPtr(
            hwnd,
            GWL_EXSTYLE,
            new IntPtr(style)
        );
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(
        IntPtr hWnd,
        int nIndex
    );

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(
        IntPtr hWnd,
        int nIndex,
        IntPtr dwNewLong
    );

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags
    );
}