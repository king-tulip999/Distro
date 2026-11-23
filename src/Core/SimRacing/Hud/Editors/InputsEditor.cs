using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class InputsEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("style", "Style", HudTab.Layout, true, O("style"), O("gearSide") with { ShowIf = o => o.Choice("style") == Widgets.InputsWidget.Dial }),
            new("shows", "Shows", HudTab.Layout, true, O("show")),
            new("extras", "Extra fields", HudTab.Content, true, O("extras") with { ShowIf = o => o.Choice("style") == Widgets.InputsWidget.Dial }),
        },
    };
}
