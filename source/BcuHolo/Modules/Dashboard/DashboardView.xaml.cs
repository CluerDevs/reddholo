using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BcuHolo.Modules.Apps;
using BcuHolo.Plugins;
using Microsoft.Win32;

namespace BcuHolo.Modules.Dashboard
{
    /// <summary>
    /// Home page. Live CPU/GPU/memory every 2 s while visible, one-off health and storage reads,
    /// and a row of tiles contributed by the other modules.
    /// CPU temperature/voltage need a kernel sensor driver and arrive with the Sensors module.
    /// </summary>
    public partial class DashboardView : UserControl
    {
        private readonly IHoloHost _host;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
        private readonly Queue<double> _cpuHist = new(), _gpuHist = new();
        private (ulong idle, ulong total) _lastCpu;
        private bool _polling, _staticLoaded;
        private int _pollCount;
        private List<NameSize> _top = new();
        private int _procCount;
        private NvidiaSmi _smi; // name, driver and VRAM only; refreshed every 30 s

        public DashboardView(IHoloHost host, IEnumerable<IHoloModule> modules)
        {
            _host = host;
            InitializeComponent();

            // Storage is built in XAML; lift it into the tile grid so it flows with the module tiles.
            var tiles = new List<FrameworkElement>();
            ((Panel)StorageTile.Parent).Children.Remove(StorageTile);
            StorageTile.Margin = new Thickness(0, 0, 16, 16);
            tiles.Add(StorageTile);
            foreach (var m in modules)
            {
                FrameworkElement tile = null;
                try { tile = m.CreateTile(host); }
                catch (Exception ex) { Trace.WriteLine($"Tile for {m.Id} failed: {ex}"); }
                if (tile == null) continue;
                tile.Margin = new Thickness(0, 0, 16, 16);
                tiles.Add(tile);
            }
            ModuleTiles.ItemsSource = tiles;

            _timer.Tick += async (_, _) => await PollAsync();
            Loaded += async (_, _) =>
            {
                // Tiles arrive one after another (top row first, then module tiles), 40 ms apart.
                int i = 0;
                foreach (var tile in Tiles(this)) Motion.Enter(tile, 60 + 40 * i++);
                _timer.Start();
                await PollAsync();
                if (_staticLoaded) return;
                _staticLoaded = true;
                LoadStorage();
                await LoadHealthAsync();
            };
            Unloaded += (_, _) => _timer.Stop(); // no polling while another page is shown
        }

        private static IEnumerable<Border> Tiles(DependencyObject root)
        {
            var style = Application.Current.FindResource("DashTile");
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);
            while (queue.Count > 0) // breadth-first: visual order, top row before the module grid
            {
                var d = queue.Dequeue();
                if (d is Border b && b.Style == style) { yield return b; continue; }
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) queue.Enqueue(VisualTreeHelper.GetChild(d, i));
            }
        }

        private void OpenApps_Click(object sender, RoutedEventArgs e) => _host.Navigate("apps");

        // ---------- live: CPU load, memory, GPU ----------

        private async Task PollAsync()
        {
            if (_polling) return;
            _polling = true;
            try
            {
                var cpu = CpuLoad();
                var mem = MemoryStatus();
                // Process scan every 10 s; nvidia-smi only every 30 s for name/driver/VRAM (live GPU numbers come from NVML).
                if (_pollCount % 5 == 0) (_top, _procCount) = await Task.Run(TopProcesses);
                var gm = BcuHolo.Modules.Gpu.GpuModel.Current;
                if (_smi == null || _pollCount % 15 == 0 || gm == null || !gm.Available) _smi = await Task.Run(NvidiaSmi.Query) ?? _smi;
                _pollCount++;
                var gpu = _smi;
                if (gpu != null && gm != null && gm.Available && gm.Temp >= 0)
                {
                    gpu.Temp = gm.Temp; gpu.Util = gm.Load; gpu.Power = gm.PowerW; gpu.PowerLimit = gm.LimitW;
                    gpu.Fan = gm.Fans.Length > 0 ? gm.Fans.Average() : double.NaN;
                }
                var top = _top;
                var procCount = _procCount;
                if (BcuHolo.Modules.Sensors.SensorHub.TryGet("", "CPU Package", "°C", out var pkg) && pkg > 0)
                {
                    CpuTemp.Text = $"{pkg:0} °C";
                    CpuTemp.SetResourceReference(TextBlock.ForegroundProperty, pkg >= 90 ? "Danger" : "Text");
                }

                if (cpu >= 0) { CpuBig.Text = $"{cpu:0}%"; SetGauge(CpuTrack, CpuArc, cpu / 100); Push(_cpuHist, cpu, CpuSpark); }
                CpuProcs.Text = procCount.ToString();
                var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
                CpuUptime.Text = up.TotalDays >= 1 ? $"{(int)up.TotalDays}d {up.Hours}h" : $"{up.Hours}h {up.Minutes}m";

                if (mem.total > 0)
                {
                    var used = mem.total - mem.avail;
                    MemUsed.Text = (used / 1073741824d).ToString("0.0");
                    MemTotal.Text = $" / {mem.total / 1073741824d:0} GB";
                    Motion.To(MemFill, used / mem.total, f =>
                    {
                        MemFill.Width = new GridLength(f, GridUnitType.Star);
                        MemRest.Width = new GridLength(1 - f, GridUnitType.Star);
                    });
                }
                MemTop.ItemsSource = top;

                if (gpu != null)
                {
                    GpuName.Text = gpu.Name;
                    GpuDriver.Text = "driver " + gpu.Driver;
                    GpuBig.Text = $"{gpu.Temp:0}°";
                    SetGauge(GpuTrack, GpuArc, Math.Min(1, gpu.Temp / 100));
                    GpuLoad.Text = $"{gpu.Util:0}%";
                    GpuPower.Text = $"{gpu.Power:0} / {gpu.PowerLimit:0} W";
                    GpuFan.Text = double.IsNaN(gpu.Fan) ? "—" : $"{gpu.Fan:0}%";
                    GpuVram.Text = $"{gpu.MemUsed / 1024:0.0} / {gpu.MemTotal / 1024:0} GB";
                    Push(_gpuHist, gpu.Util, GpuSpark);
                }
                else if (GpuBig.Text == "—")
                {
                    GpuName.Text = "GPU";
                    GpuLoad.Text = "nvidia-smi not found";
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Dashboard poll failed: " + ex);
            }
            finally
            {
                _polling = false;
            }
        }

        /// <summary>270° gauge: dash units are multiples of StrokeThickness.</summary>
        private static void SetGauge(System.Windows.Shapes.Ellipse track, System.Windows.Shapes.Ellipse arc, double frac)
        {
            const double c = Math.PI * (104 - 10) / 10; // gauge is 104 px with a 10 px stroke
            track.StrokeDashArray = new DoubleCollection { 0.75 * c, c };
            Motion.To(arc, Math.Clamp(frac, 0, 1), f => arc.StrokeDashArray = new DoubleCollection { Math.Max(0.001, 0.75 * c * f), c });
        }

        private static void Push(Queue<double> hist, double v, System.Windows.Shapes.Path path)
        {
            hist.Enqueue(v);
            while (hist.Count > 60) hist.Dequeue();
            var w = path.ActualWidth > 0 ? path.ActualWidth : 300;
            const double h = 34;
            var pts = hist.ToArray();
            var step = w / 59;
            var x0 = w - (pts.Length - 1) * step;
            var fig = new PathFigure { StartPoint = new Point(x0, h), IsClosed = true };
            for (var i = 0; i < pts.Length; i++)
                fig.Segments.Add(new LineSegment(new Point(x0 + i * step, h - h * Math.Clamp(pts[i], 0, 100) / 100), true));
            fig.Segments.Add(new LineSegment(new Point(x0 + (pts.Length - 1) * step, h), false));
            path.Data = new PathGeometry(new[] { fig });
        }

        [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        /// <summary>Whole-machine CPU % since the previous call (-1 on the first call).</summary>
        private double CpuLoad()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return -1;
            var total = (ulong)(kernel + user); // kernel time includes idle
            var prev = _lastCpu;
            _lastCpu = ((ulong)idle, total);
            if (prev.total == 0 || total <= prev.total) return -1;
            return 100.0 * (1 - (double)((ulong)idle - prev.idle) / (total - prev.total));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        private static (double total, double avail) MemoryStatus()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref m) ? (m.ullTotalPhys, m.ullAvailPhys) : (0, 0);
        }

        /// <summary>Top 3 programs by RAM, summing processes with the same name (Chrome, WSL's vmmem…).</summary>
        private static (List<NameSize>, int) TopProcesses()
        {
            var sums = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var all = Process.GetProcesses();
            foreach (var p in all)
            {
                try { sums[p.ProcessName] = sums.GetValueOrDefault(p.ProcessName) + p.WorkingSet64; }
                catch { /* exited or access denied */ }
                finally { p.Dispose(); }
            }
            return (sums.OrderByDescending(kv => kv.Value).Take(3)
                .Select(kv => new NameSize(kv.Key, $"{kv.Value / 1073741824d:0.0} GB")).ToList(), all.Length);
        }

        public sealed record NameSize(string Name, string Size);

        // ---------- one-off: storage ----------

        private void LoadStorage()
        {
            var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).ToList();
            var sysRoot = Path.GetPathRoot(Environment.SystemDirectory);
            var sys = drives.FirstOrDefault(d => string.Equals(d.Name, sysRoot, StringComparison.OrdinalIgnoreCase)) ?? drives.FirstOrDefault();
            if (sys == null) return;

            var used = (double)(sys.TotalSize - sys.TotalFreeSpace) / sys.TotalSize;
            var circ = Math.PI * (116 - 12) / 12;
            SysRing.StrokeDashArray = new DoubleCollection { used * circ, circ };
            SysPct.Text = $"{used * 100:0}%";
            SysRoot.Text = sys.Name.Substring(0, 2) + " used";
            SysFree.Text = AppRow.FormatKb(sys.TotalFreeSpace / 1024);
            SysOf.Text = $" free of {AppRow.FormatKb(sys.TotalSize / 1024)}";
            StorageCaps.Text = $"Storage · {drives.Count} drives";

            var big = new List<string>();
            try
            {
                using var lxss = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
                foreach (var id in lxss?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var k = lxss.OpenSubKey(id);
                    var basePath = (k?.GetValue("BasePath") as string)?.Replace(@"\\?\", "");
                    if (string.IsNullOrEmpty(basePath)) continue;
                    var vhd = new FileInfo(Path.Combine(basePath, k.GetValue("VhdFileName") as string ?? "ext4.vhdx"));
                    if (vhd.Exists) big.Add($"WSL {k.GetValue("DistributionName")} {AppRow.FormatKb(vhd.Length / 1024)}");
                }
            }
            catch (Exception ex) { Trace.WriteLine("WSL lookup failed: " + ex.Message); }
            SysBig.Text = big.Count > 0 ? string.Join(" · ", big) : "";

            OtherDrives.ItemsSource = drives.Where(d => d != sys).Select(DriveItem.For).ToList();
        }

        // ---------- one-off: CPU health (Raptor Lake degradation watch) ----------

        private async Task LoadHealthAsync()
        {
            var h = await Task.Run(HealthInfo.Read);
            SpecText.Text = string.Join(" · ", new[] { h.CpuShort, GpuName.Text != "GPU" ? GpuName.Text : null, h.RamGb > 0 ? $"{h.RamGb} GB" : null }.Where(s => s != null));
            CpuName.Text = h.CpuShort ?? "CPU";
            CpuCores.Text = $"{Environment.ProcessorCount} threads";

            HMicro.Text = h.Microcode == 0 ? "unknown" : $"0x{h.Microcode:X}";
            HWhea.Text = h.Whea < 0 ? "n/a" : h.Whea.ToString();
            HCrash.Text = h.Crashes < 0 ? "n/a" : h.Crashes.ToString();
            HBios.Text = h.Bios;

            const string green = "Good", warn = "Danger";
            if (!h.RaptorLake)
            {
                Status(HealthChip, HealthChipText, "Not affected", green);
                HealthNote.Text = "This CPU isn't part of Intel's 13th/14th-gen voltage issue.";
            }
            else if (h.Microcode >= 0x129)
            {
                Status(HealthChip, HealthChipText, "Protected", green);
                HealthNote.Text = "Microcode has Intel's voltage fix. Watching for new errors.";
                HMicro.Text += " ✓";
                HMicro.SetResourceReference(TextBlock.ForegroundProperty, green);
            }
            else
            {
                Status(HealthChip, HealthChipText, "Needs BIOS update", warn);
                HealthNote.Text = "Microcode is older than 0x129: the CPU can keep degrading. Update the BIOS.";
                HealthIcon.Text = "\uE7BA";
                HealthIcon.SetResourceReference(TextBlock.ForegroundProperty, warn);
                HMicro.SetResourceReference(TextBlock.ForegroundProperty, warn);
            }
            HWhea.SetResourceReference(TextBlock.ForegroundProperty, h.Whea > 0 ? warn : green);
            HCrash.SetResourceReference(TextBlock.ForegroundProperty, h.Crashes > 0 ? warn : green);

            // The pill must be able to go amber: any WHEA error, crash, or unpatched Raptor Lake counts.
            var issues = (h.RaptorLake && h.Microcode < 0x129 ? 1 : 0) + (h.Whea > 0 ? 1 : 0) + (h.Crashes > 0 ? 1 : 0);
            StatusText.Text = issues == 0 ? "● System healthy"
                : h.Crashes > 0 && issues == 1 ? $"● {h.Crashes} crash(es) in 30 days"
                : $"● {issues} things need attention";
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, issues == 0 ? green : warn);
            StatusChip.SetResourceReference(Border.BackgroundProperty, issues == 0 ? "Good.1A" : "DangerSoft");
        }

        private static void Status(Border chip, TextBlock text, string label, string key)
        {
            text.Text = label;
            text.SetResourceReference(TextBlock.ForegroundProperty, key);
            chip.SetResourceReference(Border.BackgroundProperty, key + ".14");
        }
    }

    internal sealed class HealthInfo
    {
        public string CpuShort, Bios = "unknown";
        public uint Microcode;
        public bool RaptorLake;
        public int Whea = -1, Crashes = -1, RamGb;

        public static HealthInfo Read()
        {
            var h = new HealthInfo();
            try
            {
                using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                var name = (cpu?.GetValue("ProcessorNameString") as string)?.Trim() ?? "";
                h.CpuShort = name.Replace("(R)", "").Replace("(TM)", "").Replace("13th Gen ", "").Replace("14th Gen ", "").Replace("Intel Core", "Intel").Trim();
                // Affected: 13th/14th-gen desktop Core i5/i7/i9 (Raptor Lake / Raptor Lake Refresh).
                h.RaptorLake = (name.Contains("13th Gen") || name.Contains("14th Gen")) && (name.Contains("i7") || name.Contains("i9") || name.Contains("i5"));
                if (cpu?.GetValue("Update Revision") is byte[] rev && rev.Length >= 8)
                    h.Microcode = BitConverter.ToUInt32(rev, 4);
            }
            catch (Exception ex) { Trace.WriteLine("CPU info failed: " + ex.Message); }

            h.Whea = CountEvents("*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and TimeCreated[timediff(@SystemTime) <= 2592000000]]]");
            h.Crashes = CountEvents("*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001 and TimeCreated[timediff(@SystemTime) <= 2592000000]]]");

            try
            {
                // Board maker reads better than the BIOS vendor ("EVGA 2.18" rather than "American Megatrends 2.18").
                var maker = "";
                using (var board = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_BaseBoard"))
                    foreach (ManagementObject o in board.Get())
                        maker = FirstWord(o["Manufacturer"] as string);
                using var bios = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS");
                foreach (ManagementObject o in bios.Get())
                    h.Bios = $"{maker} {o["SMBIOSBIOSVersion"]}".Trim();
                using var cs = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (ManagementObject o in cs.Get())
                    h.RamGb = (int)Math.Round(Convert.ToDouble(o["TotalPhysicalMemory"], CultureInfo.InvariantCulture) / 1073741824);
            }
            catch (Exception ex) { Trace.WriteLine("WMI failed: " + ex.Message); }
            return h;
        }

        private static string FirstWord(string s) => string.IsNullOrWhiteSpace(s) ? "" : s.Split(' ')[0];

        private static int CountEvents(string xpath)
        {
            try
            {
                using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath));
                var n = 0;
                for (var e = reader.ReadEvent(); e != null; e = reader.ReadEvent()) { n++; e.Dispose(); }
                return n;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Event query failed: " + ex.Message);
                return -1;
            }
        }
    }

    /// <summary>
    /// Reads the NVIDIA GPU through nvidia-smi (ships with the driver). Stopgap until the GPU module
    /// talks to NVML directly; costs one short process launch per poll.
    /// </summary>
    internal sealed class NvidiaSmi
    {
        public string Name, Driver;
        public double Temp, Util, Power, PowerLimit, Fan, MemUsed, MemTotal;

        private static readonly string Exe = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");

        public static NvidiaSmi Query()
        {
            if (!File.Exists(Exe)) return null;
            try
            {
                var psi = new ProcessStartInfo(Exe,
                    "--query-gpu=name,driver_version,temperature.gpu,utilization.gpu,power.draw,power.limit,fan.speed,memory.used,memory.total --format=csv,noheader,nounits")
                { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using var p = Process.Start(psi);
                var line = p!.StandardOutput.ReadLine();
                if (!p.WaitForExit(3000)) { p.Kill(); return null; }
                var f = line?.Split(',').Select(s => s.Trim()).ToArray();
                if (f == null || f.Length < 9) return null;
                double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
                return new NvidiaSmi
                {
                    Name = f[0].Replace("NVIDIA ", "").Replace("GeForce ", ""), Driver = f[1],
                    Temp = D(f[2]), Util = D(f[3]), Power = D(f[4]), PowerLimit = D(f[5]),
                    Fan = D(f[6]), MemUsed = D(f[7]), MemTotal = D(f[8]),
                };
            }
            catch (Exception ex)
            {
                Trace.WriteLine("nvidia-smi failed: " + ex.Message);
                return null;
            }
        }
    }
}
