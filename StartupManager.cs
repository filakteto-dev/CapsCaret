using Microsoft.Win32;

namespace CapsCaret;

internal static class StartupManager
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string AppName = "CapsCaret";

    public static bool IsEnabled()
    {
        using var key =
            Registry.CurrentUser.OpenSubKey(RunKeyPath);

        if (key is null)
            return false;

        var value =
            key.GetValue(AppName) as string;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var currentPath =
            Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(currentPath))
            return false;

        var expected =
            $"\"{currentPath}\"";

        return string.Equals(
            value,
            expected,
            StringComparison.OrdinalIgnoreCase
        );
    }

    public static void SetEnabled(bool enabled)
    {
        using var key =
            Registry.CurrentUser.CreateSubKey(
                RunKeyPath,
                writable: true
            );

        if (key is null)
            return;

        if (!enabled)
        {
            key.DeleteValue(
                AppName,
                throwOnMissingValue: false
            );

            return;
        }

        var path =
            Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(path))
            return;

        key.SetValue(
            AppName,
            $"\"{path}\""
        );
    }
}