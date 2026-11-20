using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class WeatherEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("main", null, HudTab.Layout, true, O("look"), O("titleRow") with { ShowIf = o => o.Choice("look") != Widgets.WeatherWidget.StripLook },
                O("temp"), O("wind"), O("turn") with { ShowIf = o => o.Bool("wind") }, O("wetBar") with { ShowIf = o => o.Choice("look") != Widgets.WeatherWidget.StripLook }),
            new("rows", "Rows", HudTab.Content, true, O("rows") with { ShowIf = o => o.Choice("look") != Widgets.WeatherWidget.StripLook }),
        },
        Regions = new Dictionary<string, string> { ["wetBar"] = "wetBar" },
    };
}
