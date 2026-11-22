using System.Diagnostics;
using System.Drawing;
using System.Windows.Controls;
using System.Windows.Media;
using GameReaderCommon;
using SimHub.Plugins;

namespace VelocitaSimHubPlugin;

[PluginDescription("Velocita's iRacing overlay widgets as SimHub overlays")]
[PluginAuthor("Velocita")]
[PluginName("Velocita")]
public class VelocitaPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
{
    private const long CheckEveryMs = 10000;
    private readonly Stopwatch _sinceCheck = new();

    public PluginManager PluginManager { get; set; } = null!;

    public ImageSource PictureIcon => this.ToIcon(MenuIcon());

    public string LeftMenuTitle => "Velocita";

    public void Init(PluginManager pluginManager)
    {
        this.AttachDelegate("Running", () => Engine.Running);
        this.AttachDelegate("On", () => Engine.On);
        this.AddAction("Toggle", (_, _) => Engine.Toggle());
        this.AddAction("TurnOn", (_, _) => Engine.SetOn(true));
        this.AddAction("TurnOff", (_, _) => Engine.SetOn(false));
        if (Engine.On && !Engine.Running) Engine.Start();
        _sinceCheck.Restart();
    }

    public void DataUpdate(PluginManager pluginManager, ref GameData data)
    {
        if (Engine.Closing || !Engine.On || _sinceCheck.ElapsedMilliseconds < CheckEveryMs) return;
        _sinceCheck.Restart();
        if (!Engine.Running) Engine.Start();
    }

    public void End(PluginManager pluginManager) => Engine.Close();

    public Control GetWPFSettingsControl(PluginManager pluginManager) => new SettingsView();

    private static Bitmap MenuIcon()
    {
        using var stream = typeof(VelocitaPlugin).Assembly.GetManifestResourceStream("Velocita.ico")!;
        using var icon = new Icon(stream, 24, 24);
        return icon.ToBitmap();
    }
}
