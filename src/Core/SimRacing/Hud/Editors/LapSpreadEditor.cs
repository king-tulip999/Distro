using Core.SimRacing.Hud.Widgets;
using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class LapSpreadEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("main", null, HudTab.Layout, true, O("style"), O("titleRow")),
            new("pace", "Lap times", HudTab.Content, true, O("basis"),
                O("laps") with { ShowIf = o => o.Choice("basis") == LapSpreadWidget.Pace }),
            new("cars", "Classes shown", HudTab.Content, true,
                new ClassFilterSetting() { ShowIf = o => o.Choice("style") == LapSpreadWidget.Dots }),
        },
    };
}
