using System.Reflection;
using Forms = System.Windows.Forms;

namespace AutoReShade.Services;

/// <summary>The icon next to the Windows clock, with a small menu. AutoReShade keeps running there when its window is closed.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _overlayItem;

    public TrayIcon(Action openSettings, Action pickMap, Action toggleOverlay, Action exit)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutoReShade.icon.ico");
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open AutoReShade", null, (_, _) => openSettings());
        menu.Items.Add("Choose map manually...", null, (_, _) => pickMap());
        _overlayItem = new Forms.ToolStripMenuItem("Show clock overlay", null, (_, _) => toggleOverlay()) { CheckOnClick = false };
        menu.Items.Add(_overlayItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = stream is null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(stream),
            Text = "AutoReShade",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => openSettings();
    }

    public void SetOverlayChecked(bool visible) => _overlayItem.Checked = visible;

    public void SetTooltip(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
