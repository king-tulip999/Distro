using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class TimingEditor
{
    public static readonly HudEditorDef Def = new()
    {
        ContentTitle = "Times",
        Groups = new HudGroup[]
        {
            new("delta", "Delta", HudTab.Layout, true, O("look"), O("reference"), O("bar") with { ShowIf = o => o.Choice("look") != "compact" }, O("range")),
            new("sectors", "Sectors", HudTab.Layout, true, O("sectors") with { ShowIf = o => o.Choice("look") != "compact" }, O("split")),
            new("times", "Times", HudTab.Content, true, new PickerSetting("fields", new HudCategory[]
            {
                new("Your laps", "current", "last", "best", "predicted", "theoretical", "consistency"),
                new("Field", "sessionBest", "classBest"),
            })),
        },
        Regions = new Dictionary<string, string> { ["delta"] = "reference", ["bar"] = "bar", ["sectors"] = "sectors" },
    };
}
