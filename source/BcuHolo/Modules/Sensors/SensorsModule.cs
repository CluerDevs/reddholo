using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BcuHolo.Plugins;

namespace BcuHolo.Modules.Sensors
{
    public sealed class SensorsModule : IHoloModule
    {
        // Above this the 13700K (Raptor Lake degradation history) is being fed too much voltage.
        internal const double VcoreWarn = 1.50;

        public string Id => "sensors";
        public string Title => "Sensors";
        public string Glyph => "";
        public int Order => 40;

        public FrameworkElement CreateView(IHoloHost host) => new SensorsView();
        public FrameworkElement CreateTile(IHoloHost host) => new SensorsTile(host).Root;

        // ---- shared helpers ----

        /// <summary>
        /// Keeps <paramref name="onUpdate"/> subscribed to SensorHub only while <paramref name="el"/> is on screen
        /// (loaded, visible, window not minimized), so a hidden page doesn't keep the 1 Hz sensor loop running.
        /// </summary>
        internal static void SubscribeWhileShown(FrameworkElement el, Action onUpdate) =>
            WhileShown(el, on => { if (on) { SensorHub.Subscribe(onUpdate); onUpdate(); } else SensorHub.Unsubscribe(onUpdate); });

        /// <summary>Calls <paramref name="set"/>(true/false) as <paramref name="el"/> comes on/off screen (loaded, visible, window not minimized); always balanced.</summary>
        internal static void WhileShown(FrameworkElement el, Action<bool> set)
        {
            bool shown = false;
            Window win = null;
            void Update(object sender, EventArgs e)
            {
                bool on = el.IsLoaded && el.IsVisible && win?.WindowState != WindowState.Minimized;
                if (on == shown) return;
                shown = on;
                set(on);
            }
            el.Loaded += (_, _) =>
            {
                if (win != null) win.StateChanged -= Update;
                win = Window.GetWindow(el);
                if (win != null) win.StateChanged += Update;
                Update(null, null);
            };
            el.Unloaded += (_, _) =>
            {
                if (win != null) win.StateChanged -= Update;
                win = null;
                Update(null, null);
            };
            el.IsVisibleChanged += (_, _) => Update(null, null);
        }
        internal static T Res<T>(string key) => (T)Application.Current.FindResource(key);

        internal static string Fmt(double v, string unit)
        {
            switch (unit)
            {
                case "V": return v.ToString("0.000") + " V";
                case "W": return v.ToString("0.0") + " W";
                case "°C": return v.ToString("0.0") + " °C";
                case "%": return v.ToString("0") + " %";
                case "RPM": return v.ToString("0") + " RPM";
                case "MHz": return v.ToString("0") + " MHz";
                default: return v.ToString("0.##") + (string.IsNullOrEmpty(unit) ? "" : " " + unit);
            }
        }

        internal static TextBlock Text(string text, double size, string brush, string font = null, Thickness? margin = null)
        {
            var tb = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis };
            tb.SetResourceReference(TextBlock.ForegroundProperty, brush);
            if (font != null) tb.SetResourceReference(TextBlock.FontFamilyProperty, font);
            if (margin.HasValue) tb.Margin = margin.Value;
            return tb;
        }

        /// <summary>Highest voltage seen since start on Vcore-like sensors (null if none).</summary>
        internal static double? PeakVcore(IReadOnlyList<SensorEntry> all)
        {
            double? peak = null;
            foreach (var e in all)
            {
                var r = e.Reading;
                if (r.Unit != "V") continue;
                bool vcore = r.Name.IndexOf("Vcore", StringComparison.OrdinalIgnoreCase) >= 0
                          || r.Name.IndexOf("CPU Core", StringComparison.OrdinalIgnoreCase) >= 0;
                if (vcore && (!peak.HasValue || e.Max > peak.Value)) peak = e.Max;
            }
            return peak;
        }

        internal static SensorEntry Find(IReadOnlyList<SensorEntry> all, string name, string unit) =>
            // 0 means "no reading" here: without PawnIO, LHM still lists CPU power/temp sensors but leaves them at 0.
            all.FirstOrDefault(e => e.Reading.Unit == unit && e.Reading.Value > 0 && e.Reading.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

        internal static SensorEntry HottestDrive(IReadOnlyList<SensorEntry> all) =>
            // Per drive, use the composite "Temperature" sensor; NVMe controller sensors ("Temperature 2") run far hotter
            // and some drives report junk (109 °C idle). Fall back to any plausible sensor if a drive has no composite.
            all.Where(e => e.Reading.Unit == "°C" && e.Reading.Value is > 0 and < 95 && LhmSensorSource.StorageGroups.ContainsKey(e.Reading.Group))
               .GroupBy(e => e.Reading.Group)
               .Select(g => g.FirstOrDefault(e => e.Reading.Name == "Temperature") ?? g.OrderBy(e => e.Reading.Value).First())
               .OrderByDescending(e => e.Reading.Value).FirstOrDefault();

        internal static SensorEntry TopFan(IReadOnlyList<SensorEntry> all) =>
            all.Where(e => e.Reading.Unit == "RPM" && e.Reading.Value > 0).OrderByDescending(e => e.Reading.Value).FirstOrDefault();
    }

    /// <summary>Dashboard tile.</summary>
    internal sealed class SensorsTile
    {
        public readonly Border Root;
        private readonly TextBlock _temp = SensorsModule.Text("--", 34, "Text", "Display");
        private readonly StackPanel _rows = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        private readonly Action _onUpdate;
        // Rows built once; Refresh only toggles visibility and text (it runs every second).
        private readonly Grid _powRow, _vcoreRow, _driveRow, _fanRow;
        private readonly TextBlock _pow, _vcore, _drive, _fan;
        private readonly TextBlock _waiting = SensorsModule.Text("Waiting for sensors. CPU readings need the PawnIO driver.", 12, "Muted");
        private bool _vcoreDanger;

        public SensorsTile(IHoloHost host)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Style = SensorsModule.Res<Style>("Caps"), Text = "SENSORS" });
            stack.Children.Add(_temp);
            stack.Children.Add(SensorsModule.Text("CPU package", 12, "Muted"));
            stack.Children.Add(_rows);
            _powRow = AddRow("Package power", out _pow);
            _vcoreRow = AddRow("Peak Vcore", out _vcore);
            _driveRow = AddRow("Hottest SSD", out _drive);
            _fanRow = AddRow("Fan", out _fan);
            _waiting.Visibility = Visibility.Collapsed; // empty until the first Refresh, as before
            _rows.Children.Add(_waiting);
            var all = new Button { Style = SensorsModule.Res<Style>("Ghost"), Content = "All sensors", HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(14, 0, 14, 0) };
            all.Click += (_, _) => host.Navigate("sensors");
            stack.Children.Add(all);

            Root = new Border { Style = SensorsModule.Res<Style>("DashTile"), Child = stack };
            _onUpdate = Refresh;
            SensorsModule.SubscribeWhileShown(Root, _onUpdate);
        }

        private void Refresh()
        {
            try
            {
                var all = SensorHub.Snapshot;
                var t = SensorsModule.Find(all, "CPU Package", "°C");
                _temp.Text = t == null ? "--" : t.Reading.Value.ToString("0") + " °C";
                var p = SensorsModule.Find(all, "CPU Package", "W");
                Set(_powRow, _pow, p == null ? null : SensorsModule.Fmt(p.Reading.Value, "W"));
                var v = SensorsModule.PeakVcore(all);
                Set(_vcoreRow, _vcore, v.HasValue ? SensorsModule.Fmt(v.Value, "V") : null);
                bool danger = v.HasValue && v.Value > SensorsModule.VcoreWarn;
                if (danger != _vcoreDanger)
                {
                    _vcoreDanger = danger;
                    _vcore.SetResourceReference(TextBlock.ForegroundProperty, danger ? "Danger" : "Text");
                }
                var d = SensorsModule.HottestDrive(all);
                Set(_driveRow, _drive, d == null ? null : SensorsModule.Fmt(d.Reading.Value, "°C"));
                var f = SensorsModule.TopFan(all);
                Set(_fanRow, _fan, f == null ? null : SensorsModule.Fmt(f.Reading.Value, "RPM"));
                _waiting.Visibility = p == null && !v.HasValue && d == null && f == null ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("SensorsTile: " + ex.Message); }
        }

        private static void Set(Grid row, TextBlock value, string text)
        {
            row.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
            if (text != null) value.Text = text;
        }

        private Grid AddRow(string label, out TextBlock value)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 0), Visibility = Visibility.Collapsed };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(SensorsModule.Text(label, 13, "Muted"));
            value = SensorsModule.Text("", 13, "Text", "Mono");
            Grid.SetColumn(value, 1);
            g.Children.Add(value);
            _rows.Children.Add(g);
            return g;
        }
    }

    /// <summary>Full page: PawnIO notice, highlights strip, then one collapsible card per hardware group.</summary>
    internal sealed class SensorsView : UserControl
    {
        private const double SparkW = 120, SparkH = 16;
        private sealed class Row { public TextBlock Value, Min, Max; public Polyline Spark; }
        private sealed class Tile { public TextBlock Value, Reason; }

        private readonly Border _pawn;
        private readonly TextBlock _note;
        private readonly Tile _tPkg, _tPow, _tVcore, _tDrive;
        private readonly StackPanel _groups = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        private readonly Dictionary<string, Row> _rows = new Dictionary<string, Row>();
        private readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>(); // user overrides, survive rebuilds
        private readonly RadioButton _threadToggle;
        private bool _showThreads;
        private string _signature = "";
        private readonly Action _onUpdate;

        public SensorsView()
        {
            var root = new StackPanel { Margin = new Thickness(24) };

            _pawn = BuildPawnCard();
            root.Children.Add(_pawn);
            _note = SensorsModule.Text("CPU temperatures, power and VIDs need the PawnIO driver. Without it only load, drive and board sensors appear.", 12, "Muted", null, new Thickness(0, 0, 0, 12));
            _note.Visibility = Visibility.Collapsed;
            root.Children.Add(_note);

            var strip = new WrapPanel { Orientation = Orientation.Horizontal };
            _tPkg = AddTile(strip, "CPU PACKAGE");
            _tPow = AddTile(strip, "PACKAGE POWER");
            _tVcore = AddTile(strip, "PEAK VCORE");
            _tDrive = AddTile(strip, "HOTTEST DRIVE");
            root.Children.Add(strip);

            _threadToggle = new RadioButton { Style = SensorsModule.Res<Style>("Chip"), Content = "Show per-thread load", GroupName = "sensorThreads",
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
            // RadioButton can't uncheck itself, so flip it by hand.
            _threadToggle.Click += (_, _) =>
            {
                _showThreads = !_showThreads;
                _threadToggle.IsChecked = _showThreads;
                try { Refresh(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine("SensorsView: " + ex.Message); }
            };
            root.Children.Add(_threadToggle);

            root.Children.Add(_groups);
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };

            _onUpdate = Refresh;
            SensorsModule.SubscribeWhileShown(this, _onUpdate);
        }

        private TextBlock _pawnTitle, _pawnBody;
        private Button _pawnAction;

        private Border BuildPawnCard()
        {
            var s = new StackPanel();
            _pawnTitle = SensorsModule.Text("", 16, "Danger", "Display");
            _pawnTitle.TextWrapping = TextWrapping.Wrap;
            s.Children.Add(_pawnTitle);
            _pawnBody = SensorsModule.Text("", 13, "Muted", null, new Thickness(0, 6, 0, 12));
            _pawnBody.TextWrapping = TextWrapping.Wrap;
            s.Children.Add(_pawnBody);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            _pawnAction = new Button { Style = SensorsModule.Res<Style>("AccentGlass"), Padding = new Thickness(14, 0, 14, 0) };
            _pawnAction.Click += async (_, _) => await PawnActionAsync();
            buttons.Children.Add(_pawnAction);
            var b = new Button { Style = SensorsModule.Res<Style>("Ghost"), Content = "What is PawnIO?", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 0, 14, 0) };
            b.Click += (_, _) =>
            {
                // Only on click; the one place this page touches the network (via the browser).
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://pawnio.eu") { UseShellExecute = true }); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine("SensorsView: " + ex.Message); }
            };
            buttons.Children.Add(b);
            s.Children.Add(buttons);

            var card = new Border { Style = SensorsModule.Res<Style>("Card"), Child = s, Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed, MaxWidth = 800, HorizontalAlignment = HorizontalAlignment.Left };
            card.SetResourceReference(Border.BorderBrushProperty, "Danger");
            card.SetResourceReference(Border.BackgroundProperty, "DangerSoft");
            UpdatePawnCard();
            return card;
        }

        private void UpdatePawnCard()
        {
            if (PawnIoInstaller.IsInstalled())
            {
                _pawnTitle.Text = "PawnIO is installed. Restart BCU Holo to load CPU sensors";
                _pawnBody.Text = "The sensor library only picks up the driver when it starts.";
                _pawnAction.Content = "Restart BCU Holo";
            }
            else
            {
                _pawnTitle.Text = "CPU temperature, power and voltage need the PawnIO driver";
                _pawnBody.Text = "PawnIO is a small signed, open-source driver that LibreHardwareMonitor uses to read CPU sensors. "
                    + "A verified copy ships with BCU Holo. It installs only when you click below, and can be removed in Windows Settings > Apps.";
                _pawnAction.Content = "Install PawnIO";
            }
            _pawnAction.IsEnabled = true;
        }

        private async System.Threading.Tasks.Task PawnActionAsync()
        {
            var owner = Window.GetWindow(this);
            if (PawnIoInstaller.IsInstalled())
            {
                // Relaunch on this page; the elevated child inherits elevation, then this instance exits cleanly.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath, "--page sensors --restart") { UseShellExecute = false });
                Application.Current.Shutdown();
                return;
            }
            if (MessageBox.Show(owner,
                    "Install the PawnIO kernel driver (version 2.2.0, by namazso)?\n\n"
                    + "It lets BCU Holo read CPU temperature, power and voltage. It runs with full system privileges and only loads signed modules.\n\n"
                    + "You can uninstall it any time from Windows Settings > Apps > PawnIO.",
                    "Install PawnIO", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            _pawnAction.IsEnabled = false;
            _pawnAction.Content = "Installing...";
            string error;
            try { error = await PawnIoInstaller.InstallAsync(); }
            catch (Exception ex) { error = "PawnIO setup failed: " + ex.Message; }
            if (error != null) MessageBox.Show(owner, error, "Install PawnIO", MessageBoxButton.OK, MessageBoxImage.Error);
            UpdatePawnCard();
        }

        private static Tile AddTile(Panel strip, string caps)
        {
            var t = new Tile { Value = SensorsModule.Text("n/a", 30, "Text", "Display", new Thickness(0, 4, 0, 0)), Reason = SensorsModule.Text("", 12, "Muted", null, new Thickness(0, 2, 0, 0)) };
            t.Value.FontWeight = FontWeights.Bold;
            var s = new StackPanel();
            s.Children.Add(new TextBlock { Style = SensorsModule.Res<Style>("Caps"), Text = caps });
            s.Children.Add(t.Value);
            s.Children.Add(t.Reason);
            strip.Children.Add(new Border { Style = SensorsModule.Res<Style>("Card"), Child = s, MinWidth = 170, Margin = new Thickness(0, 0, 12, 12) });
            return t;
        }

        private static void SetTile(Tile t, string value, string reason, bool danger)
        {
            t.Value.Text = value ?? "n/a";
            t.Reason.Text = value == null ? reason : "";
            t.Value.SetResourceReference(TextBlock.ForegroundProperty, value == null ? "Muted" : danger ? "Danger" : "Text");
        }

        private void Refresh()
        {
            try
            {
                var all = SensorHub.Snapshot;

                bool noCpu = all.Count > 0 && !all.Any(e => (e.Reading.Unit == "°C" || e.Reading.Unit == "W") && e.Reading.Value > 0
                    && (e.Reading.Name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0 || e.Reading.Name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0
                        || e.Reading.Name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0));
                var vis = noCpu ? Visibility.Visible : Visibility.Collapsed;
                _pawn.Visibility = vis; // the PawnIO card already says it; the extra note line stays hidden

                var t = SensorsModule.Find(all, "CPU Package", "°C");
                var p = SensorsModule.Find(all, "CPU Package", "W");
                var v = SensorsModule.PeakVcore(all);
                var d = SensorsModule.HottestDrive(all);
                SetTile(_tPkg, t == null ? null : SensorsModule.Fmt(t.Reading.Value, "°C"), "needs PawnIO", false);
                SetTile(_tPow, p == null ? null : SensorsModule.Fmt(p.Reading.Value, "W"), "needs PawnIO", false);
                SetTile(_tVcore, v.HasValue && v.Value > 0 ? SensorsModule.Fmt(v.Value, "V") : null, "needs PawnIO", v.HasValue && v.Value > SensorsModule.VcoreWarn);
                SetTile(_tDrive, d == null ? null : SensorsModule.Fmt(d.Reading.Value, "°C"), "no sensor", false);

                var shown = all.Where(e => _showThreads || e.Reading.Name.IndexOf("Thread", StringComparison.OrdinalIgnoreCase) < 0).ToList();
                string sig = (_showThreads ? "T" : "t") + string.Join("\n", shown.Select(e => Key(e.Reading)));
                if (sig != _signature) { _signature = sig; Rebuild(shown, all.Count); }
                foreach (var e in shown)
                {
                    if (!_rows.TryGetValue(Key(e.Reading), out var row)) continue;
                    row.Value.Text = SensorsModule.Fmt(e.Reading.Value, e.Reading.Unit);
                    row.Min.Text = SensorsModule.Fmt(e.Min, e.Reading.Unit);
                    row.Max.Text = SensorsModule.Fmt(e.Max, e.Reading.Unit);
                    if (row.Spark != null) row.Spark.Points = Points(e.History);
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("SensorsView: " + ex.Message); }
        }

        private static string Key(SensorReading r) => r.Source + "|" + r.Group + "|" + r.Name + "|" + r.Unit;

        // Temperature, Power, Voltage, Clock, Load, Fan, other.
        private static int TypeRank(string unit)
        {
            switch (unit)
            {
                case "°C": return 0;
                case "W": return 1;
                case "V": return 2;
                case "MHz": return 3;
                case "%": return 4;
                case "RPM": return 5;
                default: return 6;
            }
        }

        // "CPU Core #2" before "CPU Core #10".
        private static string NatKey(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\d+", m => m.Value.PadLeft(6, '0'));

        private static Grid RowGrid()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            foreach (var w in new[] { 90, 90, 90, (int)SparkW }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            return g;
        }

        private static TextBlock Num(string text, string brush, double size)
        {
            var tb = SensorsModule.Text(text, size, brush, "Mono");
            tb.HorizontalAlignment = HorizontalAlignment.Right;
            tb.Margin = new Thickness(0, 0, 8, 0);
            return tb;
        }

        private void Rebuild(IReadOnlyList<SensorEntry> shown, int total)
        {
            _groups.Children.Clear();
            _rows.Clear();
            foreach (var grp in shown.GroupBy(e => e.Reading.Group))
            {
                string name = grp.Key;
                var items = grp.OrderBy(e => TypeRank(e.Reading.Unit)).ThenBy(e => NatKey(e.Reading.Name), StringComparer.OrdinalIgnoreCase).ToList();
                bool open = _expanded.TryGetValue(name, out var o) ? o
                    : !(name.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0 || items.Count > 30);

                var body = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
                var head = RowGrid();
                var cap = new TextBlock { Style = SensorsModule.Res<Style>("Caps"), Text = "SENSOR" };
                head.Children.Add(cap);
                int c = 1;
                foreach (var h in new[] { "NOW", "MIN", "MAX" })
                {
                    var tb = new TextBlock { Style = SensorsModule.Res<Style>("Caps"), Text = h, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 0) };
                    Place(head, tb, c++);
                }
                body.Children.Add(head);

                foreach (var e in items)
                {
                    var r = e.Reading;
                    var row = new Row { Value = Num("", "Text", 13), Min = Num("", "Muted", 12), Max = Num("", "Muted", 12) };
                    var g = RowGrid();
                    g.Margin = new Thickness(0, 3, 0, 3);
                    g.Children.Add(SensorsModule.Text(r.Name, 13, "Muted"));
                    Place(g, row.Value, 1); Place(g, row.Min, 2); Place(g, row.Max, 3);
                    if (r.Unit == "°C" || r.Unit == "W")
                    {
                        row.Spark = new Polyline { StrokeThickness = 1.2, Width = SparkW, Height = SparkH, StrokeLineJoin = PenLineJoin.Round };
                        row.Spark.SetResourceReference(Shape.StrokeProperty, "Accent");
                        Place(g, row.Spark, 4);
                    }
                    body.Children.Add(g);
                    _rows[Key(r)] = row;
                }

                var glyph = SensorsModule.Text(open ? "\u25BE" : "\u25B8", 14, "Muted");
                glyph.Width = 18;
                var title = SensorsModule.Text(name, 15, "Text", "Display");
                var count = SensorsModule.Text(items.Count + " sensors", 12, "Muted", null, new Thickness(10, 0, 0, 0));
                count.VerticalAlignment = VerticalAlignment.Center;
                var hp = new StackPanel { Orientation = Orientation.Horizontal };
                hp.Children.Add(glyph); hp.Children.Add(title); hp.Children.Add(count);
                var header = new Border { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, Child = hp };
                body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                header.MouseLeftButtonUp += (_, _) =>
                {
                    bool now = body.Visibility != Visibility.Visible;
                    body.Visibility = now ? Visibility.Visible : Visibility.Collapsed;
                    glyph.Text = now ? "\u25BE" : "\u25B8";
                    _expanded[name] = now;
                };

                var stack = new StackPanel();
                stack.Children.Add(header);
                stack.Children.Add(body);
                _groups.Children.Add(new Border { Style = SensorsModule.Res<Style>("Card"), Child = stack, Margin = new Thickness(0, 0, 0, 12), HorizontalAlignment = HorizontalAlignment.Left });
            }
            if (total == 0)
                _groups.Children.Add(SensorsModule.Text("No readings yet.", 13, "Muted"));
        }

        private static void Place(Grid g, UIElement el, int col) { Grid.SetColumn(el, col); g.Children.Add(el); }

        // SparkW x SparkH box, 2px per sample, newest at the right edge; flat line if constant.
        private static PointCollection Points(double[] h)
        {
            var pc = new PointCollection();
            if (h == null || h.Length == 0) return pc;
            double lo = h.Min(), hi = h.Max(), span = hi - lo;
            for (int i = 0; i < h.Length; i++)
            {
                double y = span < 1e-9 ? SparkH / 2 : (SparkH - 1) - (h[i] - lo) / span * (SparkH - 2);
                pc.Add(new Point(SparkW - 2.0 * (h.Length - 1 - i) - 1, y));
            }
            return pc;
        }
    }
}
