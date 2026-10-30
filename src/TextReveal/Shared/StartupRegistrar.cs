using System.IO;
using System.Security;
using Microsoft.Win32;

namespace TextReveal.Shared;

/// <summary>
/// Manages the <c>HKCU\...\Run</c> entry used to start TextReveal with Windows.
/// No scheduled task, no service: a single, removable registry value.
/// </summary>
public static class StartupRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TextReveal";

    /// <summary>
    /// True when the startup entry exists. When <paramref name="expectedExecutablePath"/> is given,
    /// the stored command line must also point at that executable: an entry left by a previous
    /// installation is reported as disabled instead of looking healthy.
    /// </summary>
    public static bool IsEnabled(string? expectedExecutablePath = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (key?.GetValue(ValueName) is not string value || value.Length == 0)
            {
                return false;
            }

            return expectedExecutablePath is null ||
                string.Equals(value.Trim('"'), expectedExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Creates or removes the startup entry.</summary>
    public static bool TrySetEnabled(bool enabled, string executablePath)
    {
        if (enabled && !IsSafeExecutablePath(executablePath))
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// A <c>Run</c> value is a command line: only a fully qualified <c>.exe</c> path without any
    /// quote is accepted, so the value can never append arguments or escape its own quoting.
    /// </summary>
    private static bool IsSafeExecutablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Contains('"') ||
            path.Contains('\0') ||
            !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        return string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);
    }
}
