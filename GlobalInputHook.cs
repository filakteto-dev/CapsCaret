using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace CapsCaret;

internal sealed class GlobalInputHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;

    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;

    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;

    private const int WM_MOUSEWHEEL = 0x020A;

    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;

    private const int WM_MOUSEHWHEEL = 0x020E;

    private const int VK_CAPITAL = 0x14;

    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;

    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;

    private readonly object _keyLock = new();

    private readonly HashSet<int>
        _pressedInputKeys = new();

    private bool _capsPhysicallyDown;

    private int _mouseButtonsDown;

    public event Action? InputActivity;
    public event Action? CapsLockPressed;

    public bool IsInputActive
    {
        get
        {
            lock (_keyLock)
            {
                if (_pressedInputKeys.Count > 0)
                    return true;
            }

            return Volatile.Read(
                ref _mouseButtonsDown
            ) > 0;
        }
    }

    public GlobalInputHook()
    {
        _keyboardProc =
            KeyboardHookCallback;

        _mouseProc =
            MouseHookCallback;
    }

    public void Start()
    {
        if (_keyboardHook != IntPtr.Zero)
            return;

        var module =
            GetModuleHandle(null);

        _keyboardHook =
            SetWindowsHookEx(
                WH_KEYBOARD_LL,
                _keyboardProc,
                module,
                0
            );

        _mouseHook =
            SetWindowsHookEx(
                WH_MOUSE_LL,
                _mouseProc,
                module,
                0
            );

        if (_keyboardHook == IntPtr.Zero ||
            _mouseHook == IntPtr.Zero)
        {
            int error =
                Marshal.GetLastWin32Error();

            Dispose();

            throw new Win32Exception(error);
        }
    }

    private IntPtr KeyboardHookCallback(
        int nCode,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message =
                wParam.ToInt32();

            // vkCode is the first member
            // of KBDLLHOOKSTRUCT.
            int virtualKey =
                Marshal.ReadInt32(lParam);

            bool keyDown =
                message == WM_KEYDOWN ||
                message == WM_SYSKEYDOWN;

            bool keyUp =
                message == WM_KEYUP ||
                message == WM_SYSKEYUP;

            if (virtualKey == VK_CAPITAL)
            {
                if (keyDown &&
                    !_capsPhysicallyDown)
                {
                    _capsPhysicallyDown = true;

                    CapsLockPressed?.Invoke();
                }

                if (keyUp)
                {
                    _capsPhysicallyDown = false;
                }
            }
            else if (!IsModifierKey(virtualKey))
            {
                bool stateChanged = false;

                if (keyDown)
                {
                    lock (_keyLock)
                    {
                        stateChanged =
                            _pressedInputKeys.Add(
                                virtualKey
                            );
                    }
                }
                else if (keyUp)
                {
                    lock (_keyLock)
                    {
                        stateChanged =
                            _pressedInputKeys.Remove(
                                virtualKey
                            );
                    }
                }

                // Auto-repeat keydown does not produce
                // extra events because Add() returns false.
                if (stateChanged)
                {
                    InputActivity?.Invoke();
                }
            }
        }

        return CallNextHookEx(
            _keyboardHook,
            nCode,
            wParam,
            lParam
        );
    }

    private IntPtr MouseHookCallback(
        int nCode,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message =
                wParam.ToInt32();

            switch (message)
            {
                case WM_LBUTTONDOWN:
                case WM_RBUTTONDOWN:
                case WM_MBUTTONDOWN:
                case WM_XBUTTONDOWN:

                    Interlocked.Increment(
                        ref _mouseButtonsDown
                    );

                    InputActivity?.Invoke();
                    break;

                case WM_LBUTTONUP:
                case WM_RBUTTONUP:
                case WM_MBUTTONUP:
                case WM_XBUTTONUP:

                    DecrementMouseButtons();

                    InputActivity?.Invoke();
                    break;

                case WM_MOUSEWHEEL:
                case WM_MOUSEHWHEEL:

                    // Wheel is instantaneous:
                    // hide now, then App will restart
                    // its 220 ms idle delay.
                    InputActivity?.Invoke();
                    break;
            }
        }

        // WM_MOUSEMOVE is intentionally ignored.
        // Merely hovering UI must not wake CapsCaret.
        return CallNextHookEx(
            _mouseHook,
            nCode,
            wParam,
            lParam
        );
    }

    private void DecrementMouseButtons()
    {
        while (true)
        {
            int current =
                Volatile.Read(
                    ref _mouseButtonsDown
                );

            if (current <= 0)
                return;

            if (Interlocked.CompareExchange(
                    ref _mouseButtonsDown,
                    current - 1,
                    current
                ) == current)
            {
                return;
            }
        }
    }

    private static bool IsModifierKey(
        int key)
    {
        return key is
            0x10 or // Shift
            0x11 or // Ctrl
            0x12 or // Alt

            0x5B or // Left Win
            0x5C or // Right Win

            0xA0 or // Left Shift
            0xA1 or // Right Shift
            0xA2 or // Left Ctrl
            0xA3 or // Right Ctrl
            0xA4 or // Left Alt
            0xA5;   // Right Alt
    }

    public void Dispose()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(
                _keyboardHook
            );

            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(
                _mouseHook
            );

            _mouseHook = IntPtr.Zero;
        }

        lock (_keyLock)
        {
            _pressedInputKeys.Clear();
        }

        _mouseButtonsDown = 0;
    }

    private delegate IntPtr HookProc(
        int nCode,
        IntPtr wParam,
        IntPtr lParam
    );

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        HookProc lpfn,
        IntPtr hMod,
        uint dwThreadId
    );

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(
        IntPtr hhk
    );

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam
    );

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(
        string? lpModuleName
    );
}