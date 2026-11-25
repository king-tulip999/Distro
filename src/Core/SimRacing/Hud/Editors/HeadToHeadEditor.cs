using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class HeadToHeadEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("main", null, HudTab.Layout, true, O("style"),
                O("titleRow") with { ShowIf = o => o.Choice("style") != Widgets.HeadToHeadWidget.Ticker },
                O("within"), O("cars")),
        },
    };
}
