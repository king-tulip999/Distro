using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Core.Controls;
using Core.Services;
using Core.SimRacing.Hud;
using Core.SimRacing.Hud.Web;
using Core.SimRacing.Race;
using Core.Views.Pages;

namespace Core.Views;

internal sealed class SimHubPanel : Grid, IModalHost
{
    private readonly AppServices _app;
    private readonly Grid _modal = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _version = TextFit.TipWhenTrimmed(new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0) });
    private readonly Button _check = new() { Content = "Check for updates", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _run = new() { Content = "Run Velocita", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly ContentControl _overlayNote = new() { Focusable = false, Margin = new Thickness(0, 0, 0, 14), Visibility = Visibility.Collapsed };
    private readonly StackPanel _content = new() { Margin = new Thickness(23, 16, 23, 23) };
    private TaskCompletionSource<bool>? _confirm;
    private bool _shown;

    public RaceHudSection Section { get; }

    public SimHubPanel(AppServices app)
    {
        _app = app;
        Resources[WindowScale.Key] = Transform.Identity;
        SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        SetResourceReference(TextElement.ForegroundProperty, "Text");
        TextElement.SetFontSize(this, 12);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "Bg");

        Section = new RaceHudSection(app, this);
        var note = new TextBlock
        {
            Text = "Show or move the widgets in SimHub: Dash Studio → Overlays, each one listed as “Velocita - <widget>”. Changes here show on them right away.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0),
        };
        _version.Style = Ui.Res<Style>("Caption");
        _check.Click += (_, _) => SimHubEngine.Updater?.CheckNow();
        _run.Style = Ui.Res<Style>("Switch");
        _run.Unchecked += (_, _) => SimHubEngine.TurnOff();
        Section.TitleTools.Margin = new Thickness(0, 0, 16, 0);
        Section.TitleTools.VerticalAlignment = VerticalAlignment.Center;
        var updates = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { Section.TitleTools, _version, _check, _run } };
        DockPanel.SetDock(updates, Dock.Right);
        var info = new DockPanel { Margin = new Thickness(0, 0, 0, 14), Children = { updates, note } };
        if (SimHubEngine.Updater is null) _check.Visibility = Visibility.Collapsed;

        _content.Children.Add(info);
        _content.Children.Add(_overlayNote);
        _content.Children.Add(Section);
        var page = new ScrollViewer
        {
            Style = Ui.Res<Style>("PageScroll"), Focusable = false,
            Content = Ui.PageFrame(_content),
        };
        Ui.ChainWheel(page);
        var body = new Grid { Children = { page } };
        body.SetResourceReference(Panel.BackgroundProperty, "WindowBg");
        Children.Add(new TopographyBackdrop());
        Children.Add(body);
        Children.Add(_modal);
        RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled || _confirm is null) return;
            CloseModal(false);
            e.Handled = true;
        };
        ShowUpdate();
    }

    public bool Embedded
    {
        set
        {
            _run.IsChecked = true;
            _run.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void Show()
    {
        if (_shown) return;
        _shown = true;
        if (SimHubEngine.Updater is { } updater) updater.Changed += ShowUpdate;
        if (SimHubEngine.OverlayLayouts is { } layouts) layouts.Changed += ShowOverlayNote;
        ShowUpdate();
        ShowOverlayNote();
        Section.OnShown();
    }

    public void Hide()
    {
        if (!_shown) return;
        _shown = false;
        if (SimHubEngine.Updater is { } updater) updater.Changed -= ShowUpdate;
        if (SimHubEngine.OverlayLayouts is { } layouts) layouts.Changed -= ShowOverlayNote;
        CloseModal(false);
        Section.OnHidden();
        _app.FlushPendingSave();
    }

    private void ShowOverlayNote()
    {
        var repeated = SimHubEngine.OverlayLayouts?.Repeated ?? Array.Empty<RepeatedOverlay>();
        if (repeated.Count == 0)
        {
            _overlayNote.Content = null;
            _overlayNote.Visibility = Visibility.Collapsed;
            return;
        }
        var first = repeated[0];
        string title = repeated.Count == 1
            ? $"“{first.Overlay}” is in the SimHub overlay layout “{first.Layout}” {Times(first.Count)}"
            : "Some Velocita overlays are in a SimHub overlay layout more than once";
        string detail = "Copies of one overlay all show the same widget. " + Advice(first);
        if (repeated.Count > 1)
            detail = string.Join(Environment.NewLine, repeated.Select(r => $"“{r.Overlay}” in “{r.Layout}”: {Times(r.Count)}").Append(detail));
        _overlayNote.Content = Ui.Notice("Warn", "I.Alert", title, detail);
        _overlayNote.Visibility = Visibility.Visible;
    }

    private static string Times(int count) => count == 2 ? "twice" : RaceFormat.Count(count, "time");

    private string Advice(RepeatedOverlay repeated)
    {
        var def = HudCatalog.All.FirstOrDefault(d => string.Equals(SimHubDashboards.Name(d), repeated.Overlay, StringComparison.OrdinalIgnoreCase));
        const string Duplicate = "For another one, click Duplicate on that widget here, then put the numbered overlay it adds in the SimHub layout instead.";
        if (def is null) return Duplicate;
        var hud = _app.Config.SimRacing.Hud;
        int count = hud.Layout(hud.ActiveLayout)?.Widgets.Count(w => string.Equals(w.Kind, def.Kind, StringComparison.OrdinalIgnoreCase)) ?? 1;
        for (int slot = 2; slot <= Math.Min(count, HudWebServer.MaxSlot); slot++)
        {
            string name = SimHubDashboards.Name(def, slot);
            if (!repeated.IsPlaced(name)) return $"Put “{name}” in the SimHub layout in place of the extra copy: it shows {def.Name} {slot} from here.";
        }
        return $"For another one, click Duplicate on that widget here, then put the overlay it adds (“{SimHubDashboards.Name(def, Math.Max(2, count + 1))}”) in the SimHub layout instead.";
    }

    private void ShowUpdate()
    {
        var updater = SimHubEngine.Updater;
        string current = UpdateService.Show(UpdateService.Current);
        var state = updater?.State ?? SimHubUpdateState.Idle;
        _version.Text = state switch
        {
            SimHubUpdateState.Checking => "Checking for updates…",
            SimHubUpdateState.UpToDate => $"Version {current} is the latest",
            SimHubUpdateState.Ready when updater?.Ready is { } ready => $"Version {UpdateService.Show(ready.Version)} installs when you close SimHub",
            SimHubUpdateState.Failed => "Couldn't check for updates",
            _ => $"Version {current}",
        };
        _version.SetResourceReference(TextBlock.ForegroundProperty, state == SimHubUpdateState.Ready ? "Accent" : "TextMuted");
        _check.IsEnabled = state is not (SimHubUpdateState.Checking or SimHubUpdateState.Ready);
        _check.Visibility = updater is null || state == SimHubUpdateState.Ready ? Visibility.Collapsed : Visibility.Visible;
    }

    public double ModalRoom => ActualHeight - 48;

    public Task<bool> ShowModal(FrameworkElement content)
    {
        _confirm?.TrySetResult(false);
        _confirm = new TaskCompletionSource<bool>();
        content.VerticalAlignment = VerticalAlignment.Center;
        content.HorizontalAlignment = HorizontalAlignment.Center;
        if (content.Width > ActualWidth - 40) content.Width = Math.Max(300, ActualWidth - 40);
        _modal.Children.Clear();
        var dim = new Border();
        dim.SetResourceReference(Border.BackgroundProperty, "Dim");
        _modal.Children.Add(dim);
        _modal.Children.Add(content);
        _modal.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_confirm is not null && !_modal.IsKeyboardFocusWithin)
                content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        });
        return _confirm.Task;
    }

    public void CloseModal(bool result)
    {
        var pending = _confirm;
        _confirm = null;
        _modal.Visibility = Visibility.Collapsed;
        _modal.Children.Clear();
        pending?.TrySetResult(result);
    }

    public Task<bool> Confirm(string title, string message, string confirmText, bool danger = false)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("H3"), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = message, Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 11) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Margin = Ui.AfterButton };
        cancel.Click += (_, _) => CloseModal(false);
        var ok = new Button { Content = confirmText, Style = Ui.Res<Style>(danger ? "DangerConfirmButton" : "PrimaryButton") };
        ok.Click += (_, _) => CloseModal(true);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);
        var task = ShowModal(new Tile { Style = Ui.Res<Style>("LiftedCard"), Padding = new Thickness(16, 15, 16, 13), MaxWidth = 340, Child = stack });
        ok.Focus();
        return task;
    }
}
