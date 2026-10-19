using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace PasteDock.Desktop.Services;

internal sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _pause;
    private readonly Icon _icon;

    public TrayService(Action show, Action pause, Action next, Action quit)
    {
        _icon = CreateIcon();
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("Open PasteDock", null, (_, _) => show());
        _pause = new Forms.ToolStripMenuItem("Pause capture", null, (_, _) => pause());
        _menu.Items.Add(_pause);
        _menu.Items.Add("Paste next queued item", null, (_, _) => next());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Quit PasteDock", null, (_, _) => quit());
        _tray = new Forms.NotifyIcon { Icon = _icon, Text = "PasteDock · capturing", ContextMenuStrip = _menu, Visible = true };
        _tray.DoubleClick += (_, _) => show();
    }

    public void SetState(bool paused, bool locked)
    {
        _pause.Text = paused ? "Resume capture" : "Pause capture";
        _pause.Checked = paused;
        _tray.Text = locked ? "PasteDock · session locked" : paused ? "PasteDock · paused" : "PasteDock · capturing";
    }

    private static Icon CreateIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/PasteDock.ico"))
            ?? throw new InvalidOperationException("The PasteDock icon resource is missing.");
        using var stream = resource.Stream;
        return new Icon(stream, 32, 32);
    }
    public void Dispose()
    {
        _tray.Visible = false;
        _tray.Dispose(); _menu.Dispose(); _icon.Dispose();
    }
}
