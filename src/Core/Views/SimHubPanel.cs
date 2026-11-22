using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Core.Controls;
using Core.Services;
using Core.Views.Pages;

namespace Core.Views;

internal sealed class SimHubPanel : Grid, IModalHost
{
    private readonly AppServices _app;
    private readonly Grid _modal = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _version = TextFit.TipWhenTrimmed(new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0) });
    private readonly Button _check = new() { Content = "Check for updates", VerticalAlignment = VerticalAlignment.Center };
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
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var title = new TextBlock { Text = "iRacing Overlay", Style = Ui.Res<Style>("H1"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(Section.TitleTools, Dock.Right);
        heading.Children.Add(Section.TitleTools);
        heading.Children.Add(title);

        var note = new TextBlock
        {
            Text = "Show or move the widgets in SimHub: Dash Studio → Overlays, each one listed as “Velocita - <widget>”. Changes here show on them right away.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0),
        };
        _version.Style = Ui.Res<Style>("Caption");
        _check.Click += (_, _) => SimHubEngine.Updater?.CheckNow();
        var info = new TrimRow { Margin = new Thickness(0, 0, 0, 14), Children = { note, _version, _check } };
        if (SimHubEngine.Updater is null) _check.Visibility = Visibility.Collapsed;

        var page = new ScrollViewer
        {
            Style = Ui.Res<Style>("PageScroll"), Focusable = false,
            Content = Ui.PageFrame(new StackPanel { Margin = new Thickness(23, 16, 23, 23), Children = { heading, info, Section } }),
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

    public void Show()
    {
        if (_shown) return;
        _shown = true;
        if (SimHubEngine.Updater is { } updater) updater.Changed += ShowUpdate;
        ShowUpdate();
        Section.OnShown();
    }

    public void Hide()
    {
        if (!_shown) return;
        _shown = false;
        if (SimHubEngine.Updater is { } updater) updater.Changed -= ShowUpdate;
        CloseModal(false);
        Section.OnHidden();
        _app.FlushPendingSave();
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
