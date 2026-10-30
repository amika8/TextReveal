using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using TextReveal.Shared;

namespace TextReveal.Services;

/// <summary>What the user asked for in the install window.</summary>
internal readonly record struct InstallOptions(
    bool DesktopShortcut,
    bool StartMenuShortcut,
    bool StartWithWindows);

/// <summary>
/// Per-user installation, no elevation: the executable is copied to
/// <c>%LOCALAPPDATA%\Programs\TextReveal</c>, a desktop and a Start menu shortcut point at it, and
/// one <c>HKCU</c> uninstall entry lets Windows remove it like any other application.
/// </summary>
internal static class Installer
{
    private const string AppName = "TextReveal";
    private const string Publisher = "amika8";
    private const string ShortcutDescription = "TextReveal - search and copy any text on screen";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TextReveal";

    /// <summary>Folder of the installed copy: inside the user profile, writable without admin rights.</summary>
    public static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

    /// <summary>Full path of the installed executable.</summary>
    public static string InstalledExecutablePath => Path.Combine(InstallDirectory, AppName + ".exe");

    /// <summary>True when an installation already exists.</summary>
    public static bool IsInstalled => File.Exists(InstalledExecutablePath);

    /// <summary>Path of the executable currently running, or <c>null</c> when it cannot be resolved.</summary>
    public static string? CurrentExecutablePath
    {
        get
        {
            var path = Environment.ProcessPath;

            return string.IsNullOrEmpty(path) ||
                !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)
                ? null
                : path;
        }
    }

    /// <summary>True when this process already runs from the installation folder.</summary>
    public static bool IsRunningInstalled =>
        CurrentExecutablePath is { } path &&
        string.Equals(path, InstalledExecutablePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Copies the running executable into the install folder, then creates the shortcuts, the
    /// startup entry and the uninstall entry.
    /// </summary>
    /// <param name="error">Explains anything that failed, empty when everything succeeded.</param>
    /// <returns><c>true</c> when a runnable copy sits in <see cref="InstalledExecutablePath"/>.</returns>
    public static bool TryInstall(InstallOptions options, out string error)
    {
        error = string.Empty;

        if (CurrentExecutablePath is not { } source)
        {
            error = "The path of this executable could not be resolved.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(InstallDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            error = $"Could not create {InstallDirectory}.";
            return false;
        }

        if (!string.Equals(source, InstalledExecutablePath, StringComparison.OrdinalIgnoreCase) &&
            !TryCopyExecutable(source, out error))
        {
            return false;
        }

        // From here on every step is best effort: an installed executable that runs beats a
        // perfectly decorated install.
        var warnings = new List<string>();

        if (options.StartMenuShortcut)
        {
            if (StartMenuShortcutPath is { } startMenu && !TryCreateShortcut(startMenu))
            {
                warnings.Add("Start menu shortcut");
            }
        }
        else
        {
            TryDelete(StartMenuShortcutPath);
        }

        if (options.DesktopShortcut)
        {
            if (DesktopShortcutPath is { } desktop && !TryCreateShortcut(desktop))
            {
                warnings.Add("desktop shortcut");
            }
        }
        else
        {
            TryDelete(DesktopShortcutPath);
        }

        var startupApplied = options.StartWithWindows
            ? StartupRegistrar.TrySetEnabled(true, InstalledExecutablePath)
            : StartupRegistrar.TrySetEnabled(false, InstalledExecutablePath);

        if (!startupApplied)
        {
            warnings.Add("startup entry");
        }

        if (!TryRegisterUninstallEntry())
        {
            warnings.Add("uninstall entry in Windows settings");
        }

        error = warnings.Count == 0
            ? string.Empty
            : $"Installed, but these could not be set up: {string.Join(", ", warnings)}.";

        return true;
    }

    /// <summary>
    /// Removes the shortcuts, the startup entry, the uninstall entry and the folder itself.
    /// </summary>
    /// <returns><c>true</c> when nothing is left behind once this process exits.</returns>
    public static bool TryUninstall(out string error)
    {
        error = string.Empty;

        TryDelete(StartMenuShortcutPath);
        TryDelete(DesktopShortcutPath);
        StartupRegistrar.TrySetEnabled(false, InstalledExecutablePath);
        TryDeleteUninstallEntry();

        if (!IsRunningInstalled)
        {
            return TryDeleteInstallFolder(out error);
        }

        // A running executable cannot delete its own file: a hidden shell waits for this process
        // to exit and retries until the folder is gone.
        if (!TryScheduleFolderRemoval())
        {
            error = $"Could not remove {InstallDirectory}.";
            return false;
        }

        return true;
    }

    /// <summary>Starts the installed copy, the one the shortcuts point at.</summary>
    public static bool TryStartInstalled(out string error)
    {
        error = string.Empty;

        try
        {
            using var process = Process.Start(new ProcessStartInfo(InstalledExecutablePath)
            {
                UseShellExecute = true,
                WorkingDirectory = InstallDirectory,
            });

            if (process is null)
            {
                error = "Windows did not start the installed copy.";
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is Win32Exception
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static string? StartMenuShortcutPath => KnownFolder(Environment.SpecialFolder.Programs) is { } programs
        ? Path.Combine(programs, AppName + ".lnk")
        : null;

    private static string? DesktopShortcutPath => KnownFolder(Environment.SpecialFolder.DesktopDirectory) is { } desktop
        ? Path.Combine(desktop, AppName + ".lnk")
        : null;

    private static string? KnownFolder(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder);

        return string.IsNullOrEmpty(path) ? null : path;
    }

    /// <summary>
    /// Copies this executable into the install folder. A previous copy may still be running, or an
    /// antivirus may hold the new file for a second: retry briefly instead of failing on the first lock.
    /// </summary>
    private static bool TryCopyExecutable(string source, out string error)
    {
        const int Attempts = 10;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                File.Copy(source, InstalledExecutablePath, overwrite: true);
                error = string.Empty;
                return true;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or SecurityException)
            {
                if (attempt == Attempts - 1)
                {
                    break;
                }
            }

            Thread.Sleep(200);
        }

        error = $"Could not write {InstalledExecutablePath}.";
        return false;
    }

    /// <summary>
    /// Creates a <c>.lnk</c> through the Windows Script Host: no shell extension to ship, and the
    /// icon of the executable comes along for free.
    /// </summary>
    private static bool TryCreateShortcut(string shortcutPath)
    {
        object? shell = null;
        object? shortcut = null;

        try
        {
            if (Type.GetTypeFromProgID("WScript.Shell") is not { } shellType ||
                Activator.CreateInstance(shellType) is not { } host)
            {
                return false;
            }

            shell = host;

            dynamic scripting = host;
            if (scripting.CreateShortcut(shortcutPath) is not { } linkObject)
            {
                return false;
            }

            shortcut = linkObject;

            dynamic link = linkObject;
            link.TargetPath = InstalledExecutablePath;
            link.WorkingDirectory = InstallDirectory;
            link.IconLocation = InstalledExecutablePath + ",0";
            link.Description = ShortcutDescription;
            link.WindowStyle = 1;
            link.Save();

            return File.Exists(shortcutPath);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // No scripting host, no COM, a locked profile folder: the caller only needs a boolean.
            return false;
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is null)
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(instance);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // Not a COM object, or already released: nothing to do.
        }
    }

    /// <summary>Declares the application in "apps and features", so Windows can remove it.</summary>
    private static bool TryRegisterUninstallEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            var uninstallCommand = $"\"{InstalledExecutablePath}\" --uninstall";

            key.SetValue("DisplayName", AppName, RegistryValueKind.String);
            key.SetValue("DisplayIcon", InstalledExecutablePath + ",0", RegistryValueKind.String);
            key.SetValue("DisplayVersion", ApplicationVersion, RegistryValueKind.String);
            key.SetValue("Publisher", Publisher, RegistryValueKind.String);
            key.SetValue("InstallLocation", InstallDirectory, RegistryValueKind.String);
            key.SetValue("UninstallString", uninstallCommand, RegistryValueKind.String);
            key.SetValue("QuietUninstallString", uninstallCommand, RegistryValueKind.String);
            key.SetValue(
                "InstallDate",
                DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                RegistryValueKind.String);
            key.SetValue("EstimatedSize", EstimateSizeInKilobytes(), RegistryValueKind.DWord);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or SecurityException
            or IOException)
        {
            return false;
        }
    }

    private static void TryDeleteUninstallEntry()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or SecurityException
            or IOException)
        {
            // An orphan entry only costs one line in Windows settings.
        }
    }

    private static string ApplicationVersion =>
        typeof(Installer).Assembly.GetName().Version is { } version
            ? version.ToString(3)
            : "1.0.0";

    private static int EstimateSizeInKilobytes()
    {
        try
        {
            var bytes = File.Exists(InstalledExecutablePath)
                ? new FileInfo(InstalledExecutablePath).Length
                : 0L;

            return (int)Math.Clamp(bytes / 1024L, 1L, int.MaxValue);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException)
        {
            return 1;
        }
    }

    private static bool TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException)
        {
            return false;
        }
    }

    private static bool TryDeleteInstallFolder(out string error)
    {
        error = string.Empty;

        try
        {
            if (Directory.Exists(InstallDirectory))
            {
                Directory.Delete(InstallDirectory, recursive: true);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException)
        {
            error = $"Could not remove {InstallDirectory}.";
            return false;
        }
    }

    /// <summary>
    /// Windows refuses to delete a running executable, so a detached shell retries the folder
    /// removal until it succeeds - which happens as soon as this process has exited.
    /// </summary>
    private static bool TryScheduleFolderRemoval()
    {
        var command =
            $"for /l %i in (1,1,60) do @(rd /s /q \"{InstallDirectory}\" 2>nul & " +
            $"if not exist \"{InstallDirectory}\" exit & ping -n 2 127.0.0.1 >nul)";

        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + command)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            return process is not null;
        }
        catch (Exception exception) when (exception is Win32Exception
            or InvalidOperationException
            or IOException)
        {
            return false;
        }
    }
}
