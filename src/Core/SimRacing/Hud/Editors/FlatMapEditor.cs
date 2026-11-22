using Core.SimRacing.Hud.Widgets;
using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class FlatMapEditor
{
    private static Func<HudOptions, bool> Is(params string[] styles) => o => styles.Contains(o.Choice("style"));

    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("main", null, HudTab.Layout, true,
                O("style"),
                O("turns") with { ShowIf = Is(FlatMapWidget.Strip, FlatMapWidget.Profile) },
                O("sectors") with { ShowIf = Is(FlatMapWidget.Strip) },
                O("pit") with { ShowIf = Is(FlatMapWidget.Strip) },
                O("positions") with { ShowIf = Is(FlatMapWidget.Strip, FlatMapWidget.Lanes) },
                O("dotText") with { ShowIf = o => o.Choice("style") == FlatMapWidget.Strip && o.Bool("positions") },
                O("range") with { ShowIf = Is(FlatMapWidget.Around) },
                O("gaps") with { ShowIf = Is(FlatMapWidget.Around) },
                O("lapColors") with { ShowIf = Is(FlatMapWidget.Around) },
                O("bend") with { ShowIf = Is(FlatMapWidget.Profile) },
                O("upright") with { ShowIf = Is(FlatMapWidget.Bar) },
                O("dotSize") with { ShowIf = Is(FlatMapWidget.Bar) }),
            new("cars", "Classes shown", HudTab.Content, true, new ClassFilterSetting() { ShowIf = Is(FlatMapWidget.Lanes) }),
        },
    };
}
