using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Core.Views;

namespace Core.Services;

internal static class SearchHarvest
{
    private const int MaxTabDepth = 3, ShortestLabel = 3, LongestLabel = 60;

    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Close", "Cancel", "OK", "Save", "Delete", "Edit", "More", "Back", "Done", "Apply", "Add", "Remove", "Reset", "Undo", "Yes", "No",
        "Open", "Copy", "Passed", "Set", "Not set", "Changed", "All", "Keyboard", "Rename", "Search", "Clear", "Browse", "Change", "Pick", "Show", "Hide", "On", "Off", "Auto", "None", "Default", "New",
    };

    public static async Task RunAsync(MainWindow window, string file, Action<Exception> error)
    {
        var found = new Dictionary<string, HarvestedLabel>(StringComparer.OrdinalIgnoreCase);
        var userText = UserText(window.Services.Config);
        foreach (var page in window.NavKeys)
        {
            try
            {
                window.Navigate(page);
                await Settle(window, 1200);
                if (window.PageFor(page) is ISectionedPage sectioned && sectioned.Sections.Count > 0)
                {
                    foreach (var section in sectioned.Sections.ToList())
                    {
                        window.OpenSection(page, section.Id);
                        await Settle(window, 900);
                        await CrawlTabs(window, page, section.Id, section.Title, new List<string>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase), found, userText);
                    }
                }
                else
                {
                    await CrawlTabs(window, page, null, window.NavTitle(page), new List<string>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase), found, userText);
                }
            }
            catch (Exception ex)
            {
                error(ex);
            }
        }
        var ordered = found.Values.OrderBy(l => window.NavKeys.ToList().IndexOf(l.Page)).ThenBy(l => l.Section).ThenBy(l => l.Tabs.Length).ToList();
        File.WriteAllText(file, JsonSerializer.Serialize(ordered, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private static async Task CrawlTabs(MainWindow window, string page, string? section, string where, List<string> tabs, HashSet<string> known,
        Dictionary<string, HarvestedLabel> found, HashSet<string> userText)
    {
        if (window.PageFor(page) is not { } root) return;
        Crawl(window, root, page, section, where, tabs, found, userText);
        if (tabs.Count >= MaxTabDepth) return;
        var strip = Ui.VisibleTree(root).OfType<RadioButton>().Where(IsTab).ToList();
        var fresh = strip.Where(r => !known.Contains(TabName(r))).ToList();
        if (fresh.Count == 0) return;
        var inner = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
        foreach (var r in strip) inner.Add(TabName(r));
        var start = fresh.FirstOrDefault(r => r.IsChecked == true);
        foreach (var name in fresh.Select(TabName).ToList())
        {
            if (Ui.FindTab(root, name) is not { } radio) continue;
            if (radio.IsChecked != true)
            {
                radio.IsChecked = true;
                await Settle(window, 700);
            }
            await CrawlTabs(window, page, section, where, tabs.Append(name).ToList(), inner, found, userText);
        }
        if (start is not null && Ui.FindTab(root, TabName(start)) is { IsChecked: false } back)
        {
            back.IsChecked = true;
            await Settle(window, 500);
        }
    }

    private static void Crawl(MainWindow window, FrameworkElement root, string page, string? section, string where, List<string> tabs,
        Dictionary<string, HarvestedLabel> found, HashSet<string> userText)
    {
        var selected = Ui.VisibleTree(root).OfType<RadioButton>().Where(r => IsTab(r) && r.IsChecked == true).Select(TabName).ToArray();
        var parts = new List<string> { window.NavTitle(page) };
        if (section is not null && !string.Equals(where, window.NavTitle(page), StringComparison.OrdinalIgnoreCase)) parts.Add(where);
        parts.AddRange(selected.Where(t => !parts.Contains(t, StringComparer.OrdinalIgnoreCase)));
        string category = string.Join(" › ", parts);
        foreach (var e in Ui.VisibleTree(root))
        {
            if (InData(e, root) || LabelFor(e) is not { } label) continue;
            string kind = label.Kind, title = label.Title.Trim().TrimEnd(':', '…').Trim();
            if (title.Length < ShortestLabel || title.Length > LongestLabel || CommonWords.Contains(title) || userText.Contains(title) || !title.Any(char.IsLetter) || LooksLikeData(title))
                continue;
            if (IsTab(e) && tabs.Contains(title, StringComparer.OrdinalIgnoreCase)) continue;
            string key = page + "|" + title;
            if (found.ContainsKey(key)) continue;
            found[key] = kind == "tab"
                ? new HarvestedLabel(title, page, section, NearestAnchor(e, root), new[] { title }, string.Join(" › ", parts.Take(section is null ? 1 : 2)), kind)
                : new HarvestedLabel(title, page, section, NearestAnchor(e, root), selected, category, kind);
        }
    }

    private static (string Title, string Kind)? LabelFor(FrameworkElement e)
    {
        if (e is TextBlock text && !string.IsNullOrWhiteSpace(text.Text))
        {
            string raw = Ui.RawText(text);
            int weight = text.FontWeight.ToOpenTypeWeight();
            if (text.Style == Ui.Res<Style>("H2") || text.Style == Ui.Res<Style>("H3") || text.Style == Ui.Res<Style>("Eyebrow")) return (raw, "heading");
            if (Theme.TextCase.GetUpper(text) && weight >= FontWeights.Bold.ToOpenTypeWeight()) return (raw, "heading");
            if (text.Style == Ui.Res<Style>("Body") && weight >= FontWeights.SemiBold.ToOpenTypeWeight()) return (raw, "row");
            if (Within<ButtonBase>(text, 2) is { } button && button is not RadioButton && weight >= FontWeights.SemiBold.ToOpenTypeWeight()) return (raw, "action");
            if (Ui.IsSearchLabel(text) || BesideInput(text)) return (raw, "setting");
            return null;
        }
        if (IsTab(e)) return (TabName((RadioButton)e), "tab");
        if (e is CheckBox { Content: string box }) return (box, "tick");
        if (e is Control and (ComboBox or TextBox or Slider or CheckBox or ToggleButton) && AutomationProperties.GetName(e) is { Length: > 0 } name) return (name, "setting");
        return null;
    }

    private static T? Within<T>(DependencyObject e, int levels) where T : class
    {
        var node = LogicalTreeHelper.GetParent(e);
        for (int i = 0; i < levels && node is not null; i++, node = LogicalTreeHelper.GetParent(node))
            if (node is T found) return found;
        return null;
    }

    private static bool IsInput(object o) => o is ComboBox or TextBox or Slider;

    private static bool BesideInput(TextBlock text)
    {
        var node = LogicalTreeHelper.GetParent(text);
        for (int i = 0; i < 3 && node is not null; i++, node = LogicalTreeHelper.GetParent(node))
        {
            if (node is ButtonBase or ItemsControl) return false;
            if (node is Panel panel && panel.Children.OfType<UIElement>().Any(IsInput)) return true;
        }
        return false;
    }

    private static bool InData(FrameworkElement e, FrameworkElement root)
    {
        for (DependencyObject? node = e; node is not null && !ReferenceEquals(node, root); node = LogicalTreeHelper.GetParent(node))
            if (node is FrameworkElement fe && Ui.GetNoSearch(fe)) return true;
        return false;
    }

    private static readonly System.Text.RegularExpressions.Regex DataPattern =
        new(@"[“”]|\(\d+\)$|v\d+\.\d|\d{3,}|\d[.,]\d|^(Mon|Tue|Wed|Thu|Fri|Sat|Sun) \w+ \d", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static bool LooksLikeData(string title) => DataPattern.IsMatch(title);

    private static bool IsTab(FrameworkElement e) => Ui.IsPageTab(e);

    private static string TabName(RadioButton r) => (string)r.Content;

    private static string? NearestAnchor(FrameworkElement e, FrameworkElement root)
    {
        for (DependencyObject? node = e; node is not null; node = LogicalTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement fe && fe.GetValue(Ui.AnchorProperty) is string id) return id;
            if (ReferenceEquals(node, root)) break;
        }
        return null;
    }

    private static HashSet<string> UserText(object config)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(JsonElement j)
        {
            switch (j.ValueKind)
            {
                case JsonValueKind.String when j.GetString() is { Length: > 0 } s: set.Add(s.Trim()); break;
                case JsonValueKind.Object: foreach (var p in j.EnumerateObject()) Walk(p.Value); break;
                case JsonValueKind.Array: foreach (var item in j.EnumerateArray()) Walk(item); break;
            }
        }
        try { Walk(JsonSerializer.SerializeToElement(config, config.GetType())); }
        catch (Exception ex) { App.LogError(ex); }
        return set;
    }

    private static async Task Settle(Window window, int ms)
    {
        await Task.Delay(ms);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
