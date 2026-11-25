using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class ControlsEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("main", null, HudTab.Layout, true, O("style"),
                O("titleRow") with { ShowIf = o => o.Choice("style") != Widgets.ControlsWidget.Strip },
                O("hold"),
                O("stay") with { ShowIf = o => o.Choice("style") == Widgets.ControlsWidget.Strip }),
        },
    };
}
