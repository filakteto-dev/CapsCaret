using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CapsCaret;

internal static class NativeMethods
{
    private const int VK_CAPITAL = 0x14;
    private const uint OBJID_CARET = 0xFFFFFFF8;
    private const int CHILDID_SELF = 0;

    private const uint GA_ROOT = 2;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    internal const uint EventSystemMoveSizeStart = 0x000A;
    internal const uint EventSystemMoveSizeEnd = 0x000B;

    private static readonly Guid IID_IAccessible =
        new("618736E0-3C3D-11CF-810C-00AA00389B71");

    // ------------------------------------------------------------
    // NATIVE STRUCTURES
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
    private struct WINDOW_RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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

    internal readonly record struct WindowBounds(
        int Left,
        int Top,
        int Right,
        int Bottom
    );

    // ------------------------------------------------------------
    // STANDARD WINDOW MOVE / RESIZE EVENTS
    // ------------------------------------------------------------

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
    // FOREGROUND WINDOW GEOMETRY
    // ------------------------------------------------------------

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(
        IntPtr hwnd,
        uint gaFlags
    );

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(
        IntPtr hwnd,
        out WINDOW_RECT rect
    );

    internal static bool TryGetForegroundRootBounds(
        out IntPtr rootHwnd,
        out WindowBounds bounds)
    {
        rootHwnd = IntPtr.Zero;
        bounds = default;

        var foreground =
            GetForegroundWindow();

        if (foreground == IntPtr.Zero)
            return false;

        var foregroundRoot =
            GetAncestor(
                foreground,
                GA_ROOT
            );

        if (foregroundRoot == IntPtr.Zero)
        {
            foregroundRoot = foreground;
        }

        if (!GetWindowRect(
                foregroundRoot,
                out var rect))
        {
            return false;
        }

        rootHwnd = foregroundRoot;

        bounds = new WindowBounds(
            rect.Left,
            rect.Top,
            rect.Right,
            rect.Bottom
        );

        return true;
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
    // GUI THREAD INFO
    // ------------------------------------------------------------

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern bool GetGUIThreadInfo(
        uint idThread,
        ref GUITHREADINFO lpgui
    );

    private static bool TryGetGuiThreadInfo(
        out GUITHREADINFO info)
    {
        info = new GUITHREADINFO
        {
            cbSize =
                Marshal.SizeOf<GUITHREADINFO>()
        };

        return GetGUIThreadInfo(
            0,
            ref info
        );
    }

    // ------------------------------------------------------------
    // ACTIVE ACCESSIBILITY CARET
    //
    // Used first because Chromium exposes a useful caret here.
    // Kept separate from the classic Win32 caret deliberately.
    // ------------------------------------------------------------

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr hWnd,
        out uint lpdwProcessId
    );

    private static bool ShouldSkipAccessibleCaret(
        IntPtr hwnd)
    {
        var root =
            GetAncestor(
                hwnd,
                GA_ROOT
            );

        if (root == IntPtr.Zero)
        {
            root = hwnd;
        }

        GetWindowThreadProcessId(
            root,
            out uint processId
        );

        if (processId == 0 ||
            processId > int.MaxValue)
        {
            return false;
        }

        try
        {
            using var process =
                Process.GetProcessById(
                    (int)processId
                );

            // Telegram Desktop can expose a formally valid OBJID_CARET
            // whose geometry becomes stale after mouse interaction.
            // Let UI Automation handle Telegram instead.
            return string.Equals(
                process.ProcessName,
                "Telegram",
                StringComparison.OrdinalIgnoreCase
            );
        }
        catch
        {
            // If process inspection fails, preserve the previous behavior.
            return false;
        }
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        IntPtr hwnd,
        uint dwId,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)]
        out object? ppvObject
    );

    public static bool TryGetAccessibleCaretPosition(
        out int x,
        out int y)
    {
        x = 0;
        y = 0;

        if (!TryGetGuiThreadInfo(out var info))
            return false;

        if (info.hwndFocus == IntPtr.Zero)
            return false;

        if (ShouldSkipAccessibleCaret(
                info.hwndFocus))
        {
            return false;
        }

        object? accessibleObject = null;

        try
        {
            var iid = IID_IAccessible;

            int result =
                AccessibleObjectFromWindow(
                    info.hwndFocus,
                    OBJID_CARET,
                    ref iid,
                    out accessibleObject
                );

            if (result < 0 ||
                accessibleObject is not
                    Accessibility.IAccessible accessible)
            {
                return false;
            }

            accessible.accLocation(
                out int left,
                out int top,
                out int width,
                out int height,
                CHILDID_SELF
            );

            if (left < 0 ||
                top < 0 ||
                height <= 0)
            {
                return false;
            }

            x =
                left +
                Math.Max(width, 1) / 2;

            y =
                top +
                height;

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (accessibleObject is not null &&
                Marshal.IsComObject(accessibleObject))
            {
                Marshal.ReleaseComObject(
                    accessibleObject
                );
            }
        }
    }

    // Classic Win32 caret is intentionally not used as a fallback.
    // Some custom applications expose native caret coordinates that
    // become stale or incorrect after mouse interaction.
}
