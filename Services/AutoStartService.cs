using Microsoft.Win32;
using System.Diagnostics;

namespace ExCord.Services;

public sealed class AutoStartService
{
    private const string AppName = "ExCord";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        if (key is null)
        {
            return false;
        }

        var value = key.GetValue(AppName);
        return value is not null;
    }

    public void ReconcilePathIfNeeded(bool autoStartEnabled)
    {
        if (!autoStartEnabled)
        {
            return;
        }

        try
        {
            if (!IsEnabled())
            {
                return;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            var registeredPath = key?.GetValue(AppName)?.ToString()?.Trim().Trim('"');
            var currentPath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(registeredPath) || string.IsNullOrWhiteSpace(currentPath))
            {
                return;
            }

            if (!string.Equals(registeredPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                SetEnabled(true);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AutoStartService.ReconcilePathIfNeeded");
        }
    }

    public void SetEnabled(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true) ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (key is null)
        {
            return;
        }

        var executablePath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        if (enable)
        {
            key.SetValue(AppName, $"\"{executablePath}\"");
            return;
        }

        key.DeleteValue(AppName, false);
    }
}
