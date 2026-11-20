using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class LedBoardEditor
{
    private static bool Flags(HudOptions o) => o.Choice("show") == "flag";

    private static bool Numbers(HudOptions o) => !Flags(o);

    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("main", null, HudTab.Layout, true, O("show"),
                O("frame") with { ShowIf = Numbers },
                O("frameColor") with { Label = "Custom color", ShowIf = o => Numbers(o) && o.Choice("frame") == "custom" }),
            new("numbers", "Animations", HudTab.Layout, true,
                O("change") with { ShowIf = Numbers },
                O("rev") with { ShowIf = o => o.Choice("show") == "gear" },
                O("limiter") with { ShowIf = o => o.Choice("show") == "gear" },
                O("placeColors") with { ShowIf = o => o.Choice("show") == "pos" },
                O("lastLaps") with { ShowIf = o => o.Choice("show") == "left" }),
            new("flags", "Flags", HudTab.Layout, true, FlagSettings()),
        },
    };

    private static HudSetting[] FlagSettings()
    {
        var list = new List<HudSetting> { O("previewFlag") with { ShowIf = Flags }, O("animate") with { ShowIf = Flags } };
        foreach (var kind in LedFlags.All) list.Add(O(kind.Key) with { ShowIf = Flags, Dropdown = true });
        return list.ToArray();
    }
}
