using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class MapEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("main", null, HudTab.Layout, true, O("titleRow"), O("zoom"), O("rotate"), O("turns"), O("pit")),
            new("dots", "Dots", HudTab.Layout, true, O("dotStyle"), O("dotSize"), O("myDotSize"), O("myColor"), O("carColor"), O("positions"), O("dotText") with { ShowIf = o => o.Bool("positions") },
                O("spread") with { ShowIf = Badges }, O("maxMove") with { ShowIf = o => Badges(o) && o.Bool("spread") }, O("lapColors")),
        },
    };

    private static bool Badges(HudOptions o) => o.Value("dotStyle") is "pill" or "circle";
}
