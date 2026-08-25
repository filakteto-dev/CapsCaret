using System;
using System.Runtime.InteropServices;

namespace CapsCaret;

internal static class NativeMethods
{
    private const int VK_CAPITAL = 0x14;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;

        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;

        public RECT rcCaret;
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetGUIThreadInfo(
        uint idThread,
        ref GUITHREADINFO lpgui
    );

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(
        IntPtr hWnd,
        ref POINT lpPoint
    );

    public static bool IsCapsLockOn()
    {
        return (GetKeyState(VK_CAPITAL) & 1) != 0;
    }

    public static bool TryGetCaretPosition(
        out int x,
        out int y
    )
    {
        var info = new GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<GUITHREADINFO>()
        };

        if (!GetGUIThreadInfo(0, ref info))
        {
            x = 0;
            y = 0;
            return false;
        }

        if (info.hwndCaret == IntPtr.Zero)
        {
            x = 0;
            y = 0;
            return false;
        }

        var point = new POINT
        {
            X = info.rcCaret.Right,
            Y = info.rcCaret.Top
        };

        if (!ClientToScreen(info.hwndCaret, ref point))
        {
            x = 0;
            y = 0;
            return false;
        }

        x = point.X;
        y = point.Y;

        return true;
    }
}