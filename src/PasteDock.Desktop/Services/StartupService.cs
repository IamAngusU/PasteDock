using Microsoft.Win32;

namespace PasteDock.Desktop.Services;

internal static class StartupService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is unavailable.");
            if (!Path.GetFileName(executable).Equals("PasteDock.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Enable startup from the published PasteDock.exe, not dotnet run.");
            key.SetValue("PasteDock", $"\"{executable}\" --background", RegistryValueKind.String);
        }
        else key.DeleteValue("PasteDock", throwOnMissingValue: false);
    }
}
