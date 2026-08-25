using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

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

    public OverlayWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            MakeClickThrough();
        };
    }

    public void MoveNearCaret(int x, int y)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        SetWindowPos(
            hwnd,
            HWND_TOPMOST,
            x + 8,
            y + 3,
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