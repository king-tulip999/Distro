using System.Drawing;
using System.Windows.Forms;
using Core.SimRacing.Hud;
using Core.Theme;

namespace Core.Services;

internal sealed class SimHubTray : IDisposable
{
    private readonly AppServices _app;
    private readonly Func<HudHost?> _hud;
    private readonly Action _openSettings;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly Icon _image;
    private Font? _bold;

    public SimHubTray(AppServices app, Func<HudHost?> hud, Action openSettings)
    {
        _app = app;
        _hud = hud;
        _openSettings = openSettings;
        _image = TrayService.TileIcon(Brand.TrayKey, ThemeManager.Current.Id) ?? (Icon)SystemIcons.Application.Clone();
        _menu.ShowImageMargin = false;
        _menu.ShowItemToolTips = true;
        _menu.Opening += (_, e) =>
        {
            Rebuild();
            e.Cancel = false;
        };
        _menu.Opened += (_, _) => TrayMenuPlace.KeepAboveTaskbar(_menu);
        _icon = new NotifyIcon { Icon = _image, Text = AppIdentity.Name, ContextMenuStrip = _menu };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _openSettings();
        };
        try { _icon.Visible = true; }
        catch (System.ComponentModel.Win32Exception ex) { App.LogError(ex); }
    }

    private void Rebuild()
    {
        var old = _menu.Items.Cast<ToolStripItem>().ToList();
        _menu.Items.Clear();
        foreach (var item in old) item.Dispose();
        _bold ??= new Font(_menu.Font, FontStyle.Bold);
        _menu.Items.Add(new ToolStripMenuItem("Settings", null, (_, _) => _openSettings()) { Font = _bold });
        var h = _app.Config.SimRacing.Hud;
        if (!h.VrOverlays) return;
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Edit VR widgets in the headset", null, (_, _) => _hud()?.ToggleVrAdjust()) { ShortcutKeyDisplayString = Core.Hotkeys.Hotkey.Display(h.VrAdjustHotkey) });
        _menu.Items.Add(new ToolStripMenuItem("Recenter VR widgets", null, (_, _) => _hud()?.RecenterVr()) { ShortcutKeyDisplayString = Core.Hotkeys.Hotkey.Display(h.VrRecenterHotkey) });
    }

    public void Dispose()
    {
        try { _icon.Visible = false; }
        catch (System.ComponentModel.Win32Exception) { }
        _icon.Dispose();
        foreach (var item in _menu.Items.Cast<ToolStripItem>().ToList()) item.Dispose();
        _menu.Dispose();
        _bold?.Dispose();
        _image.Dispose();
    }
}
