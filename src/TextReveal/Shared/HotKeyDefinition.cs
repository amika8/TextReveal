using System.Text;
using System.Text.Json.Serialization;
using System.Windows.Input;

using KeyEnum = System.Windows.Input.Key;

namespace TextReveal.Shared;

/// <summary>
/// Describes a global shortcut: a set of modifier keys plus a single virtual key.
/// </summary>
public readonly record struct HotKeyDefinition
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    /// <summary>The only modifier bits a shortcut may carry.</summary>
    private const ModifierKeys SupportedModifiers =
        ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows;

    public HotKeyDefinition(ModifierKeys modifiers, KeyEnum key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    /// <summary>
    /// Shortcut shipped with TextReveal. <c>Win + F</c> is avoided on purpose:
    /// Windows already owns it (Feedback Hub), so it would never register.
    /// </summary>
    public static HotKeyDefinition Default => new(ModifierKeys.Control | ModifierKeys.Alt, KeyEnum.F);

    public ModifierKeys Modifiers { get; init; }

    public KeyEnum Key { get; init; }

    /// <summary>
    /// A shortcut is only usable when it has at least one real modifier and a defined key.
    /// Undefined modifier bits or an undefined key (a hand-edited <c>config.json</c> can hold both)
    /// would otherwise register a bare key as a global shortcut and swallow it everywhere.
    /// </summary>
    [JsonIgnore]
    public bool IsValid =>
        Key != KeyEnum.None &&
        Enum.IsDefined(Key) &&
        Modifiers != ModifierKeys.None &&
        (Modifiers & ~SupportedModifiers) == ModifierKeys.None;

    /// <summary>MOD_* flags understood by <c>RegisterHotKey</c>.</summary>
    [JsonIgnore]
    public uint Win32Modifiers
    {
        get
        {
            var value = ModNoRepeat;
            if (Modifiers.HasFlag(ModifierKeys.Alt)) value |= ModAlt;
            if (Modifiers.HasFlag(ModifierKeys.Control)) value |= ModControl;
            if (Modifiers.HasFlag(ModifierKeys.Shift)) value |= ModShift;
            if (Modifiers.HasFlag(ModifierKeys.Windows)) value |= ModWin;
            return value;
        }
    }

    /// <summary>Virtual-key code understood by <c>RegisterHotKey</c>.</summary>
    [JsonIgnore]
    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    /// <summary>Human readable form, e.g. <c>Ctrl + Alt + F</c>.</summary>
    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            if (Key == KeyEnum.None)
            {
                return "Not set";
            }

            var builder = new StringBuilder();
            if (Modifiers.HasFlag(ModifierKeys.Control)) builder.Append("Ctrl + ");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) builder.Append("Alt + ");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) builder.Append("Shift + ");
            if (Modifiers.HasFlag(ModifierKeys.Windows)) builder.Append("Win + ");
            builder.Append(KeyLabel(Key));
            return builder.ToString();
        }
    }

    public override string ToString() => DisplayText;

    /// <summary>Returns a short label for a key (<c>D5</c> becomes <c>5</c>).</summary>
    public static string KeyLabel(KeyEnum key) => key switch
    {
        >= KeyEnum.D0 and <= KeyEnum.D9 => ((char)('0' + (key - KeyEnum.D0))).ToString(),
        >= KeyEnum.NumPad0 and <= KeyEnum.NumPad9 => $"Numpad {key - KeyEnum.NumPad0}",
        KeyEnum.Space => "Space",
        _ => key.ToString(),
    };
}
