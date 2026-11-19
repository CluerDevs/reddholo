using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BcuHolo.Modules.Sensors;

namespace BcuHolo.Widget
{
    /// <summary>Persisted widget configuration (%LOCALAPPDATA%\reddholo\widget.json). Unknown/missing items are fine.</summary>
    public sealed class WidgetSettings
    {
        public string Layout { get; set; } = "Compact";   // Compact | Stack | Strip
        public List<string> Items { get; set; } = WidgetCatalog.Enthusiast.ToList();
        public double Opacity { get; set; } = 1.0;
        public double Scale { get; set; } = 1.0;
        public bool OnTop { get; set; } = true;            // false = pinned to the desktop, behind windows
        public bool Locked { get; set; }
        public bool Open { get; set; }                      // reopen with the app
        public double? Left { get; set; }
        public double? Top { get; set; }   // null until first moved (JSON can't store NaN)
        public int Version { get; set; }   // 0 = first widget version (item ids migrated on load)

        private static readonly string Path_ = Path.Combine(BcuHolo.App.DataDir, "widget.json");

        public static WidgetSettings Load()
        {
            try { if (File.Exists(Path_)) return JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(Path_)) ?? new(); }
            catch (Exception) { /* bad file: defaults */ }
            return new();
        }

        public void Save()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(Path_)); File.WriteAllText(Path_, JsonSerializer.Serialize(this)); }
            catch (Exception) { /* settings are a convenience; the widget still works */ }
        }
    }

    /// <summary>
    /// Floating glass telemetry HUD. Everything is configured from its right-click menu (WidgetMenu.cs). Reads the same live
    /// sources as the app (GpuModel.Current, SensorHub) plus local counters, never writes to hardware.
    /// Updates at 1 Hz by mutating existing elements; the visual tree is only rebuilt when the configuration changes.
    /// </summary>
    public sealed partial class WidgetWindow : Window
    {
        private const double Gap = 8;
        private readonly WidgetSettings _s = WidgetSettings.Load();
        private readonly HudFrame _chrome;
        private readonly Border _content = new();
        private readonly List<Part> _parts = new();
        private readonly Telemetry _tel = new();
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly Action _onSensors = () => { };

        public WidgetWindow()
        {
            Title = "reddholo widget";
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; SizeToContent = SizeToContent.WidthAndHeight;
            UseLayoutRounding = true;

            // Chamfered HUD frame (WidgetControls.HudFrame): shadow on its own backplate, content clipped to the outline.
            var layers = new Grid();
            layers.Children.Add(WUi.DotGrid());
            layers.Children.Add(_content);
            _chrome = new HudFrame(layers, new Thickness(14));
            Content = new Border { Padding = new Thickness(12), Child = _chrome }; // room for the shadow

            MouseLeftButtonDown += (_, e) => { if (!_s.Locked && e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            LocationChanged += (_, _) => { _s.Left = Left; _s.Top = Top; };
            MouseRightButtonUp += (_, _) => { ContextMenu = BuildMenu(); ContextMenu.IsOpen = true; };
            Activated += (_, _) => SendToBackIfPinned();
            SourceInitialized += (_, _) => SendToBackIfPinned();
            Closed += (_, _) => { _timer.Stop(); WantGpu(false); SensorHub.Unsubscribe(_onSensors); _tel.Dispose(); _s.Save(); };

            Migrate();
            Rebuild();
            ApplyWindowSettings();
            WindowStartupLocation = WindowStartupLocation.Manual;
            if (_s.Left is double l && _s.Top is double t && OnScreen(l, t)) { Left = l; Top = t; }
            else { Left = SystemParameters.WorkArea.Right - 500; Top = SystemParameters.WorkArea.Top + 40; }

            SensorHub.Subscribe(_onSensors); // keeps the sensor loop alive while the widget is open
            _timer.Tick += (_, _) => Refresh();
            _timer.Start();
        }

        /// <summary>Remember whether the widget is open, so it comes back with the app.</summary>
        public void RememberOpen(bool open) { _s.Open = open; _s.Save(); }
        public static bool WasOpen() => WidgetSettings.Load().Open;

        /// <summary>
        /// First-version settings: an untouched old default becomes the Enthusiast set; a customised list keeps its choices
        /// (old ids mapped to their replacements) and gains the header.
        /// </summary>
        private void Migrate()
        {
            if (_s.Items == null) _s.Items = WidgetCatalog.Enthusiast.ToList();
            if (_s.Version < 2)
            {
                bool untouched = _s.Items.Count == WidgetCatalog.OldDefault.Length && !WidgetCatalog.OldDefault.Except(_s.Items).Any();
                if (untouched) _s.Items = WidgetCatalog.Enthusiast.ToList();
                else if (!_s.Items.Contains("Header") && _s.Items.Count > 0) _s.Items.Insert(0, "Header");
                _s.Version = 2;
            }
            _s.Items = WidgetCatalog.Normalize(_s.Items);
            if (_s.Layout != "Stack" && _s.Layout != "Strip") _s.Layout = "Compact";
            _s.Save();
        }

        // ---------------- layout ----------------

        /// <summary>
        /// Compact: wide sections full width (420), small ones paired two per row. Stack: one 300 px column.
        /// Strip: one horizontal row, small sections stacked two per column.
        /// </summary>
        private void Rebuild()
        {
            _parts.Clear();
            var on = new HashSet<string>(_s.Items);
            string layout = _s.Layout;
            double wide = layout == "Stack" ? 300 : 420, half = layout == "Stack" ? 300 : 206;

            var wides = new List<Func<double, Part>>();
            var halves = new List<Func<double, Part>>();
            bool Has(string id) => on.Contains(id);

            if (Has("CpuDial") || Has("GpuDial")) wides.Add(w => new ThermalPart(w, Has("CpuDial"), Has("GpuDial")));
            if (Has("Threads") || Has("CoreTemps")) wides.Add(w => new CoresPart(w, Has("Threads"), Has("CoreTemps")));
            var sparks = WidgetCatalog.Sparks.Where(d => Has(d.Id)).ToList();
            if (sparks.Count > 0) wides.Add(w => new TrendsPart(w, sparks));
            if (Has("Clocks")) halves.Add(w => new ClocksPart(w));
            if (Has("Power")) halves.Add(w => new PowerPart(w));
            if (Has("Vcore") || Has("Whea")) halves.Add(w => new RaptorPart(w, Has("Vcore"), Has("Whea")));
            if (Has("Ram") || Has("Vram")) halves.Add(w => new MemoryPart(w, Has("Ram"), Has("Vram")));
            if (Has("Fans") || Has("Drive")) halves.Add(w => new CoolingPart(w, Has("Fans"), Has("Drive")));
            if (Has("Net") || Has("Disk")) halves.Add(w => new IoPart(w, Has("Net"), Has("Disk")));
            _tel.WantIo(Has("Net"), Has("Disk"), Has("Whea"));
            WantGpu(Has("GpuDial") || Has("Clocks") || Has("Power") || Has("Vram") || Has("Fans") || sparks.Any(d => d.Id.StartsWith("SparkGpu")));

            var root = new StackPanel();
            if (Has("Header")) root.Children.Add(Track(new HeaderPart()));

            if (layout == "Strip")
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var make in wides) AddH(row, Track(make(wide)));
                for (int i = 0; i < halves.Count; i += 2)
                {
                    var col = new StackPanel();
                    AddV(col, Track(halves[i](half)));
                    if (i + 1 < halves.Count) AddV(col, Track(halves[i + 1](half)));
                    AddH(row, col);
                }
                if (row.Children.Count > 0) AddV(root, row);
            }
            else if (layout == "Stack")
            {
                foreach (var make in wides.Concat(halves)) AddV(root, Track(make(wide)));
            }
            else
            {
                foreach (var make in wides) AddV(root, Track(make(wide)));
                for (int i = 0; i < halves.Count; i += 2)
                {
                    if (i + 1 >= halves.Count) { AddV(root, Track(halves[i](wide))); break; } // odd one out spans the row
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    AddH(row, Track(halves[i](half)));
                    AddH(row, Track(halves[i + 1](half)));
                    AddV(root, row);
                }
            }

            if (_parts.Count == 0)
                root.Children.Add(WUi.Label("Right-click to choose what to show", 13, "Muted"));

            _content.Child = root;
            Refresh();
        }

        private bool _wantGpu;
        /// <summary>Holds GpuModel polling demand while the layout shows GPU readings; balanced across Rebuild and Closed.</summary>
        private void WantGpu(bool on)
        {
            if (on == _wantGpu) return;
            _wantGpu = on;
            BcuHolo.Modules.Gpu.GpuModel.Want(on);
        }

        private FrameworkElement Track(Part p) { _parts.Add(p); return p.Root; }

        private static void AddV(Panel host, FrameworkElement e)
        {
            if (host.Children.Count > 0) e.Margin = new Thickness(0, Gap, 0, 0);
            host.Children.Add(e);
        }

        private static void AddH(Panel host, FrameworkElement e)
        {
            if (host.Children.Count > 0) e.Margin = new Thickness(Gap, 0, 0, 0);
            host.Children.Add(e);
        }

        // ---------------- data ----------------

        private void Refresh()
        {
            try { _tel.Sample(); }
            catch (Exception ex) { Trace.WriteLine("Widget telemetry: " + ex); }
            foreach (var p in _parts)
            {
                try { p.Update(_tel); }
                catch (Exception ex) { Trace.WriteLine("Widget " + p.GetType().Name + ": " + ex); }
            }
        }

        // ---------------- window ----------------

        private void ApplyWindowSettings()
        {
            Opacity = _s.Opacity;
            _chrome.LayoutTransform = new ScaleTransform(_s.Scale, _s.Scale);
            Topmost = _s.OnTop;
            SendToBackIfPinned();
            _s.Save();
        }

        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        /// <summary>
        /// "Pinned" = kept at the bottom of the z-order. Ceiling: Win+D (show desktop) still minimizes it; true desktop
        /// embedding (parenting to WorkerW) is the upgrade path but is fragile across Explorer restarts.
        /// </summary>
        private void SendToBackIfPinned()
        {
            if (_s.OnTop) return;
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero) SetWindowPos(h, new IntPtr(1) /*HWND_BOTTOM*/, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /*NOSIZE|NOMOVE|NOACTIVATE*/);
        }

        private static bool OnScreen(double left, double top) =>
            left > SystemParameters.VirtualScreenLeft - 50 && top > SystemParameters.VirtualScreenTop - 50
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50;
    }

    /// <summary>
    /// "Start with Windows" for an elevated app: a logon task with highest privileges, so there's no UAC prompt at boot.
    /// A Run-key entry would prompt every logon.
    /// </summary>
    internal static class StartupTask
    {
        private const string Name = "reddholo widget";
        private const string OldName = "BCU Holo Widget"; // pre-rename; points at the old exe

        public static bool IsRegistered()
        {
            if (Run($"/Query /TN \"{OldName}\"").code == 0 && Register() == null) Run($"/Delete /TN \"{OldName}\" /F");
            return Run($"/Query /TN \"{Name}\"").code == 0;
        }

        public static string Register()
        {
            var exe = Environment.ProcessPath;
            var (code, output) = Run($"/Create /TN \"{Name}\" /TR \"\\\"{exe}\\\" --widget\" /SC ONLOGON /RL HIGHEST /IT /F");
            return code == 0 ? null : "Couldn't create the startup task:\n" + output;
        }

        public static string Remove()
        {
            var (code, output) = Run($"/Delete /TN \"{Name}\" /F");
            return code == 0 ? null : "Couldn't remove the startup task:\n" + output;
        }

        private static (int code, string output) Run(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args)
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = Process.Start(psi);
                var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(10000);
                return (p.ExitCode, output.Trim());
            }
            catch (Exception ex) { return (-1, ex.Message); }
        }
    }
}
