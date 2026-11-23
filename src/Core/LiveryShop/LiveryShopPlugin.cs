using Core.Config;
using Core.Plugins;
using Core.Services;
using Core.Views;

namespace Core.LiveryShop;

internal sealed class LiveryShopPlugin : Plugin
{
    public const string PluginId = "livery-shop";
    public const string PageKey = "livery";
    public const string Anchor = "livery.editor";

    public override string Id => PluginId;
    public override AppKind App => AppKind.Velocita;
    public override string Name => "Livery Shop";
    public override string Description => "Paint your iRacing car, helmet and suit: shapes, numbers, logos and finishes, saved straight into iRacing.";
    public override string Icon => "I.Palette";
    public override (string Soft, string Deep) Colors => ("PlumSoft", "PlumDeep");
    public override int Order => 65;

    public override string Reads =>
        "While its page is open: the car folders in Documents\\iRacing\\paint, and your customer ID and last car from your newest telemetry files. The car template files you choose to load. For the car preview: iRacing's own car picture service on this PC (only while the iRacing app is open).";
    public override string BackgroundWork =>
        "None while its page is closed. While it's open, the editor runs in a WebView2 window inside Velocita and saves your paint shortly after each change.";
    public override string Changes =>
        "Only your paint files: car_ID.tga and car_spec_ID.tga in the car's folder (or helmet_ID.tga / suit_ID.tga) in Documents\\iRacing\\paint, written when the editor saves. In a session it asks iRacing to reload paints. Your designs are kept in Velocita's own folder (livery-shop).";

    public override IReadOnlyList<PluginPage> Pages { get; } = new[]
    {
        new PluginPage(PageKey, "Livery Shop", "I.Palette", (app, window) => new LiveryShopPage(app, window)),
    };

    public override bool ShowsPages(AppServices app) => app.Config.LiveryShop.ShowPage;

    public override IEnumerable<SearchEntry> SearchEntries { get; } = new SearchEntry[]
    {
        new("Livery Shop", "Design car, helmet and suit paints with templates, team colors, numbers, logos and finishes. Saves into iRacing and previews your car.", "Livery Shop", PageKey, Anchor,
            "livery paint painter tga spec map finish template trading paints decal number sponsor logo helmet suit car preview"),
    };

    protected override void OnStart(AppServices app) { }

    protected override void OnStop(AppServices app) { }
}

public sealed class LiveryShopSettings
{
    public bool ShowPage { get; set; }
}

internal static class LiveryShopAccess
{
    public const string Section = "liveryShop";

    extension(AppConfig config)
    {
        public LiveryShopSettings LiveryShop => config.Section<LiveryShopSettings>(Section);
    }
}
