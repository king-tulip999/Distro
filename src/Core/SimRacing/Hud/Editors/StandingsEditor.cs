using static Core.SimRacing.Hud.Editors.FieldSettings;
using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class StandingsEditor
{
    private static readonly MergedSetting TitleRow = new("titleRow", "Title row", new[] { "title", "progress" }, new MergedChoice[]
    {
        new("Off", V(("title", false))),
        new("On", V(("title", true), ("progress", false)), "Session name and time or laps left"),
        new("On + progress", V(("title", true), ("progress", true)), "With a session progress bar under it"),
    });

    private static readonly MergedSetting Grouping = new("grouping", "Group by class", new[] { "group", "classOrder" }, new MergedChoice[]
    {
        new("Off", V(("group", false)), "One list, overall positions"),
        new("By leader", V(("group", true), ("classOrder", "position")), "Multiclass: one block per class, in their leaders' order"),
        new("Fastest first", V(("group", true), ("classOrder", "fastest")), "Multiclass: one block per class, the fastest class first"),
    });

    private static bool Grouped(HudOptions o) => o.Bool("group");

    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("columns", "Columns", HudTab.Content, true, new PickerSetting("columns", ColumnCategories)) { Page = "Columns" },
            new("names", "Driver names", HudTab.Layout, true, O("nameFormat") with { Sample = format => Race.RaceFormat.Name("Lucía Fernández", format) }, O("noDigits"), O("proAm"), O("badgeFormat") with { ShowIf = Has("columns", "badge") }) { Page = "Columns" },
            new("timing", "Values", HudTab.Layout, true, O("gapTo"), O("timeFormat"), O("decimals"), O("lastLaps") with { ShowIf = Has("columns", "last") }, O("avgLaps") with { ShowIf = Has("columns", "avglap") },
                O("pitShow") with { ShowIf = Has("columns", "pit") }, O("pitTime") with { ShowIf = Has("columns", "pit") }) { Page = "Columns" },
            new("rows", "Your class", HudTab.Layout, true, O("top"), O("around"), O("divider")) { Page = "Rows" },
            new("cars", "Other classes", HudTab.Content, true, new ClassFilterSetting(), Grouping, O("otherTop") with { ShowIf = Grouped, InlineHelp = true }, O("otherRows") with { ShowIf = Grouped, InlineHelp = true },
                O("classInfo") with { ShowIf = Grouped, Label = "Class headings" }) { Page = "Rows" },
            new("layout", "Rows", HudTab.Layout, true, MyRow, O("stripes"), O("classRows"), O("classBorder"), O("marks"), O("retired")) { Page = "Rows" },
            new("header", "Title", HudTab.Layout, true, TitleRow, O("showHeader")) { Page = "Header and footer" },
            new("infobars", "Info bars", HudTab.Content, true, InfoBars) { Page = "Header and footer" },
            new("colors", "Flags", HudTab.Layout, true, FlagBorder) { Page = "Look" },
        },
        Regions = new Dictionary<string, string>
        {
            ["title"] = "titleRow",
            ["headings"] = "showHeader",
            ["myrow"] = "myRow",
            ["classhead"] = "classInfo",
            ["divider"] = "divider",
            ["rows"] = "top",
        },
    };
}
