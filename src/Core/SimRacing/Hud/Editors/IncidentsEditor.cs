using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class IncidentsEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("main", null, HudTab.Layout, true, O("style"),
                O("titleRow") with { ShowIf = o => o.Choice("style") != Widgets.IncidentsWidget.Line },
                O("count") with { ShowIf = o => o.Choice("style") == Widgets.IncidentsWidget.Chips }),
        },
    };
}
