using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace QuickFind.Services;

public sealed class TrayService : IDisposable
{
    readonly NotifyIcon _icon;
    readonly Action _show;
    readonly Action _exit;
    bool _disposed;

    public TrayService(Action show, Action exit)
    {
        _show = show;
        _exit = exit;
        _icon = new NotifyIcon
        {
            Text = "QuickFind",
            Visible = true,
            Icon = LoadIcon()
        };
        _icon.DoubleClick += (_, _) => _show();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show QuickFind", null, (_, _) => _show());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _exit());
        _icon.ContextMenuStrip = menu;
    }

    static Icon LoadIcon()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "QuickFind.ico");
            if (System.IO.File.Exists(path)) return new Icon(path);
        }
        catch { }
        return SystemIcons.Application;
    }

    public void ShowBalloon(string title, string text)
    {
        try { _icon.ShowBalloonTip(2000, title, text, ToolTipIcon.Info); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
