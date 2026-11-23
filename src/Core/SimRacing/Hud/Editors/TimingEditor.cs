using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class TimingEditor
{
    private static bool Look(HudOptions o, params string[] looks) => Array.IndexOf(looks, o.Choice("look")) >= 0;

    public static readonly HudEditorDef Def = new()
    {
        ContentTitle = "Times",
        Groups = new HudGroup[]
        {
            new("delta", "Delta", HudTab.Layout, true, O("look"), O("reference"), O("pill") with { ShowIf = o => Look(o, "pill") },
                O("bar") with { ShowIf = o => Look(o, "full", "compact") }, O("range") with { ShowIf = o => Look(o, "full", "compact") }),
            new("sectors", "Sectors", HudTab.Layout, true, O("sectors") with { ShowIf = o => Look(o, "full") },
                O("split") with { ShowIf = o => !Look(o, "compact", "pill") }),
            new("times", "Times", HudTab.Content, true, new PickerSetting("fields", new HudCategory[]
            {
                new("Your laps", "current", "last", "best", "predicted", "theoretical", "consistency"),
                new("Field", "sessionBest", "classBest"),
            }) { ShowIf = o => Look(o, "full") }),
        },
        Regions = new Dictionary<string, string> { ["delta"] = "reference", ["bar"] = "bar", ["sectors"] = "sectors", ["graph"] = "look" },
    };
}
