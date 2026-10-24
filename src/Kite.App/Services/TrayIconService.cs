using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Kite.App.Services;

/// <summary>the icon in the notification area, with its right click menu.</summary>
internal sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _openItem;
    private readonly System.Drawing.Icon? _icon;

    public TrayIconService()
    {
        _icon = LoadIcon();
        _menu = new Forms.ContextMenuStrip();
        _openItem = new Forms.ToolStripMenuItem("Open Kite", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        _openItem.Font = new System.Drawing.Font(_openItem.Font, System.Drawing.FontStyle.Bold);
        _menu.Items.Add(_openItem);
        _menu.Items.Add(new Forms.ToolStripMenuItem("Settings", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(new Forms.ToolStripMenuItem("Quit Kite", null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty)));

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon ?? System.Drawing.SystemIcons.Application,
            Text = "Kite",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += OnMouseClick;
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? QuitRequested;

    /// <summary>show the current shortcut in the menu and tooltip.</summary>
    public void SetShortcutText(string? shortcut)
    {
        _openItem.Text = string.IsNullOrEmpty(shortcut) ? "Open Kite" : $"Open Kite ({shortcut})";

        // the tooltip has a short length limit
        var tip = string.IsNullOrEmpty(shortcut) ? "Kite" : $"Kite • {shortcut}";
        _notifyIcon.Text = tip.Length > 60 ? tip[..60] : tip;
    }

    public void ShowMessage(string title, string message, bool isWarning)
    {
        _notifyIcon.ShowBalloonTip(
            5000,
            title,
            message,
            isWarning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _notifyIcon.MouseClick -= OnMouseClick;

        // hide first so no ghost icon stays in the tray
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon?.Dispose();
    }

    private static System.Drawing.Icon? LoadIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/kite.ico"));
            if (resource is null)
            {
                return null;
            }

            using var stream = resource.Stream;
            var small = Forms.SystemInformation.SmallIconSize;
            return new System.Drawing.Icon(stream, small.Width, small.Height);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            Log.Error("Could not load the tray icon", ex);
            return null;
        }
    }

    private void OnMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
