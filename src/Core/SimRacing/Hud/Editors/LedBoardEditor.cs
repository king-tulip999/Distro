using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class LedBoardEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[] { new("main", null, HudTab.Layout, true, O("show"), O("frame"), O("frameColor") with { Label = "Custom color", ShowIf = o => o.Choice("frame") == "custom" }) },
    };
}
