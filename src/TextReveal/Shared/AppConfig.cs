namespace TextReveal.Shared;

/// <summary>
/// User preferences persisted in <c>config.json</c>.
/// The startup entry itself lives in the registry: this file never duplicates it.
/// </summary>
public sealed record AppConfig
{
    /// <summary>Global shortcut used to freeze the screen.</summary>
    public HotKeyDefinition HotKey { get; init; } = HotKeyDefinition.Default;

    /// <summary>BCP-47 tag of the OCR language; <c>null</c> means "Windows profile languages".</summary>
    public string? OcrLanguage { get; init; }

    /// <summary>
    /// True once the "install TextReveal for this user?" question has been answered, so the
    /// portable copy never asks twice.
    /// </summary>
    public bool InstallPromptHandled { get; init; }
}
