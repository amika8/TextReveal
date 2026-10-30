using System.Windows;
using Forms = System.Windows.Forms;

namespace TextReveal.Tray;

/// <summary>
/// Tray presence of TextReveal: start a search, open the settings or quit.
/// The icon is the one embedded in the executable, so the tray, the taskbar and the file
/// explorer all show the same artwork.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _searchItem;
    private bool _disposed;

    public TrayIcon(Action searchRequested, Action openSettings, Action createShortcuts, Action exit)
    {
        ArgumentNullException.ThrowIfNull(searchRequested);
        ArgumentNullException.ThrowIfNull(openSettings);
        ArgumentNullException.ThrowIfNull(createShortcuts);
        ArgumentNullException.ThrowIfNull(exit);

        _searchItem = new Forms.ToolStripMenuItem("Search", null, (_, _) => searchRequested());

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_searchItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Settings…", null, (_, _) => openSettings()));
        menu.Items.Add(new Forms.ToolStripMenuItem("Install shortcuts…", null, (_, _) => createShortcuts()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Quit TextReveal", null, (_, _) => exit()));

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "TextReveal",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _notifyIcon.DoubleClick += (_, _) => searchRequested();
    }

    /// <summary>Reflects the shortcut currently owned by the application.</summary>
    public void SetHotKeyLabel(string label)
    {
        _searchItem.Text = $"Search ({label})";
        _notifyIcon.Text = $"TextReveal - {label}";
    }

    public void ShowError(string message) =>
        _notifyIcon.ShowBalloonTip(6000, "TextReveal", message, Forms.ToolTipIcon.Warning);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
    }

    /// <summary>Loads the frame of the embedded <c>.ico</c> that matches the notification area.</summary>
    private static System.Drawing.Icon LoadIcon()
    {
        var resource = Application.GetResourceStream(
            new Uri("Assets/TextReveal.ico", UriKind.Relative));

        if (resource is null)
        {
            return System.Drawing.SystemIcons.Application;
        }

        using var stream = resource.Stream;
        using var icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);

        return (System.Drawing.Icon)icon.Clone();
    }
}
