using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class IncidentsEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("main", null, HudTab.Layout, true, O("titleRow"), O("count")),
            new("columns", "Columns", HudTab.Content, true, O("columns")),
        },
    };
}
