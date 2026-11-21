using Microsoft.Win32;

namespace MochiIsland;

internal static class LoginStartup
{
    private const string ValueName = "MochiIsland";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string text && text.Length > 0;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;
        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return;
        }
        var exe = Environment.ProcessPath ?? "";
        key.SetValue(ValueName, exe.Contains(' ') ? "\"" + exe + "\"" : exe);
    }
}
