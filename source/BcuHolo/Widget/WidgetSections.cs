using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BcuHolo.Modules.Sensors;

namespace BcuHolo.Widget
{
    /// <summary>What the widget can show, how it's grouped in the menu, and the presets.</summary>
    internal static class WidgetCatalog
    {
        internal sealed record Entry(string Id, string Label, string Section);

        // Order here is display order inside each section.
        public static readonly Entry[] Items =
        {
            new("Header", "Header bar (clock, uptime, live)", "Header"),
            new("CpuDial", "CPU dial (package temp, load ring)", "Thermals"),
            new("GpuDial", "GPU dial (temp, load ring)", "Thermals"),
            new("Threads", "Per-thread load heatmap", "CPU cores"),
            new("CoreTemps", "Per-core temperatures", "CPU cores"),
            new("SparkCpuLoad", "CPU load", "Trends"),
            new("SparkGpuLoad", "GPU load", "Trends"),
            new("SparkCpuTemp", "CPU temperature", "Trends"),
            new("SparkGpuTemp", "GPU temperature", "Trends"),
            new("SparkCpuPower", "CPU power", "Trends"),
            new("SparkGpuPower", "GPU power", "Trends"),
            new("Clocks", "CPU + GPU clocks", "Clocks"),
            new("Power", "CPU + GPU power, total", "Power"),
            new("Vcore", "Vcore, peak, VID", "Raptor Lake watch"),
            new("Whea", "WHEA error count", "Raptor Lake watch"),
            new("Ram", "Memory", "Memory"),
            new("Vram", "GPU memory (VRAM)", "Memory"),
            new("Fans", "GPU + board fans", "Cooling"),
            new("Drive", "Hottest drive", "Cooling"),
            new("Net", "Network down/up", "I/O"),
            new("Disk", "Disk read/write", "I/O"),
        };

        public static readonly (string Name, string[] Ids)[] Presets =
        {
            ("Minimal", new[] { "Header", "CpuDial", "GpuDial", "Ram", "Vram" }),
            ("Gamer", new[] { "Header", "CpuDial", "GpuDial", "SparkCpuLoad", "SparkGpuLoad", "SparkGpuTemp", "SparkGpuPower", "Clocks", "Power", "Fans", "Ram", "Vram", "Net" }),
            ("Enthusiast (everything)", Items.Select(i => i.Id).ToArray()),
            ("Raptor Lake watch", new[] { "Header", "CpuDial", "Threads", "CoreTemps", "SparkCpuTemp", "SparkCpuPower", "Clocks", "Power", "Vcore", "Whea", "Drive" }),
        };

        public static string[] Enthusiast => Presets[2].Ids;

        /// <summary>Ids from the first widget version, mapped to what replaced them.</summary>
        private static readonly Dictionary<string, string> Legacy = new()
        {
            ["CpuTemp"] = "CpuDial", ["CpuLoad"] = "SparkCpuLoad", ["GpuLoad"] = "SparkGpuLoad", ["GpuClock"] = "Clocks",
            ["CpuPower"] = "Power", ["GpuPower"] = "Power", ["Fan"] = "Fans",
        };

        public static readonly string[] OldDefault = { "GpuDial", "CpuTemp", "CpuLoad", "GpuLoad", "Fan", "Ram", "Vram" };

        /// <summary>Maps legacy ids, drops unknown ones, removes duplicates.</summary>
        public static List<string> Normalize(IEnumerable<string> ids)
        {
            var known = new HashSet<string>(Items.Select(i => i.Id));
            var list = new List<string>();
            foreach (var raw in ids ?? Enumerable.Empty<string>())
            {
                if (raw == null) continue;
                var id = Legacy.TryGetValue(raw, out var m) ? m : raw;
                if (known.Contains(id) && !list.Contains(id)) list.Add(id);
            }
            return list;
        }

        public static readonly (string Id, string Caption, string Hue, string Unit, double Hot)[] Sparks =
        {
            ("SparkCpuLoad", "CPU LOAD", "Accent", "%", double.NaN),
            ("SparkGpuLoad", "GPU LOAD", "Violet", "%", double.NaN),
            ("SparkCpuTemp", "CPU TEMP", "Accent", "°C", 90),
            ("SparkGpuTemp", "GPU TEMP", "Violet", "°C", 80),
            ("SparkCpuPower", "CPU POWER", "Accent", "W", double.NaN),
            ("SparkGpuPower", "GPU POWER", "Violet", "W", double.NaN),
        };

        public static Series SeriesFor(Telemetry t, string id) => id switch
        {
            "SparkCpuLoad" => t.CpuLoad, "SparkGpuLoad" => t.GpuLoad, "SparkCpuTemp" => t.CpuTemp,
            "SparkGpuTemp" => t.GpuTemp, "SparkCpuPower" => t.CpuPower, "SparkGpuPower" => t.GpuPower, _ => null,
        };

        // Thresholds shared by dials, sparklines and bars.
        public const double CpuHot = 90, GpuHot = 80;
    }

    /// <summary>One block of the widget. Built once per configuration change; Update mutates existing elements only.</summary>
    internal abstract class Part
    {
        public FrameworkElement Root;
        public abstract void Update(Telemetry t);

        protected static string TempHue(double v) => double.IsNaN(v) ? "Muted" : v >= 90 ? "Danger" : v >= 80 ? "Danger.CC" : v >= 60 ? "Accent" : "Good";
    }

    // ---------------- header ----------------

    internal sealed class HeaderPart : Part
    {
        private readonly Readout _clock = new(15, "Text", bold: true), _up = new(11, "Muted"), _hw = new(11, "Muted");
        private readonly Ellipse _dot = new() { Width = 7, Height = 7 }, _halo = new() { Width = 13, Height = 13, StrokeThickness = 1 };
        private bool _beat;
        private string _hwText;

        public HeaderPart()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tag = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap };
            tag.SetResourceReference(TextBlock.FontFamilyProperty, "Mono");
            tag.Inlines.Add(WUi.MakeRun("BCU", "Text"));
            tag.Inlines.Add(WUi.MakeRun(".", "Accent"));
            tag.Inlines.Add(WUi.MakeRun("HOLO", "Text"));
            tag.Inlines.Add(WUi.MakeRun("  //  ", "Muted"));
            tag.Inlines.Add(WUi.MakeRun("TELEMETRY", "Accent"));
            _hw.Tb.TextTrimming = TextTrimming.CharacterEllipsis;
            _hw.Set("", "Muted");
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(tag); left.Children.Add(_hw.Tb);

            _dot.SetResourceReference(Shape.FillProperty, "Good");
            _halo.SetResourceReference(Shape.StrokeProperty, "Good.66");
            var dotHost = new Grid { Width = 13, Height = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
            dotHost.Children.Add(_halo); dotHost.Children.Add(_dot);
            var live = WUi.Label("LIVE", 11, "Good", bold: true);
            live.SetResourceReference(TextBlock.FontFamilyProperty, "Mono");
            _clock.Tb.Margin = new Thickness(10, 0, 0, 0);
            var row1 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            row1.Children.Add(dotHost); row1.Children.Add(live); row1.Children.Add(_clock.Tb);
            _up.Tb.HorizontalAlignment = HorizontalAlignment.Right;
            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 2, 0) };
            right.Children.Add(row1); right.Children.Add(_up.Tb);
            Grid.SetColumn(right, 1);
            g.Children.Add(left); g.Children.Add(right);

            // Linework: accent trace with a dog-leg on the left, tick ruler on the right, faint rule between.
            var lines = new Grid { Height = 7, Margin = new Thickness(0, 6, 0, 0), IsHitTestVisible = false };
            var rule = new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0.5, 0, 0) };
            rule.SetResourceReference(Shape.FillProperty, "Line.1F");
            var trace = new Path { Data = Geometry.Parse("M0,0.5 H64 L70,6.5 H118"), StrokeThickness = 1.2, HorizontalAlignment = HorizontalAlignment.Left };
            trace.SetResourceReference(Shape.StrokeProperty, "Accent.99");
            var ticks = new Path { Data = Geometry.Parse("M0,0 V4 M6,0 V3 M12,0 V3 M18,0 V3 M24,0 V6 M30,0 V3 M36,0 V3 M42,0 V3 M48,0 V6"), StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 2, 0) };
            ticks.SetResourceReference(Shape.StrokeProperty, "Line.66");
            lines.Children.Add(rule); lines.Children.Add(trace); lines.Children.Add(ticks);

            var root = new StackPanel { Margin = new Thickness(2, 0, 0, 0) };
            root.Children.Add(g); root.Children.Add(lines);
            Root = root;
        }

        public override void Update(Telemetry t)
        {
            _clock.Set(DateTime.Now.ToString("HH:mm:ss"));
            var u = t.Uptime;
            _up.Set($"UP {u.Days}d {u.Hours:00}:{u.Minutes:00}:{u.Seconds:00}", "Muted");
            string hw = string.Join("  ·  ", new[] { t.CpuName, t.GpuName }.Where(s => !string.IsNullOrEmpty(s)));
            if (hw != _hwText) { _hwText = hw; _hw.Set(hw, "Muted"); }
            // Heartbeat on the 1 Hz sample instead of a running animation: the window is layered, so a continuous animation would
            // repaint the whole surface every frame.
            _beat = !_beat;
            _dot.Opacity = _beat ? 1 : 0.45;
            _halo.Opacity = _beat ? 1 : 0;
        }
    }

    // ---------------- thermals (dials) ----------------

    internal sealed class ThermalPart : Part
    {
        private readonly WDial _cpu, _gpu;

        public ThermalPart(double width, bool cpu, bool gpu)
        {
            double inner = WUi.Inner(width);
            int count = (cpu ? 1 : 0) + (gpu ? 1 : 0);
            double size = Math.Floor(Math.Min(176, count == 2 ? (inner - 8) / 2 : inner));
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            if (cpu) { _cpu = new WDial(size, "CPU PACKAGE", "°C", 100, 5, 4); row.Children.Add(_cpu); }
            if (gpu)
            {
                _gpu = new WDial(size, "GPU CORE", "°C", 100, 5, 4);
                if (cpu) _gpu.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(_gpu);
            }
            Root = WUi.Panel(null, width, row, out _);
        }

        public override void Update(Telemetry t)
        {
            if (_cpu != null)
            {
                double v = t.CpuTemp.Last, load = t.CpuLoad.Last;
                _cpu.Set(v, WUi.Num(v, "0"), v >= WidgetCatalog.CpuHot ? "Danger" : "Good",
                    double.IsNaN(load) ? double.NaN : load / 100, "LOAD " + (WUi.Num(load, "0", "%") ?? "--"), "Accent",
                    (WUi.Num(t.CpuPower.Last, "0", " W") ?? "-- W") + "  ▲" + (WUi.Num(t.CpuTemp.Max, "0", "°") ?? "--"), t.CpuTemp.Max);
            }
            if (_gpu != null)
            {
                double v = t.GpuTemp.Last, load = t.GpuLoad.Last;
                _gpu.Set(v, WUi.Num(v, "0"), v >= WidgetCatalog.GpuHot ? "Danger" : "Good",
                    double.IsNaN(load) ? double.NaN : load / 100, "LOAD " + (WUi.Num(load, "0", "%") ?? "--"), "Violet",
                    (WUi.Num(t.GpuPower.Last, "0", " W") ?? "-- W") + "  ▲" + (WUi.Num(t.GpuTemp.Max, "0", "°") ?? "--"), t.GpuTemp.Max);
            }
        }
    }

    // ---------------- CPU cores (heatmap + per-core temps) ----------------

    internal sealed class CoresPart : Part
    {
        private const double Gutter = 36, CoreGap = 4, GroupGap = 12, ThreadGap = 1, LoadH = 30, TempH = 20;
        private readonly bool _threads, _temps;
        private readonly double _inner;
        private readonly Border _host = new();
        private readonly Readout _right;
        private string _topo;
        private readonly List<(CoreInfo Core, VBar[] Cells, VBar Temp)> _cores = new();

        public CoresPart(double width, bool threads, bool temps)
        {
            _threads = threads; _temps = temps; _inner = WUi.Inner(width);
            _host.Child = WUi.Label("Waiting for per-core sensors…");
            Root = WUi.Panel("CPU cores", width, _host, out _right);
        }

        private void Build(Telemetry t)
        {
            _topo = t.Topology;
            _cores.Clear();
            if (t.Cores.Count == 0) { _host.Child = WUi.Label("Waiting for per-core sensors…"); return; }

            var groups = t.Cores.Select(c => c.Group).Distinct().ToList();
            int threads = t.Cores.Sum(c => Math.Max(1, c.Threads));
            double fixedW = Gutter + (groups.Count - 1) * GroupGap + (t.Cores.Count - groups.Count) * CoreGap
                            + t.Cores.Sum(c => (Math.Max(1, c.Threads) - 1) * ThreadGap);
            double cell = Math.Floor(Math.Clamp((_inner - fixedW) / Math.Max(1, threads), 4, 16));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gutter) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var gutter = new StackPanel();
            gutter.Children.Add(new Border { Height = 16 });
            if (_threads) gutter.Children.Add(new Border { Height = LoadH, Child = WUi.Label("LOAD") });
            if (_temps) gutter.Children.Add(new Border { Height = TempH, Margin = new Thickness(0, _threads ? 3 : 0, 0, 0), Child = WUi.Label("TEMP") });
            grid.Children.Add(gutter);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(row, 1);
            grid.Children.Add(row);
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var members = t.Cores.Where(c => c.Group == groups[gi]).ToList();
                int nThreads = members.Sum(c => Math.Max(1, c.Threads));
                double groupW = members.Sum(c => Math.Max(1, c.Threads) * cell + (Math.Max(1, c.Threads) - 1) * ThreadGap) + (members.Count - 1) * CoreGap;
                string name = groups[gi] == "P" ? "P-CORES" : groups[gi] == "E" ? "E-CORES" : "CORES";

                var col = new StackPanel { Margin = new Thickness(gi > 0 ? GroupGap : 0, 0, 0, 0) };
                var label = WUi.Label($"{name} {members.Count}C/{nThreads}T", 11, "Muted");
                label.Width = Math.Max(groupW, 20);
                label.Height = 16;
                col.Children.Add(label);

                var loadRow = new StackPanel { Orientation = Orientation.Horizontal, Visibility = _threads ? Visibility.Visible : Visibility.Collapsed };
                var tempRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, _threads ? 3 : 0, 0, 0), Visibility = _temps ? Visibility.Visible : Visibility.Collapsed };
                for (int ci = 0; ci < members.Count; ci++)
                {
                    var c = members[ci];
                    int n = Math.Max(1, c.Threads);
                    double coreW = n * cell + (n - 1) * ThreadGap;
                    var cells = new VBar[n];
                    var block = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, ci < members.Count - 1 ? CoreGap : 0, 0) };
                    for (int k = 0; k < n; k++)
                    {
                        cells[k] = new VBar(cell, LoadH) { Margin = new Thickness(0, 0, k < n - 1 ? ThreadGap : 0, 0) };
                        block.Children.Add(cells[k]);
                    }
                    loadRow.Children.Add(block);
                    var temp = new VBar(coreW, TempH) { Margin = new Thickness(0, 0, ci < members.Count - 1 ? CoreGap : 0, 0) };
                    tempRow.Children.Add(temp);
                    _cores.Add((c, cells, temp));
                }
                col.Children.Add(loadRow);
                col.Children.Add(tempRow);
                row.Children.Add(col);
            }
            _host.Child = grid;
        }

        public override void Update(Telemetry t)
        {
            if (t.Topology != _topo) Build(t);
            CoreInfo hottest = null;
            double tempSum = 0;
            int tempN = 0;
            foreach (var (c, cells, temp) in _cores)
            {
                for (int k = 0; k < cells.Length; k++)
                {
                    double v = c.Load[k];
                    string track = double.IsNaN(v) ? "Line.12" : v >= 75 ? "Accent.40" : v >= 40 ? "Accent.29" : v >= 10 ? "Accent.1A" : "Line.12";
                    cells[k].Set(double.IsNaN(v) ? 0 : v / 100, v >= 98 ? "Text" : "Accent", track, "Line.00");
                }
                if (!double.IsNaN(c.Temp))
                {
                    tempSum += c.Temp; tempN++;
                    if (hottest == null || c.Temp > hottest.Temp) hottest = c;
                }
            }
            foreach (var (c, _, temp) in _cores)
            {
                double v = c.Temp;
                string hue = TempHue(v);
                temp.Set(double.IsNaN(v) ? 0 : (v - 25) / 75, hue == "Muted" ? "Line.24" : hue, "Line.12", c == hottest ? "Text.CC" : "Line.00");
            }

            if (_temps && hottest != null)
                _right.Set($"HOT {hottest.Label} {hottest.Temp:0}°  AVG {tempSum / tempN:0}°", hottest.Temp >= WidgetCatalog.CpuHot ? "Danger" : "Muted");
            else if (_threads && !double.IsNaN(t.CpuLoad.Last))
                _right.Set($"TOTAL {t.CpuLoad.Last:0}%", "Muted");
            else _right.Set("", "Muted");
        }
    }

    // ---------------- trends (sparklines) ----------------

    internal sealed class TrendsPart : Part
    {
        private sealed class Cell
        {
            public string Id, Hue, Unit; public double Hot;
            public Readout Value, MinMax; public Graph Graph;
        }

        private readonly List<Cell> _cells = new();

        public TrendsPart(double width, IReadOnlyList<(string Id, string Caption, string Hue, string Unit, double Hot)> defs)
        {
            double inner = WUi.Inner(width);
            const double gap = 10;
            int cols = inner >= 600 ? 3 : inner >= 260 ? 2 : 1;
            cols = Math.Min(cols, Math.Max(1, defs.Count));
            double cellW = Math.Floor(inner / cols - gap);
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = cols, Margin = new Thickness(-gap / 2, 0, -gap / 2, 0) };
            foreach (var d in defs)
            {
                var c = new Cell { Id = d.Id, Hue = d.Hue, Unit = d.Unit, Hot = d.Hot, Value = new Readout(13, "Text", bold: true), MinMax = new Readout(11, "Muted"), Graph = new Graph(cellW, 26, d.Hue) };
                var head = new Grid { Height = 18 };
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cellW >= 180 ? 58 : 0) });
                head.Children.Add(WUi.Label(d.Caption, 11, "Muted", bold: true));
                c.MinMax.Tb.Margin = new Thickness(0, 0, 6, 0);
                Grid.SetColumn(c.MinMax.Tb, 1);
                c.Value.Tb.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(c.Value.Tb, cellW >= 180 ? 2 : 1);
                head.Children.Add(c.Value.Tb);

                var stack = new StackPanel { Margin = new Thickness(gap / 2, 0, gap / 2, 6) };
                stack.Children.Add(head);
                stack.Children.Add(c.Graph);
                if (cellW >= 180) head.Children.Add(c.MinMax.Tb);
                else { Grid.SetColumn(c.MinMax.Tb, 0); c.MinMax.Tb.Margin = new Thickness(0, 1, 0, 0); stack.Children.Add(c.MinMax.Tb); }
                grid.Children.Add(stack);
                _cells.Add(c);
            }
            Root = WUi.Panel("Trends · last 2 min", width, grid, out _);
        }

        public override void Update(Telemetry t)
        {
            foreach (var c in _cells)
            {
                var s = WidgetCatalog.SeriesFor(t, c.Id);
                if (s == null) continue;
                string fmt = "0";
                string suffix = c.Unit == "%" ? " %" : c.Unit == "°C" ? " °C" : " W";
                bool hot = !double.IsNaN(c.Hot) && s.Last >= c.Hot;
                c.Value.Set(WUi.Num(s.Last, fmt, suffix), hot ? "Danger" : "Text");
                c.MinMax.Set(double.IsNaN(s.Min) ? null : $"↓{s.Min.ToString(fmt)} ↑{s.Max.ToString(fmt)}", "Muted");
                c.Graph.SetHue(hot ? "Danger" : c.Hue);

                double lo, hi;
                if (c.Unit == "%") { lo = 0; hi = 100; }
                else
                {
                    s.Range(out lo, out hi);
                    if (!double.IsNaN(lo))
                    {
                        double minSpan = c.Unit == "°C" ? 15 : 20, mid = (lo + hi) / 2;
                        if (hi - lo < minSpan) { lo = mid - minSpan / 2; hi = mid + minSpan / 2; }
                        lo = Math.Max(0, lo - 2); hi += 2;
                    }
                }
                c.Graph.Draw(s, lo, hi);
            }
        }
    }

    // ---------------- clocks ----------------

    internal sealed class ClocksPart : Part
    {
        private readonly HBar _cpuMax = new(4, "Accent"), _cpuAvg = new(4, "Accent.80"), _gpuCore = new(4, "Violet"), _gpuMem = new(4, "Violet.80");
        private readonly Readout _vCpuMax = new(12), _vCpuAvg = new(12), _vGpuCore = new(12), _vGpuMem = new(12), _right;

        public ClocksPart(double width)
        {
            var s = new StackPanel();
            s.Children.Add(WUi.BarRow("CPU MAX", 58, _cpuMax, _vCpuMax, 70));
            s.Children.Add(WUi.BarRow("CPU AVG", 58, _cpuAvg, _vCpuAvg, 70));
            s.Children.Add(WUi.BarRow("GPU CORE", 58, _gpuCore, _vGpuCore, 70));
            s.Children.Add(WUi.BarRow("GPU MEM", 58, _gpuMem, _vGpuMem, 70));
            Root = WUi.Panel("Clocks", width, s, out _right);
        }

        public override void Update(Telemetry t)
        {
            double cpuRef = Math.Max(5000, double.IsNaN(t.CpuClockPeak) ? 0 : t.CpuClockPeak);
            _cpuMax.Set(t.CpuClockMax / cpuRef);
            _vCpuMax.Set(WUi.Num(t.CpuClockMax / 1000, "0.00", " GHz"));
            _cpuAvg.Set(t.CpuClockAvg / cpuRef);
            _vCpuAvg.Set(WUi.Num(t.CpuClockAvg / 1000, "0.00", " GHz"));
            _gpuCore.Set(t.GpuCoreMHz / (double.IsNaN(t.GpuMaxCoreMHz) ? 3000 : t.GpuMaxCoreMHz));
            _vGpuCore.Set(WUi.Num(t.GpuCoreMHz, "0", " MHz"));
            _gpuMem.Set(t.GpuMemMHz / (double.IsNaN(t.GpuMaxMemMHz) ? 12000 : t.GpuMaxMemMHz));
            _vGpuMem.Set(WUi.Num(t.GpuMemMHz, "0", " MHz"));
            _right.Set(double.IsNaN(t.CpuClockPeak) ? "" : "PEAK " + (t.CpuClockPeak / 1000).ToString("0.00"), "Muted");
        }
    }

    // ---------------- power ----------------

    internal sealed class PowerPart : Part
    {
        private readonly Readout _total = new(20, "Text", mono: false, bold: true, font: "Display"), _cpu = new(12), _gpu = new(12), _right;
        private readonly ScaleTransform _cpuScale = new(0, 1), _gpuScale = new(0, 1);
        private readonly TranslateTransform _gpuShift = new();
        private readonly double _barW;

        public PowerPart(double width)
        {
            double inner = WUi.Inner(width);
            _barW = inner;
            var s = new StackPanel();

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(WUi.Label("TOTAL"));
            _total.Tb.FontWeight = FontWeights.Bold;
            Grid.SetColumn(_total.Tb, 1);
            top.Children.Add(_total.Tb);
            s.Children.Add(top);

            var bar = new Grid { Width = inner, Height = 8, Margin = new Thickness(0, 3, 0, 5), ClipToBounds = true };
            var track = new Rectangle { RadiusX = 3, RadiusY = 3 };
            track.SetResourceReference(Shape.FillProperty, "Line.1F");
            var cpu = new Rectangle { RenderTransform = _cpuScale, RenderTransformOrigin = new Point(0, 0.5) };
            cpu.SetResourceReference(Shape.FillProperty, "Accent");
            var gpuT = new TransformGroup();
            gpuT.Children.Add(_gpuScale); gpuT.Children.Add(_gpuShift);
            var gpu = new Rectangle { RenderTransform = gpuT, RenderTransformOrigin = new Point(0, 0.5) };
            gpu.SetResourceReference(Shape.FillProperty, "Violet");
            bar.Children.Add(track); bar.Children.Add(cpu); bar.Children.Add(gpu);
            s.Children.Add(bar);

            s.Children.Add(Legend("CPU PKG", "Accent", _cpu));
            s.Children.Add(Legend("GPU BOARD", "Violet", _gpu));
            Root = WUi.Panel("Power", width, s, out _right);
        }

        private static Grid Legend(string label, string key, Readout value)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = WUi.Label(label);
            Grid.SetColumn(l, 1);
            Grid.SetColumn(value.Tb, 2);
            g.Children.Add(WUi.Swatch(key)); g.Children.Add(l); g.Children.Add(value.Tb);
            return g;
        }

        public override void Update(Telemetry t)
        {
            double c = t.CpuPower.Last, g = t.GpuPower.Last, total = t.TotalPower.Last;
            _total.Set(WUi.Num(total, "0", " W"));
            double scale = Math.Max(150, double.IsNaN(t.TotalPower.Max) ? 0 : t.TotalPower.Max * 1.05);
            double cf = double.IsNaN(c) ? 0 : Math.Clamp(c / scale, 0, 1), gf = double.IsNaN(g) ? 0 : Math.Clamp(g / scale, 0, 1 - cf);
            _cpuScale.ScaleX = cf;
            _gpuScale.ScaleX = gf;
            _gpuShift.X = cf * _barW;
            _cpu.Set(WUi.Num(c, "0", " W"));
            _gpu.Set(double.IsNaN(g) ? null : g.ToString("0") + (double.IsNaN(t.GpuLimitW) ? " W" : " / " + t.GpuLimitW.ToString("0") + " W"));
            _right.Set(double.IsNaN(t.TotalPower.Max) ? "" : "PEAK " + t.TotalPower.Max.ToString("0") + " W", "Muted");
        }
    }

    // ---------------- Raptor Lake watch ----------------

    internal sealed class RaptorPart : Part
    {
        private const double Lo = 0.6, Hi = 1.6;
        private readonly Readout _v = new(18, "Text", bold: true), _peak = new(12), _vid = new(12), _whea = new(12), _ucode = new(12), _right;
        private readonly ScaleTransform _fillScale = new(0, 1);
        private readonly Paint _fill;
        private readonly Line _peakMark;
        private readonly double _w;
        private readonly FrameworkElement _vcoreTip;

        public RaptorPart(double width, bool vcore, bool whea)
        {
            _w = WUi.Inner(width);
            var s = new StackPanel();
            if (vcore)
            {
                var row = WUi.Row("VCORE", _v, 0);
                _vcoreTip = row;
                s.Children.Add(row);

                // 0.6–1.6 V scale with the warning zone above SensorsModule.VcoreWarn and a peak tick.
                var cv = new Canvas { Width = _w, Height = 10, Margin = new Thickness(0, 3, 0, 4), ClipToBounds = true };
                var track = new Rectangle { Width = _w, Height = 4, RadiusX = 2, RadiusY = 2 };
                track.SetResourceReference(Shape.FillProperty, "Line.1F");
                Canvas.SetTop(track, 3);
                double wx = X(SensorsModule.VcoreWarn);
                var warn = new Rectangle { Width = Math.Max(0, _w - wx), Height = 10 };
                warn.SetResourceReference(Shape.FillProperty, "Danger.33");
                Canvas.SetLeft(warn, wx);
                var fill = new Rectangle { Width = _w, Height = 4, RadiusX = 2, RadiusY = 2, RenderTransform = _fillScale };
                _fill = new Paint(fill, Shape.FillProperty, "Good");
                Canvas.SetTop(fill, 3);
                _peakMark = new Line { Y1 = 0, Y2 = 10, StrokeThickness = 2, Visibility = Visibility.Collapsed };
                _peakMark.SetResourceReference(Shape.StrokeProperty, "Text.CC");
                cv.Children.Add(track); cv.Children.Add(warn); cv.Children.Add(fill); cv.Children.Add(_peakMark);
                s.Children.Add(cv);

                var pair = new Grid();
                pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var a = WUi.Row("PEAK", _peak, 0);
                a.Margin = new Thickness(0, 0, 6, 0);
                a.ToolTip = "Highest Vcore / core VID since the widget opened (same sensors as the Sensors page's Peak Vcore)";
                var b = WUi.Row("VID", _vid, 0);
                b.Margin = new Thickness(6, 0, 0, 0);
                b.ToolTip = "Highest per-core VID right now";
                Grid.SetColumn(b, 1);
                pair.Children.Add(a); pair.Children.Add(b);
                s.Children.Add(pair);
            }
            if (whea)
            {
                var row = WUi.Row("WHEA", _whea, vcore ? 3 : 0);
                row.ToolTip = "WHEA-Logger events in the System log: since boot / last 30 days (checked every minute)";
                s.Children.Add(row);
            }
            // Own row: in the panel header it collided with the title at Compact width.
            var uc = WUi.Row("MICROCODE", _ucode, 3);
            uc.ToolTip = "CPU microcode revision. 0x12F or newer has Intel's Vmin Shift Instability fix.";
            s.Children.Add(uc);
            Root = WUi.Panel("Raptor Lake watch", width, s, out _right);
        }

        private double X(double volts) => Math.Clamp((volts - Lo) / (Hi - Lo), 0, 1) * _w;

        public override void Update(Telemetry t)
        {
            double warnV = SensorsModule.VcoreWarn;
            if (_fill != null)
            {
                double v = t.Vcore.Last, peak = t.VcorePeak;
                _v.Set(WUi.Num(v, "0.000", " V"), v > warnV ? "Danger" : "Text");
                _fillScale.ScaleX = double.IsNaN(v) ? 0 : X(v) / Math.Max(1, _w);
                _fill.Set(v > warnV ? "Danger" : "Good");
                _peak.Set(WUi.Num(peak, "0.000"), peak > warnV ? "Danger" : "Text");
                _vid.Set(WUi.Num(t.VidMax, "0.000"), t.VidMax > warnV ? "Danger" : "Text");
                if (double.IsNaN(peak)) _peakMark.Visibility = Visibility.Collapsed;
                else
                {
                    _peakMark.Visibility = Visibility.Visible;
                    double x = Math.Clamp(X(peak), 1, _w - 1);
                    _peakMark.X1 = x; _peakMark.X2 = x;
                }
                string tip = string.IsNullOrEmpty(t.VcoreSource) ? "No Vcore sensor (needs PawnIO)" : "Source: " + t.VcoreSource + $". Warn above {warnV:0.00} V.";
                if (!Equals(_vcoreTip.ToolTip, tip)) _vcoreTip.ToolTip = tip;
            }
            if (t.WheaBoot >= 0)
                _whea.Set($"{t.WheaBoot} boot · {(t.Whea30d >= 0 ? t.Whea30d.ToString() : "--")} 30d", t.WheaBoot > 0 ? "Danger" : t.Whea30d > 0 ? "Accent" : "Good");
            else _whea.Set(null);
            if (t.Microcode == 0) _ucode.Set(null);
            else _ucode.Set("0x" + t.Microcode.ToString("X") + (t.Microcode >= 0x12F ? " ✓" : " update BIOS"), t.Microcode >= 0x12F ? "Good" : "Danger");
            _right.Set("", "Muted");
        }
    }

    // ---------------- memory ----------------

    internal sealed class MemoryPart : Part
    {
        private readonly MiniRing _ram, _vram;
        private readonly Readout _ramV = new(12), _vramV = new(12);

        public MemoryPart(double width, bool ram, bool vram)
        {
            var s = new StackPanel();
            if (ram) { _ram = new MiniRing(34, 4, "Blue"); s.Children.Add(Row(_ram, "RAM", _ramV, false)); }
            if (vram) { _vram = new MiniRing(34, 4, "Violet"); s.Children.Add(Row(_vram, "VRAM", _vramV, ram)); }
            Root = WUi.Panel("Memory", width, s, out _);
        }

        private static Grid Row(MiniRing ring, string label, Readout value, bool gap)
        {
            var g = new Grid { Margin = new Thickness(0, gap ? 4 : 0, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(WUi.Label(label, 11, "Muted", bold: true));
            text.Children.Add(value.Tb);
            Grid.SetColumn(text, 1);
            g.Children.Add(ring); g.Children.Add(text);
            return g;
        }

        public override void Update(Telemetry t)
        {
            if (_ram != null) Set(_ram, _ramV, t.RamUsedGB, t.RamTotalGB, "Blue");
            if (_vram != null) Set(_vram, _vramV, t.VramUsedGB, t.VramTotalGB, "Violet");
        }

        private static void Set(MiniRing ring, Readout v, double used, double total, string hue)
        {
            bool ok = !double.IsNaN(used) && !double.IsNaN(total) && total > 0;
            double f = ok ? used / total : double.NaN;
            ring.Set(f, f >= 0.9 ? "Danger" : hue);
            v.Set(ok ? $"{used:0.0} / {total:0} GB" : null);
        }
    }

    // ---------------- cooling + drive ----------------

    internal sealed class CoolingPart : Part
    {
        private const int MaxBoardFans = 2;
        private readonly Readout _gpuFan = new(12), _drive = new(12);
        private readonly Readout[] _fanV = new Readout[MaxBoardFans];
        private readonly TextBlock[] _fanL = new TextBlock[MaxBoardFans];
        private readonly FrameworkElement[] _fanRow = new FrameworkElement[MaxBoardFans];
        private readonly FrameworkElement _driveRow;
        private readonly bool _fans;

        public CoolingPart(double width, bool fans, bool drive)
        {
            _fans = fans;
            var s = new StackPanel();
            if (fans)
            {
                s.Children.Add(WUi.Row("GPU FAN", _gpuFan, 0));
                for (int i = 0; i < MaxBoardFans; i++)
                {
                    _fanV[i] = new Readout(12);
                    var row = WUi.Row("FAN", _fanV[i]);
                    _fanL[i] = (TextBlock)row.Children[0];
                    row.Visibility = Visibility.Collapsed;
                    _fanRow[i] = row;
                    s.Children.Add(row);
                }
            }
            if (drive)
            {
                var row = WUi.Row("HOTTEST DRIVE", _drive, fans ? 2 : 0);
                _driveRow = row;
                s.Children.Add(row);
            }
            Root = WUi.Panel("Cooling", width, s, out _);
        }

        public override void Update(Telemetry t)
        {
            if (_fans)
            {
                _gpuFan.Set(t.GpuFans.Length == 0 ? null : string.Join(" · ", t.GpuFans.Select(f => f.ToString())) + " %");
                for (int i = 0; i < MaxBoardFans; i++)
                {
                    bool has = i < t.BoardFans.Count;
                    var vis = has ? Visibility.Visible : Visibility.Collapsed;
                    if (_fanRow[i].Visibility != vis) _fanRow[i].Visibility = vis;
                    if (!has) continue;
                    string name = t.BoardFans[i].Key.ToUpperInvariant();
                    if (_fanL[i].Text != name) _fanL[i].Text = name;
                    _fanV[i].Set(t.BoardFans[i].Value.ToString("0") + " RPM");
                }
            }
            if (_driveRow != null)
            {
                _drive.Set(WUi.Num(t.DriveTemp, "0", " °C"), t.DriveTemp >= 70 ? "Danger" : "Text");
                if (!Equals(_driveRow.ToolTip, t.DriveName)) _driveRow.ToolTip = string.IsNullOrEmpty(t.DriveName) ? null : t.DriveName;
            }
        }
    }

    // ---------------- network + disk ----------------

    internal sealed class IoPart : Part
    {
        private readonly Readout _down = new(12, "Accent"), _up = new(12, "Violet"), _read = new(12), _write = new(12);
        private readonly Graph _graph;
        private readonly bool _net, _disk;

        public IoPart(double width, bool net, bool disk)
        {
            _net = net; _disk = disk;
            var s = new StackPanel();
            if (net)
            {
                var pair = new Grid();
                pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var d = WUi.Row("↓", _down, 0); d.Margin = new Thickness(0, 0, 6, 0);
                var u = WUi.Row("↑", _up, 0); u.Margin = new Thickness(6, 0, 0, 0);
                Grid.SetColumn(u, 1);
                pair.Children.Add(d); pair.Children.Add(u);
                pair.ToolTip = "Network throughput from local adapter counters (adapters with a gateway)";
                s.Children.Add(pair);
                _graph = new Graph(WUi.Inner(width), 22, "Accent", "Violet") { Margin = new Thickness(0, 3, 0, 0) };
                s.Children.Add(_graph);
            }
            if (disk)
            {
                var pair = new Grid { Margin = new Thickness(0, net ? 4 : 0, 0, 0) };
                pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var r = WUi.Row("R", _read, 0); r.Margin = new Thickness(0, 0, 6, 0);
                var w = WUi.Row("W", _write, 0); w.Margin = new Thickness(6, 0, 0, 0);
                Grid.SetColumn(w, 1);
                pair.Children.Add(r); pair.Children.Add(w);
                pair.ToolTip = "Disk read / write, all physical disks (Windows PhysicalDisk counters)";
                s.Children.Add(WUi.Label("DISK", 11, "Muted", bold: true));
                s.Children.Add(pair);
            }
            Root = WUi.Panel(net ? "Network" + (disk ? " · disk" : "") : "Disk", width, s, out _);
        }

        public override void Update(Telemetry t)
        {
            if (_net)
            {
                _down.Set(WUi.Bits(t.NetDown.Last), "Accent");
                _up.Set(WUi.Bits(t.NetUp.Last), "Violet");
                t.NetDown.Range(out _, out var hd);
                t.NetUp.Range(out _, out var hu);
                double hi = Math.Max(1e6, Math.Max(double.IsNaN(hd) ? 0 : hd, double.IsNaN(hu) ? 0 : hu));
                _graph.Draw(t.NetDown, 0, hi * 1.1, t.NetUp);
            }
            if (_disk)
            {
                _read.Set(WUi.Bytes(t.DiskRead));
                _write.Set(WUi.Bytes(t.DiskWrite));
            }
        }
    }
}
