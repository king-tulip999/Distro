using System.Drawing;
using System.Drawing.Drawing2D;
using Units = Core.Services.CapsUnits;
using Core.SimRacing.FuelEngine;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class TimingWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("look", "Look", "trace",
            new HudChoice("trace", "Delta trace", "The delta over the lap with your last lap's line and each sector's delta"),
            new HudChoice("strip", "Delta strip", "A short, wide delta graph"),
            new HudChoice("full", "Times and sectors", "Delta, times and sectors"),
            new HudChoice("compact", "Compact", "The delta, a bar and what it's measured to"),
            new HudChoice("column", "Sector column", "One row per sector"),
            new HudChoice("board", "Sector board", "Sectors side by side"),
            new HudChoice("ribbon", "Lap ribbon", "The lap as a bar with the delta at your car and lap times under it"),
            new HudChoice("thin", "Thin ribbon", "The lap as a thin bar with the delta at your car"),
            new HudChoice("pill", "Delta pill", "The delta on its own, tinted green or red")),
        HudOption.Choice("pill", "Beside the delta", "trend", new HudChoice("trend", "Recent trend"), new HudChoice("predicted", "Predicted lap"), new HudChoice("none", "Nothing")),
        HudOption.Choice("reference", "Delta to", "optimal", new HudChoice("best", "My best lap"), new HudChoice("session", "Session best"), new HudChoice("optimal", "My optimal lap", "Best sectors combined"),
            new HudChoice("lastlap", "My last lap"), new HudChoice("sessionOptimal", "Session optimal lap", "The best sectors of the session combined")),
        HudOption.Bool("bar", "Delta bar", true),
        HudOption.Number("range", "Bar range", 1.0, 0.2, 5, 0.1, Units.S),
        HudOption.Columns("fields", "Times", "last,current,best,predicted",
            new HudChoice("current", "Current lap"), new HudChoice("last", "Last lap"), new HudChoice("best", "My best"), new HudChoice("predicted", "Predicted lap"),
            new HudChoice("sessionBest", "Session best (all)"), new HudChoice("classBest", "Best in my class"), new HudChoice("theoretical", "Optimal lap (best sectors)"),
            new HudChoice("consistency", "Consistency", "Half the spread of your last 5 clean laps")),
        HudOption.Bool("sectors", "Sectors", true),
        HudOption.Choice("split", "Sector split", "track", new HudChoice("track", "The track's sectors"), new HudChoice("6", "6 equal sectors"),
            new HudChoice("8", "8 equal sectors"), new HudChoice("10", "10 equal sectors"), new HudChoice("12", "12 equal sectors"))
            with { Help = "Equal sectors time every widget's sectors and the optimal lap from then on (the most any Lap timing widget asks for)" },
    };

    private enum SectorState : byte { Ahead, Running, Done }

    private struct SectorView
    {
        public SectorState State;
        public double? Time, Delta, Reference;
        public Color Fill;
        public float Start, End;
    }

    private readonly GraphicsPath _line = new(), _area = new();
    private SectorView[] _sectors = new SectorView[16];
    private int _sectorCount;
    private double?[] _best = Array.Empty<double?>(), _reference = Array.Empty<double?>();
    private IReadOnlyList<double?>? _previous;
    private (int Laps, int Sectors, string Reference, int LastLap) _sectorKey = (-1, -1, "", -1);

    public override void Dispose()
    {
        _line.Dispose();
        _area.Dispose();
        base.Dispose();
    }

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Lap timing")) return;
        var f = v.Frame;
        p.Panel();
        var b = p.Bounds;
        float pad = 8 * p.Density;
        var area = new RectangleF(pad, pad * 0.7f, b.Width - pad * 2, b.Height - pad * 1.4f);
        string reference = v.Options.Choice("reference");
        var (delta, ok, refLap) = reference switch
        {
            "session" => (f.DeltaSessionBest, f.DeltaSessionBestOk, FieldOrder.FastestLap(f)?.Time),
            "optimal" => (f.DeltaOptimal, f.DeltaOptimalOk, LapMath.Theoretical(LapMath.BestSectors(v.Race.Laps.Laps, v.Race.Laps.SectorCount))),
            "lastlap" => (f.RawValue("LapDeltaToSessionLastlLap"), f.RawValue("LapDeltaToSessionLastlLap_OK") is > 0, f.LapLast),
            "sessionOptimal" => (f.RawValue("LapDeltaToSessionOptimalLap"), f.RawValue("LapDeltaToSessionOptimalLap_OK") is > 0, null),
            _ => (f.DeltaBest, f.DeltaBestOk, f.LapBest),
        };
        double? d = ok ? delta : null;
        var color = DeltaColor(p, d);
        string refLabel = reference switch { "session" => "Δ SESSION BEST", "optimal" => "Δ OPTIMAL", "lastlap" => "Δ LAST LAP", "sessionOptimal" => "Δ SESSION OPTIMAL", _ => "Δ MY BEST" };
        switch (v.Options.Choice("look"))
        {
            case "compact":
                Compact(p, v, d, color, refLabel[2..]);
                return;
            case "trace":
                Trace(p, v, area, d, color, refLabel, refLap, reference, true);
                return;
            case "strip":
                Trace(p, v, area, d, color, refLabel, refLap, reference, false);
                return;
            case "column":
                Column(p, v, area, d, color, refLap, reference);
                return;
            case "board":
                Board(p, v, area, d, color, refLap, reference);
                return;
            case "ribbon":
                Ribbon(p, v, area, d, color, refLap, reference, false);
                return;
            case "thin":
                Ribbon(p, v, area, d, color, refLap, reference, true);
                return;
            case "pill":
                Pill(p, v, d, color, refLabel, refLap, reference);
                return;
        }
        var fields = v.Options.Columns("fields");
        bool bar = v.Options.Bool("bar");
        bool sectors = v.Options.Bool("sectors") && v.Race.Laps.SectorCount > 1;
        float barH = bar ? 7 * p.Density + 6 : 0, sectorH = sectors ? p.LineHeight(p.Small) + 10 : 0;
        bool wide = area.Width >= 300 * p.Density && fields.Count > 0;
        float topH = Math.Max(p.LineHeight(p.Big) + p.LineHeight(p.Label) + 10, area.Height - barH - sectorH - (wide ? 0 : p.BoxedStatHeight() + 4));
        var top = area.Top(Math.Min(topH, area.Height), out var rest);
        var deltaBlock = wide ? top.Left(top.Width * 0.42f, out var times) : top.Top(Math.Min(top.Height, p.LineHeight(p.Huge) + p.LineHeight(p.Label) + 10), out times);
        if (!wide) times = rest.Top(Math.Min(rest.Height, p.BoxedStatHeight()), out rest);
        p.Region("delta", deltaBlock);
        var tint = d is null ? p.Raised(0.05) : HudPalette.Mix(p.Raised(0.05), d < 0 ? p.P.Good : p.P.Danger, 0.22);
        if (d is null) p.Fill(deltaBlock, p.Solid(tint, 130));
        else
        {
            var state = d < 0 ? p.P.Good : p.P.Danger;
            p.Soft(deltaBlock, state, 75, false);
            p.Outline(deltaBlock, HudPalette.Alpha(state, 115));
        }
        var inner = deltaBlock.Inflated(-8 * p.Density, -4 * p.Density);
        var label = inner.Top(p.LineHeight(p.Label) + 1, out var number);
        p.Text(p.LabelText(refLabel), p.Label, p.LabelColor, label, HAlign.Left, VAlign.Top);
        var deltaFont = number.Height >= p.LineHeight(p.Huge) * 0.9f ? p.Huge : p.Big;
        p.Text(d is null ? RaceFormat.Missing : RaceFormat.Delta(d), deltaFont, color, number, HAlign.Left, VAlign.Center, false);
        float gap = 4 * p.Density, cellH = p.BoxedStatHeight();
        times = RectangleF.FromLTRB(times.Left + (wide ? 8 * p.Density : 0), times.Top, times.Right + gap, times.Bottom);
        int cols = wide ? 2 : Math.Max(1, (int)(times.Width / (74 * p.Density)));
        int rowsN = wide ? Math.Max(1, (int)((times.Height + gap) / (cellH + gap))) : 1;
        int fit = times.Height < cellH - 1 ? 0 : Math.Min(fields.Count, cols * rowsN);
        int rowsUsed = Math.Max(1, (fit + cols - 1) / cols);
        float cw = times.Width / Math.Max(1, Math.Min(cols, fit)), ch = wide ? (times.Height + gap) / rowsUsed : times.Height + gap;
        for (int i = 0; i < fit; i++)
        {
            var box = new RectangleF(times.X + i % cols * cw, times.Y + i / cols * ch, cw - gap, ch - gap);
            var (name, value, valueColor) = fields[i] switch
            {
                "current" => ("LAP", RaceFormat.LapTime(f.LapCurrent, 1), (Color?)null),
                "last" => ("LAST", RaceFormat.LapTime(f.LapLast), LastColor(p, v)),
                "best" => ("BEST", RaceFormat.LapTime(f.LapBest), null),
                "predicted" => ("PREDICTED", RaceFormat.LapTime(Predicted(d, refLap)), (Color?)null),
                "sessionBest" => ("SESSION", RaceFormat.LapTime(FieldColumns.Fastest(f, null)?.Time), p.P.Fastest),
                "classBest" => (p.LabelText(f.Session.Player?.ClassLabel is { Length: > 0 } pl ? pl : "CLASS"), RaceFormat.LapTime(FieldColumns.Fastest(f, f.Session.Player?.ClassId)?.Time), p.P.Fastest),
                "theoretical" => ("OPTIMAL", RaceFormat.LapTime(LapMath.Theoretical(LapMath.BestSectors(v.Race.Laps.Laps, v.Race.Laps.SectorCount))), (Color?)null),
                "consistency" => ("CONSISTENCY", RaceFormat.Spread(LapMath.Stats(v.Race.Laps.Laps, 5) is { Count: >= 3 } cs ? cs.Spread : null), (Color?)null),
                _ => ("", "", null),
            };
            if (name.Length > 0) p.BoxedStat(box, name, value, valueColor);
            p.Item("fields", fields[i], box);
        }
        for (int i = fit; i < fields.Count; i++) p.Overflow?.Add(("fields", fields[i]));
        if (bar && rest.Height >= 7 * p.Density + 4)
        {
            var barBox = rest.Top(barH, out rest);
            p.Region("bar", barBox);
            var track = new RectangleF(barBox.X, barBox.Y + 4, barBox.Width, 7 * p.Density);
            p.Fill(track, p.Solid(p.Raised(0.10), 130), 3);
            float mid = track.X + track.Width / 2;
            if (d is { } dv)
            {
                float len = (float)(Math.Clamp(dv / v.Options.Number("range"), -1, 1) * track.Width / 2);
                var fill = len < 0 ? new RectangleF(mid + len, track.Y, -len, track.Height) : new RectangleF(mid, track.Y, len, track.Height);
                p.Fill(fill, d < 0 ? p.P.Good : p.P.Danger, 3);
            }
            p.Line(mid, track.Y - 2, mid, track.Bottom + 2, p.P.Heading, 1.5f);
        }
        if (sectors && rest.Height > p.LineHeight(p.Small) + 6)
        {
            p.Region("sectors", rest);
            Sectors(p, v, rest);
        }
    }

    private static double? Predicted(double? d, double? refLap) => d is { } dd && refLap is { } rl ? rl + dd : null;

    private static Color DeltaColor(HudPainter p, double? d) => d is not { } v || Math.Abs(v) < 0.0005 ? p.P.Muted : v < 0 ? p.P.GoodText : p.P.DangerText;

    private static string SectorReferenceLabel(string reference) => reference switch
    {
        "best" => "Δ my best",
        "lastlap" => "Δ last lap",
        "optimal" => "Δ optimal",
        _ => "Δ best sectors",
    };

    private void FillSectors(HudPainter p, HudView v, string reference, double? d, DeltaTrace trace)
    {
        var tracker = v.Race.Laps;
        var laps = tracker.Laps;
        int n = tracker.SectorCount;
        var key = (laps.Count, n, reference, laps.Count > 0 ? laps[^1].Lap : -1);
        if (key != _sectorKey)
        {
            _sectorKey = key;
            _best = LapMath.BestSectors(laps, n);
            _previous = laps.Count > 0 ? laps[^1].Sectors : null;
            _reference = reference switch
            {
                "best" => SectorsOf(BestLap(laps), n),
                "lastlap" => SectorsOf(laps.Count > 0 ? laps[^1] : null, n),
                _ => _best,
            };
        }
        if (_sectors.Length < n) _sectors = new SectorView[n];
        _sectorCount = n;
        var starts = tracker.SectorStarts;
        var current = tracker.CurrentSectors;
        var f = v.Frame;
        for (int i = 0; i < n; i++)
        {
            ref var s = ref _sectors[i];
            s.Start = (float)starts[i];
            s.End = i + 1 < n ? (float)starts[i + 1] : 1f;
            s.Reference = i < _reference.Length ? _reference[i] : null;
            if (i < current.Count && current[i] is { } time)
            {
                s.State = SectorState.Done;
                s.Time = time;
                s.Delta = s.Reference is { } r ? time - r : null;
                bool personalBest = _best[i] is null || time <= _best[i]!.Value + 0.0005;
                bool faster = _previous is not null && i < _previous.Count && _previous[i] is { } before && time < before;
                s.Fill = personalBest ? p.P.Fastest : faster ? p.P.Good : p.P.Warn;
            }
            else if (i == tracker.CurrentSector)
            {
                s.State = SectorState.Running;
                s.Time = f.LapCurrent is { } lapTime && lapTime >= tracker.SectorStartTime ? lapTime - tracker.SectorStartTime : null;
                s.Delta = d is { } now ? i == 0 ? now : trace.At(s.Start) is { } atStart ? now - atStart : null : null;
                s.Fill = p.P.Accent;
            }
            else
            {
                s.State = SectorState.Ahead;
                s.Time = null;
                s.Delta = null;
                s.Fill = Color.Empty;
            }
        }
    }

    private static LapRecord? BestLap(IReadOnlyList<LapRecord> laps)
    {
        LapRecord? best = null;
        foreach (var lap in laps)
            if (lap.Clean && !lap.PitLap && (best is null || lap.Time < best.Time)) best = lap;
        return best;
    }

    private static double?[] SectorsOf(LapRecord? lap, int n)
    {
        var sectors = new double?[n];
        if (lap is not null)
            for (int i = 0; i < Math.Min(n, lap.Sectors.Count); i++) sectors[i] = lap.Sectors[i];
        return sectors;
    }

    private void Trace(HudPainter p, HudView v, RectangleF area, double? d, Color color, string refLabel, double? refLap, string reference, bool full)
    {
        float k = p.Density;
        var trace = v.Race.Deltas.For(reference);
        FillSectors(p, v, reference, d, trace);
        if (!full)
        {
            float sideW = Math.Max(p.Measure("+0.000", p.Big), p.Measure(p.LabelText(refLabel), p.Label)) + 6 * k;
            p.Need(sideW + 90 * k + area.X * 2, p.LineHeight(p.Big) + p.LineHeight(p.Label) + 2 + area.Y * 2);
            var side = area.Left(sideW, out var graphArea);
            p.Region("delta", side);
            float block = p.LineHeight(p.Label) + 1 + p.LineHeight(p.Big);
            var stack = new RectangleF(side.X, side.Y + Math.Max(0, (side.Height - block) / 2), side.Width, block);
            var labelBox = stack.Top(p.LineHeight(p.Label) + 1, out var numberBox);
            p.Text(p.LabelText(refLabel), p.Label, p.LabelColor, labelBox, HAlign.Left, VAlign.Top);
            p.Text(RaceFormat.Delta(d), p.Big, color, numberBox, HAlign.Left, VAlign.Center, false);
            graphArea = RectangleF.FromLTRB(graphArea.Left + 4 * k, graphArea.Top, graphArea.Right, graphArea.Bottom);
            p.Region("graph", graphArea);
            Graph(p, v, graphArea, trace, d, false);
            return;
        }
        float headH = p.LineHeight(p.Big), footH = p.ValueStatHeight(p.Bold);
        p.Need(220 * k, headH + 40 * k + area.Y * 2);
        var head = area.Top(headH, out var rest);
        p.Region("delta", head);
        p.Text(p.LabelText(refLabel), p.Label, p.LabelColor, head, HAlign.Left, VAlign.Center);
        p.Text(RaceFormat.Delta(d), p.Big, color, head, HAlign.Right, VAlign.Center, false);
        bool foot = rest.Height >= footH + 4 * k + 46 * k;
        var graph = rest;
        var footer = RectangleF.Empty;
        if (foot)
        {
            graph = rest.Top(rest.Height - footH - 4 * k, out footer);
            footer = RectangleF.FromLTRB(footer.Left, footer.Top + 4 * k, footer.Right, footer.Bottom);
        }
        graph = RectangleF.FromLTRB(graph.Left, graph.Top + 3 * k, graph.Right, graph.Bottom);
        p.Region("graph", graph);
        Graph(p, v, graph, trace, d, true);
        if (!foot) return;
        float w = footer.Width / 3;
        p.ValueStat(new RectangleF(footer.X, footer.Y, w, footer.Height), "Predicted", RaceFormat.LapTime(Predicted(d, refLap)), null, p.Bold);
        p.ValueStat(new RectangleF(footer.X + w, footer.Y, w, footer.Height), "Lap", RaceFormat.LapTime(v.Frame.LapCurrent, 1), null, p.Bold, HAlign.Center);
        p.ValueStat(new RectangleF(footer.X + w * 2, footer.Y, w, footer.Height), "Reference", RaceFormat.LapTime(refLap), null, p.Bold, HAlign.Right);
    }

    private void Graph(HudPainter p, HudView v, RectangleF box, DeltaTrace trace, double? d, bool labels)
    {
        float k = p.Density;
        p.Fill(box, p.Solid(p.Raised(0.05), 130), p.CornerRadius(4));
        var labelFont = p.Font(10f, true);
        labels = labels && box.Height >= p.LineHeight(labelFont) + 30 * k;
        float labelH = labels ? p.LineHeight(labelFont) + 2 * k : 0, inset = box.Height >= 24 * k ? 3 * k : 1;
        var plot = RectangleF.FromLTRB(box.Left + 2 * k, box.Top + labelH + inset, box.Right - 2 * k, box.Bottom - inset);
        if (plot.Width < 10 || plot.Height < 4) return;
        float most = 0.25f;
        for (int i = 0; i <= trace.Filled; i++)
            if (trace.Current(i) is var c && float.IsFinite(c)) most = Math.Max(most, Math.Abs(c));
        for (int i = 0; i <= trace.PreviousFilled; i++)
            if (trace.Previous(i) is var c && float.IsFinite(c)) most = Math.Max(most, Math.Abs(c));
        if (d is { } now) most = Math.Max(most, (float)Math.Abs(now));
        most = Math.Min(10, MathF.Ceiling(most * 4) / 4);
        float mid = p.Snap(plot.Y + plot.Height / 2), half = plot.Height / 2;
        var edge = HudPalette.Alpha(p.P.LineStrong, 220);
        for (int i = 1; i < _sectorCount; i++)
        {
            float x = plot.X + _sectors[i].Start * plot.Width;
            p.Line(x, box.Top + 2 * k, x, box.Bottom - 2 * k, edge, 1);
        }
        if (labels)
        {
            for (int i = 0; i < _sectorCount; i++)
            {
                ref var s = ref _sectors[i];
                if (s.State == SectorState.Ahead || s.Delta is not { } sd) continue;
                float x0 = plot.X + s.Start * plot.Width, x1 = plot.X + s.End * plot.Width;
                string text = RaceFormat.Gap(sd, 2);
                if (p.Measure(text, labelFont) > x1 - x0 - 4 * k) continue;
                var textColor = s.State == SectorState.Running ? p.P.AccentText : sd < 0 ? p.P.GoodText : p.P.DangerText;
                p.Text(text, labelFont, textColor, new RectangleF(x0, box.Top + 2 * k, x1 - x0, labelH), HAlign.Center, VAlign.Center, false);
            }
        }
        p.Line(plot.X, mid, plot.Right, mid, HudPalette.Alpha(p.P.Heading, 110), 1);
        if (labels && plot.Height >= 40 * k)
        {
            p.Text(RaceFormat.Gap(most, 2), p.Tiny, p.P.Faint, new RectangleF(plot.X, plot.Y, plot.Width - 2 * k, p.LineHeight(p.Tiny)), HAlign.Right, VAlign.Top, false);
            p.Text(RaceFormat.Gap(-most, 2), p.Tiny, p.P.Faint, new RectangleF(plot.X, plot.Bottom - p.LineHeight(p.Tiny), plot.Width - 2 * k, p.LineHeight(p.Tiny)), HAlign.Right, VAlign.Top, false);
        }
        var g = p.G;
        if (trace.PreviousFilled > 0)
        {
            Build(trace, true, plot, mid, half, most);
            g.DrawPath(p.Pen(HudPalette.Alpha(p.P.Faint, 150), 1.5f * k), _line);
        }
        Build(trace, false, plot, mid, half, most);
        var state = g.Save();
        g.SetClip(RectangleF.FromLTRB(plot.Left - 2, box.Top, plot.Right + 2, mid));
        g.FillPath(p.Brush(HudPalette.Alpha(p.P.Danger, 60)), _area);
        g.DrawPath(p.Pen(p.P.Danger, 1.75f * k), _line);
        g.Restore(state);
        state = g.Save();
        g.SetClip(RectangleF.FromLTRB(plot.Left - 2, mid, plot.Right + 2, box.Bottom));
        g.FillPath(p.Brush(HudPalette.Alpha(p.P.Good, 60)), _area);
        g.DrawPath(p.Pen(p.P.Good, 1.75f * k), _line);
        g.Restore(state);
        if (d is { } at && v.Frame.LapDistPct is { } pct)
            p.Dot(plot.X + (float)Math.Clamp(pct, 0, 1) * plot.Width, mid - (float)Math.Clamp(at / most, -1, 1) * half, 3 * k, at < 0 ? p.P.Good : p.P.Danger);
    }

    private void Build(DeltaTrace trace, bool previous, RectangleF plot, float mid, float half, float most)
    {
        _line.Reset();
        _area.Reset();
        int last = previous ? trace.PreviousFilled : trace.Filled;
        bool open = false;
        float px = 0, py = 0;
        for (int i = 0; i <= last; i++)
        {
            float value = previous ? trace.Previous(i) : trace.Current(i);
            if (!float.IsFinite(value))
            {
                if (open) { _area.AddLine(px, py, px, mid); _area.CloseFigure(); }
                open = false;
                continue;
            }
            float x = plot.X + (i + 0.5f) / DeltaTrace.Bins * plot.Width, y = mid - Math.Clamp(value / most, -1, 1) * half;
            if (!open)
            {
                _line.StartFigure();
                _area.StartFigure();
                _area.AddLine(x, mid, x, y);
                open = true;
            }
            else
            {
                _line.AddLine(px, py, x, y);
                _area.AddLine(px, py, x, y);
            }
            px = x;
            py = y;
        }
        if (open) { _area.AddLine(px, py, px, mid); _area.CloseFigure(); }
    }

    private void Column(HudPainter p, HudView v, RectangleF area, double? d, Color color, double? refLap, string reference)
    {
        float k = p.Density;
        FillSectors(p, v, reference, d, v.Race.Deltas.For(reference));
        int n = _sectorCount, rows = n + 2;
        float minRow = p.LineHeight(p.SmallBold) + 4 * k;
        float nameW = Math.Max(p.Measure("S12", p.SmallBold), p.Measure(p.LabelText("Lap"), p.Label)) + 8 * k;
        float stripeW = 3 * k;
        p.Need(stripeW + nameW + 150 * k + area.X * 2, rows * minRow + area.Y * 2);
        float rowH = Math.Max(minRow, Math.Min(30 * k, area.Height / rows));
        var valueFont = rowH >= p.LineHeight(p.Bold) + 8 * k ? p.Bold : p.SmallBold;
        int decimals = n > 6 ? 2 : 3;
        float deltaW = p.Measure("+0.000", valueFont) + 4 * k;
        float textX = area.X + stripeW + 6 * k;
        float y = area.Y + Math.Max(0, (area.Height - rowH * rows) / 2);
        p.Text(p.LabelText("Time"), p.Label, p.LabelColor, new RectangleF(textX + nameW, y, area.Width, rowH), HAlign.Left, VAlign.Center);
        p.Text(p.LabelText(SectorReferenceLabel(reference)), p.Label, p.LabelColor, new RectangleF(area.X, y, area.Width, rowH), HAlign.Right, VAlign.Center);
        y += rowH;
        p.Region("split", new RectangleF(area.X, y, area.Width, rowH * n));
        var rule = HudPalette.Alpha(p.P.Line, 220);
        for (int i = 0; i < n; i++, y += rowH)
        {
            ref var s = ref _sectors[i];
            p.Line(area.X, y, area.Right, y, rule, 1);
            p.Fill(new RectangleF(area.X, y + rowH * 0.2f, stripeW, rowH * 0.6f), s.State == SectorState.Ahead ? p.Raised(0.10) : s.Fill, 1);
            p.Text(PooledText.Number("S", i + 1), p.SmallBold, s.State == SectorState.Running ? p.P.AccentText : p.P.Muted, new RectangleF(textX, y, nameW, rowH), HAlign.Left, VAlign.Center, false);
            var timeBox = RectangleF.FromLTRB(textX + nameW, y, area.Right - deltaW, y + rowH);
            var deltaBox = RectangleF.FromLTRB(area.Right - deltaW, y, area.Right, y + rowH);
            switch (s.State)
            {
                case SectorState.Done:
                    p.Text(RaceFormat.LapTime(s.Time, decimals), valueFont, p.P.Heading, timeBox, HAlign.Left, VAlign.Center, false);
                    p.Text(RaceFormat.Gap(s.Delta, decimals), valueFont, DeltaColor(p, s.Delta), deltaBox, HAlign.Right, VAlign.Center, false);
                    break;
                case SectorState.Running:
                    p.Text(RaceFormat.LapTime(s.Time, 1), valueFont, p.P.Heading, timeBox, HAlign.Left, VAlign.Center, false);
                    p.Text(RaceFormat.Gap(s.Delta, decimals), valueFont, DeltaColor(p, s.Delta), deltaBox, HAlign.Right, VAlign.Center, false);
                    break;
                default:
                    p.Text(RaceFormat.LapTime(s.Reference, decimals), ReferenceEquals(valueFont, p.Bold) ? p.Body : p.Small, p.P.Faint, timeBox, HAlign.Left, VAlign.Center, false);
                    p.Text(RaceFormat.Missing, valueFont, p.P.Faint, deltaBox, HAlign.Right, VAlign.Center, false);
                    break;
            }
        }
        p.Line(area.X, y, area.Right, y, rule, 1);
        var lapRow = new RectangleF(area.X, y, area.Width, rowH);
        p.Region("delta", lapRow);
        p.Text(p.LabelText("Lap"), p.Label, p.LabelColor, new RectangleF(textX, y, nameW, rowH), HAlign.Left, VAlign.Center, false);
        p.Text(RaceFormat.LapTime(Predicted(d, refLap)), valueFont, p.P.Heading, RectangleF.FromLTRB(textX + nameW, y, area.Right - deltaW, y + rowH), HAlign.Left, VAlign.Center, false);
        p.Text(RaceFormat.Delta(d), valueFont, color, RectangleF.FromLTRB(area.Right - deltaW, y, area.Right, y + rowH), HAlign.Right, VAlign.Center, false);
    }

    private void Board(HudPainter p, HudView v, RectangleF area, double? d, Color color, double? refLap, string reference)
    {
        float k = p.Density;
        FillSectors(p, v, reference, d, v.Race.Deltas.For(reference));
        int n = _sectorCount, lapSpan = n > 6 ? 2 : 1, cells = n + lapSpan;
        float gap = 4 * k;
        float cellH = p.LineHeight(p.Label) + p.LineHeight(p.Bold) + p.LineHeight(p.SmallBold) + 9 * k;
        p.Need(cells * (p.Measure("00.0", p.SmallBold) + 6 * k + gap) + area.X * 2, cellH + area.Y * 2);
        float cw = (area.Width - gap * (cells - 1)) / cells;
        var timeFont = cw >= p.Measure("00.000", p.Bold) + 8 * k ? p.Bold : p.SmallBold;
        int decimals = cw >= p.Measure("00.000", timeFont) + 6 * k ? 3 : cw >= p.Measure("00.00", timeFont) + 6 * k ? 2 : 1;
        int deltaDecimals = cw >= p.Measure("+0.000", p.SmallBold) + 6 * k ? 3 : cw >= p.Measure("+0.00", p.SmallBold) + 4 * k ? 2 : 1;
        p.Region("split", new RectangleF(area.X, area.Y, (cw + gap) * n - gap, area.Height));
        for (int i = 0; i < n; i++)
        {
            ref var s = ref _sectors[i];
            var cell = new RectangleF(area.X + i * (cw + gap), area.Y, cw, area.Height);
            string name = PooledText.Number("S", i + 1);
            switch (s.State)
            {
                case SectorState.Done:
                    Cell(p, cell, name, p.LabelColor, RaceFormat.LapTime(s.Time, decimals), timeFont, p.P.Heading, RaceFormat.Gap(s.Delta, deltaDecimals), DeltaColor(p, s.Delta), s.Fill);
                    break;
                case SectorState.Running:
                    Cell(p, cell, name, p.P.AccentText, RaceFormat.LapTime(s.Time, 1), timeFont, p.P.Heading, RaceFormat.Gap(s.Delta, deltaDecimals), DeltaColor(p, s.Delta), s.Fill);
                    break;
                default:
                    Cell(p, cell, name, p.LabelColor, RaceFormat.LapTime(s.Reference, decimals), timeFont, p.P.Faint, RaceFormat.Missing, p.P.Faint, null);
                    break;
            }
        }
        var lapCell = new RectangleF(area.X + n * (cw + gap), area.Y, cw * lapSpan + gap * (lapSpan - 1), area.Height);
        p.Region("delta", lapCell);
        Cell(p, lapCell, p.LabelText("Lap"), p.LabelColor, RaceFormat.LapTime(Predicted(d, refLap)), lapSpan > 1 || decimals == 3 ? timeFont : p.SmallBold, p.P.Heading, RaceFormat.Delta(d), color, null);
    }

    private static void Cell(HudPainter p, RectangleF cell, string label, Color labelColor, string time, Font timeFont, Color timeColor, string delta, Color deltaColor, Color? stripe)
    {
        float k = p.Density;
        p.Box(cell);
        float lh = p.LineHeight(p.Label), th = p.LineHeight(timeFont), dh = p.LineHeight(p.SmallBold);
        float y = cell.Y + Math.Max(2 * k, (cell.Height - lh - th - dh - 3 * k) / 2);
        p.Text(label, p.Label, labelColor, new RectangleF(cell.X, y, cell.Width, lh), HAlign.Center, VAlign.Center, false);
        y += lh;
        p.Text(time, timeFont, timeColor, new RectangleF(cell.X, y, cell.Width, th), HAlign.Center, VAlign.Center, false);
        y += th;
        p.Text(delta, p.SmallBold, deltaColor, new RectangleF(cell.X, y, cell.Width, dh), HAlign.Center, VAlign.Center, false);
        if (stripe is { } c) p.Fill(new RectangleF(cell.X + 1, cell.Bottom - 3 * k - 1, cell.Width - 2, 3 * k), c);
    }

    private void Ribbon(HudPainter p, HudView v, RectangleF area, double? d, Color color, double? refLap, string reference, bool thin)
    {
        float k = p.Density;
        FillSectors(p, v, reference, d, v.Race.Deltas.For(reference));
        var f = v.Frame;
        var badgeFont = thin ? p.SmallBold : p.Bold;
        float badgeH = p.LineHeight(badgeFont) + 2 * k, rise = 5 * k;
        float trackH = thin ? 8 * k : Math.Max(p.LineHeight(p.SmallBold) + 8 * k, 24 * k);
        float statsH = thin ? 0 : p.ValueStatHeight(p.Bold);
        var bar = area;
        float endW = 0;
        if (thin)
        {
            endW = Math.Max(p.Measure("0:00.000", p.Bold), p.Measure(p.LabelText("Predicted"), p.Label)) + 6 * k;
            p.Need(endW + 140 * k + area.X * 2, Math.Max(badgeH + rise + trackH + 3 * k, p.ValueStatHeight(p.Bold)) + area.Y * 2);
            bar = RectangleF.FromLTRB(area.Left, area.Top, area.Right - endW - 10 * k, area.Bottom);
        }
        else p.Need(280 * k + area.X * 2, badgeH + rise + trackH + 6 * k + statsH + area.Y * 2);
        float block = thin ? Math.Max(badgeH + rise + trackH + 3 * k, p.ValueStatHeight(p.Bold)) : badgeH + rise + trackH + 6 * k + statsH;
        float top = area.Y + Math.Max(0, (area.Height - block) / 2);
        float trackY = thin ? top + block - trackH - 3 * k : top + badgeH + rise;
        var track = new RectangleF(bar.X, trackY, bar.Width, trackH);
        p.Region("split", track);
        p.Fill(track, p.Solid(p.Raised(0.10), 130), p.CornerRadius(4));
        float w = track.Width;
        float car = f.LapDistPct is { } at ? (float)Math.Clamp(at, 0, 1) : 0;
        float cx = track.X + car * w;
        for (int i = 0; i < _sectorCount; i++)
        {
            ref var s = ref _sectors[i];
            var seg = RectangleF.FromLTRB(track.X + s.Start * w, track.Y, track.X + s.End * w, track.Bottom);
            string text = "";
            var textColor = p.P.Faint;
            if (s.State == SectorState.Done)
            {
                p.Fill(seg, s.Fill);
                textColor = HudPalette.On(s.Fill);
                if (!thin)
                {
                    text = RaceFormat.LapTime(s.Time, 2);
                    if (p.Measure(text, p.SmallBold) + 6 * k > seg.Width) text = RaceFormat.LapTime(s.Time, 1);
                }
            }
            else
            {
                if (s.State == SectorState.Running && cx > seg.Left)
                    p.Fill(RectangleF.FromLTRB(seg.Left, seg.Top, Math.Min(cx, seg.Right), seg.Bottom), HudPalette.Alpha(d is < 0 ? p.P.Good : p.P.Danger, 115));
                if (!thin) text = PooledText.Number("S", i + 1);
                if (s.State == SectorState.Running) textColor = p.P.AccentText;
            }
            if (text.Length > 0 && p.Measure(text, p.SmallBold) + 6 * k <= seg.Width) p.Text(text, p.SmallBold, textColor, seg, HAlign.Center, VAlign.Center, false);
        }
        var cut = p.Solid(p.PanelColor);
        for (int i = 1; i < _sectorCount; i++)
        {
            float x = track.X + _sectors[i].Start * w;
            p.Line(x, track.Y, x, track.Bottom, cut, 2 * k);
        }
        p.Line(cx, track.Y - 3 * k, cx, track.Bottom + 3 * k, p.P.Heading, 2 * k);
        string deltaText = RaceFormat.Delta(d);
        float bw = p.Measure(deltaText, badgeFont) + 12 * k;
        float bx = Math.Clamp(cx - bw / 2, bar.X, Math.Max(bar.X, bar.Right - bw));
        var badge = new RectangleF(bx, track.Y - rise - badgeH, bw, badgeH);
        p.Region("delta", badge);
        p.Fill(badge, d is null ? p.Raised(0.10) : d < 0 ? p.P.GoodSoft : p.P.DangerSoft, p.CornerRadius(4));
        p.Text(deltaText, badgeFont, color, badge, HAlign.Center, VAlign.Center, false);
        if (thin)
        {
            float vh = p.ValueStatHeight(p.Bold);
            p.ValueStat(new RectangleF(area.Right - endW, top + (block - vh) / 2, endW, vh), "Predicted", RaceFormat.LapTime(Predicted(d, refLap)), null, p.Bold, HAlign.Right);
            return;
        }
        var stats = new RectangleF(area.X, track.Bottom + 6 * k, area.Width, statsH);
        float cw = stats.Width / 4;
        p.ValueStat(new RectangleF(stats.X, stats.Y, cw, statsH), "Lap", RaceFormat.LapTime(f.LapCurrent, 1), null, p.Bold);
        p.ValueStat(new RectangleF(stats.X + cw, stats.Y, cw, statsH), "Predicted", RaceFormat.LapTime(Predicted(d, refLap)), null, p.Bold, HAlign.Center);
        p.ValueStat(new RectangleF(stats.X + cw * 2, stats.Y, cw, statsH), "Best", RaceFormat.LapTime(f.LapBest), null, p.Bold, HAlign.Center);
        p.ValueStat(new RectangleF(stats.X + cw * 3, stats.Y, cw, statsH), "Last", RaceFormat.LapTime(f.LapLast), LastColor(p, v), p.Bold, HAlign.Right);
    }

    private void Pill(HudPainter p, HudView v, double? d, Color color, string refLabel, double? refLap, string reference)
    {
        float k = p.Density;
        var b = p.Bounds;
        if (d is { } now) p.Fill(b, HudPalette.Alpha(now < 0 ? p.P.GoodSoft : p.P.DangerSoft, Math.Max(150, p.PanelAlpha)), p.Look.Corner);
        var area = b.Inflated(-10 * k, -5 * k);
        string extra = v.Options.Choice("pill");
        float sideW = extra == "none" ? 0 : Math.Max(90 * k, Math.Max(p.Measure(p.LabelText(refLabel), p.Label), p.Measure("0:00.000", p.Bold)) + 4 * k);
        float numberW = p.Measure("+0.000", p.Big);
        p.Need(numberW + (sideW > 0 ? sideW + 10 * k : 0) + 20 * k, p.LineHeight(p.Big) + 10 * k);
        bool side = sideW > 0 && area.Width >= sideW + 10 * k + numberW;
        var numberBox = area;
        var sideBox = RectangleF.Empty;
        if (side)
        {
            sideBox = area.Right(sideW, out numberBox);
            numberBox = RectangleF.FromLTRB(numberBox.Left, numberBox.Top, numberBox.Right - 10 * k, numberBox.Bottom);
        }
        var font = p.Fitting("+0.000", numberBox.Width, numberBox.Height, p.Font(Math.Max(14, numberBox.Height * 0.9f), true), p.Huge, p.Big, p.Bold);
        p.Region("delta", numberBox);
        p.Text(RaceFormat.Delta(d), font, color, numberBox, side ? HAlign.Left : HAlign.Center, VAlign.Center, false);
        if (!side) return;
        p.Region("pill", sideBox);
        float lh = p.LineHeight(p.Label) + 1;
        if (extra == "predicted")
        {
            float block = lh + p.LineHeight(p.Bold);
            var stack = new RectangleF(sideBox.X, sideBox.Y + Math.Max(0, (sideBox.Height - block) / 2), sideBox.Width, block);
            var labelBox = stack.Top(lh, out var valueBox);
            p.Text(p.LabelText("Predicted"), p.Label, p.LabelColor, labelBox, HAlign.Left, VAlign.Top);
            p.Text(RaceFormat.LapTime(Predicted(d, refLap)), p.Bold, p.P.Heading, valueBox, HAlign.Left, VAlign.Center, false);
            return;
        }
        float sparkH = Math.Min(22 * k, Math.Max(0, sideBox.Height - lh - 2 * k));
        float y = sideBox.Y + Math.Max(0, (sideBox.Height - lh - 2 * k - sparkH) / 2);
        p.Text(p.LabelText(refLabel), p.Label, p.LabelColor, new RectangleF(sideBox.X, y, sideBox.Width, lh), HAlign.Left, VAlign.Top);
        Spark(p, v, new RectangleF(sideBox.X, y + lh + 2 * k, sideBox.Width, sparkH), v.Race.Deltas.For(reference), d);
    }

    private void Spark(HudPainter p, HudView v, RectangleF r, DeltaTrace trace, double? d)
    {
        int count = trace.RecentCount;
        if (r.Height < 6) return;
        float mid = p.Snap(r.Y + r.Height / 2), half = r.Height / 2 - 1;
        p.Line(r.X, mid, r.Right, mid, HudPalette.Alpha(p.P.Heading, 70), 1);
        if (count < 2) return;
        double from = v.Now - 10;
        float most = 0.1f;
        for (int i = 0; i < count; i++)
        {
            var (t, value) = trace.Recent(i);
            if (t >= from) most = Math.Max(most, Math.Abs(value));
        }
        _line.Reset();
        bool open = false;
        float px = 0, py = 0;
        for (int i = 0; i < count; i++)
        {
            var (t, value) = trace.Recent(i);
            if (t < from) continue;
            float x = r.X + (float)Math.Clamp((t - from) / 10, 0, 1) * r.Width, y = mid - Math.Clamp(value / most, -1, 1) * half;
            if (!open) { _line.StartFigure(); open = true; }
            else _line.AddLine(px, py, x, y);
            px = x;
            py = y;
        }
        p.G.DrawPath(p.Pen(d is < 0 ? p.P.Good : p.P.Danger, 1.75f * p.Density), _line);
    }

    private static void Compact(HudPainter p, HudView v, double? d, Color color, string label)
    {
        float k = p.Density;
        var area = p.Bounds.Inflated(-10 * k, -6 * k);
        p.Region("delta", area);
        float labelH = p.LineHeight(p.SmallBold), barH = 6 * k, gap = 5 * k;
        float contentW = Math.Max(Math.Max(p.Measure("+00.000", p.Huge), 120 * k), p.Measure("SESSION OPTIMAL", p.SmallBold)) + 8 * k;
        p.Need(contentW + 20 * k, p.LineHeight(p.Huge) + gap + barH + gap + labelH + 12 * k);
        float numberH = Math.Max(0, area.Height - labelH - barH - gap * 2);
        var font = p.Fitting("+00.00", area.Width, numberH, p.Font(Math.Max(14, numberH * 0.95f), true), p.Huge, p.Big, p.Bold);
        float block = p.LineHeight(font) + gap + barH + gap + labelH;
        float y = area.Y + Math.Max(0, (area.Height - block) / 2);
        p.Text(d is null ? RaceFormat.Missing : RaceFormat.Delta(d), font, color, new RectangleF(area.X, y, area.Width, p.LineHeight(font)), HAlign.Center, VAlign.Center, false);
        y += p.LineHeight(font) + gap;
        float trackW = Math.Min(area.Width, 120 * k);
        var track = new RectangleF(area.X + (area.Width - trackW) / 2, y, trackW, barH);
        p.Region("bar", track);
        p.Fill(track, p.Solid(p.Raised(0.10), 130), 3);
        float mid = track.X + track.Width / 2;
        if (d is { } dv)
        {
            float len = (float)(Math.Clamp(dv / v.Options.Number("range"), -1, 1) * track.Width / 2);
            var fill = len < 0 ? new RectangleF(mid + len, track.Y, -len, track.Height) : new RectangleF(mid, track.Y, len, track.Height);
            p.Fill(fill, d < 0 ? p.P.Good : p.P.Danger, 3);
        }
        p.Line(mid, track.Y - 2, mid, track.Bottom + 2, p.P.Heading, 1.5f);
        y += barH + gap;
        p.Text(label, p.SmallBold, p.P.Heading, new RectangleF(area.X, y, area.Width, labelH), HAlign.Center, VAlign.Center);
    }

    private static Color LastColor(HudPainter p, HudView v)
    {
        var laps = v.Race.Laps.Laps;
        if (laps.Count == 0) return p.P.Heading;
        var last = laps[^1];
        return !last.Clean ? p.P.WarnText : v.Frame.LapBest is { } best && Math.Abs(last.Time - best) < 0.0005 ? p.P.Fastest : p.P.Heading;
    }

    private static void Sectors(HudPainter p, HudView v, RectangleF r)
    {
        var tracker = v.Race.Laps;
        int n = tracker.SectorCount;
        if (n <= 1) return;
        var best = LapMath.BestSectors(tracker.Laps, n);
        var previous = tracker.Laps.Count > 0 ? tracker.Laps[^1].Sectors : null;
        float gap = n > 8 ? 2 : 4;
        float w = (r.Width + gap) / n, h = Math.Min(r.Height, p.LineHeight(p.Small) + 8);
        string format = w - gap >= p.Measure("00.00", p.SmallBold) + 6 ? "0.00" : "0.0";
        bool labels = w - gap >= p.Measure("00.0", p.SmallBold) + 4;
        for (int i = 0; i < n; i++)
        {
            var box = new RectangleF(r.X + i * w, r.Y + 2, w - gap, h);
            double? t = i < tracker.CurrentSectors.Count ? tracker.CurrentSectors[i] : null;
            Color fill = p.Solid(p.Raised(0.07), 130), text = p.P.Muted;
            string label = labels ? PooledText.Number("S", i + 1) : "";
            if (t is { } st)
            {
                if (labels) label = PooledText.Number("", st, format, System.Globalization.CultureInfo.InvariantCulture);
                bool pb = best[i] is null || st <= best[i]!.Value + 0.0005;
                bool faster = previous is not null && i < previous.Count && previous[i] is { } ps && st < ps;
                fill = pb ? p.P.Fastest : faster ? p.P.Good : p.P.Warn;
                text = p.P.Heading;
                p.Chip(box, fill);
                if (label.Length > 0) p.Text(label, p.SmallBold, text, box, HAlign.Center, VAlign.Center);
                continue;
            }
            else if (i == tracker.CurrentSector) { fill = HudPalette.Alpha(p.P.AccentSoft, 200); text = p.P.AccentText; }
            p.Fill(box, fill, p.CornerRadius(3));
            if (label.Length > 0) p.Text(label, p.SmallBold, text, box, HAlign.Center, VAlign.Center);
        }
    }
}

internal sealed class FuelWidget : HudWidget
{
    private static readonly HudChoice[] Figures =
    {
        new("level", "Fuel in the tank"), new("perlap", "Per lap (average)"), new("last", "Last lap"), new("pitexit", "Pit exit position", PitExitHelp),
        new("max", "Most on one lap"), new("laps", "Laps it lasts"), new("time", "Time it lasts"),
        new("tofinish", "Needed to the flag"), new("surplus", "Spare / short at the flag"), new("atflag", "Left at the flag (expected)"),
        new("add", "Add at the next stop"), new("stops", "Stops left"), new("window", "Pit window"), new("togo", "Laps to go"),
    };

    private static readonly HudChoice[] Parts =
    {
        new("header", "Laps to go, next pit, stops", "A line of key figures (Header shows)"),
        new("gauge", "Tank gauge", "Fuel in the tank, the laps it lasts and the tank's size over a bar in the fuel status color"),
        new("grid", "Consumption grid", "A row per rate (this lap, average, most…) with what it means: per lap, laps, refuel, left at the flag"),
        new("plan", "Plan state", "No stop, window open, pit needed… as a colored band"),
        new("figures", "Big figures", "Large numbers (Big figures shows)"),
        new("table", "Last / average / most table", "Columns of rates, rows of per lap, laps and refuel (Table columns / rows)"),
        new("save", "Fuel saving", "What each lap may use to make the flag with one stop fewer, how much that saves per lap, and what this lap is on course for. Green target = you've already done a lap that frugal. With no stop needed: the spare per lap"),
    };

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Columns("parts", "Sections", "header,gauge,grid,plan,figures", Parts)
            with { Help = "The blocks the widget is built of, top to bottom; each one's figures are picked below it" },
        HudOption.Columns("head", "Header", "togo,nextpit,stops,pitby,add,level",
            new HudChoice("togo", "Laps to go", "Colored by how sure the estimate is"), new HudChoice("nextpit", "Next pit lap", "The lap the pit window opens (one stop then reaches the flag)"),
            new HudChoice("pitby", "Pit by lap", "The last lap before the tank runs dry"), new HudChoice("stops", "Stops left"),
            new HudChoice("add", "Add at the next stop"), new HudChoice("level", "Fuel in the tank"), new HudChoice("pitexit", "Pit exit position", PitExitHelp)),
        HudOption.Columns("gridRows", "Grid rows", "curr,avg,max,min,last",
            new HudChoice("curr", "This lap", "Projected from what this lap has used so far"), new HudChoice("avg", "Average", "The rate the plan uses (clean, green laps)"),
            new HudChoice("avgN", "Average of the last laps", "Plain average of your last clean laps (Laps in the average)"),
            new HudChoice("max", "Most on one lap"), new HudChoice("min", "Least on one lap"), new HudChoice("last", "Last lap")),
        HudOption.Columns("gridCols", "Grid columns", "use,laps,refuel,finish",
            new HudChoice("use", "Per lap"), new HudChoice("laps", "Laps it lasts"), new HudChoice("refuel", "Refuel to the flag", "What to add, in total, to reach the flag at that rate (with the plan's reserve)"),
            new HudChoice("finish", "Left at the flag", "Fuel in the tank at the flag at that rate, without stopping (negative = short)")),
        HudOption.Int("avgLaps", "Laps in the average", 5, 3, 10),
        HudOption.Bool("statusEdge", "Status border", false, "Green: enough to the flag. Amber: a stop to come. Red: under the low-fuel alert's laps"),
        HudOption.Bool("title", "Title row", false, "“Fuel” with how many clean laps the estimate has"),
        HudOption.Columns("top", "Big figures", "level,laps,togo,perlap", Figures),
        HudOption.Columns("cols", "Table columns", "last,avg,max",
            new HudChoice("last", "Last lap"), new HudChoice("avg", "Average", "The rate the plan uses (clean, green laps)"), new HudChoice("max", "Most on one lap")),
        HudOption.Columns("rows", "Table rows", "perlap,laps,refuel",
            new HudChoice("perlap", "Per lap"), new HudChoice("laps", "Laps it lasts"), new HudChoice("refuel", "Refuel to the flag", "What to add, in total, to reach the flag at that rate (with the plan's reserve)")),
    };

    private const string PitExitHelp = "Where a stop now would put you in your class, with the fuel and tires set in your pit menu, and the places that gains or loses. ≈ until the pit lane, fill rate and tire change time are learned for this track and car; gray under caution";

    private static (string Value, Color Color) PitExitValue(HudPainter p, HudView v)
    {
        var (pos, posColor, change, _) = PitExitParts(p, v);
        return (change.Length == 0 ? pos : PooledText.Concat(pos, " ", change), posColor);
    }

    private static (string Pos, Color PosColor, string Change, Color ChangeColor) PitExitParts(HudPainter p, HudView v)
    {
        var x = v.Race.PitExit;
        if (x is not { Valid: true }) return (v.Frame.OnPitRoad ? "In pit" : RaceFormat.Missing, p.P.Faint, "", p.P.Faint);
        string pos = PooledText.Number("P", x.ProjectedClassPos);
        if (!x.Calibrated) pos = RaceFormat.About(pos);
        int change = x.CurrentClassPos - x.ProjectedClassPos;
        string gained = change == 0 ? "" : PooledText.Concat(change > 0 ? "+" : "−", PooledText.Number("", Math.Abs(change)));
        var changeColor = x.Unreliable ? p.P.Faint : change > 0 ? p.P.GoodText : p.P.DangerText;
        return (pos, x.Unreliable ? p.P.Faint : p.P.Heading, gained, changeColor);
    }

    private static void DrawPitExit(HudPainter p, HudView v, RectangleF box, HAlign align)
    {
        var (pos, posColor, change, changeColor) = PitExitParts(p, v);
        if (change.Length == 0) { p.Number(pos, p.Bold, posColor, box, align, VAlign.Center); return; }
        float gap = 5 * p.Density, pw = p.Measure(pos, p.Bold), cw = p.Measure(change, p.SmallBold), total = pw + gap + cw;
        if (total > box.Width) { p.Text(pos, p.Bold, posColor, box, align, VAlign.Center); return; }
        float x = align switch { HAlign.Right => box.Right - total, HAlign.Center => box.X + (box.Width - total) / 2, _ => box.X };
        p.Text(pos, p.Bold, posColor, new RectangleF(x, box.Y, pw + 1, box.Height), HAlign.Left, VAlign.Center, false);
        float lh = p.LineHeight(p.SmallBold), drop = p.LineHeight(p.Bold) - lh;
        var cb = new RectangleF(x + pw + gap, box.Y + box.Height / 2 + p.LineHeight(p.Bold) / 2 - lh - drop * 0.22f, cw + 1, lh);
        p.Text(change, p.SmallBold, changeColor, cb, HAlign.Left, VAlign.Top, false);
    }

    private readonly List<(string Id, string Label, string Value, Color? Color)> _top = new();

    internal const float WidgetMin = 450;

    private readonly List<string> _shown = new();
    private readonly List<(string Label, string Value, Color Color, bool Dot, float Natural)> _head = new();
    private readonly List<float> _heights = new();

    private enum Status { Safe, Caution, Danger }

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Fuel")) return;
        var f = v.Frame;
        var r = v.Race;
        var plan = r.Fuel;
        var estimate = r.FuelData.Estimate;
        int measured = estimate.UsedLapCount;
        float pad = 7 * p.Density, partPad = 6 * p.Density, gap = partPad * 2 + p.PieceGap;
        var bounds = p.Bounds;
        float top = 0;
        if (v.Options.Bool("title"))
        {
            string sub = measured == 0 ? "measuring…" : PooledText.Concat(RaceFormat.Count(measured, "clean lap"), " · ", Confidence(estimate.Reliability));
            p.TitleBar(new RectangleF(0, 0, bounds.Width, p.PillHeight), "Fuel", r.FuelData.EnergyUnits ? "" : sub);
            p.Region("title", new RectangleF(0, 0, bounds.Width, p.PillHeight));
            top = p.PillHeight + p.PieceGap;
        }
        if (f.Fuel is not { } level || r.FuelData.EnergyUnits)
        {
            var body = new RectangleF(0, top, bounds.Width, bounds.Height - top);
            p.Panel(body);
            p.Text(r.FuelData.EnergyUnits ? "This car reports energy (kWh), not fuel" : "This car doesn't report fuel", p.Small, p.P.Faint, body.Inflated(-pad, -pad), HAlign.Center, VAlign.Center);
            return;
        }
        double lowLaps = r.Alerts.Threshold("fuel.low");
        var status = StatusOf(plan, lowLaps);
        Color? edge = v.Options.Bool("statusEdge") ? HudPalette.Alpha(status switch { Status.Danger => p.P.Danger, Status.Caution => p.P.Warn, _ => p.P.Good }, 170) : null;
        var parts = v.Options.Columns("parts");
        _shown.Clear();
        _heights.Clear();
        p.NeedWidth(WidgetMin * p.Density);
        foreach (var part in parts)
        {
            if (Height(part, p, v) <= 0) continue;
            _shown.Add(part);
            _heights.Add(Natural(part, p, v));
        }
        float edgeTop = float.NaN, edgeBottom = 0;
        var area = new RectangleF(pad, top + partPad, bounds.Width - pad * 2, bounds.Height - top - partPad * 2);
        int last = _shown.Count - 1;
        for (int i = 0; i < _shown.Count; i++)
        {
            string part = _shown[i];
            if (area.Height < Height(part, p, v) - 0.5f)
            {
                for (int k = i; k < _shown.Count; k++) p.Overflow?.Add(("parts", _shown[k]));
                break;
            }
            float h = i == last && part is "grid" or "table" ? area.Height : Math.Min(area.Height, _heights[i]);
            var box = area.Top(h, out area);
            p.Item("parts", part, box);
            float pieceTop = box.Y - partPad, pieceBottom = i == last ? bounds.Height : box.Bottom + partPad;
            var piece = new RectangleF(0, pieceTop, bounds.Width, pieceBottom - pieceTop);
            p.Panel(piece);
            edgeTop = float.IsNaN(edgeTop) ? piece.Y : Math.Min(edgeTop, piece.Y);
            edgeBottom = Math.Max(edgeBottom, piece.Bottom);
            switch (part)
            {
                case "header": HeaderLine(p, v, box, level, plan); break;
                case "gauge": Gauge(p, v, box, level, plan, status); break;
                case "grid": Grid(p, v, box, level, plan); break;
                case "plan": PlanBand(p, v, box, plan, measured); break;
                case "figures": Figures_(p, v, box, plan, lowLaps); break;
                case "table": TablePart(p, v, box, level, plan); break;
                case "save": SavePart(p, v, box, level, plan); break;
            }
            area = new RectangleF(area.X, area.Y + gap, area.Width, Math.Max(0, area.Height - gap));
        }
        if (edge is { } edgeColor && !float.IsNaN(edgeTop)) p.Outline(new RectangleF(0, edgeTop, bounds.Width, edgeBottom - edgeTop).Inflated(-1, -1), edgeColor, 2, p.CornerRadius(4));
    }

    private static Status StatusOf(FuelPlan? plan, double lowLaps)
    {
        if (plan is null) return Status.Safe;
        if (plan.LapsOfFuel < lowLaps) return Status.Danger;
        if (plan.LapsToGo is null) return plan.LapsOfFuel < lowLaps * 2 ? Status.Caution : Status.Safe;
        return plan.Stops == 0 ? Status.Safe : Status.Caution;
    }

    private float Natural(string part, HudPainter p, HudView v) => part switch
    {
        "grid" => HeadHeight(p) + p.LineHeight(p.Label) + 4 + v.Options.Columns("gridRows").Count * (p.LineHeight(p.Body) + 4 * p.Density),
        "table" => HeadHeight(p) + p.LineHeight(p.Label) + 4 + v.Options.Columns("rows").Count * (p.LineHeight(p.Bold) + 5 * p.Density + 2),
        _ => Height(part, p, v),
    };

    private float Height(string part, HudPainter p, HudView v) => part switch
    {
        "header" => p.LineHeight(p.Label) + p.LineHeight(p.Bold) + 6 * p.Density,
        "gauge" => p.LineHeight(p.SmallBold) + 2 + 6 * p.Density,
        "grid" => v.Options.Columns("gridRows").Count == 0 || v.Options.Columns("gridCols").Count == 0 ? 0
            : HeadHeight(p) + p.LineHeight(p.Label) + 4 + Math.Min(2, v.Options.Columns("gridRows").Count) * (p.LineHeight(p.Body) + 4 * p.Density),
        "plan" => p.LineHeight(p.Bold) + 8 * p.Density,
        "save" => HeadHeight(p) + p.LineHeight(p.Label) + p.LineHeight(p.Bold) + 4 * p.Density,
        "figures" => FigureRows(p, v) is var (_, rows) && rows > 0 ? HeadHeight(p) + rows * FigureRowHeight(p) : 0,
        "table" => v.Options.Columns("cols").Count == 0 || v.Options.Columns("rows").Count == 0 ? 0
            : HeadHeight(p) + p.LineHeight(p.Label) + 4 + (p.LineHeight(p.Bold) + 5 * p.Density + 2),
        _ => 0,
    };

    private static void SavePart(HudPainter p, HudView v, RectangleF box, double level, FuelPlan? plan)
    {
        var u = v.Units;
        var target = FuelSave.Target(plan, level, v.Race.Tank, RowRate("min", v, plan).Rate);
        if (target is not { } t)
        {
            p.Text("Shows once the plan has laps to go and a tank size", p.Small, p.P.Faint, box, HAlign.Center, VAlign.Center);
            return;
        }
        string title = t.Spare ? "No stop needed" : t.StopsNow == 1 ? "Save to finish without a stop" : PooledText.Concat("Save to finish with ", RaceFormat.Count(t.StopsNow - 1, "stop"));
        var cells = SectionHead(p, box, "Fuel saving", title);
        double? now = RowRate("curr", v, plan).Rate;
        float tileGap = 3 * p.Density, cw = (cells.Width - tileGap * 2) / 3;
        var targetColor = t.Spare || t.Proven ? p.P.GoodText : p.P.WarnText;
        string share = PooledText.Number(t.Spare ? "SPARE · " : "SAVE · ", Math.Abs(t.Share) * 100, "0", System.Globalization.CultureInfo.InvariantCulture, "%");
        Cell(0, "TARGET", RaceFormat.Fuel(t.Target, u), targetColor);
        Cell(1, share, RaceFormat.Fuel(Math.Abs(t.Save), u), p.P.Heading);
        Cell(2, "THIS LAP", now is { } n ? RaceFormat.Fuel(n, u) : RaceFormat.Missing, now is { } m ? m <= t.Target ? p.P.GoodText : p.P.DangerText : p.P.Faint);

        void Cell(int i, string label, string value, Color color)
        {
            var c = new RectangleF(cells.X + i * (cw + tileGap), cells.Y, cw, cells.Height);
            p.Tile(c);
            var inner = c.Inflated(-4 * p.Density, -2 * p.Density);
            var vb = inner.Top(p.LineHeight(p.Bold), out var lb);
            p.Number(value, p.Bold, color, vb, HAlign.Center, VAlign.Center);
            p.Text(label, p.Label, p.LabelColor, lb, HAlign.Center, VAlign.Top);
        }
    }

    private void HeaderLine(HudPainter p, HudView v, RectangleF box, double level, FuelPlan? plan)
    {
        var f = v.Frame;
        var u = v.Units;
        var ids = v.Options.Columns("head");
        if (ids.Count == 0) return;
        _head.Clear();
        for (int i = 0; i < ids.Count; i++)
        {
            Color color = p.P.Heading;
            string label, value;
            bool laps = false;
            switch (ids[i])
            {
                case "togo":
                    label = "TO GO";
                    double? togo = plan?.LapsToGo ?? (f.SessionKind == SessionKind.Race ? v.Race.LapsToGo(f) : null);
                    value = togo is { } t ? PooledText.Number("", Math.Ceiling(t), "0", System.Globalization.CultureInfo.CurrentCulture, " LAPS") : RaceFormat.Missing;
                    if (togo is not null && plan is not null)
                    {
                        color = plan.Reliability switch { EstimateReliability.High => p.P.GoodText, EstimateReliability.Moderate => p.P.WarnText, _ => p.P.DangerText };
                        if (plan.Reliability == EstimateReliability.Moderate || plan.LapsToGoForecast) value = RaceFormat.About(value);
                        laps = true;
                    }
                    break;
                case "nextpit":
                    label = "NEXT PIT";
                    value = plan?.Stops switch
                    {
                        0 => "None",
                        > 0 when plan.WindowOpensIn is { } o && f.Lap is { } lap => o <= 0 ? "Now" : PooledText.Number("L", lap + (int)Math.Ceiling(o)),
                        _ => RaceFormat.Missing,
                    };
                    if (plan is { Stops: > 0, WindowOpensIn: <= 0 }) color = p.P.GoodText;
                    break;
                case "pitby":
                    label = "PIT BY";
                    value = plan is not null && f.Lap is { } now ? PooledText.Number("L", now + (int)Math.Floor(plan.LapsOfFuel)) : RaceFormat.Missing;
                    break;
                case "stops":
                    label = "STOPS";
                    value = plan?.Stops is { } st ? st.ToString() : RaceFormat.Missing;
                    break;
                case "add":
                    label = "ADD";
                    value = plan?.Stops is 0 ? "–" : plan?.AddAtNextStop is { } add ? RaceFormat.Fuel(add, u) : RaceFormat.Missing;
                    break;
                case "pitexit":
                    label = "PIT EXIT";
                    (value, color) = PitExitValue(p, v);
                    break;
                default:
                    label = "TANK";
                    value = RaceFormat.Fuel(level, u);
                    break;
            }
            string shortest = laps && value.EndsWith(" LAPS", StringComparison.Ordinal) ? value[..^5] : value;
            float natural = Math.Max(p.Measure(p.LabelText(label), p.Label), p.Measure(shortest, p.Bold)) + 10 * p.Density;
            _head.Add((label, value, color, laps, natural));
        }
        float tileGap = 3 * p.Density;
        NeedColumn(p, _head[0].Natural);
        int fit = ids.Count;
        while (fit > 1 && Used(fit) > box.Width) fit--;
        for (int i = fit; i < ids.Count; i++) p.Overflow?.Add(("head", ids[i]));
        float spare = (box.Width - Used(fit)) / fit, at = box.X;
        for (int i = 0; i < fit; i++)
        {
            var (label, value, color, laps, natural) = _head[i];
            var cell = new RectangleF(at, box.Y, natural + spare, box.Height);
            at += cell.Width + tileGap;
            const HAlign align = HAlign.Center;
            p.Tile(cell);
            p.Item("head", ids[i], cell);
            float lh = p.LineHeight(p.Label);
            var inner = cell.Inflated(0, -2 * p.Density);
            var valueBox = new RectangleF(inner.X, inner.Y, inner.Width, inner.Height - lh);
            p.Text(p.LabelText(label), p.Label, p.LabelColor, new RectangleF(inner.X, inner.Bottom - lh, inner.Width, lh), align, VAlign.Top);
            if (laps)
            {
                if (p.Measure(value, p.Bold) > valueBox.Width && value.EndsWith(" LAPS", StringComparison.Ordinal)) value = value[..^5];
                p.Text(value, p.Bold, color, valueBox, align, VAlign.Center);
            }
            else if (ids[i] == "pitexit") DrawPitExit(p, v, valueBox, align);
            else p.Number(value, p.Bold, color, valueBox, align, VAlign.Center);
        }

        float Used(int n)
        {
            float sum = tileGap * (n - 1);
            for (int i = 0; i < n; i++) sum += _head[i].Natural;
            return sum;
        }
    }

    private void Gauge(HudPainter p, HudView v, RectangleF box, double level, FuelPlan? plan, Status status)
    {
        var u = v.Units;
        var r = v.Race;
        float lh = p.LineHeight(p.SmallBold);
        var line = new RectangleF(box.X, box.Y, box.Width, lh);
        float tankW = p.Measure("TANK", p.Label) + 5 * p.Density;
        p.Text("TANK", p.Label, p.LabelColor, line, HAlign.Left, VAlign.Center, false);
        string amount = RaceFormat.Fuel(level, u);
        float amountW = p.Measure(amount, p.SmallBold);
        p.Text(amount, p.SmallBold, p.P.Heading, new RectangleF(line.X + tankW, line.Y, amountW + 2, line.Height), HAlign.Left, VAlign.Center, false);
        string lasts = plan is not null ? PooledText.Number(" · ", plan.LapsOfFuel, "0.00", System.Globalization.CultureInfo.InvariantCulture, " laps") : "";
        float leftW = tankW + amountW + (lasts.Length > 0 ? p.Measure(lasts, p.Small) : 0);
        string needText = plan?.ToFinish is { } tf && tf > 0 ? PooledText.Concat("NEED ", FuelNumber(tf, u)) : "";
        string capText = r.Tank is { } cap ? PooledText.Concat("CAP ", PooledText.Number("", RaceFormat.FuelValue(cap, u), "0", System.Globalization.CultureInfo.InvariantCulture, u == Config.HudUnits.Imperial ? " " + Core.Services.CapsUnits.Gal : " " + Core.Services.CapsUnits.L)) : "";
        string rightText = needText.Length > 0 && capText.Length > 0 ? PooledText.Concat(needText, " · ", capText) : needText.Length > 0 ? needText : capText;
        float rightW = rightText.Length > 0 ? p.Measure(rightText, p.Label) : 0;
        if (lasts.Length > 0 && leftW + rightW + 12 * p.Density <= line.Width)
            p.Text(lasts, p.Small, p.LabelColor, new RectangleF(line.X + tankW + amountW + 2, line.Y, line.Width - tankW - amountW - 2, line.Height), HAlign.Left, VAlign.Center, false);
        if (rightText.Length > 0 && tankW + amountW + rightW + 12 * p.Density <= line.Width)
            p.Text(rightText, p.Label, p.LabelColor, line, HAlign.Right, VAlign.Center, false);
        NeedColumn(p, tankW + amountW + 16 * p.Density);
        var track = new RectangleF(box.X, line.Bottom + 2, box.Width, 6 * p.Density);
        if (r.Tank is not { } tank || tank <= 0) return;
        var fill = status switch { Status.Danger => p.P.Danger, Status.Caution => p.P.Warn, _ => p.P.Good };
        p.Bar(track, level / tank, fill, HudPalette.Alpha(p.P.LineStrong, 120));
        if (plan?.ToFinish is { } need && need > 0)
        {
            float x = track.X + (float)Math.Clamp(need / tank, 0, 1) * track.Width;
            p.Line(x, track.Y - 2, x, track.Bottom + 2, need <= level ? p.P.Heading : p.P.DangerText, 2);
        }
    }

    private static (string Label, double? Rate) RowRate(string id, HudView v, FuelPlan? plan)
    {
        var r = v.Race;
        var laps = r.FuelData.Laps;
        switch (id)
        {
            case "curr":
                double? used = r.FuelThisLap;
                double? rate = used is { } u && v.Frame.LapDistPct is { } pct && pct - r.FuelThisLapFrom is var driven and > 0.1 && u > 0 ? u / driven : null;
                return ("CURR", rate ?? (laps.Count > 0 && laps[^1].FuelUsedLiters > 0 ? laps[^1].FuelUsedLiters : null));
            case "avg": return ("AVG", plan?.PerLap ?? r.FuelPerLap);
            case "avgN":
                int n = v.Options.Int("avgLaps");
                double sum = 0;
                int count = 0;
                for (int i = laps.Count - 1; i >= 0 && count < n; i--)
                    if (laps[i].IsEligible) { sum += laps[i].FuelUsedLiters; count++; }
                return (PooledText.Number("AVG ", n), count > 0 ? sum / count : null);
            case "max": return ("MAX", MostOnOneLap(laps));
            case "min":
                double? least = null;
                foreach (var l in laps) if (l.IsEligible && (least is null || l.FuelUsedLiters < least)) least = l.FuelUsedLiters;
                return ("MIN", least);
            case "last": return ("LAST", laps.Count > 0 && laps[^1].FuelUsedLiters > 0 ? laps[^1].FuelUsedLiters : null);
            default: return (id, null);
        }
    }

    private static void Grid(HudPainter p, HudView v, RectangleF box, double level, FuelPlan? plan)
    {
        box = SectionHead(p, box, "Consumption", null);
        var u = v.Units;
        var rows = v.Options.Columns("gridRows");
        var cols = v.Options.Columns("gridCols");
        if (rows.Count == 0 || cols.Count == 0) return;
        float headH = p.LineHeight(p.Label) + 4, rowH = p.LineHeight(p.Body) + 4 * p.Density;
        int fit = Math.Clamp((int)((box.Height - headH) / rowH), 0, rows.Count);
        for (int i = fit; i < rows.Count; i++) p.Overflow?.Add(("gridRows", rows[i]));
        if (fit == 0) return;
        double reserve = plan is { ToFinish: { } tf, LapsToGo: { } tg } ? Math.Max(0, tf - tg * plan.PerLap) : 0;
        double? togo = plan?.LapsToGo ?? (v.Frame.SessionKind == SessionKind.Race ? v.Race.LapsToGo(v.Frame) : null);
        float labelW = Math.Min(box.Width * 0.26f, p.Measure("AVG 10", p.Label) + 14 * p.Density);
        float cw = (box.Width - labelW) / cols.Count;
        p.Text(RaceFormat.FuelUnit(u), p.Label, p.LabelColor, new RectangleF(box.X + 6 * p.Density, box.Y, labelW, headH), HAlign.Left, VAlign.Center, false);
        for (int c = 0; c < cols.Count; c++)
        {
            string name = cols[c] switch { "use" => "Per lap", "laps" => "Laps", "refuel" => "Refuel", _ => "Finish" };
            var hb = new RectangleF(box.X + labelW + c * cw, box.Y, cw, headH);
            p.Text(p.LabelText(name), p.Label, p.LabelColor, hb, HAlign.Center, VAlign.Center, false);
            p.Item("gridCols", cols[c], hb);
        }
        float y = box.Y + headH;
        for (int i = 0; i < fit; i++)
        {
            var (label, rate) = RowRate(rows[i], v, plan);
            bool plan_ = rows[i] == "avg";
            var row = new RectangleF(box.X, y, box.Width, rowH);
            if (plan_)
            {
                p.Fill(row, p.Solid(HudPalette.Mix(p.Raised(0), p.P.Accent, 0.13), 90), p.CornerRadius(2));
                p.Fill(new RectangleF(row.X, row.Y, 3 * p.Density, row.Height), p.P.Accent);
            }
            else if (i % 2 == 0) p.Fill(row, p.Solid(p.Raised(p.P.Dark ? 0.06 : 0.045), 90), p.CornerRadius(2));
            p.Text(label, p.Label, plan_ ? p.P.AccentText : p.LabelColor, new RectangleF(row.X + 6 * p.Density, row.Y, labelW, row.Height), HAlign.Left, VAlign.Center, false);
            p.Item("gridRows", rows[i], new RectangleF(row.X, row.Y, labelW, row.Height));
            for (int c = 0; c < cols.Count; c++)
            {
                string text = RaceFormat.Missing;
                Color color = plan_ ? p.P.Heading : p.P.Text;
                if (rate is { } x && x > 0)
                    switch (cols[c])
                    {
                        case "use": text = FuelNumber(x, u); break;
                        case "laps": text = PooledText.Number("", (level / x), "0.00", System.Globalization.CultureInfo.InvariantCulture); break;
                        case "refuel":
                            if (togo is { } t)
                            {
                                double add = Math.Max(0, t * x + reserve - level);
                                text = FuelNumber(add, u);
                                color = add <= 0 ? p.P.GoodText : color;
                            }
                            break;
                        case "finish":
                            if (togo is { } t2)
                            {
                                double left = level - t2 * x;
                                text = (left < 0 ? "−" : "") + FuelNumber(Math.Abs(left), u);
                                color = left < 0 ? p.P.DangerText : p.P.GoodText;
                            }
                            break;
                    }
                p.Text(text, plan_ ? p.Bold : p.Body, text == RaceFormat.Missing ? p.P.Faint : color, new RectangleF(box.X + labelW + c * cw, row.Y, cw, row.Height), HAlign.Center, VAlign.Center);
            }
            y += rowH;
        }
    }

    private static void PlanBand(HudPainter p, HudView v, RectangleF band, FuelPlan? plan, int measured)
    {
        var (fill, label, value) = State(p, v, plan, measured);
        var pal = p.P;
        Color? tone = !pal.SoftBands ? null : fill == pal.Warn ? pal.WarnText : fill == pal.Good ? pal.GoodText : fill == pal.Danger ? pal.DangerText : null;
        p.Fill(band, tone is null ? fill : p.Solid(HudPalette.Mix(pal.Surface, fill, 0.2), 200), p.CornerRadius(4));
        var on = tone ?? HudPalette.On(fill);
        var inner = band.Inflated(-8 * p.Density, 0);
        float lw = Math.Min(inner.Width * 0.6f, p.Measure(p.LabelText(label), p.Label) + 8);
        p.Text(p.LabelText(label), p.Label, HudPalette.Alpha(on, 215), inner.Left(lw, out var right), HAlign.Left, VAlign.Center);
        p.Number(value, p.Bold, on, right, HAlign.Right, VAlign.Center);
    }

    private static float FigureRowHeight(HudPainter p) => p.LineHeight(p.Bold) + 4 * p.Density;

    private const int MostFigureColumns = 3;

    private void NeedColumn(HudPainter p, float inner) =>
        p.NeedWidth(inner + 14 * p.Density);

    private (int Columns, int Rows) FigureRows(HudPainter p, HudView v)
    {
        _top.Clear();
        double level = v.Frame.Fuel ?? 0;
        foreach (var id in v.Options.Columns("top"))
            if (Cell(id, p, v, level, v.Race.Fuel) is { } c) _top.Add(c);
        if (_top.Count == 0) return (0, 0);
        float widest = 0;
        foreach (var (_, label, value, _) in _top)
            widest = Math.Max(widest, p.Measure(p.LabelText(label), p.Label) + p.Measure(value, p.Bold) + 22 * p.Density);
        NeedColumn(p, widest);
        float width = Math.Max(1, p.Bounds.Width - 14 * p.Density);
        int columns = Math.Clamp((int)(width / Math.Max(widest, 150 * p.Density)), 1, Math.Min(MostFigureColumns, _top.Count));
        int rows = (_top.Count + columns - 1) / columns;
        return ((_top.Count + rows - 1) / rows, rows);
    }

    private void Figures_(HudPainter p, HudView v, RectangleF box, FuelPlan? plan, double lowLaps)
    {
        var (columns, rows) = FigureRows(p, v);
        if (_top.Count == 0 || columns == 0) return;
        var area = SectionHead(p, box, "Race", null);
        float rowH = FigureRowHeight(p), colGap = 6 * p.Density;
        float cw = (area.Width - colGap * (columns - 1)) / columns;
        int fitRows = Math.Clamp((int)((area.Height + 0.5f) / rowH), 0, rows);
        for (int i = 0; i < _top.Count; i++)
        {
            int col = i / rows, line = i % rows;
            if (line >= fitRows)
            {
                p.Overflow?.Add(("top", _top[i].Id));
                continue;
            }
            var (id, label, value, color) = _top[i];
            if (id == "laps" && plan is { } pl) color = pl.LapsOfFuel < lowLaps ? p.P.DangerText : pl.LapsOfFuel < lowLaps * 2 ? p.P.WarnText : color;
            var row = new RectangleF(area.X + col * (cw + colGap), area.Y + line * rowH, cw, rowH);
            if (line % 2 == 0) p.Fill(row, p.Solid(p.Raised(p.P.Dark ? 0.06 : 0.045), 90), p.CornerRadius(2));
            var inner = row.Inflated(-6 * p.Density, 0);
            float vw = Math.Min(inner.Width * 0.6f, p.Measure(value, p.Bold) + 12 * p.Density);
            var valueBox = inner.Right(vw, out var labelBox);
            p.Text(p.LabelText(label), p.Label, p.LabelColor, labelBox, HAlign.Left, VAlign.Center);
            if (id == "pitexit") DrawPitExit(p, v, valueBox, HAlign.Right);
            else p.Number(value, p.Bold, color ?? p.P.Heading, valueBox, HAlign.Right, VAlign.Center);
            p.Item("top", id, row);
        }
    }

    private static float HeadHeight(HudPainter p) => p.LineHeight(p.Label) + 4 * p.Density;

    private static RectangleF SectionHead(HudPainter p, RectangleF box, string title, string? right)
    {
        var head = box.Top(HeadHeight(p), out var rest);
        var line = new RectangleF(head.X, head.Y, head.Width, head.Height - 3 * p.Density);
        float tw = p.Measure(p.LabelText(title), p.Label) + 8 * p.Density;
        p.Text(p.LabelText(title), p.Label, p.P.AccentText, line, HAlign.Left, VAlign.Center, false);
        if (right is not null) p.Text(p.LabelText(right), p.Label, p.LabelColor, new RectangleF(line.X + tw, line.Y, Math.Max(0, line.Width - tw), line.Height), HAlign.Right, VAlign.Center);
        return rest;
    }

    private static void TablePart(HudPainter p, HudView v, RectangleF r, double level, FuelPlan? plan)
    {
        r = SectionHead(p, r, "Rates", null);
        var cols = v.Options.Columns("cols");
        var rows = v.Options.Columns("rows");
        float rowH = p.LineHeight(p.Bold) + 5 * p.Density, headH = p.LineHeight(p.Label) + 4;
        int rowsFit = Math.Clamp((int)((r.Height - headH + 2) / (rowH + 2)), 0, rows.Count);
        for (int i = rowsFit; i < rows.Count; i++) p.Overflow?.Add(("rows", rows[i]));
        if (rowsFit > 0) Table(p, v, new RectangleF(r.X, r.Y, r.Width, headH + rowsFit * (rowH + 2) - 2), cols, rows, rowsFit, rowH, headH, level, plan);
    }

    private static void Table(HudPainter p, HudView v, RectangleF r, IReadOnlyList<string> cols, IReadOnlyList<string> rows, int rowsFit,
        float rowH, float headH, double level, FuelPlan? plan)
    {
        var u = v.Units;
        var data = v.Race.FuelData;
        double? Rate(string col) => col switch
        {
            "last" => data.Laps.Count > 0 && data.Laps[^1].FuelUsedLiters > 0 ? data.Laps[^1].FuelUsedLiters : null,
            "avg" => plan?.PerLap ?? v.Race.FuelPerLap,
            "max" => MostOnOneLap(data.Laps),
            _ => null,
        };
        double reserve = plan is { ToFinish: { } tf, LapsToGo: { } tg } ? Math.Max(0, tf - tg * plan.PerLap) : 0;
        double? togo = plan?.LapsToGo ?? (v.Frame.SessionKind == SessionKind.Race ? v.Race.LapsToGo(v.Frame) : null);
        float widest = 0;
        for (int i = 0; i < rowsFit; i++) widest = Math.Max(widest, p.Measure(p.LabelText(RowLabel(rows[i])), p.Label));
        float labelW = Math.Min(r.Width * 0.34f, widest + 14 * p.Density);
        float cw = (r.Width - labelW) / cols.Count;
        int avgAt = -1;
        for (int c = 0; c < cols.Count; c++) if (cols[c] == "avg") avgAt = c;
        for (int c = 0; c < cols.Count; c++)
        {
            string name = cols[c] switch { "last" => "Last", "avg" => "Avg", "max" => "Max", _ => "" };
            p.Text(p.LabelText(name), p.Label, c == avgAt ? p.P.AccentText : p.LabelColor, new RectangleF(r.X + labelW + c * cw, r.Y, cw, headH), HAlign.Center, VAlign.Center, false);
            p.Item("cols", cols[c], new RectangleF(r.X + labelW + c * cw, r.Y, cw, headH));
        }
        p.Text(RaceFormat.FuelUnit(u), p.Label, p.LabelColor, new RectangleF(r.X + 7 * p.Density, r.Y, labelW, headH), HAlign.Left, VAlign.Center, false);
        float y = r.Y + headH;
        for (int i = 0; i < rowsFit; i++)
        {
            var row = new RectangleF(r.X, y, r.Width, rowH);
            if (i % 2 == 0) p.Fill(row, p.Solid(p.Raised(p.P.Dark ? 0.06 : 0.045), 90), p.CornerRadius(2));
            p.Text(p.LabelText(RowLabel(rows[i])), p.Label, p.LabelColor, new RectangleF(row.X + 7 * p.Density, row.Y, labelW - 7 * p.Density, row.Height), HAlign.Left, VAlign.Center);
            p.Item("rows", rows[i], new RectangleF(row.X, row.Y, labelW, row.Height));
            for (int c = 0; c < cols.Count; c++)
            {
                double? rate = Rate(cols[c]);
                string text = RaceFormat.Missing;
                Color color = c == avgAt ? p.P.Heading : p.P.Text;
                if (rate is { } x && x > 0)
                {
                    switch (rows[i])
                    {
                        case "perlap": text = FuelNumber(x, u); break;
                        case "laps":
                            double laps = level / x;
                            text = PooledText.Number("", laps, "0.00", System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "refuel":
                            if (togo is { } t)
                            {
                                double add = Math.Max(0, t * x + reserve - level);
                                text = FuelNumber(add, u);
                                if (add <= 0) color = p.P.GoodText;
                            }
                            break;
                    }
                }
                p.Text(text, c == avgAt ? p.Bold : p.Body, color, new RectangleF(r.X + labelW + c * cw, row.Y, cw, row.Height), HAlign.Center, VAlign.Center);
            }
            y += rowH + 2;
        }
    }

    private static double? MostOnOneLap(IReadOnlyList<FuelLapSample> laps)
    {
        double? most = null;
        for (int i = 0; i < laps.Count; i++)
            if (laps[i].IsEligible && (most is null || laps[i].FuelUsedLiters > most)) most = laps[i].FuelUsedLiters;
        return most;
    }

    private static string RowLabel(string id) => id switch { "perlap" => "Per lap", "laps" => "Laps", "refuel" => "Refuel", _ => id };

    private static string FuelNumber(double litres, Config.HudUnits u)
    {
        double x = RaceFormat.FuelValue(litres, u);
        return PooledText.Number("", x, "0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static (Color Fill, string Label, string Value) State(HudPainter p, HudView v, FuelPlan? plan, int measured)
    {
        var u = v.Units;
        var neutral = p.Raised(0.12);
        if (plan is null) return (neutral, "MEASURING", measured == 0 ? "After 1 clean lap" : "…");
        if (plan.Reliability is EstimateReliability.Learning or EstimateReliability.Low && plan.LapsToGo is not null)
            return (p.P.Warn, "LOW CONFIDENCE", RaceFormat.Count(measured, "clean lap"));
        if (plan.LapsToGo is null) return (neutral, "PER LAP", RaceFormat.Fuel(plan.PerLap, u));
        if (plan.Stops == 0) return (p.P.Good, "NO STOP · EST.", PooledText.Concat("+", RaceFormat.Fuel(plan.Surplus ?? 0, u)));
        if (plan.Stops is not { } stops) return (p.P.Danger, "SHORT · EST.", RaceFormat.Fuel(-(plan.Surplus ?? 0), u));
        string add = plan.AddAtNextStop is { } a ? PooledText.Concat("Add ", RaceFormat.Fuel(a, u)) : "Pit";
        string many = stops > 1 ? PooledText.Number(" · ", stops, " STOPS") : "";
        return plan.WindowOpensIn is <= 0
            ? (p.P.Good, PooledText.Concat("WINDOW OPEN", many), add)
            : (p.P.Warn, PooledText.Concat(plan.WindowOpensIn is { } o ? PooledText.Number("WINDOW IN ", o, "0.00", System.Globalization.CultureInfo.InvariantCulture, " LAPS") : "PIT NEEDED", many), add);
    }

    internal static string Confidence(EstimateReliability r) => r switch
    {
        EstimateReliability.High => "settled",
        EstimateReliability.Moderate => "fair",
        EstimateReliability.Low => "low confidence",
        _ => "learning",
    };

    private static (string Id, string Label, string Value, Color? Color)? Cell(string id, HudPainter p, HudView v, double level, FuelPlan? plan)
    {
        var r = v.Race;
        var f = v.Frame;
        var u = v.Units;
        switch (id)
        {
            case "level": return (id, "TANK", RaceFormat.Fuel(level, u), null);
            case "perlap": return (id, "PER LAP", r.FuelPerLap is { } pl ? RaceFormat.Fuel(pl, u) : RaceFormat.Missing, null);
            case "last": return (id, "LAST LAP", r.FuelData.Laps.Count > 0 ? RaceFormat.Fuel(r.FuelData.Laps[^1].FuelUsedLiters, u) : RaceFormat.Missing, null);
            case "max": return (id, "MOST", MostOnOneLap(r.FuelData.Laps) is { } mx ? RaceFormat.Fuel(mx, u) : RaceFormat.Missing, null);
            case "atflag":
                return (id, "AT THE FLAG", plan?.AtFlag is { } at ? PooledText.Concat(at < 0 ? "−" : "", RaceFormat.Fuel(Math.Abs(at), u)) : RaceFormat.Missing,
                    plan?.AtFlag is < 0 ? p.P.DangerText : null);
            case "laps": return (id, "LAPS LEFT", plan is null ? RaceFormat.Missing : PooledText.Number("", plan.LapsOfFuel, "0.00", System.Globalization.CultureInfo.InvariantCulture), null);
            case "time":
                double? pace = r.Laps.Laps.Count > 0 ? LapMath.Stats(r.Laps.Laps, 5)?.Average : f.LapBest;
                return (id, "TIME LEFT", plan is not null && pace is { } pc ? RaceFormat.Clock(plan.LapsOfFuel * pc) : RaceFormat.Missing, null);
            case "togo": return (id, "LAPS TO GO", plan?.LapsToGo is { } tg ? PooledText.Number("", tg, "0.00", System.Globalization.CultureInfo.InvariantCulture) : RaceFormat.Missing, null);
            case "tofinish": return (id, "TO FINISH", plan?.ToFinish is { } tf ? RaceFormat.Fuel(tf, u) : RaceFormat.Missing, null);
            case "surplus":
                return (id, plan?.Surplus is < 0 ? "SHORT" : "SPARE", plan?.Surplus is { } s ? RaceFormat.Fuel(Math.Abs(s), u) : RaceFormat.Missing,
                    plan?.Surplus is < 0 ? p.P.DangerText : plan?.Surplus is >= 0 ? p.P.GoodText : null);
            case "add":
                return (id, "ADD", plan?.Stops is 0 ? "No stop" : plan?.AddAtNextStop is { } add ? RaceFormat.Fuel(add, u) : RaceFormat.Missing, plan?.Stops is 0 ? p.P.GoodText : null);
            case "stops": return (id, "STOPS", plan?.Stops is { } st ? st.ToString() : RaceFormat.Missing, null);
            case "pitexit":
                var (exit, exitColor) = PitExitValue(p, v);
                return (id, "PIT EXIT", exit, exitColor);
            case "window":
                string win = plan?.Stops switch
                {
                    0 => "Not needed",
                    > 0 when plan.WindowOpensIn is { } o => o <= 0 ? "Open" : PooledText.Number("In ", o, "0.00", System.Globalization.CultureInfo.InvariantCulture, " LAPS"),
                    _ => RaceFormat.Missing,
                };
                return (id, "PIT WINDOW", win, plan?.WindowOpensIn is <= 0 && plan.Stops > 0 ? p.P.GoodText : null);
            default: return null;
        }
    }
}
