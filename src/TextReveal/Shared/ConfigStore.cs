using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TextReveal.Shared;

/// <summary>
/// Reads and writes <c>%LOCALAPPDATA%\TextReveal\config.json</c>.
/// Every failure falls back to defaults: a broken file must never block the app.
/// </summary>
public static class ConfigStore
{
    private const string FolderName = "TextReveal";
    private const string FileName = "config.json";

    /// <summary>A preferences file is a few hundred bytes: anything much larger is not ours.</summary>
    private const long MaxFileBytes = 64 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        // String-only enum values: a hand-edited numeric enum is simply rejected by the loader.
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    /// <summary>UTF-8 without a byte order mark: the file stays plain ASCII/UTF-8 JSON.</summary>
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Folder holding the configuration file.</summary>
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName);

    /// <summary>Full path of the configuration file.</summary>
    public static string FilePath => Path.Combine(DirectoryPath, FileName);

    /// <summary>Loads the configuration, or defaults when missing/invalid.</summary>
    public static AppConfig Load()
    {
        try
        {
            // Size is checked before reading: a huge or hostile file must not exhaust memory.
            var file = new FileInfo(FilePath);
            if (!file.Exists || file.Length is <= 0 or > MaxFileBytes)
            {
                return new AppConfig();
            }

            var json = File.ReadAllText(FilePath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);

            return config is null
                ? new AppConfig()
                : config with { HotKey = config.HotKey.IsValid ? config.HotKey : HotKeyDefinition.Default };
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or NotSupportedException
            or ArgumentException
            or JsonException)
        {
            return new AppConfig();
        }
    }

    /// <summary>Persists the configuration atomically (write then replace).</summary>
    public static bool TrySave(AppConfig config)
    {
        // A unique name in the target folder: no predictable path that another process could
        // pre-create (symbolic link, junction) to redirect the write.
        var temporaryPath = Path.Combine(DirectoryPath, $"{FileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(config, SerializerOptions), Utf8NoBom);
            File.Move(temporaryPath, FilePath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or NotSupportedException
            or JsonException)
        {
            return false;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    /// <summary>Removes a leftover temporary file; never throws.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            // A stale .tmp file is harmless: it is never read and never overwritten.
        }
    }
}
