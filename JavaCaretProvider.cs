using System.Runtime.InteropServices;

namespace CapsCaret;

internal sealed class JavaCaretProvider
{
    private const string AccessBridgeDll =
        "WindowsAccessBridge-64.dll";

    private bool _available;
    private bool _initializationAttempted;

    public void Initialize()
    {
        if (_initializationAttempted)
            return;

        _initializationAttempted = true;

        try
        {
            // Инициализирует Windows-side Java Access Bridge.
            Windows_run();

            _available = true;
        }
        catch (DllNotFoundException)
        {
            _available = false;
        }
        catch (EntryPointNotFoundException)
        {
            _available = false;
        }
        catch (BadImageFormatException)
        {
            _available = false;
        }
    }

    public bool TryGetCaretPosition(
        out int x,
        out int y)
    {
        x = 0;
        y = 0;

        if (!_initializationAttempted)
            Initialize();

        if (!_available)
            return false;

        var hwnd = GetForegroundWindow();

        if (hwnd == IntPtr.Zero)
            return false;

        // Быстро отсеиваем Chrome, Notepad и всё,
        // что вообще не является Java window.
        if (isJavaWindow(hwnd) == 0)
            return false;

        long accessibleContext = 0;
        int vmId = 0;

        try
        {
            if (getAccessibleContextWithFocus(
                    hwnd,
                    out vmId,
                    out accessibleContext) == 0)
            {
                return false;
            }

            if (accessibleContext == 0)
                return false;

            // Получаем реальный индекс caret в Java text component.
            if (getAccessibleTextInfo(
                    vmId,
                    accessibleContext,
                    out var textInfo,
                    0,
                    0) == 0)
            {
                return false;
            }

            if (textInfo.caretIndex < 0)
                return false;

            // И уже по индексу — его экранную геометрию.
            if (getCaretLocation(
                    vmId,
                    accessibleContext,
                    out var rect,
                    textInfo.caretIndex) == 0)
            {
                return false;
            }

            if (rect.x < 0 || rect.y < 0)
                return false;

            // Наш Overlay ожидает центр caret по X
            // и нижнюю точку по Y.
            x = rect.x + Math.Max(rect.width, 1) / 2;
            y = rect.y + Math.Max(rect.height, 1);

            return true;
        }
        finally
        {
            // AccessibleContext — объект JVM.
            // Его обязательно надо отпустить.
            if (accessibleContext != 0)
            {
                releaseJavaObject(
                    vmId,
                    accessibleContext
                );
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccessibleTextInfo
    {
        public int charCount;
        public int caretIndex;
        public int indexAtPoint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccessibleTextRectInfo
    {
        public int x;
        public int y;
        public int width;
        public int height;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport(
        AccessBridgeDll,
        EntryPoint = "Windows_run",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void Windows_run();

    [DllImport(
        AccessBridgeDll,
        EntryPoint = "isJavaWindow",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int isJavaWindow(
        IntPtr hwnd
    );

    [DllImport(
        AccessBridgeDll,
        EntryPoint = "getAccessibleContextWithFocus",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int getAccessibleContextWithFocus(
        IntPtr hwnd,
        out int vmId,
        out long accessibleContext
    );

    [DllImport(
        AccessBridgeDll,
        EntryPoint = "getAccessibleTextInfo",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int getAccessibleTextInfo(
        int vmId,
        long accessibleText,
        out AccessibleTextInfo textInfo,
        int x,
        int y
    );

    [DllImport(
        AccessBridgeDll,
        EntryPoint = "getCaretLocation",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int getCaretLocation(
        int vmId,
        long accessibleContext,
        out AccessibleTextRectInfo rectInfo,
        int index
    );

    [DllImport(
        AccessBridgeDll,
        EntryPoint = "releaseJavaObject",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void releaseJavaObject(
        int vmId,
        long javaObject
    );
}