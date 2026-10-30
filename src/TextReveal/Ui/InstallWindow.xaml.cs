using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TextReveal.Services;
using TextReveal.Shared;

namespace TextReveal.Ui;

/// <summary>
/// Asked once, right after the download: install TextReveal in the user profile and create the
/// shortcuts that make it launchable like any other application. No elevation, no service, nothing
/// written outside the account.
/// </summary>
public sealed partial class InstallWindow : Window
{
    private readonly bool _isUpdate;

    public InstallWindow()
    {
        InitializeComponent();

        Icon = AppIcon.Load();
        _isUpdate = Installer.IsInstalled;

        HeadlineText.Text = _isUpdate ? "Update TextReveal" : "Install TextReveal";
        InstallButton.Content = _isUpdate ? "Update" : "Install";

        LocationText.Text = $"The application is copied to {Installer.InstallDirectory} and started from there.";
        DetailsText.Text =
            "No administrator rights and no service: everything stays in your own account. " +
            "To remove it later, uninstall TextReveal from Windows settings, apps, installed apps.";

        DesktopCheckBox.IsChecked = true;
        StartMenuCheckBox.IsChecked = true;

        // First install: starting with Windows is the sensible default for a tray tool. On an
        // update, keep whatever the user already chose.
        StartupCheckBox.IsChecked = !_isUpdate || StartupRegistrar.IsEnabled(Installer.InstalledExecutablePath);
    }

    /// <summary>True once the executable sits in the install folder.</summary>
    public bool Installed { get; private set; }

    /// <summary>True when the installed copy has to take over from the one that is running now.</summary>
    public bool RestartRequired => Installed && !Installer.IsRunningInstalled;

    private void OnInstallClick(object sender, RoutedEventArgs e)
    {
        var options = new InstallOptions(
            DesktopCheckBox.IsChecked == true,
            StartMenuCheckBox.IsChecked == true,
            StartupCheckBox.IsChecked == true);

        if (!Installer.TryInstall(options, out var report))
        {
            Report(report, isError: true);
            return;
        }

        Installed = true;

        if (report.Length > 0)
        {
            // Installed, but a decorative step failed: say it before the window closes.
            MessageBox.Show(this, report, "TextReveal", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        DialogResult = true;
    }

    private void OnSkipClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }

    private void Report(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource(isError ? "ErrorBrush" : "SuccessBrush");
    }
}
