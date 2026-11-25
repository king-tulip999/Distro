using Core.SimRacing.Hud.Widgets;
using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class TickerStandingsEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("main", null, HudTab.Layout, true, O("style"), O("titleRow"),
                O("perClass") with { ShowIf = o => o.Choice("style") == TickerStandingsWidget.Classes }),
            new("cars", "Classes shown", HudTab.Content, true,
                new ClassFilterSetting() { ShowIf = o => o.Choice("style") != TickerStandingsWidget.Around }),
        },
    };
}
