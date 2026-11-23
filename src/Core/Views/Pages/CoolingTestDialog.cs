using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Config;
using Core.Controls;
using Core.Fans;
using Core.Services;
using Core.SimRacing.Race;
using Core.Theme;

namespace Core.Views.Pages;

internal static class CoolingApply
{
    public sealed record Change(string Curve, List<CurvePoint>? OldPoints, bool Created, List<(FanPresetEntry Entry, FanMode Mode, string? Curve)> Entries, CoolingGroupResult Group, string? OldGroupCurve);

    public static string NewCurveName(FanSettings fans, CoolingGroupResult group, string sensorName)
    {
        string baseName = $"{group.Name} ({sensorName})";
        if (group.CurveName is null) baseName = $"Tested fans ({sensorName})";
        if (baseName.Length > Ui.NameLength) baseName = baseName[..Ui.NameLength];
        string name = baseName;
        for (int i = 2; fans.Curves.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)); i++)
        {
            string suffix = $" {i}";
            name = baseName[..Math.Min(baseName.Length, Ui.NameLength - suffix.Length)] + suffix;
        }
        return name;
    }

    public static bool Replaces(FanSettings fans, CoolingGroupResult group) =>
        fans.Curves.FirstOrDefault(c => c.Name == group.CurveName) is { Type: FanCurveType.Graph };

    public static Change? Apply(AppServices app, CoolingGroupResult group, CoolingSuggestion suggestion)
    {
        var fans = app.Config.Fans;
        var points = suggestion.Points.Select(p => new CurvePoint { Temp = p.Temp, Percent = p.Percent }).ToList();
        if (fans.Curves.FirstOrDefault(c => c.Name == group.CurveName) is { Type: FanCurveType.Graph } curve)
        {
            var old = curve.Points;
            curve.Points = points;
            return new Change(curve.Name, old, false, new(), group, group.CurveName);
        }
        if (suggestion.SensorId is "cpu" or "gpu") return null;
        string name = NewCurveName(fans, group, suggestion.SensorName);
        var sensorName = app.Hardware.Catalog?.Temperatures.FirstOrDefault(t => t.Id == suggestion.SensorId)?.Name ?? suggestion.SensorName;
        fans.Curves.Add(new FanCurve { Name = name, Type = FanCurveType.Graph, SensorId = suggestion.SensorId, SensorName = sensorName, Points = points });
        var entries = new List<(FanPresetEntry, FanMode, string?)>();
        if (fans.Active is { } preset)
            foreach (var id in group.FanIds)
            {
                var entry = preset.EntryFor(id);
                entries.Add((entry, entry.Mode, entry.CurveName));
                entry.Mode = FanMode.Curve;
                entry.CurveName = name;
            }
        var oldGroupCurve = group.CurveName;
        group.CurveName = name;
        return new Change(name, null, true, entries, group, oldGroupCurve);
    }

    public static void Undo(AppServices app, IEnumerable<Change> changes)
    {
        var fans = app.Config.Fans;
        foreach (var c in changes.Reverse())
        {
            if (c.Created)
            {
                foreach (var (entry, mode, curve) in c.Entries)
                {
                    entry.Mode = mode;
                    entry.CurveName = curve;
                }
                fans.Curves.RemoveAll(x => x.Name == c.Curve);
                c.Group.CurveName = c.OldGroupCurve;
            }
            else if (fans.Curves.FirstOrDefault(x => x.Name == c.Curve) is { } curve && c.OldPoints is not null)
                curve.Points = c.OldPoints;
        }
        app.FanSettingsChanged();
    }

    public static bool Same(IReadOnlyList<CurvePoint> a, IReadOnlyList<CurvePoint> b) =>
        a.Count == b.Count && a.OrderBy(p => p.Temp).Zip(b.OrderBy(p => p.Temp)).All(x => Math.Abs(x.First.Temp - x.Second.Temp) < 0.5 && Math.Abs(x.First.Percent - x.Second.Percent) < 0.5);
}

internal sealed class CoolingTestDialog
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _content = new();
    private readonly StackPanel _buttons = new();
    private readonly TextBlock _subtitle;
    private CancellationTokenSource? _cts;
    private CoolingTestResult? _result;
    private readonly Dictionary<CoolingGroupResult, CheckBox> _picks = new();

    public Border Card { get; }

    private FanSettings Fans => _app.Config.Fans;

    public CoolingTestDialog(AppServices app, MainWindow window, bool showLast)
    {
        _app = app;
        _window = window;
        Card = Ui.Dialog(window, "Cooling test", "", _content, _buttons, width: 760);
        _subtitle = new TextBlock { Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        if (showLast && Fans.CoolingTest is { } last)
        {
            _result = last;
            ShowResult();
        }
        else ShowIntro();
    }

    public void Cancel() => _cts?.Cancel();

    private List<CoolingGroupPlan> CurrentPlan() => _app.Hardware.Catalog is { } catalog ? CoolingTest.Plan(Fans, catalog) : new();

    private string FanNames(IEnumerable<string> ids)
    {
        var catalog = _app.Hardware.Catalog;
        return string.Join(", ", ids.Select(id => catalog?.Fans.FirstOrDefault(f => f.ControlId == id) is { } f ? _app.FanName(f) : id));
    }

    private void ShowIntro()
    {
        _content.Children.Clear();
        var plan = CurrentPlan();
        int limit = CoolingTest.LimitFor(Fans);
        var minutes = (int)Math.Ceiling(CoolingTest.Estimate(plan, _app.Demo).TotalMinutes);
        foreach (var line in new[]
                 {
                     "Runs the CPU and graphics card at full load, steps each fan group from 100% down and waits for the temperatures to settle at each step.",
                     $"Takes about {RaceFormat.Count(minutes, "minute")}. Close games and heavy apps, and leave the PC alone until it's done.",
                     $"A step ends early if any temperature reaches {limit} °C. The emergency temperature stops the whole test.",
                     "Fans go back to normal afterwards. Curves change only when you press Apply.",
                 })
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new Icon { Data = Ui.Res<Geometry>("I.Shield"), Width = 11, Height = 11, Foreground = Ui.Res<Brush>("SageDeep"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 6, 0) });
            row.Children.Add(new TextBlock { Text = line, Style = Ui.Res<Style>("Body"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
            _content.Children.Add(row);
        }

        _content.Children.Add(Ui.SubHeader("Fan groups"));
        if (plan.Count == 0)
            _content.Children.Add(Ui.EmptyState("I.Fan", "No fans to test", "None of the detected fans can be controlled (pumps and hidden fans are left out)."));
        foreach (var group in plan)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(Ui.Text(group.Name, "H3"));
            var fans = TextFit.TipWhenTrimmed(new TextBlock { Text = FanNames(group.FanIds), Style = Ui.Res<Style>("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(fans, 1);
            row.Children.Add(fans);
            _content.Children.Add(row);
        }

        _buttons.Children.Clear();
        if (Fans.CoolingTest is not null)
        {
            var last = new Button { Content = "Last result", Margin = Ui.AfterButton };
            last.Click += (_, _) => { _result = Fans.CoolingTest; ShowResult(); };
            _buttons.Children.Add(last);
        }
        var cancel = new Button { Content = "Cancel", Margin = Ui.AfterButton };
        cancel.Click += (_, _) => _window.CloseModal(false);
        var start = new Button { Content = "Start test", Style = Ui.Res<Style>("PrimaryButton"), IsEnabled = plan.Count > 0 };
        start.Click += (_, _) => Start();
        _buttons.Children.Add(cancel);
        _buttons.Children.Add(start);
    }

    public async void Start()
    {
        var catalog = _app.Hardware.Catalog;
        if (catalog is null) return;
        var plan = CoolingTest.Plan(Fans, catalog);
        if (plan.Count == 0) return;
        var sensors = CoolingTest.Sensors(Fans, catalog, _app.Hardware.Latest, plan);
        _content.Children.Clear();

        var status = new TextBlock { Text = "Starting…", Style = Ui.Res<Style>("H3"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 2) };
        var left = new TextBlock { Style = Ui.Res<Style>("Muted") };
        _content.Children.Add(status);
        _content.Children.Add(left);
        var bar = new Meter { Height = 6, Steps = 10, StepsUseFill = true, Fill = Ui.Res<Brush>("Accent"), Margin = new Thickness(0, 10, 0, 12) };
        _content.Children.Add(bar);

        var temps = new StackPanel { Orientation = Orientation.Horizontal };
        var tempTexts = new Dictionary<string, TextBlock>();
        foreach (var s in sensors)
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 32, 0) };
            block.Children.Add(new TextBlock { Text = s.Name, Style = Ui.Res<Style>("Caption") });
            var value = new TextBlock { Text = RaceFormat.Missing, Style = Ui.Res<Style>("BigValue"), FontSize = 26 };
            tempTexts[s.Id] = value;
            block.Children.Add(value);
            temps.Children.Add(block);
        }
        _content.Children.Add(temps);

        var stepChips = new Dictionary<(string Group, int Percent), int>();
        var groupRows = new Dictionary<string, WrapPanel>();
        foreach (var g in plan)
        {
            _content.Children.Add(Ui.SubHeader(g.Name));
            var chips = new WrapPanel();
            foreach (int percent in g.Steps)
            {
                var pending = Ui.Chip($"{percent}%");
                pending.Margin = new Thickness(0, 0, 6, 6);
                pending.Opacity = 0.45;
                stepChips[(g.Name, percent)] = chips.Children.Count;
                chips.Children.Add(pending);
            }
            groupRows[g.Name] = chips;
            _content.Children.Add(chips);
        }

        _buttons.Children.Clear();
        var stop = new Button { Content = "Stop" };
        stop.Click += (_, _) => _cts?.Cancel();
        _buttons.Children.Add(stop);

        string mainSensor(CoolingGroupPlan g) =>
            Fans.Curves.FirstOrDefault(c => c.Name == g.CurveName) is { Type: FanCurveType.Graph, SensorId: { } id } && sensors.Any(s => s.Id == id) ? id : sensors[0].Id;
        var progress = new Progress<CoolingProgress>(p =>
        {
            status.Text = p.Status;
            left.Text = p.Progress >= 1 ? "" : $"About {RaceFormat.Count(Math.Max(1, (int)Math.Ceiling(p.Left.TotalMinutes)), "minute")} left";
            Motion.To(bar, Meter.ValueProperty, p.Progress, 400);
            foreach (var (id, t) in p.Temps)
                if (tempTexts.TryGetValue(id, out var text)) TextCase.Set(text, $"{t:0.0} °C");
            if (p.Finished is { } step && p.Group is { } name && groupRows.TryGetValue(name, out var row))
            {
                var g = plan.First(x => x.Name == name);
                string id = mainSensor(g);
                string sensorName = sensors.First(s => s.Id == id).Name;
                string text = step.OverLimit ? $"{step.Percent}% · too hot" : step.Temps.TryGetValue(id, out var v) ? $"{step.Percent}% · {sensorName} {(step.Predicted ? "≈" : "")}{v:0.0} °C" : $"{step.Percent}%";
                var chip = step.OverLimit ? Ui.Chip(text, "DangerSoft", "DangerDeep") : Ui.Chip(text);
                chip.Margin = new Thickness(0, 0, 6, 6);
                if (stepChips.TryGetValue((name, step.Percent), out int at) && at < row.Children.Count)
                {
                    row.Children.RemoveAt(at);
                    row.Children.Insert(at, chip);
                }
                else row.Children.Add(chip);
                Motion.FadeIn(chip);
                if (step.OverLimit)
                    for (int i = at + 1; i < row.Children.Count; i++) row.Children[i].Opacity = 0.2;
            }
        });

        _cts = new CancellationTokenSource();
        _app.Hardware.Acquire("Cooling test");
        var test = new CoolingTest(_app.Hardware, _app.Demo, CoolingTest.LimitFor(Fans));
        try
        {
            _result = await test.RunAsync(plan, sensors, progress, _cts.Token);
            Fans.CoolingTest = _result;
            _app.Save();
            ShowResult();
        }
        catch (OperationCanceledException)
        {
            status.Text = "Stopped. The fans are back to normal.";
            ShowCloseOnly();
        }
        catch (CalibrationAbortedException ex)
        {
            status.Text = (ex.Safety ? "Stopped for safety: " : "Stopped: ") + ex.Message;
            status.Foreground = Ui.Res<Brush>("Danger");
            ShowCloseOnly();
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            status.Text = "The test failed: " + ex.Message;
            ShowCloseOnly();
        }
        finally
        {
            _app.Hardware.Release("Cooling test");
            SaveTraces(test, sensors);
        }
    }

    private void SaveTraces(CoolingTest test, List<CoolingSensor> sensors)
    {
        if (_app.Demo || test.Traces.Count == 0) return;
        string path = System.IO.Path.Combine(ConfigStore.Folder, "cooling-trace.json");
        string json = System.Text.Json.JsonSerializer.Serialize(new { Date = DateTime.Now, Sensors = sensors, test.Traces });
        Task.Run(() =>
        {
            try { System.IO.File.WriteAllText(path, json); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
        });
    }

    private void ShowCloseOnly()
    {
        _buttons.Children.Clear();
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => _window.CloseModal(false);
        _buttons.Children.Add(close);
    }

    private void ShowResult()
    {
        var r = _result!;
        _content.Children.Clear();
        _picks.Clear();
        _subtitle.Text = $"Tested {Dates.ShortTime(r.Date)}" + (r.GpuLoaded ? "" : $". Without graphics load{(r.GpuProblem is { } p ? ": " + p : "")}");
        _content.Children.Add(_subtitle);

        var goalRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var goalLabel = Ui.Text("Aim for", "H3");
        goalLabel.VerticalAlignment = VerticalAlignment.Center;
        goalLabel.Margin = new Thickness(0, 0, 13, 0);
        Ui.AddInfo(goalLabel, "Quiet: lowest speeds, runs up to a few degrees warmer. Balanced: stops where more speed saves under 2 °C. Cool: nearly the lowest temperatures the fans can reach.");
        goalRow.Children.Add(goalLabel);
        var goals = new[] { CoolingGoal.Quiet, CoolingGoal.Balanced, CoolingGoal.Cool };
        var groupsHost = new StackPanel();
        goalRow.Children.Add(Ui.Segmented(new[] { "Quiet", "Balanced", "Cool" }, Array.IndexOf(goals, Fans.CoolingGoal), i =>
        {
            Fans.CoolingGoal = goals[i];
            _app.Save();
            BuildGroups(groupsHost);
            Motion.FadeIn(groupsHost);
        }, "coolinggoal"));
        _content.Children.Add(goalRow);
        _content.Children.Add(groupsHost);

        _buttons.Children.Clear();
        var again = new Button { Content = "Run again", Margin = Ui.AfterButton };
        again.Click += (_, _) => ShowIntro();
        var close = new Button { Content = "Close", Margin = Ui.AfterButton };
        close.Click += (_, _) => _window.CloseModal(false);
        var apply = new Button { Content = "Apply", Style = Ui.Res<Style>("PrimaryButton") };
        apply.Click += (_, _) => Apply();
        _buttons.Children.Add(again);
        _buttons.Children.Add(close);
        _buttons.Children.Add(apply);
        _applyButton = apply;
        BuildGroups(groupsHost);
    }

    private Button? _applyButton;
    private int _groupTab;

    private void UpdateApply(Button apply) => apply.IsEnabled = _picks.Values.Any(b => b.IsChecked == true && b.IsEnabled);

    private void BuildGroups(StackPanel host)
    {
        var r = _result!;
        host.Children.Clear();
        var previous = _picks.ToDictionary(kv => kv.Key, kv => kv.Value.IsChecked == true);
        _picks.Clear();
        var sections = new List<FrameworkElement>();
        _groupTab = Math.Clamp(_groupTab, 0, Math.Max(0, r.Groups.Count - 1));
        if (r.Groups.Count > 1)
        {
            var tabs = Ui.PageTabs(r.Groups.Select(g => g.Name).ToList(), _groupTab, i =>
            {
                _groupTab = i;
                for (int k = 0; k < sections.Count; k++) sections[k].Visibility = k == i ? Visibility.Visible : Visibility.Collapsed;
                Motion.FadeIn(sections[i]);
            }, "coolinggroups", compact: true);
            tabs.Margin = new Thickness(0, 10, 0, 0);
            tabs.HorizontalAlignment = HorizontalAlignment.Left;
            host.Children.Add(tabs);
        }
        double chartHeight = Math.Clamp(_window.ModalRoom - 430, 120, 200);
        foreach (var group in r.Groups)
        {
            var curve = Fans.Curves.FirstOrDefault(c => c.Name == group.CurveName);
            var suggestion = CoolingAnalysis.Suggest(r, group, curve, Fans.CoolingGoal);
            var section = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            var head = new DockPanel { Margin = new Thickness(0, 12, 0, 4), LastChildFill = true };
            if (suggestion is not null && VerdictChip(suggestion) is { } chip)
            {
                chip.VerticalAlignment = VerticalAlignment.Center;
                chip.Margin = new Thickness(10, 0, 0, 0);
                DockPanel.SetDock(chip, Dock.Right);
                head.Children.Add(chip);
            }
            var title = Ui.Text(group.Name, "H3");
            title.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(title, Dock.Left);
            if (r.Groups.Count == 1) head.Children.Add(title);
            var fans = TextFit.TipWhenTrimmed(new TextBlock { Text = FanNames(group.FanIds), Style = Ui.Res<Style>("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(fans);
            section.Children.Add(head);

            if (suggestion is null)
            {
                section.Children.Add(new TextBlock { Text = "Not enough steps were measured for this group.", Style = Ui.Res<Style>("Muted") });
                section.Visibility = sections.Count == _groupTab ? Visibility.Visible : Visibility.Collapsed;
                sections.Add(section);
                host.Children.Add(section);
                continue;
            }

            section.Children.Add(new TextBlock { Text = Explain(group, curve, suggestion), Style = Ui.Res<Style>("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

            var response = suggestion.Response;
            bool held = response.HeldAt is not null;
            var points = held ? response.Points.Select(p => (p.Percent, 100 - p.Temp)).ToList() : response.Points.ToList();
            var worked = group.Steps.Where(st => st.Predicted && !CoolingAnalysis.TooHot(r, st)).Select(st => st.Percent).ToHashSet();

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ui.PanelSpacing) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var chartSide = new StackPanel();
            string chartTitle = held ? $"{suggestion.SensorName} power kept at each fan speed" : $"{suggestion.SensorName} temperature at each fan speed";
            chartSide.Children.Add(new TextBlock { Text = chartTitle, Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 0, 0, 4) });
            var chart = new CoolingChart { Height = chartHeight, ToolTip = worked.Count > 0 ? "Hollow points were worked out from how the temperature was still moving." : null };
            chart.Show(new[] { new CoolingSeries("", points, true) }, suggestion.BarelyMatters ? null : suggestion.Knee, response.TooHotAt, worked, held ? "%" : "°C");
            chartSide.Children.Add(chart);
            grid.Children.Add(chartSide);

            var curveSide = new StackPanel();
            bool replaces = CoolingApply.Replaces(Fans, group);
            var curveHead = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            if (replaces)
            {
                var legend = new TextBlock { Text = "Dashed: your curve now", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(legend, Dock.Right);
                curveHead.Children.Add(legend);
            }
            curveHead.Children.Add(new TextBlock { Text = $"Suggested curve · {suggestion.SensorName}", Style = Ui.Res<Style>("Eyebrow") });
            curveSide.Children.Add(curveHead);
            var editor = new CurveEditor { ReadOnly = true, Height = chartHeight, MinHeight = chartHeight };
            editor.SetPoints(suggestion.Points);
            if (replaces) editor.SetGhost(curve!.Points.ToList());
            curveSide.Children.Add(editor);
            Grid.SetColumn(curveSide, 2);
            grid.Children.Add(curveSide);
            section.Children.Add(grid);

            bool same = replaces && CoolingApply.Same(curve!.Points, suggestion.Points);
            bool canCreate = replaces || suggestion.SensorId is not ("cpu" or "gpu");
            string fanNames = FanNames(group.FanIds);
            string newName = CoolingApply.NewCurveName(Fans, group, suggestion.SensorName);
            string label = same ? $"“{curve!.Name}” already matches"
                : replaces ? $"Apply to “{curve!.Name}”"
                : curve is not null ? $"Switch {fanNames} from “{curve.Name}” to their own curve on the {suggestion.SensorName}"
                : $"Give {fanNames} their own curve on the {suggestion.SensorName}";
            string? tip = same || !canCreate ? null
                : replaces ? $"Replaces the points of “{curve!.Name}” with the suggested curve. Every fan that uses “{curve.Name}” follows it. Undo appears after Apply."
                : (curve is not null
                    ? $"Now these fans follow “{curve.Name}”, which takes the highest speed its curves ask for, so whatever runs hottest spins them up. The test found they mostly cool the {suggestion.SensorName}. "
                    : $"Now these fans aren't on a temperature curve. The test found they mostly cool the {suggestion.SensorName}. ")
                  + $"Apply makes a new curve “{newName}” that only follows the {suggestion.SensorName} and switches these fans to it in “{Fans.ActivePreset}”."
                  + (curve is not null ? $" “{curve.Name}” stays as it is for anything else that uses it." : "")
                  + " Undo appears after Apply.";
            bool wanted = suggestion.Verdict is CoolingVerdict.Quieter or CoolingVerdict.MoreSpeed;
            var box = new CheckBox { IsChecked = !same && canCreate && previous.GetValueOrDefault(group, replaces && wanted), IsEnabled = !same && canCreate, Margin = new Thickness(0, 6, 0, 0) };
            var boxTitle = ToggleCards.RowTitle(label);
            if (tip is not null) Ui.AddInfo(boxTitle, tip);
            ToggleCards.Row(box, boxTitle, !canCreate ? "The sensor can't be picked for a curve." : null);
            _picks[group] = box;
            section.Children.Add(box);
            section.Visibility = sections.Count == _groupTab ? Visibility.Visible : Visibility.Collapsed;
            sections.Add(section);
            host.Children.Add(section);
        }
        if (_applyButton is { } apply)
        {
            foreach (var box in _picks.Values) box.Click += (_, _) => UpdateApply(apply);
            UpdateApply(apply);
        }
    }

    private string GoalWord => Fans.CoolingGoal switch
    {
        CoolingGoal.Quiet => "Quiet",
        CoolingGoal.Cool => "Cool",
        _ => "Balanced",
    };

    private FrameworkElement? VerdictChip(CoolingSuggestion s)
    {
        string now = s.Current is { } c ? $"Under heavy load like the test, your curve runs these fans at about {c}%. " : "";
        string sensor = s.SensorName;
        (Border Chip, string Tip)? made = s.Verdict switch
        {
            CoolingVerdict.Quieter => (Ui.Chip("Can run quieter", "SageSoft", "SageDeep"),
                now + $"About {s.Knee}% cools the {sensor} nearly as well, so the extra speed is mostly noise. Applying the suggestion makes these fans quieter."),
            CoolingVerdict.MoreSpeed => (Ui.Chip("Room to run cooler"),
                "Not a warning: nothing got too hot, and your curve is fine as it is. " + now + $"Letting them go up to {s.Knee}% would cool the {sensor} a little more, at the cost of more noise. Only worth it if you want it cooler. “{GoalWord}” sets how much cooling counts as worth it."),
            CoolingVerdict.AboutRight => (Ui.Chip("About right"),
                now + $"That's close to where more speed stops helping the {sensor}, so there's nothing to change."),
            CoolingVerdict.BarelyMatters => (Ui.Chip("Speed barely matters"),
                $"Between their slowest and fastest tested speed these fans change the {sensor} by under {CoolingAnalysis.BarelyMattersC:0.#} °C, so they can stay slow and quiet."),
            _ => null,
        };
        if (made is not { } m) return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        m.Chip.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(m.Chip);
        var tip = HudSettings.HelpTip.For(m.Tip);
        tip.VerticalAlignment = VerticalAlignment.Center;
        tip.Margin = new Thickness(5, 0, 0, 0);
        row.Children.Add(tip);
        return row;
    }

    private string Explain(CoolingGroupResult group, FanCurve? curve, CoolingSuggestion s)
    {
        var response = s.Response;
        var top = response.Points.OrderByDescending(p => p.Percent).First();
        var low = response.Points.OrderBy(p => p.Percent).First();
        string text;
        if (response.HeldAt is { } ceiling)
        {
            double keptLow = 100 - low.Temp, keptKnee = 100 - CoolingAnalysis.TempAt(response.Points, s.Knee);
            text = $"Under full load the {s.SensorName} stays at its own {ceiling:0} °C limit at every fan speed and slows itself a little instead. "
                + (s.BarelyMatters
                    ? $"Even at {low.Percent}% it keeps {keptLow:0.#}% of its power, so these fans can stay slow."
                    : $"At {low.Percent}% it keeps {keptLow:0.#}% of its power, at {s.Knee}% {keptKnee:0.#}%.");
        }
        else if (s.BarelyMatters)
            text = $"These fans barely change the {s.SensorName}: {response.Spread:0.0} °C between {low.Percent}% and {top.Percent}%.";
        else if (s.Knee >= top.Percent)
            text = $"Every step up still cools the {s.SensorName}: {response.Spread:0.0} °C between {low.Percent}% and {top.Percent}%.";
        else
        {
            double best = response.Points.Where(p => p.Percent >= s.Knee).Min(p => p.Temp);
            double gain = CoolingAnalysis.TempAt(response.Points, s.Knee) - best;
            text = $"Between {low.Percent}% and {top.Percent}% these fans change the {s.SensorName} by up to {response.Spread:0.0} °C. "
                + (gain < 0.5 ? $"Running them faster than {s.Knee}% doesn't lower it any further." : $"Faster than {s.Knee}% lowers it only {gain:0.0} °C more.");
        }

        if (s.Current is { } now)
        {
            text += $" Under this load your curve runs them at about {now}%.";
            text += s.Verdict switch
            {
                CoolingVerdict.Quieter => $" About {s.Knee}% does nearly the same job.",
                CoolingVerdict.MoreSpeed => $" Letting them reach {s.Knee}% would cool it a little more, if you don't mind the noise. Nothing got too hot.",
                _ => "",
            };
        }
        if (response.TooHotAt is { } hot) text += $" At {hot}% the {s.SensorName} got too hot, so the suggestion stays above that speed.";
        foreach (int cut in response.Unfinished) text += $" The {cut}% step was cut short, so it's left out.";
        if (curve is not null && !CoolingApply.Replaces(Fans, group)) text += $" These fans follow “{curve.Name}”, which mixes other curves, so Apply gives them a curve of their own.";
        return text;
    }

    private void Apply()
    {
        var r = _result!;
        var changes = new List<CoolingApply.Change>();
        foreach (var (group, box) in _picks)
        {
            if (box.IsChecked != true || !box.IsEnabled) continue;
            var curve = Fans.Curves.FirstOrDefault(c => c.Name == group.CurveName);
            if (CoolingAnalysis.Suggest(r, group, curve, Fans.CoolingGoal) is not { } s) continue;
            if (CoolingApply.Apply(_app, group, s) is { } change) changes.Add(change);
        }
        if (changes.Count == 0) return;
        _app.FanSettingsChanged();
        _app.Notify("Curves updated", string.Join(", ", changes.Select(c => $"“{c.Curve}”")), NoticeKind.Success,
            action: new NoticeAction("Undo", () => CoolingApply.Undo(_app, changes)));
        _window.CloseModal(true);
    }
}
