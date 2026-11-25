using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Core.Theme;
using Core.Views;

namespace Core.Controls;

internal sealed class ToggleChip : Border
{
    private readonly TextBlock _text;
    private readonly Icon? _icon;
    private bool _on, _hover;

    public ToggleChip(string text, string? icon = null, string? iconBrush = null, bool on = true)
    {
        Height = 24;
        Padding = new Thickness(9, 0, 10, 0);
        Margin = new Thickness(0, 0, 6, 0);
        CornerRadius = ThemeManager.Round(12);
        Hairline.Frame(this);
        Focusable = true;
        Cursor = Cursors.Hand;
        FocusVisualStyle = Ui.Res<Style>("FocusRing");
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null)
        {
            _icon = new Icon { Data = Ui.Res<Geometry>(icon), Width = 12, Height = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            if (iconBrush is not null) _icon.SetResourceReference(Icon.ForegroundProperty, iconBrush);
            row.Children.Add(_icon);
        }
        _text = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_text);
        Child = row;
        AutomationProperties.SetName(this, text);
        _on = on;
        Paint();
        MouseEnter += (_, _) => { _hover = true; Paint(); };
        MouseLeave += (_, _) => { _hover = false; Paint(); };
        MouseLeftButtonUp += (_, e) => { e.Handled = true; Flip(); };
        KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Space or Key.Enter)) return;
            e.Handled = true;
            Flip();
        };
    }

    public event Action<bool>? Changed;

    public bool IsOn
    {
        get => _on;
        set
        {
            if (_on == value) return;
            _on = value;
            Paint();
        }
    }

    private void Flip()
    {
        IsOn = !_on;
        Changed?.Invoke(_on);
    }

    private void Paint()
    {
        if (_on) SetResourceReference(BackgroundProperty, "AccentSoft");
        else Background = Brushes.Transparent;
        SetResourceReference(BorderBrushProperty, _on ? "Accent" : _hover ? "InputEdgeHover" : "LineStrong");
        _text.SetResourceReference(TextBlock.ForegroundProperty, _on ? "Heading" : _hover ? "Text" : "TextMuted");
        if (_icon is not null) _icon.Opacity = _on ? 1 : 0.55;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ChipPeer(this);

    private sealed class ChipPeer : FrameworkElementAutomationPeer, IToggleProvider
    {
        private readonly ToggleChip _chip;

        public ChipPeer(ToggleChip chip) : base(chip) => _chip = chip;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.CheckBox;

        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.Toggle ? this : base.GetPattern(pattern);

        public ToggleState ToggleState => _chip._on ? ToggleState.On : ToggleState.Off;

        public void Toggle() => _chip.Flip();
    }
}
