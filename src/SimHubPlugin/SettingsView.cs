using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SimHub.Plugins.Styles;

namespace VelocitaSimHubPlugin;

internal sealed class SettingsView : UserControl
{
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _update = new() { Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public SettingsView()
    {
        var open = new SHButtonPrimary { Content = "Open overlay settings", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
        open.Click += (_, _) => Engine.OpenSettings();
        var how = new TextBlock
        {
            Text = "Every widget is in Dash Studio → Overlays, named “Velocita - …”. Turn one on and place or resize it like any SimHub overlay. While iRacing is closed they show a sample race to place them by.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
        };
        var settings = new TextBlock
        {
            Text = "What each widget shows, its colors and when it shows are set in Velocita's overlay settings.",
            TextWrapping = TextWrapping.Wrap,
        };
        var body = new StackPanel { Margin = new Thickness(0, 4, 0, 0), Children = { _status, _update, open, how, settings } };
        Content = new ScrollViewer { Content = new SHSection { Title = "VELOCITA", ShowSeparator = true, Content = body } };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        _status.Text = !Engine.Installed ? Engine.ExeName + " is missing from the SimHub folder: copy it next to Velocita.SimHub.Plugin.dll and restart SimHub."
            : Engine.Running ? "Running."
            : Engine.Problem is { } problem ? "Not running: " + problem
            : "Starting…";
        string note = Engine.UpdateNote ?? "";
        _update.Text = note;
        _update.Visibility = note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
