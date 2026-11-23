using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class TyresEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("main", null, HudTab.Layout, true, O("look"), O("titleRow")),
            new("shows", "Shows", HudTab.Layout, true, new TogglesSetting("shows", "Each corner shows", new[] { "pressure", "temps", "wear" })),
            new("limits", "Temperature limits", HudTab.Layout, false, O("low") with { ShowIf = o => o.Bool("temps") }, O("high") with { ShowIf = o => o.Bool("temps") }),
        },
    };
}
