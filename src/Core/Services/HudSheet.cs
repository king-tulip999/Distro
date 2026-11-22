using System.Drawing;
using System.IO;
using Core.Config;
using Core.SimRacing.Hud;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.Services;

internal static class HudSheet
{
    public const string Arg = "--hud-sheet";
    public const string FramesArg = "--hud-frames";
    public const string SiteArg = "--hud-site";
    public const string SiteFramesArg = "--hud-site-frames";

    public static void RunSite(App app, string folder, bool frames = false)
    {
        Running = true;
        try
        {
            Directory.CreateDirectory(folder);
            RenderSite(folder, frames);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(folder, "error.txt"), ex.ToString()); } catch { }
        }
        app.Shutdown();
    }

    private sealed record SiteShot(string Kind, string Label, (string Key, string Value)[] Options, RaceFlags? Flags = null, double? WindFrom = null, double At = 0, bool Fit = false, float H = 0, float W = 0, double Speed = 0, int? Bg = null);

    private sealed class SiteRace
    {
        public readonly RaceHudSettings Settings = new();
        public readonly RaceState Race;
        private readonly DemoTelemetry _demo = new();
        private readonly TelemetryFrame _frame = new();
        private long _seq;
        public double T = -1500;

        public SiteRace()
        {
            Race = new RaceState(() => Settings) { MapLoader = SimRacing.TrackMaps.TrackMapLibrary.ForRace, Clock = () => T };
        }

        public void To(double at)
        {
            for (; T <= at; T += 0.25)
            {
                _demo.Fill(_frame, T);
                _frame.Sequence = ++_seq;
                Race.Update(_frame);
            }
        }

        public void Settle()
        {
            Thread.Sleep(300);
            _demo.Fill(_frame, T);
            _frame.Sequence = ++_seq;
            Race.Update(_frame);
        }

        public void Run(double until)
        {
            while (T < until - 1e-9)
            {
                T = Math.Min(until, T + 0.25);
                _demo.Fill(_frame, T);
                _frame.Sequence = ++_seq;
                Race.Update(_frame);
            }
        }
    }

    private const float SiteScale = 2;
    private const double SiteClipSeconds = 8;
    private static readonly HashSet<string> Searched = new(StringComparer.OrdinalIgnoreCase) { "tyres", "incidents", "alerts", "laps", "notes", "events" };
    private static readonly Dictionary<string, double> SiteSpeeds = new(StringComparer.OrdinalIgnoreCase) { ["block"] = 1, ["incidents"] = 60, ["notes"] = 10, ["laps"] = 80 };

    private static void RenderSite(string folder, bool frames)
    {
        static (string, string)[] O(params (string, string)[] o) => o;
        const double Ahead = 0, Cross = Math.PI / 2, Behind = Math.PI;
        var shots = new List<SiteShot>
        {
            new("relative", "Default", O()),
            new("relative", "Different header and footer", O(("header", "lap,best,delta,sof,air,track"), ("footer", "wind,humidity,wetness,cars,csof"))),
            new("relative", "Six cars each way", O(("ahead", "6"), ("behind", "6")), H: 360),
            new("relative", "Blue flag banner", O(("flagBanner", "top")), RaceFlags.Blue),
            new("relative", "Checkered flag banner", O(("flagBanner", "top")), RaceFlags.Checkered),
            new("standings", "Grouped by class", O()),
            new("standings", "One list, gap to the car ahead", O(("group", "false"), ("gapTo", "interval")), H: 330),
            new("standings", "Only my class", O(("classes", "mine")), H: 300),
            new("timing", "Delta to my optimal lap", O()),
            new("timing", "Delta to the session best", O(("reference", "session"))),
            new("timing", "Delta to my best lap", O(("reference", "best"))),
            new("timing", "Compact", O(("look", "compact"), ("reference", "session")), H: 110),
            new("fuel", "Default", O(), At: 600),
            new("fuel", "Pit exit", O(("parts", "header,figures"), ("head", "togo,pitexit,stops,add"), ("top", "pitexit,level,laps")), At: 600, H: 122),
            new("fuel", "Fuel saving", O(("parts", "header,save")), At: 600, H: 122),
            new("fuel", "Every section", O(("parts", "header,gauge,plan,save,figures,table,grid"), ("head", "togo,nextpit,stops,pitby,add,pitexit,level"), ("gridRows", "curr,avg,max,min,last")), At: 600, Fit: true),
            new("session", "Default", O()),
            new("session", "Time, position, phase and track", O(("fields", "remain,elapsed,pos,cpos,phase,track"))),
            new("map", "Whole track", O()),
            new("map", "Zoomed in on you", O(("zoom", "4"))),
            new("map", "Turns with the car", O(("rotate", "true"), ("zoom", "3"))),
            new("tyres", "Default", O()),
            new("tyres", "Without wear", O(("wear", "false"))),
            new("weather", "Panel", O()),
            new("weather", "Strip", O(("look", "strip")), W: 340, H: 124, Speed: 300),
            new("weather", "°C", O(("temp", "c"))),
            new("weather", "°F", O(("temp", "f"))),
            new("weather", "°C and °F", O(("temp", "both"))),
            new("wind", "Headwind", O(("title", "true")), WindFrom: Ahead),
            new("wind", "Crosswind", O(("title", "true")), WindFrom: Cross),
            new("wind", "Tailwind", O(("title", "true")), WindFrom: Behind),
            new("wind", "Accent color", O(("colors", "accent")), WindFrom: Cross + 0.6),
            new("laps", "Last 10 laps", O()),
            new("laps", "Last 3 laps, no statistics", O(("count", "3"), ("stats", "false")), Fit: true),
            new("incidents", "Default", O()),
            new("events", "Default", O()),
            new("events", "Longer timeline", O(("count", "14")), H: 330),
            new("notes", "Default", O()),
            new("alerts", "Default", O()),
            new("result", "Card", O()),
            new("result", "Banner", O(("look", "banner")), Fit: true),
            new("result", "Stamp", O(("look", "stamp")), Fit: true),
            new("result", "Lap story", O(("look", "story")), Fit: true),
            new("inputs", "Gear dial", O()),
            new("inputs", "Gear dial with extra fields", O(("extras", "position,lapNumber,fuelPct,last,best,air")), Fit: true),
            new("inputs", "Trace only", O(("style", "bare"))),
            new("inputs", "Pedals and speed", O(("show", "throttle,brake,speed"))),
            new("inputs", "Pedals and RPM", O(("show", "throttle,brake,rpm"))),
            new("block", "Speed", O(("source", "speed"))),
            new("block", "RPM with a bar", O(("source", "rpm"), ("visual", "bar"))),
            new("block", "Fuel", O(("source", "fuel")), Speed: 20),
            new("block", "Throttle with a bar", O(("source", "throttle"), ("visual", "bar"))),
            new("block", "Gear", O(("source", "gear")), Speed: 2),
            new("ledboard", "Gear", O(("show", "gear"))),
            new("ledboard", "Waving green flag", O(("show", "flag")), RaceFlags.Green, At: 0.3),
            new("ledboard", "Position, white frame", O(("show", "pos"), ("frame", "white"))),
            new("ledboard", "Laps left, no frame", O(("show", "left"), ("frame", "off"))),
            new("ledboard", "Green flag", O(("show", "flag")), RaceFlags.Green | RaceFlags.StartGo, At: 0.4),
            new("ledboard", "Yellow flag", O(("show", "flag")), RaceFlags.Yellow),
            new("ledboard", "Blue flag, edge chase", O(("show", "flag"), ("flagBlue", "chase")), RaceFlags.Blue),
            new("ledboard", "White flag", O(("show", "flag")), RaceFlags.White, At: 0.4),
            new("ledboard", "Checkered flag", O(("show", "flag")), RaceFlags.Checkered),
            new("ledboard", "Black flag", O(("show", "flag")), RaceFlags.Black),
            new("ledboard", "Red flag", O(("show", "flag")), RaceFlags.Red),
            new("ledboard", "Safety car", O(("show", "flag")), RaceFlags.Caution),
            new("ledboard", "Safety car, SC and chase", O(("show", "flag"), ("flagSafetyCar", "chase")), RaceFlags.Caution, At: 0.2),
            new("ledboard", "Repair flag", O(("show", "flag")), RaceFlags.Repair),
            new("ledboard", "Yellow, hazard stripes", O(("show", "flag"), ("flagYellow", "stripes")), RaceFlags.Yellow),
            new("ledboard", "Checkered, fireworks", O(("show", "flag"), ("flagCheckered", "fireworks")), RaceFlags.Checkered, At: 0.3),
            new("ledboard", "Gear, rev lights", O(("show", "gear"))),
            new("ledboard", "Gear, no frame", O(("show", "gear"), ("frame", "off"))),
        };
        foreach (var (kind, options, count, name) in new[]
        {
            ("traffic", O(("window", "30"), ("max", "6")), 2, "Wider window"), ("traffic", O(("window", "30"), ("max", "6"), ("sameClass", "true")), 1, "Wider window, my class too"),
            ("radar", O(), 2, "Default"), ("radar", O(("range", "40")), 1, "Longer range"), 
        })
        {
            var moments = Busiest(kind, options, count);
            for (int i = 0; i < moments.Count; i++)
                shots.Add(new SiteShot(kind, moments.Count > 1 ? $"{name}, moment {i + 1}" : name, options, At: moments[i]));
        }
        shots.Add(new SiteShot("ledboard", "Checkered, wave", O(("show", "flag"), ("flagCheckered", "wave")), RaceFlags.Checkered));
        shots.Add(new SiteShot("map", "Whole track, no panel", O(("titleRow", "false")), Bg: 0));

        var json = new System.Text.StringBuilder("[");
        var races = new Dictionary<double, SiteRace>();
        using var painter = new HudPainter();
        int n = 0;
        foreach (var shot in shots)
        {
            if (!races.TryGetValue(shot.At, out var site))
            {
                site = new SiteRace();
                site.To(shot.At);
                site.Settle();
                races[shot.At] = site;
            }
            var def = HudCatalog.Find(shot.Kind)!;
            var config = new HudWidgetConfig { Id = "site-" + def.Kind, Kind = def.Kind, W = def.W, H = def.H, Background = shot.Bg };
            foreach (var (key, value) in shot.Options) config.Options[key] = value;
            var frame = site.Race.Frame;
            var restore = Override(shot, frame, clip: null);
            var view = new HudOptions(def, config);
            SizeF size = def.Natural?.Invoke(view) is { Width: > 0, Height: > 0 } natural ? natural
                : shot.H > 0 ? new SizeF(shot.W > 0 ? shot.W : (float)def.W, shot.H) : shot.Fit ? Fit(def, config, site) : new SizeF((float)def.W, (float)def.H);
            if (config.Options.TryGetValue("flagBanner", out var where))
            {
                var (fl, ft, fr, fb) = HudFlags.Room(where);
                size = new SizeF(size.Width + fl + fr, size.Height + ft + fb);
            }
            string file = $"{def.Kind}-{n++:00}.png";
            using (var surface = new Core.Overlay.DibSurface())
            {
                var g = surface.Ensure(new Size((int)Math.Ceiling(size.Width * SiteScale), (int)Math.Ceiling(size.Height * SiteScale)));
                painter.Simulated = true;
                painter.IsPreview = true;
                painter.Begin(g, SiteScale, size, HudPalette.Of(site.Settings.Appearance), site.Settings.Appearance);
                painter.Background = config.Background;
                def.Create().Paint(painter, new HudView { Frame = frame, Race = site.Race, Settings = site.Settings, Config = config, Options = view, Def = def });
                painter.FinishPanels();
                painter.End(surface.Dc, surface.Size);
                surface.Bitmap!.Save(Path.Combine(folder, file));
            }
            restore();
            if (frames) Animate(shot, def, config, size, Path.Combine(folder, Path.GetFileNameWithoutExtension(file)));
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            json.Append(json.Length > 1 ? "," : "").Append(string.Format(inv, "{{\"kind\":\"{0}\",\"label\":\"{1}\",\"file\":\"{2}\",\"w\":{3:0},\"h\":{4:0}}}",
                def.Kind, System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping.Encode(shot.Label), file, Math.Ceiling(size.Width), Math.Ceiling(size.Height)));
        }
        File.WriteAllText(Path.Combine(folder, "site.json"), json.Append(']').ToString());

        static Action Override(SiteShot shot, TelemetryFrame frame, double? clip)
        {
            for (int car = 0; car < frame.CarFlags.Length; car++) frame.CarFlags[car] &= ~RaceFlags.Black;
            var (flags, windDir, windSpeed, yaw) = (frame.Flags, frame.WindDir, frame.WindSpeed, frame.YawNorth);
            if (shot.Flags is { } f) frame.Flags = f;
            if (shot.WindFrom is { } from)
            {
                double heading = clip is null ? 0 : yaw ?? 0;
                (frame.WindDir, frame.WindSpeed, frame.YawNorth) = (heading + from, 6.5, heading);
            }
            if (clip is { } c && frame.WindDir is { } dir && frame.WindSpeed is { } speed)
            {
                double turn = 2 * Math.PI * c / SiteClipSeconds;
                frame.WindSpeed = speed * (1 + 0.22 * Math.Sin(turn * 2) + 0.08 * Math.Sin(turn * 5 + 1));
                frame.WindDir = dir + 0.2 * Math.Sin(turn + 0.5);
            }
            if (frame.WindDir is { } wrap) frame.WindDir = (wrap % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
            return () => (frame.Flags, frame.WindDir, frame.WindSpeed, frame.YawNorth) = (flags, windDir, windSpeed, yaw);
        }

        static void Animate(SiteShot shot, HudWidgetDef def, HudWidgetConfig config, SizeF size, string dir)
        {
            Directory.CreateDirectory(dir);
            double speed = shot.Speed > 0 ? shot.Speed : SiteSpeeds.GetValueOrDefault(def.Kind, Speeds.GetValueOrDefault(def.Kind, 1));
            var site = new SiteRace();
            site.To(Searched.Contains(def.Kind) ? SiteBusiest(shot, def, config, size, speed) : shot.At);
            double from = site.T;
            site.Settle();
            int fps = EveryFrame.Contains(def.Kind) || def.Kind is "ledboard" or "block" or "result" ? 30 : 15;
            double start = site.T;
            var widget = def.Create();
            var view = new HudOptions(def, config);
            var palette = HudPalette.Of(site.Settings.Appearance);
            using var painter = new HudPainter();
            using var surface = new Core.Overlay.DibSurface();
            var pixels = new Size((int)Math.Ceiling(size.Width * SiteScale), (int)Math.Ceiling(size.Height * SiteScale));
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var saved = new List<string>();
            double seconds = def.Kind == "result" ? 4 : SiteClipSeconds;
            for (int i = 0; i < seconds * fps; i++)
            {
                site.Run(start + i * speed / fps);
                var frame = site.Race.Frame;
                var restore = Override(shot, frame, i / (double)fps);
                var g = surface.Ensure(pixels);
                painter.Simulated = true;
                painter.IsPreview = true;
                painter.PlaysEntrances = true;
                painter.Begin(g, SiteScale, size, palette, site.Settings.Appearance);
                painter.Background = config.Background;
                widget.Paint(painter, new HudView { Frame = frame, Race = site.Race, Settings = site.Settings, Config = config, Options = view, Def = def });
                painter.FinishPanels();
                painter.End(surface.Dc, surface.Size);
                restore();
                string name = $"{i:0000}.png";
                surface.Bitmap!.Save(Path.Combine(dir, name));
                saved.Add(string.Format(inv, "{{\"t\":{0:0.000},\"f\":\"{1}\"}}", i / (double)fps, name));
            }
            File.WriteAllText(Path.Combine(dir, "frames.json"), string.Format(inv, "{{\"kind\":\"{0}\",\"scale\":{1},\"fps\":{2},\"speed\":{3},\"start\":{4:0.0},\"frames\":[{5}]}}", def.Kind, SiteScale, fps, speed, from, string.Join(",", saved)));
        }

        static double SiteBusiest(SiteShot shot, HudWidgetDef def, HudWidgetConfig config, SizeF size, double speed)
        {
            var site = new SiteRace();
            var widget = def.Create();
            var view = new HudOptions(def, config);
            var palette = HudPalette.Of(site.Settings.Appearance);
            using var painter = new HudPainter();
            using var surface = new Core.Overlay.DibSurface();
            var pixels = new Size((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height));
            var previous = new int[pixels.Width * pixels.Height];
            var current = new int[previous.Length];
            var times = new List<double>();
            var changes = new List<double>();
            double step = 0.5 * speed;
            for (double at = SearchFrom; at < SearchTo; at += step)
            {
                site.Run(at);
                var frame = site.Race.Frame;
                var restore = Override(shot, frame, clip: null);
                var g = surface.Ensure(pixels);
                painter.Simulated = true;
                painter.IsPreview = true;
                painter.Begin(g, 1, size, palette, site.Settings.Appearance);
                widget.Paint(painter, new HudView { Frame = frame, Race = site.Race, Settings = site.Settings, Config = config, Options = view, Def = def });
                painter.FinishPanels();
                painter.End(surface.Dc, surface.Size);
                restore();
                var bitmap = surface.Bitmap!;
                var data = bitmap.LockBits(new Rectangle(Point.Empty, pixels), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                for (int y = 0; y < pixels.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, current, y * pixels.Width, pixels.Width);
                bitmap.UnlockBits(data);
                int differing = 0;
                for (int i = 0; i < current.Length; i++) if (current[i] != previous[i]) differing++;
                times.Add(at);
                changes.Add(times.Count == 1 ? 0 : Math.Min(0.15, differing / (double)current.Length));
                (previous, current) = (current, previous);
            }
            int window = (int)Math.Round(SiteClipSeconds * speed / step);
            double best = -1, bestAt = shot.At;
            for (int i = 1; i + window <= changes.Count; i++)
            {
                double score = 0;
                for (int j = i + 1; j < i + window; j++) score += changes[j];
                if (score > best) (best, bestAt) = (score, times[i]);
            }
            return bestAt;
        }

        static SizeF Fit(HudWidgetDef def, HudWidgetConfig config, SiteRace site)
        {
            using var fit = new HudPainter { Overflow = new() };
            using var surface = new Core.Overlay.DibSurface();
            var widget = def.Create();
            var palette = HudPalette.Of(site.Settings.Appearance);
            bool Clean(float w, float h)
            {
                var g = surface.Ensure(new Size((int)Math.Ceiling(w), (int)Math.Ceiling(h)));
                fit.Overflow!.Clear();
                fit.Simulated = true;
                fit.IsPreview = true;
                fit.Begin(g, 1, new SizeF(w, h), palette, site.Settings.Appearance);
                widget.Draw(fit, new HudView { Frame = site.Race.Frame, Race = site.Race, Settings = site.Settings, Config = config, Options = new HudOptions(def, config), Def = def });
                fit.FinishPanels();
                return fit.Overflow!.Count == 0 && !fit.Spilled && fit.MinWidth <= w + 0.5f;
            }
            const float Tall = 2400;
            Clean((float)def.W, Tall);
            float width = Math.Max((float)def.W, fit.MinWidth);
            Clean(width, Tall);
            width = Math.Max(width, fit.MinWidth);
            float lo = (float)def.MinH, hi = Tall;
            while (hi - lo > 1)
            {
                float mid = MathF.Round((lo + hi) / 2);
                if (mid <= lo || mid >= hi) break;
                if (Clean(width, mid)) hi = mid; else lo = mid;
            }
            return new SizeF(width, MathF.Ceiling(hi));
        }

        static List<double> Busiest(string kind, (string, string)[] options, int count)
        {
            var def = HudCatalog.Find(kind)!;
            var config = new HudWidgetConfig { Id = "busy-" + kind, Kind = kind, W = def.W, H = def.H };
            foreach (var (key, value) in options) config.Options[key] = value;
            var site = new SiteRace();
            using var painter = new HudPainter();
            using var surface = new Core.Overlay.DibSurface();
            var size = new Size((int)def.W, (int)def.H);
            var palette = HudPalette.Of(site.Settings.Appearance);
            var scored = new List<(double At, int Lit)>();
            var pixels = new int[size.Width * size.Height];
            for (double at = 60; at <= 1500; at += 5)
            {
                site.To(at);
                var g = surface.Ensure(size);
                painter.Simulated = true;
                painter.IsPreview = true;
                painter.Begin(g, 1, new SizeF(size.Width, size.Height), palette, site.Settings.Appearance);
                def.Create().Paint(painter, new HudView { Frame = site.Race.Frame, Race = site.Race, Settings = site.Settings, Config = config, Options = new HudOptions(def, config), Def = def });
                painter.FinishPanels();
                painter.End(surface.Dc, surface.Size);
                var data = surface.Bitmap!.LockBits(new Rectangle(Point.Empty, size), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                for (int y = 0; y < size.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * size.Width, size.Width);
                surface.Bitmap!.UnlockBits(data);
                int lit = 0;
                foreach (var px in pixels) if (((px >> 16) & 0xFF) + ((px >> 8) & 0xFF) + (px & 0xFF) > 330) lit++;
                scored.Add((at, lit));
            }
            var picked = new List<double>();
            foreach (var (at, _) in scored.OrderByDescending(s => s.Lit))
            {
                if (picked.Any(p => Math.Abs(p - at) < 90)) continue;
                picked.Add(at);
                if (picked.Count == count) break;
            }
            return picked;
        }
    }

    public static bool Running { get; private set; }

    public static void Run(App app, string folder, bool frames = false)
    {
        Running = true;
        try
        {
            Directory.CreateDirectory(folder);
            if (frames) RenderFrames(folder);
            else
            {
                Render(folder);
                Time(folder);
            }
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(folder, "error.txt"), ex.ToString()); } catch { }
        }
        app.Shutdown();
    }

    private static void Render(string folder)
    {
        var settings = new RaceHudSettings();
        var race = new RaceState(() => settings) { MapLoader = SimRacing.TrackMaps.TrackMapLibrary.ForRace };
        var demo = new DemoTelemetry();
        var frame = new TelemetryFrame();
        long seq = 0;
        for (double t = -1500; t <= 0; t += 0.25)
        {
            demo.Fill(frame, t);
            frame.Sequence = ++seq;
            race.Update(frame);
        }
        Thread.Sleep(300);
        demo.Fill(frame, 0.25);
        frame.Sequence = ++seq;
        race.Update(frame);
        if (race.Incidents.Count < 4)
            foreach (var (at, lap, sector, points) in new[] { (380.2, 3, 1, 1), (742.6, 6, 2, 2), (1105.9, 9, 0, 4), (1310.4, 11, 2, 1) })
                race.AddIncident(new IncidentRecord(at, lap, sector, points, 0));
        using var painter = new HudPainter();
        foreach (var name in new[] { "Default" })
        {
            var palette = HudPalette.Of(settings.Appearance);
            string dir = Path.Combine(folder, name);
            Directory.CreateDirectory(dir);
            foreach (var def in HudCatalog.All)
            {
                Save(def, def.W, def.H, Path.Combine(dir, def.Kind + ".png"));
                Save(def, def.MinW, def.MinH, Path.Combine(dir, def.Kind + "-min.png"));
            }
            var relative = HudCatalog.Find("relative")!;
            Save(relative, 460, relative.H, Path.Combine(dir, "relative-strips.png"), 1, ("header", "lap,best,delta,sof,air,track"), ("footer", "wind,humidity,wetness,cars,csof"));
            var flags = race.Frame.Flags;
            foreach (var (file, where, flag) in new[] { ("relative-flag-top", "top", RaceFlags.Yellow), ("relative-flag-checkered", "top", RaceFlags.Checkered), ("relative-flag-blue", "top", RaceFlags.Blue), ("relative-flag-meatball", "top", RaceFlags.Repair) })
            {
                race.Frame.Flags = flag;
                var (fl, ft, fr, fb) = HudFlags.Room(where);
                Save(relative, relative.W + fl + fr, relative.H + ft + fb, Path.Combine(dir, file + ".png"), 1, ("flagBanner", where), ("header", "lap,best,delta,sof,air,track"), ("footer", "wind,humidity,wetness,cars,csof"));
            }
            race.Frame.Flags = flags;

            var inputs = HudCatalog.Find("inputs")!;
            foreach (var (file, style, show) in new[] { ("inputs-dial", "dial", ""), ("inputs-trimmed", "dial", "throttle,brake,speed"), ("inputs-rpm-trimmed", "dial", "throttle,brake,rpm"), ("inputs-bare", "bare", ""), ("inputs-no-gear", "dial", "throttle,brake,clutch,steering,speed,rpm,trace,abs") })
            {
                var config = new HudWidgetConfig { Options = { ["style"] = style } };
                if (show.Length > 0) config.Options["show"] = show;
                var size = inputs.Natural!(new HudOptions(inputs, config));
                Save(inputs, size.Width, size.Height, Path.Combine(dir, file + ".png"), 1, config.Options.Select(kv => (kv.Key, kv.Value)).ToArray());
            }

            foreach (var (kind, file, options) in new[]
            {
                ("result", "result-banner", new[] { ("look", "banner") }),
                ("result", "result-stamp", new[] { ("look", "stamp") }),
                ("result", "result-story", new[] { ("look", "story") }),
            })
            {
                var def = HudCatalog.Find(kind)!;
                var config = new HudWidgetConfig();
                foreach (var (key, value) in options) config.Options[key] = value;
                var size = def.Natural!(new HudOptions(def, config));
                Save(def, size.Width, size.Height, Path.Combine(dir, file + ".png"), 1, options);
            }

            var dialExtras = new HudWidgetConfig { Options = { ["extras"] = "position,lapNumber,fuelPct,last,best,air,bias,tc" } };
            var dialSize = inputs.Natural!(new HudOptions(inputs, dialExtras));
            Save(inputs, dialSize.Width, dialSize.Height, Path.Combine(dir, "inputs-extras.png"), 1, ("extras", "position,lapNumber,fuelPct,last,best,air,bias,tc"));
            var timing = HudCatalog.Find("timing")!;
            Save(timing, 200, 110, Path.Combine(dir, "timing-compact.png"), 1, ("look", "compact"), ("reference", "session"));
            var weather = HudCatalog.Find("weather")!;
            Save(weather, 340, 124, Path.Combine(dir, "weather-strip.png"), 1, ("look", "strip"));
            var ledboard = HudCatalog.Find("ledboard")!;
            foreach (var (file, flag) in new[] { ("ledboard-green", RaceFlags.Green | RaceFlags.StartGo), ("ledboard-sc", RaceFlags.Caution), ("ledboard-checkered", RaceFlags.Checkered) })
            {
                race.Frame.Flags = flag;
                Save(ledboard, 150, 150, Path.Combine(dir, file + ".png"), 1, ("show", "flag"));
            }
            race.Frame.Flags = flags;
            var fuel = HudCatalog.Find("fuel")!;
            Save(fuel, fuel.W, 150, Path.Combine(dir, "fuel-pitexit.png"), 1, ("parts", "header,figures"), ("head", "togo,pitexit,stops,add"), ("top", "pitexit,level,laps"));
            Save(fuel, fuel.W, 150, Path.Combine(dir, "fuel-save.png"), 1, ("parts", "header,save"));
            var everyFuel = new[]
            {
                ("parts", "header,gauge,plan,save,figures,table,grid"), ("head", "togo,nextpit,stops,pitby,add,pitexit,level"),
                ("gridRows", "curr,avg,max,min,last"), ("top", "level,perlap,togo,laps,add,stops,window,pitexit,atflag,surplus,tofinish,max,last,time"),
            };
            var everySize = Fitted(fuel, everyFuel);
            Save(fuel, everySize.Width, everySize.Height, Path.Combine(dir, "fuel-every.png"), 1, everyFuel);
            var threeLaps = new[] { ("lastLaps", "3") };
            var standings = HudCatalog.Find("standings")!;
            Save(standings, 400, standings.H, Path.Combine(dir, "standings-narrow.png"));
            var classColors = new[] { ("classRows", "true"), ("classBorder", "true") };
            Save(standings, standings.W, standings.H, Path.Combine(dir, "standings-classes.png"), 1, classColors);
            Save(standings, standings.W, standings.H, Path.Combine(dir, "standings-classes-list.png"), 1, classColors.Append(("group", "false")).ToArray());
            Save(relative, relative.W, relative.H, Path.Combine(dir, "relative-classes.png"), 1, classColors);
            Save(relative, relative.W, relative.H, Path.Combine(dir, "relative-outlines.png"), 1, ("classBorder", "true"));
            Save(relative, 640, relative.H, Path.Combine(dir, "relative-logos.png"), 1, ("columns", "cpos,num,logo,badge,name,flag,out,last,gap"));
            Save(standings, standings.W, standings.H, Path.Combine(dir, "standings-logos.png"), 1, ("columns", "cpos,logo,badge,num,name,pit,change,last,int,gap"));
            var standingsThree = Asked(standings, threeLaps);
            Save(standings, standingsThree.Width, standingsThree.Height, Path.Combine(dir, "standings-last3.png"), 1, threeLaps);
            var threeLapsSize = Asked(relative, threeLaps);
            Save(relative, threeLapsSize.Width, threeLapsSize.Height, Path.Combine(dir, "relative-last3.png"), 1, threeLaps);

            SizeF Asked(HudWidgetDef def, (string Key, string Value)[] options)
            {
                var config = new HudWidgetConfig { Id = "asked-" + def.Kind, Kind = def.Kind, W = def.W, H = def.H };
                foreach (var (key, value) in options) config.Options[key] = value;
                using var fit = new HudPainter { Overflow = new() };
                using var surface = new Core.Overlay.DibSurface();
                var g = surface.Ensure(new Size((int)def.W, 2400));
                fit.Simulated = true;
                fit.IsPreview = true;
                fit.Begin(g, 1, new SizeF((float)def.W, 2400), palette, settings.Appearance);
                def.Create().Draw(fit, new HudView { Frame = race.Frame, Race = race, Settings = settings, Config = config, Options = new HudOptions(def, config), Def = def });
                fit.FinishPanels();
                return fit.Needed is { } need ? new SizeF(Math.Max(need.Width, fit.MinWidth), need.Height) : new SizeF((float)def.W, (float)def.H);
            }

            SizeF Fitted(HudWidgetDef def, (string Key, string Value)[] options)
            {
                var config = new HudWidgetConfig { Id = "fit-" + def.Kind, Kind = def.Kind };
                foreach (var (key, value) in options) config.Options[key] = value;
                using var fit = new HudPainter { Overflow = new() };
                using var surface = new Core.Overlay.DibSurface();
                var widget = def.Create();
                bool Clean(float w, float h)
                {
                    var g = surface.Ensure(new Size((int)Math.Ceiling(w), (int)Math.Ceiling(h)));
                    fit.Simulated = true;
                    fit.IsPreview = true;
                    fit.Begin(g, 1, new SizeF(w, h), palette, settings.Appearance);
                    widget.Draw(fit, new HudView { Frame = race.Frame, Race = race, Settings = settings, Config = config, Options = new HudOptions(def, config), Def = def });
                    fit.FinishPanels();
                    return fit.Overflow!.Count == 0 && !fit.Spilled && fit.MinWidth <= w + 0.5f;
                }
                const float Tall = 2400;
                Clean((float)def.W, Tall);
                float width = Math.Max((float)def.W, fit.MinWidth);
                Clean(width, Tall);
                width = Math.Max(width, fit.MinWidth);
                float lo = (float)def.MinH, hi = Tall;
                while (hi - lo > 1)
                {
                    float mid = MathF.Round((lo + hi) / 2);
                    if (mid <= lo || mid >= hi) break;
                    if (Clean(width, mid)) hi = mid; else lo = mid;
                }
                return new SizeF(width, MathF.Ceiling(hi));
            }

            foreach (var kind in new[] { "relative", "standings", "fuel" })
                Save(HudCatalog.Find(kind)!, HudCatalog.Find(kind)!.W, HudCatalog.Find(kind)!.H, Path.Combine(dir, kind + "@150.png"), 1.5f);

            foreach (var kind in new[] { "radar" })
                Save(HudCatalog.Find(kind)!, HudCatalog.Find(kind)!.W, HudCatalog.Find(kind)!.H, Path.Combine(dir, kind + "@250.png"), 2.5f);

            var mixRelative = HudCatalog.Find("relative")!;
            double mixAt = 0.25;
            foreach (double until in new[] { 40.0, 80, 120, 160, 200, 240 })
            {
                for (; mixAt < until; mixAt += 0.25)
                {
                    demo.Fill(frame, mixAt);
                    frame.Sequence = ++seq;
                    race.Update(frame);
                }
                string at = ((int)until).ToString(System.Globalization.CultureInfo.InvariantCulture);
                Save(mixRelative, mixRelative.W, mixRelative.H, Path.Combine(dir, $"mix-{at}-both.png"), 1, ("classRows", "true"), ("classBorder", "true"));
                Save(mixRelative, mixRelative.W, mixRelative.H, Path.Combine(dir, $"mix-{at}-outlines.png"), 1, ("classBorder", "true"));
            }

            var mapDef = HudCatalog.Find("map")!;
            frame.Simulated = false;
            frame.Session = new SimRacing.Telemetry.SessionInfo { TrackName = "unmapped sheet track", TrackDisplayName = "Unmapped track" };
            frame.Sequence = ++seq;
            race.Update(frame);
            Save(mapDef, mapDef.W, mapDef.H, Path.Combine(dir, "map-none.png"));
            Save(mapDef, mapDef.MinW, mapDef.MinH, Path.Combine(dir, "map-none-min.png"));

            void Save(HudWidgetDef def, double w, double h, string path, float scale = 1, params (string Key, string Value)[] options)
            {
                var config = new HudWidgetConfig { Id = "sheet-" + def.Kind, Kind = def.Kind, W = w, H = h };
                foreach (var (key, value) in options) config.Options[key] = value;
                using var surface = new Core.Overlay.DibSurface();
                var g = surface.Ensure(new Size((int)Math.Round(w * scale), (int)Math.Round(h * scale)));
                {
                    painter.Simulated = true;
                    painter.IsPreview = true;
                    painter.Begin(g, scale, new SizeF((float)w, (float)h), palette, settings.Appearance);
                    var view = new HudView { Frame = race.Frame, Race = race, Settings = settings, Config = config, Options = new HudOptions(def, config), Def = def };
                    def.Create().Draw(painter, view);
                    painter.FinishPanels();
                    painter.End(surface.Dc, surface.Size);
                }
                surface.Bitmap!.Save(path);
            }
        }
    }

    private const int TimedFrames = 600, WarmFrames = 60;

    private static void Time(string folder)
    {
        var settings = new RaceHudSettings();
        var race = new RaceState(() => settings) { MapLoader = SimRacing.TrackMaps.TrackMapLibrary.ForRace };
        var demo = new DemoTelemetry();
        var frame = new TelemetryFrame();
        long seq = 0;
        double t = -1500;
        for (; t <= 0; t += 0.25) Step();
        var palette = HudPalette.Of(settings.Appearance);
        var lines = new List<string>();
        var clock = new System.Diagnostics.Stopwatch();
        double totalDraw = 0, totalUpdate = 0;
        long totalBytes = 0;
        using var painter = new HudPainter();
        using var surface = new Core.Overlay.DibSurface();
        foreach (var def in HudCatalog.All)
        {
            var config = new HudWidgetConfig { Id = "time-" + def.Kind, Kind = def.Kind, W = def.W, H = def.H };
            var options = new HudOptions(def, config);
            var widget = def.Create();
            var size = new Size((int)Math.Round(def.W), (int)Math.Round(def.H));
            double draw = 0, update = 0;
            long bytes = 0;
            for (int i = 0; i < WarmFrames + TimedFrames; i++)
            {
                bool timed = i >= WarmFrames;
                long before = GC.GetAllocatedBytesForCurrentThread();
                clock.Restart();
                t += 1.0 / FrameRate;
                Step();
                double updated = clock.Elapsed.TotalMilliseconds;
                var g = surface.Ensure(size);
                painter.Simulated = true;
                painter.Begin(g, 1, new SizeF((float)def.W, (float)def.H), palette, settings.Appearance);
                widget.Paint(painter, new HudView { Frame = race.Frame, Race = race, Settings = settings, Config = config, Options = options, Def = def });
                painter.FinishPanels();
                painter.End(surface.Dc, surface.Size);
                double drawn = clock.Elapsed.TotalMilliseconds - updated;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (!timed) continue;
                draw += drawn;
                update += updated;
                bytes += allocated;
            }
            totalDraw += draw;
            totalUpdate += update;
            totalBytes += bytes;
            lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "hud_{0}_draw_ms {1:0.000}", def.Kind, draw / TimedFrames));
            lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "hud_{0}_alloc_kb {1:0.0}", def.Kind, bytes / 1024.0 / TimedFrames));
        }
        int count = HudCatalog.All.Count();
        lines.Insert(0, string.Format(System.Globalization.CultureInfo.InvariantCulture, "hud_race_update_ms {0:0.000}", totalUpdate / (TimedFrames * count)));
        lines.Insert(1, string.Format(System.Globalization.CultureInfo.InvariantCulture, "hud_draw_mean_ms {0:0.000}", totalDraw / (TimedFrames * count)));
        lines.Insert(2, string.Format(System.Globalization.CultureInfo.InvariantCulture, "hud_alloc_mean_kb {0:0.0}", totalBytes / 1024.0 / (TimedFrames * count)));
        File.WriteAllLines(Path.Combine(folder, "timing.txt"), lines);

        void Step()
        {
            demo.Fill(frame, t);
            frame.Sequence = ++seq;
            race.Update(frame);
        }
    }

    private const int FrameRate = 60;
    private const double ClipSeconds = 12, FrameScale = 2.5, ShownFrom = 1.5, ShownFor = 3.5, Probe = 0.25, SearchFrom = 60, SearchTo = 1500;
    private static readonly HashSet<string> EveryFrame = new(StringComparer.OrdinalIgnoreCase) { "inputs", "radar", "map", "timing", "wind" };
    private static readonly Dictionary<string, double> Speeds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["relative"] = 4, ["standings"] = 12, ["timing"] = 2, ["laps"] = 40, ["fuel"] = 20, ["session"] = 10, ["alerts"] = 3,
        ["traffic"] = 4, ["map"] = 4, ["tyres"] = 4, ["weather"] = 30, ["wind"] = 6, ["notes"] = 8, ["events"] = 30, ["block"] = 40,
    };

    private static void RenderFrames(string folder)
    {
        foreach (var def in HudCatalog.All)
        {
            double speed = Speeds.GetValueOrDefault(def.Kind, 1);
            double start = BusiestStart(def, speed) - ShownFrom * speed;
            var settings = new RaceHudSettings();
            var race = new RaceState(() => settings) { MapLoader = SimRacing.TrackMaps.TrackMapLibrary.ForRace };
            var demo = new DemoTelemetry();
            var frame = new TelemetryFrame();
            long seq = 0;
            for (double t = start - 1500; t < start; t += 0.25) Step(t);
            var palette = HudPalette.Of(settings.Appearance);
            var config = new HudWidgetConfig { Id = "frames-" + def.Kind, Kind = def.Kind, W = def.W, H = def.H };
            var options = new HudOptions(def, config);
            var widget = def.Create();
            using var painter = new HudPainter();
            using var surface = new Core.Overlay.DibSurface();
            var size = new Size((int)Math.Round(def.W * FrameScale), (int)Math.Round(def.H * FrameScale));
            string dir = Path.Combine(folder, def.Kind);
            Directory.CreateDirectory(dir);
            int every = EveryFrame.Contains(def.Kind) ? 1 : speed > 1 ? 2 : FrameRate / 15;
            int substeps = (int)Math.Ceiling(speed / FrameRate / 0.25);
            var saved = new List<string>();
            for (int i = 0; i <= ClipSeconds * FrameRate; i++)
            {
                for (int s = substeps; s >= 1; s--) Step(start + (i - (s - 1) / (double)substeps) * speed / FrameRate);
                var g = surface.Ensure(size);
                painter.Simulated = true;
                painter.Begin(g, (float)FrameScale, new SizeF((float)def.W, (float)def.H), palette, settings.Appearance);
                widget.Paint(painter, new HudView { Frame = race.Frame, Race = race, Settings = settings, Config = config, Options = options, Def = def });
                painter.FinishPanels();
                painter.End(surface.Dc, surface.Size);
                if (i % every != 0) continue;
                string file = $"{saved.Count:0000}.png";
                surface.Bitmap!.Save(Path.Combine(dir, file));
                saved.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{{\"t\":{0:0.000},\"f\":\"{1}\"}}", i / (double)FrameRate, file));
            }
            File.WriteAllText(Path.Combine(dir, "frames.json"), $"{{\"kind\":\"{def.Kind}\",\"scale\":{FrameScale.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"speed\":{speed.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"frames\":[{string.Join(",", saved)}]}}");

            void Step(double t)
            {
                demo.Fill(frame, t);
                frame.Sequence = ++seq;
                race.Update(frame);
            }
        }
    }

    private static double BusiestStart(HudWidgetDef def, double speed)
    {
        var settings = new RaceHudSettings();
        var race = new RaceState(() => settings) { MapLoader = SimRacing.TrackMaps.TrackMapLibrary.ForRace };
        var demo = new DemoTelemetry();
        var frame = new TelemetryFrame();
        long seq = 0;
        double simTime = SearchFrom - 600;
        for (; simTime < SearchFrom; simTime += 0.25) Step(simTime);
        var palette = HudPalette.Of(settings.Appearance);
        var config = new HudWidgetConfig { Id = "busy-" + def.Kind, Kind = def.Kind, W = def.W, H = def.H };
        var options = new HudOptions(def, config);
        var widget = def.Create();
        using var painter = new HudPainter();
        using var surface = new Core.Overlay.DibSurface();
        var size = new Size((int)Math.Round(def.W), (int)Math.Round(def.H));
        var previous = new int[size.Width * size.Height];
        var current = new int[previous.Length];
        var times = new List<double>();
        var changes = new List<double>();
        double step = Probe * speed;
        for (double at = SearchFrom; at < SearchTo; at += step)
        {
            for (; simTime <= at; simTime += 0.25) Step(simTime);
            var g = surface.Ensure(size);
            painter.Simulated = true;
            painter.Begin(g, 1, new SizeF((float)def.W, (float)def.H), palette, settings.Appearance);
            widget.Paint(painter, new HudView { Frame = race.Frame, Race = race, Settings = settings, Config = config, Options = options, Def = def });
            painter.FinishPanels();
            painter.End(surface.Dc, surface.Size);
            var bitmap = surface.Bitmap!;
            var data = bitmap.LockBits(new Rectangle(Point.Empty, size), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            for (int y = 0; y < size.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, current, y * size.Width, size.Width);
            bitmap.UnlockBits(data);
            int differing = 0;
            for (int i = 0; i < current.Length; i++) if (current[i] != previous[i]) differing++;
            times.Add(at);
            changes.Add(Math.Min(0.15, differing / (double)current.Length));
            (previous, current) = (current, previous);
        }
        int window = (int)Math.Round(ShownFor / Probe);
        double best = -1, bestAt = SearchFrom + ShownFrom * speed;
        for (int i = 1; i + window <= changes.Count; i++)
        {
            if (times[i] - ShownFrom * speed < SearchFrom - 590) continue;
            double score = 0;
            for (int j = i; j < i + window; j++) score += changes[j];
            if (score > best) (best, bestAt) = (score, times[i]);
        }
        return bestAt;

        void Step(double t)
        {
            demo.Fill(frame, t);
            frame.Sequence = ++seq;
            race.Update(frame);
        }
    }
}
