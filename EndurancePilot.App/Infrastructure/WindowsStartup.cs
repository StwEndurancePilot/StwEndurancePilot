using Microsoft.Win32;

namespace EndurancePilot.App;

internal static class WindowsStartup
{
    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (!enabled) { key.DeleteValue("EndurancePilot", false); return; }
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Application path is unavailable.");
        if (!Path.IsPathFullyQualified(executable) || executable.Contains('"') || !File.Exists(executable)) throw new InvalidOperationException("Application path is invalid.");
        key.SetValue("EndurancePilot", $"\"{executable}\" --tray", RegistryValueKind.String);
    }
}
