using Microsoft.Win32;
using System.IO;

namespace Pulsive;

internal static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Pulsive";
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string command && !string.IsNullOrWhiteSpace(command);
    }
    internal static string BuildCommand(string executable, string? assemblyPath = null)
    {
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(assemblyPath)) throw new InvalidOperationException("Cannot locate the pulsive application.");
            return $"\"{executable}\" \"{assemblyPath}\" --startup";
        }
        return $"\"{executable}\" --startup";
    }
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Windows startup settings are unavailable.");
        if (enabled)
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the pulsive executable.");
            string assemblyPath = Path.Combine(AppContext.BaseDirectory, typeof(App).Assembly.GetName().Name + ".dll");
            key.SetValue(ValueName, BuildCommand(executable, assemblyPath), RegistryValueKind.String);
        }
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
