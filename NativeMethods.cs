using System;
using System.Runtime.InteropServices;

namespace CapsCaret;

internal static class NativeMethods
{
    private const int VK_CAPITAL = 0x14;

    // ------------------------------------------------------------
    // WINDOW MOVE / RESIZE EVENTS
    // ------------------------------------------------------------

    internal const uint EventSystemMoveSizeStart = 0x000A;
    internal const uint EventSystemMoveSizeEnd = 0x000B;

    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    internal delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime
    );

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags
    );

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(
        IntPtr hWinEventHook
    );

    internal static IntPtr InstallMoveSizeHook(
        WinEventDelegate callback)
    {
        return SetWinEventHook(
            EventSystemMoveSizeStart,
            EventSystemMoveSizeEnd,
            IntPtr.Zero,
            callback,
            0,
            0,
            WINEVENT_OUTOFCONTEXT
        );
    }

    internal static void RemoveWinEventHook(
        IntPtr hook)
    {
        if (hook != IntPtr.Zero)
        {
            UnhookWinEvent(hook);
        }
    }

    // ------------------------------------------------------------
    // CAPS LOCK STATE
    // ------------------------------------------------------------

    [DllImport("user32.dll")]
    private static extern short GetKeyState(
        int nVirtKey
    );

    public static bool IsCapsLockOn()
    {
        return (GetKeyState(VK_CAPITAL) & 1) != 0;
    }

    // ------------------------------------------------------------
    // CLASSIC WIN32 CARET
    // ------------------------------------------------------------

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

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern bool GetGUIThreadInfo(
        uint idThread,
        ref GUITHREADINFO lpgui
    );

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(
        IntPtr hWnd,
        ref POINT lpPoint
    );

    public static bool TryGetCaretPosition(
        out int x,
        out int y)
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
            X =
                (info.rcCaret.Left +
                 info.rcCaret.Right) / 2,

            Y = info.rcCaret.Bottom
        };

        if (!ClientToScreen(
                info.hwndCaret,
                ref point))
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