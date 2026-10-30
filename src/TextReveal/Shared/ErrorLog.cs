using System.Globalization;
using System.IO;
using System.Security;
using System.Text;

namespace TextReveal.Shared;

/// <summary>
/// Best-effort exception log in <c>%LOCALAPPDATA%\TextReveal\errors.log</c>.
/// Only exception types, messages and stack traces are written: never screen content, never
/// the clipboard, never anything about what the user was looking at. A logging failure is
/// swallowed, because logging must never be the reason the app dies.
/// </summary>
public static class ErrorLog
{
    private const string FileName = "errors.log";

    /// <summary>Upper bound of the log; past it the file is restarted, so it never grows forever.</summary>
    private const long MaxFileBytes = 256 * 1024;

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly object Gate = new();

    /// <summary>Full path of the log file.</summary>
    public static string FilePath => Path.Combine(ConfigStore.DirectoryPath, FileName);

    /// <summary>Appends <paramref name="exception"/>; does nothing and throws nothing on failure.</summary>
    public static void Write(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            var entry = new StringBuilder()
                .Append('[').Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)).Append("] ")
                .AppendLine(exception.GetType().FullName)
                .AppendLine(exception.Message)
                .AppendLine(exception.StackTrace)
                .AppendLine()
                .ToString();

            lock (Gate)
            {
                Directory.CreateDirectory(ConfigStore.DirectoryPath);

                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxFileBytes)
                {
                    file.Delete();
                }

                File.AppendAllText(FilePath, entry, Utf8NoBom);
            }
        }
        catch (Exception loggingFailure) when (loggingFailure is IOException
            or UnauthorizedAccessException
            or SecurityException
            or NotSupportedException)
        {
            // Logging is a courtesy: the app keeps running even when the disk is read-only.
        }
    }
}
