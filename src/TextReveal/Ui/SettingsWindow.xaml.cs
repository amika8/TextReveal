using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TextReveal.Ocr;
using TextReveal.Shared;

namespace TextReveal.Ui;

/// <summary>
/// Settings window: global shortcut, Windows startup entry and OCR language.
/// Everything is written to <c>config.json</c>, which the running application reloads live -
/// the settings window and the search overlay are two windows of the same process.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private const string CapturePrompt = "Press the combination…";
    private const string IdleHint = "At least one modifier (Ctrl, Alt, Shift or Win) is required.";
    private const string CaptureHint = "Esc cancels · Backspace restores the saved shortcut.";

    private readonly AppConfig _savedConfig;

    private HotKeyDefinition _hotKey;
    private bool _isCapturing;

    public SettingsWindow()
    {
        InitializeComponent();

        Icon = AppIcon.Load();

        _savedConfig = ConfigStore.Load();
        _hotKey = _savedConfig.HotKey;

        LoadOcrLanguages(_savedConfig.OcrLanguage);
        LoadStartupPreference();

        UpdateShortcutText();
        ShortcutHint.Text = IdleHint;
    }

    /// <summary>Path of this very executable: the startup entry must point at it.</summary>
    private static string? ExecutablePath
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

    private void LoadOcrLanguages(string? selectedTag)
    {
        var options = OcrLanguageCatalog.Load();

        OcrLanguageBox.ItemsSource = options;
        OcrLanguageBox.SelectedItem =
            options.FirstOrDefault(option => string.Equals(option.Tag, selectedTag, StringComparison.OrdinalIgnoreCase))
            ?? options[0];

        OcrLanguageHint.Text = options.Count switch
        {
            <= 1 => "No extra OCR language detected: Windows will use the languages of your profile.",
            2 => "1 OCR language installed on this PC.",
            _ => $"{options.Count - 1} OCR languages installed on this PC.",
        };
    }

    private void LoadStartupPreference()
    {
        var executablePath = ExecutablePath;
        StartupCheckBox.IsChecked = StartupRegistrar.IsEnabled(executablePath);

        if (executablePath is null)
        {
            StartupCheckBox.IsEnabled = false;
            StartupHint.Text = "The executable path could not be resolved: automatic startup is unavailable.";
            StartupHint.Visibility = Visibility.Visible;
        }
    }

    private void UpdateShortcutText() => ShortcutButton.Content = _hotKey.DisplayText;

    /// <summary>Starts listening for the next key combination.</summary>
    private void BeginCapture()
    {
        _isCapturing = true;
        ShortcutButton.Content = CapturePrompt;
        ShortcutHint.Text = CaptureHint;
        Keyboard.Focus(ShortcutButton);
    }

    private void EndCapture()
    {
        _isCapturing = false;
        UpdateShortcutText();
        ShortcutHint.Text = IdleHint;
    }

    private void OnShortcutButtonClick(object sender, RoutedEventArgs e) => BeginCapture();

    private void OnShortcutButtonLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_isCapturing)
        {
            EndCapture();
        }
    }


    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_isCapturing)
        {
            HandleCaptureKey(e);
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void HandleCaptureKey(KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            EndCapture();
            return;
        }

        if (IsModifierKey(key))
        {
            ShortcutButton.Content = DescribeModifiers(Keyboard.Modifiers);
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && (key is Key.Back or Key.Delete))
        {
            _hotKey = _savedConfig.HotKey;
            EndCapture();
            Report($"Shortcut restored: {_hotKey.DisplayText}.", isError: false);
            return;
        }

        var candidate = new HotKeyDefinition(Keyboard.Modifiers, key);

        if (!candidate.IsValid)
        {
            ShortcutHint.Text = "Add Ctrl, Alt, Shift or Win: a global shortcut needs at least one modifier.";
            ShortcutButton.Content = DescribeModifiers(Keyboard.Modifiers);
            return;
        }

        _hotKey = candidate;
        EndCapture();
        Report($"Shortcut selected: {candidate.DisplayText}. Not saved.", isError: false);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_isCapturing)
        {
            EndCapture();
        }

        var runAtStartup = StartupCheckBox.IsChecked == true && StartupCheckBox.IsEnabled;

        // Reload first: this window owns two fields, not the whole file. Anything else the app
        // stores in config.json is written back untouched.
        var config = ConfigStore.Load() with
        {
            HotKey = _hotKey,
            OcrLanguage = (OcrLanguageBox.SelectedItem as OcrLanguageOption)?.Tag,
        };

        if (!ConfigStore.TrySave(config))
        {
            Report("Could not write config.json (insufficient rights on %LOCALAPPDATA%).", isError: true);
            return;
        }

        if (!TryApplyStartupPreference(runAtStartup, out var error))
        {
            Report(error, isError: true);
            return;
        }

        Report($"Saved: {config.HotKey.DisplayText}.", isError: false);
    }

    private static bool TryApplyStartupPreference(bool enabled, out string error)
    {
        if (!enabled)
        {
            error = StartupRegistrar.TrySetEnabled(false, string.Empty)
                ? string.Empty
                : "Could not remove the startup entry.";

            return error.Length == 0;
        }

        if (ExecutablePath is not { } executablePath)
        {
            error = "The executable path could not be resolved: automatic startup was not configured.";
            return false;
        }

        if (!StartupRegistrar.TrySetEnabled(true, executablePath))
        {
            error = "Could not write the startup entry (registry unreachable).";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (_isCapturing)
        {
            EndCapture();
        }

        _hotKey = HotKeyDefinition.Default;
        UpdateShortcutText();
        Report($"Shortcut reset to {_hotKey.DisplayText}. Not saved.", isError: false);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void Report(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource(isError ? "ErrorBrush" : "SuccessBrush");
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System or Key.None;

    private static string DescribeModifiers(ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.None)
        {
            return CapturePrompt;
        }

        var builder = new StringBuilder();
        if (modifiers.HasFlag(ModifierKeys.Control)) builder.Append("Ctrl + ");
        if (modifiers.HasFlag(ModifierKeys.Alt)) builder.Append("Alt + ");
        if (modifiers.HasFlag(ModifierKeys.Shift)) builder.Append("Shift + ");
        if (modifiers.HasFlag(ModifierKeys.Windows)) builder.Append("Win + ");
        builder.Append('…');

        return builder.ToString();
    }
}
