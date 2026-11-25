using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class CoachEditor
{
    private static Func<HudOptions, bool> Is(string style) => o => o.Choice("style") == style;

    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("main", null, HudTab.Layout, true, O("style"),
                O("titleRow") with { ShowIf = o => o.Choice("style") != Widgets.CoachWidget.Strip },
                O("callSize") with { ShowIf = Is(Widgets.CoachWidget.Strip) },
                O("direction") with { ShowIf = Is(Widgets.CoachWidget.RacingLine) },
                O("zoom") with { ShowIf = Is(Widgets.CoachWidget.RacingLine) },
                O("straights") with { ShowIf = Is(Widgets.CoachWidget.Debrief) },
                O("listed") with { ShowIf = o => Is(Widgets.CoachWidget.Debrief)(o) && o.Bool("lost") },
                O("beep"), O("volume") with { ShowIf = o => o.Bool("beep") }),
            new("show", "Show", HudTab.Content, true,
                O("pace") with { ShowIf = Is(Widgets.CoachWidget.Strip) },
                O("pedals") with { ShowIf = Is(Widgets.CoachWidget.Strip) },
                O("next") with { ShowIf = Is(Widgets.CoachWidget.Strip) },
                O("boards") with { ShowIf = Is(Widgets.CoachWidget.Strip) },
                O("result") with { ShowIf = Is(Widgets.CoachWidget.Strip) },
                O("chips") with { ShowIf = Is(Widgets.CoachWidget.RacingLine) },
                O("grid") with { ShowIf = Is(Widgets.CoachWidget.RacingLine) },
                O("speed") with { ShowIf = Is(Widgets.CoachWidget.RacingLine) },
                O("legend") with { ShowIf = Is(Widgets.CoachWidget.RacingLine) },
                O("rows") with { ShowIf = Is(Widgets.CoachWidget.Compare) },
                O("map") with { ShowIf = Is(Widgets.CoachWidget.Debrief) },
                O("lost") with { ShowIf = Is(Widgets.CoachWidget.Debrief) },
                O("gained") with { ShowIf = Is(Widgets.CoachWidget.Debrief) }),
            new("colors", "Colors", HudTab.Content, false,
                O("you"), O("best"),
                O("brakeColor") with { ShowIf = Is(Widgets.CoachWidget.RacingLine) },
                O("partColor") with { ShowIf = Is(Widgets.CoachWidget.RacingLine) },
                O("fullColor") with { ShowIf = Is(Widgets.CoachWidget.RacingLine) },
                O("lostColor") with { ShowIf = Is(Widgets.CoachWidget.Debrief) },
                O("gainedColor") with { ShowIf = Is(Widgets.CoachWidget.Debrief) }),
        },
    };
}
