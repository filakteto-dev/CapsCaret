using System;
using System.Runtime.InteropServices;

namespace CapsCaret;

internal static class NativeMethods
{
    private const int VK_BACK = 0x08;
    private const int VK_DELETE = 0x2E;
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;

    private const int VK_HOME = 0x24;
    private const int VK_END = 0x23;
    
    private const int GUI_INMOVESIZE = 0x00000002;

    public static bool IsWindowMovingOrResizing()
    {
        var info = new GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<GUITHREADINFO>()
        };

        if (!GetGUIThreadInfo(0, ref info))
            return false;

        return (info.flags & GUI_INMOVESIZE) != 0;
    }
    
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    
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
        WinEventDelegate callback
    )
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

    internal static void RemoveWinEventHook(IntPtr hook)
    {
        if (hook != IntPtr.Zero)
            UnhookWinEvent(hook);
    }
    
    public static bool IsInputKeyHeld()
    {
        for (int key = 0x08; key <= 0xFE; key++)
        {
            if (ShouldIgnoreKey(key))
                continue;

            if ((GetAsyncKeyState(key) & 0x8000) != 0)
                return true;
        }

        return false;
    }
    private static bool ShouldIgnoreKey(int key)
    {
        return key switch
        {
            // Mouse buttons
            0x01 or // Left mouse
                0x02 or // Right mouse
                0x04 or // Middle mouse
                0x05 or
                0x06 => true,

            // Modifier keys themselves
            0x10 or // Shift
                0x11 or // Ctrl
                0x12 or // Alt

                0xA0 or // Left Shift
                0xA1 or // Right Shift
                0xA2 or // Left Ctrl
                0xA3 or // Right Ctrl
                0xA4 or // Left Alt
                0xA5 => true,

            // Lock keys
            0x14 or // Caps Lock
                0x90 or // Num Lock
                0x91 => true, // Scroll Lock

            _ => false
        };
    }
    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }
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
            X = (info.rcCaret.Left + info.rcCaret.Right) / 2,
            Y = info.rcCaret.Bottom
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