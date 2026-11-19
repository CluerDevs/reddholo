using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using BcuHolo.Modules.Gpu;
using BcuHolo.Modules.Sensors;

namespace BcuHolo.Widget
{
    /// <summary>Fixed-size history (oldest first) with min/max since the widget opened. NaN = no reading.</summary>
    internal sealed class Series
    {
        public const int Len = 120;
        private readonly double[] _buf = new double[Len];
        private int _head, _count;
        public double Last = double.NaN, Min = double.NaN, Max = double.NaN;
        public int Count => _count;

        public void Push(double v)
        {
            if (double.IsInfinity(v)) v = double.NaN;
            _buf[_head] = v;
            _head = (_head + 1) % Len;
            if (_count < Len) _count++;
            Last = v;
            if (double.IsNaN(v)) return;
            if (double.IsNaN(Min) || v < Min) Min = v;
            if (double.IsNaN(Max) || v > Max) Max = v;
        }

        /// <summary>i = 0 is the oldest sample still held.</summary>
        public double this[int i] => _buf[(_head - _count + i + Len) % Len];

        /// <summary>Min/max of the samples currently held (NaN if none).</summary>
        public void Range(out double lo, out double hi)
        {
            lo = double.NaN; hi = double.NaN;
            for (int i = 0; i < _count; i++)
            {
                double v = this[i];
                if (double.IsNaN(v)) continue;
                if (double.IsNaN(lo) || v < lo) lo = v;
                if (double.IsNaN(hi) || v > hi) hi = v;
            }
        }
    }

    /// <summary>One physical core as LHM reports it ("CPU Core #n", "CPU Core #n Thread #k").</summary>
    internal sealed class CoreInfo
    {
        public string Kind = "";   // "P"/"E" if the sensor name said so, else ""
        public int Num;            // number from the sensor name
        public int Threads;        // highest "Thread #k" seen (0 = loads not seen yet)
        public string Group = "";  // "P", "E" or "" (non-hybrid) after classification
        public string Label = "";  // "P3", "E5", "C2"
        public readonly double[] Load = new double[4];
        public double Temp, Clock, Vid;

        public void Reset()
        {
            for (int i = 0; i < Load.Length; i++) Load[i] = double.NaN;
            Temp = Clock = Vid = double.NaN;
        }
    }

    /// <summary>
    /// Everything the widget shows, sampled once per tick in one pass over SensorHub.Snapshot plus GpuModel.Current.
    /// Read-only: no hardware writes, no network calls (network numbers are local interface counters).
    /// </summary>
    internal sealed class Telemetry : IDisposable
    {
        public readonly Series CpuLoad = new(), GpuLoad = new(), CpuTemp = new(), GpuTemp = new(),
            CpuPower = new(), GpuPower = new(), TotalPower = new(), Vcore = new(), NetDown = new(), NetUp = new();

        public double CpuClockMax = double.NaN, CpuClockAvg = double.NaN, CpuClockPeak = double.NaN, VidMax = double.NaN;
        public double VcorePeak = double.NaN; // since widget start, PeakVcore's name rule (Vcore + "CPU Core" incl. VIDs)
        public string VcoreSource = "";
        public string CpuName = "", GpuName = "";

        public readonly List<CoreInfo> Cores = new();
        public string Topology = "";

        public bool GpuOk;
        public int[] GpuFans = new int[0];
        public double GpuFanMax = double.NaN, GpuCoreMHz = double.NaN, GpuMemMHz = double.NaN, GpuMaxCoreMHz = double.NaN,
            GpuMaxMemMHz = double.NaN, GpuLimitW = double.NaN, VramUsedGB = double.NaN, VramTotalGB = double.NaN;
        public double RamUsedGB = double.NaN, RamTotalGB = double.NaN;

        public readonly List<KeyValuePair<string, double>> BoardFans = new();
        public string DriveName = "";
        public double DriveTemp = double.NaN;
        public double DiskRead = double.NaN, DiskWrite = double.NaN; // bytes/s
        public int WheaBoot = -1, Whea30d = -1;
        public readonly uint Microcode = ReadMicrocode();
        public TimeSpan Uptime;

        private readonly Dictionary<string, CoreInfo> _coreMap = new();
        private int _coreSig = -1;
        private string _cpuGroup, _gpuRaw;
        private readonly IoSampler _io = new();

        public Telemetry() => SelfCheck();

        public void Sample()
        {
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            foreach (var c in _coreMap.Values) c.Reset();
            BoardFans.Clear();

            double cpuLoad = double.NaN, cpuTemp = double.NaN, cpuPow = double.NaN, cpuCoreV = double.NaN, boardV = double.NaN, peakNow = double.NaN;
            string boardVName = "";
            var all = SensorHub.Snapshot;
            foreach (var e in all)
            {
                var r = e?.Reading;
                if (r == null || r.Name == null || r.Unit == null) continue;
                string n = r.Name;
                double v = r.Value;
                switch (r.Unit)
                {
                    case "%":
                        if (n == "CPU Total") cpuLoad = v;
                        else Core(r.Unit, n, v);
                        break;
                    case "°C":
                        if (n == "CPU Package")
                        {
                            if (v > 0) cpuTemp = v;
                            if (!string.IsNullOrEmpty(r.Group) && r.Group != _cpuGroup) { _cpuGroup = r.Group; CpuName = ShortName(r.Group); }
                        }
                        else Core(r.Unit, n, v);
                        break;
                    case "W":
                        if (n == "CPU Package" && v > 0) cpuPow = v;
                        break;
                    case "V":
                        // Same rule as SensorsModule.PeakVcore for the peak; the live value prefers LHM's "CPU Core" (MSR) reading.
                        bool vcoreLike = n.IndexOf("Vcore", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("CPU Core", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (vcoreLike && v > 0 && (double.IsNaN(peakNow) || v > peakNow)) peakNow = v;
                        if (n == "CPU Core") { if (v > 0) cpuCoreV = v; }
                        else if ((n == "Vcore" || n == "CPU Vcore") && v > 0) { boardV = v; boardVName = r.Group + " " + n; }
                        else Core(r.Unit, n, v);
                        break;
                    case "MHz":
                        Core(r.Unit, n, v);
                        break;
                    case "RPM":
                        if (v > 0 && !(r.Group != null && LhmSensorSource.StorageGroups.ContainsKey(r.Group)))
                            BoardFans.Add(new KeyValuePair<string, double>(n, v));
                        break;
                }
            }

            ClassifyCores();
            double clkMax = double.NaN, clkSum = 0, vidMax = double.NaN;
            int clkN = 0;
            foreach (var c in Cores)
            {
                if (!double.IsNaN(c.Clock)) { clkSum += c.Clock; clkN++; if (double.IsNaN(clkMax) || c.Clock > clkMax) clkMax = c.Clock; }
                if (!double.IsNaN(c.Vid) && (double.IsNaN(vidMax) || c.Vid > vidMax)) vidMax = c.Vid;
            }
            CpuClockMax = clkMax;
            CpuClockAvg = clkN > 0 ? clkSum / clkN : double.NaN;
            if (!double.IsNaN(clkMax) && (double.IsNaN(CpuClockPeak) || clkMax > CpuClockPeak)) CpuClockPeak = clkMax;
            VidMax = vidMax;

            double vcore = !double.IsNaN(cpuCoreV) ? cpuCoreV : boardV;
            VcoreSource = !double.IsNaN(cpuCoreV) ? "LHM \"CPU Core\" (MSR)" : !double.IsNaN(boardV) ? boardVName : "";
            if (!double.IsNaN(peakNow) && (double.IsNaN(VcorePeak) || peakNow > VcorePeak)) VcorePeak = peakNow;

            // Stable fan order: "Fan #2" before "Fan #10".
            BoardFans.Sort((a, b) => a.Key.Length != b.Key.Length ? a.Key.Length.CompareTo(b.Key.Length) : string.CompareOrdinal(a.Key, b.Key));

            var d = SensorsModule.HottestDrive(all);
            DriveTemp = d == null ? double.NaN : d.Reading.Value;
            DriveName = d == null ? "" : d.Reading.Group ?? "";

            // GPU (NVML via the GPU module; null until that module starts).
            var g = GpuModel.Current;
            GpuOk = g != null && g.Available;
            double gTemp = double.NaN, gLoad = double.NaN, gPow = double.NaN;
            if (GpuOk)
            {
                gTemp = g.Temp >= 0 ? g.Temp : double.NaN;
                gLoad = g.Load >= 0 ? g.Load : double.NaN;
                gPow = g.PowerW > 0 ? g.PowerW : double.NaN;
                GpuLimitW = g.LimitW > 0 ? g.LimitW : double.NaN;
                GpuFans = g.Fans ?? new int[0];
                GpuFanMax = GpuFans.Length > 0 ? GpuFans.Max() : double.NaN;
                GpuCoreMHz = g.CoreMHz > 0 ? g.CoreMHz : double.NaN;
                GpuMemMHz = g.MemMHz > 0 ? g.MemMHz : double.NaN;
                GpuMaxCoreMHz = g.MaxCoreMHz > 0 ? g.MaxCoreMHz : double.NaN;
                GpuMaxMemMHz = g.MaxMemMHz > 0 ? g.MaxMemMHz : double.NaN;
                VramUsedGB = g.VramUsedMB >= 0 && g.VramTotalMB > 0 ? g.VramUsedMB / 1024.0 : double.NaN;
                VramTotalGB = g.VramTotalMB > 0 ? g.VramTotalMB / 1024.0 : double.NaN;
                if (g.Name != _gpuRaw) { _gpuRaw = g.Name; GpuName = ShortName(g.Name ?? ""); }
            }
            else
            {
                GpuLimitW = GpuFanMax = GpuCoreMHz = GpuMemMHz = GpuMaxCoreMHz = GpuMaxMemMHz = VramUsedGB = VramTotalGB = double.NaN;
                GpuFans = new int[0];
            }

            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref m) && m.ullTotalPhys > 0)
            {
                RamTotalGB = m.ullTotalPhys / 1073741824.0;
                RamUsedGB = (m.ullTotalPhys - Math.Min(m.ullAvailPhys, m.ullTotalPhys)) / 1073741824.0;
            }
            else RamTotalGB = RamUsedGB = double.NaN;

            var io = _io.Current;
            DiskRead = io.Read; DiskWrite = io.Write;
            WheaBoot = io.WheaBoot; Whea30d = io.Whea30d;

            CpuLoad.Push(cpuLoad);
            CpuTemp.Push(cpuTemp);
            CpuPower.Push(cpuPow);
            GpuLoad.Push(gLoad);
            GpuTemp.Push(gTemp);
            GpuPower.Push(gPow);
            TotalPower.Push(double.IsNaN(cpuPow) && double.IsNaN(gPow) ? double.NaN : (double.IsNaN(cpuPow) ? 0 : cpuPow) + (double.IsNaN(gPow) ? 0 : gPow));
            Vcore.Push(vcore);
            NetDown.Push(io.Down);
            NetUp.Push(io.Up);
        }

        /// <summary>
        /// Parses "CPU Core #n" / "CPU Core #n Thread #k" (also "P-Core #n", "E-Core #n", "Core #n"); thread = 0 when absent.
        /// Rejects suffixed names like "CPU Core #1 Distance to TjMax".
        /// </summary>
        internal static bool TryParseCore(string n, out string kind, out int num, out int thread)
        {
            kind = ""; num = 0; thread = 0;
            int at = n.IndexOf("Core #", StringComparison.Ordinal);
            if (at < 0) return false;
            if (at == 0 || (at == 4 && n.StartsWith("CPU ", StringComparison.Ordinal))) kind = "";
            else if (at == 2 && n.StartsWith("P-", StringComparison.Ordinal)) kind = "P";
            else if (at == 2 && n.StartsWith("E-", StringComparison.Ordinal)) kind = "E";
            else return false;

            int i = at + 6, digits = 0;
            while (i < n.Length && digits < 4 && n[i] >= '0' && n[i] <= '9') { num = num * 10 + (n[i] - '0'); i++; digits++; }
            if (digits == 0) return false;
            if (i == n.Length) return true;

            const string T = " Thread #";
            if (n.Length - i <= T.Length || string.CompareOrdinal(n, i, T, 0, T.Length) != 0) return false;
            i += T.Length;
            int d = 0;
            while (i < n.Length && d < 2 && n[i] >= '0' && n[i] <= '9') { thread = thread * 10 + (n[i] - '0'); i++; d++; }
            return d > 0 && i == n.Length && thread >= 1 && thread <= 4;
        }

        /// <summary>Runnable check for the parser (Debug builds, once per widget open).</summary>
        [Conditional("DEBUG")]
        internal static void SelfCheck()
        {
            Debug.Assert(TryParseCore("CPU Core #3", out var k, out var n, out var t) && k == "" && n == 3 && t == 0, "CPU Core #3");
            Debug.Assert(TryParseCore("CPU Core #12 Thread #2", out k, out n, out t) && n == 12 && t == 2, "Thread #2");
            Debug.Assert(TryParseCore("E-Core #5", out k, out n, out _) && k == "E" && n == 5, "E-Core");
            Debug.Assert(!TryParseCore("CPU Core #1 Distance to TjMax", out _, out _, out _), "Distance");
            Debug.Assert(!TryParseCore("CPU Core", out _, out _, out _) && !TryParseCore("CPU Core Max", out _, out _, out _), "no number");
            Debug.Assert(!TryParseCore("Core #1 (CCD1)", out _, out _, out _), "AMD suffix");
            // 13700K shape: generic 2-thread cores fold onto P-cores, 1-thread onto E-cores.
            var m = new Dictionary<string, CoreInfo>();
            CoreInfo Mk(string k, int n, int th) { var ci = new CoreInfo { Kind = k, Num = n, Threads = th }; ci.Reset(); ci.Threads = th; m[k + n] = ci; return ci; }
            Mk("", 1, 2); Mk("", 9, 1); var p1 = Mk("P", 1, 0); var e1 = Mk("E", 1, 0);
            var g2k = new Dictionary<int, string>();
            MergeGeneric(m, g2k);
            Debug.Assert(m.Count == 2 && p1.Threads == 2 && e1.Threads == 1 && g2k[1] == "P1" && g2k[9] == "E1", "hybrid merge");
        }

        private void Core(string unit, string n, double v)
        {
            if (!TryParseCore(n, out string kind, out int num, out int thread)) return;
            string key = kind + num.ToString();
            if (kind == "" && _genericToKind.TryGetValue(num, out var mapped)) key = mapped; // "CPU Core #9" -> "E1"
            if (!_coreMap.TryGetValue(key, out var c))
            {
                c = new CoreInfo { Kind = kind, Num = num };
                c.Reset();
                _coreMap[key] = c;
            }
            switch (unit)
            {
                case "%":
                    int t = thread == 0 ? 1 : thread;
                    c.Load[t - 1] = v;
                    if (t > c.Threads) c.Threads = t;
                    break;
                case "°C": if (thread == 0 && v > 0) c.Temp = v; break;
                case "MHz": if (thread == 0 && v > 0) c.Clock = v; break;
                case "V": if (thread == 0 && v > 0) c.Vid = v; break;
            }
        }

        /// <summary>Rebuilds Cores/Topology only when the set of cores or thread counts changes (normally once).</summary>
        private void ClassifyCores()
        {
            MergeGeneric(_coreMap, _genericToKind);
            int sig = _coreMap.Count;
            foreach (var c in _coreMap.Values) sig = unchecked(sig * 31 + Math.Max(1, c.Threads)); // Debug builds check overflow
            if (sig == _coreSig) return;
            _coreSig = sig;

            Cores.Clear();
            Cores.AddRange(_coreMap.Values.OrderBy(c => c.Kind == "P" ? 0 : c.Kind == "" ? 1 : 2).ThenBy(c => c.Num));
            bool explicitKinds = Cores.Any(c => c.Kind != "");
            bool mixed = !explicitKinds && Cores.Select(c => Math.Max(1, c.Threads)).Distinct().Count() > 1;
            int p = 0, e = 0, o = 0;
            foreach (var c in Cores)
            {
                c.Group = explicitKinds ? c.Kind : mixed ? (c.Threads > 1 ? "P" : "E") : "";
                c.Label = c.Group == "P" ? "P" + (++p) : c.Group == "E" ? "E" + (++e) : "C" + (++o);
            }
            // Hybrid parts list P-cores first in LHM; keep P before E regardless of numbering.
            if (mixed) { var sorted = Cores.OrderBy(c => c.Group == "P" ? 0 : 1).ThenBy(c => c.Num).ToList(); Cores.Clear(); Cores.AddRange(sorted); }
            Topology = string.Join(",", Cores.Select(c => c.Group + Math.Max(1, c.Threads)));
        }

        // Hybrid Intel in LHM 0.9.6: loads are "CPU Core #1..8 Thread #1/2" (P) and "CPU Core #9..16" (E), while temps and
        // clocks are "P-Core #1..8" / "E-Core #1..8". Fold the generic load cores into the named ones: multi-thread generic
        // cores map in order onto P-cores, single-thread ones onto E-cores. The map is kept so later ticks write directly.
        private readonly Dictionary<int, string> _genericToKind = new();

        internal static void MergeGeneric(Dictionary<string, CoreInfo> map, Dictionary<int, string> genericToKind)
        {
            var generic = map.Values.Where(c => c.Kind == "").OrderBy(c => c.Num).ToList();
            if (generic.Count == 0) return;
            var p = map.Values.Where(c => c.Kind == "P").OrderBy(c => c.Num).ToList();
            var e = map.Values.Where(c => c.Kind == "E").OrderBy(c => c.Num).ToList();
            if (p.Count + e.Count == 0) return; // not hybrid-named: generic cores are the real ones
            int pi = 0, ei = 0;
            foreach (var g in generic)
            {
                var target = g.Threads > 1 && pi < p.Count ? p[pi++] : ei < e.Count ? e[ei++] : null;
                if (target == null) continue;
                target.Threads = Math.Max(target.Threads, g.Threads);
                for (int i = 0; i < g.Load.Length; i++) if (!double.IsNaN(g.Load[i])) target.Load[i] = g.Load[i];
                genericToKind[g.Num] = target.Kind + target.Num;
                map.Remove("" + g.Num);
            }
        }

        private static string ShortName(string s)
        {
            int gen = s.IndexOf(" Gen ", StringComparison.Ordinal); // "13th Gen Intel Core i7-13700K"
            if (gen >= 0 && gen < 6) s = s.Substring(gen + 5);
            return s.Replace("(R)", "").Replace("(TM)", "").Replace("Intel Core ", "").Replace("Intel ", "").Replace("NVIDIA GeForce ", "")
             .Replace("NVIDIA ", "").Replace("AMD Ryzen ", "Ryzen ").Replace("  ", " ").Trim();
        }

        /// <summary>Intel microcode revision from the registry (high dword of "Update Revision"); 0 if unknown. Read once.</summary>
        private static uint ReadMicrocode()
        {
            try
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (k?.GetValue("Update Revision") is byte[] b && b.Length >= 8) return BitConverter.ToUInt32(b, 4);
            }
            catch (Exception ex) { Trace.WriteLine("Widget microcode: " + ex.Message); }
            return 0;
        }

        public void Dispose() => _io.Dispose();

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    }

    /// <summary>
    /// Slow local counters, sampled on a pool thread so the UI tick never waits on them:
    /// network bytes (interface counters only, no traffic), PhysicalDisk perf counters, WHEA-Logger event counts.
    /// Everything fails soft to NaN / -1.
    /// </summary>
    internal sealed class IoSampler : IDisposable
    {
        internal sealed record State(double Down, double Up, double Read, double Write, int WheaBoot, int Whea30d);

        private State _state = new(double.NaN, double.NaN, double.NaN, double.NaN, -1, -1);
        public State Current => Volatile.Read(ref _state);

        private readonly object _gate = new();
        private readonly Timer _timer;
        private volatile bool _disposed;
        private long _ticks;
        private NetworkInterface[] _nics = new NetworkInterface[0];
        private readonly Dictionary<string, (long Rx, long Tx)> _last = new();
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private double _lastT;
        private PerformanceCounter _rd, _wr;
        private bool _diskFailed;
        private int _wheaBoot = -1, _whea30 = -1;

        public IoSampler() => _timer = new Timer(_ => Tick(), null, 300, 1000);

        private void Tick()
        {
            if (!Monitor.TryEnter(_gate)) return;
            try
            {
                if (_disposed) return;
                long tick = _ticks++;
                if (tick % 30 == 0) _nics = PickNics();

                double now = _sw.Elapsed.TotalSeconds, dt = now - _lastT;
                _lastT = now;
                long rx = 0, tx = 0;
                bool any = false;
                foreach (var ni in _nics)
                {
                    try
                    {
                        var st = ni.GetIPStatistics();
                        long r = st.BytesReceived, t = st.BytesSent;
                        if (_last.TryGetValue(ni.Id, out var p) && r >= p.Rx && t >= p.Tx) { rx += r - p.Rx; tx += t - p.Tx; any = true; }
                        _last[ni.Id] = (r, t);
                    }
                    catch (Exception) { /* adapter went away */ }
                }
                double down = any && dt > 0.2 ? rx * 8.0 / dt : double.NaN, up = any && dt > 0.2 ? tx * 8.0 / dt : double.NaN;

                double read = double.NaN, write = double.NaN;
                if (_rd == null && !_diskFailed)
                {
                    try
                    {
                        _rd = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", true);
                        _wr = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", true);
                        _rd.NextValue(); _wr.NextValue(); // first sample is always 0
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine("Widget disk counters: " + ex.Message);
                        _diskFailed = true;
                        _rd?.Dispose(); _wr?.Dispose(); _rd = _wr = null;
                    }
                }
                else if (_rd != null)
                {
                    try { read = _rd.NextValue(); write = _wr.NextValue(); }
                    catch (Exception) { read = write = double.NaN; }
                }

                if (tick % 60 == 0)
                {
                    _wheaBoot = CountWhea(Environment.TickCount64);
                    _whea30 = CountWhea(30L * 24 * 3600 * 1000);
                }
                Volatile.Write(ref _state, new State(down, up, read, write, _wheaBoot, _whea30));
            }
            catch (Exception ex) { Trace.WriteLine("Widget IO sampler: " + ex.Message); }
            finally
            {
                if (_disposed) ReleaseCounters();
                Monitor.Exit(_gate);
            }
        }

        /// <summary>
        /// Up, non-loopback adapters that have a gateway: the physical uplink. WSL/Hyper-V vEthernet and WireGuard carry the same
        /// bytes again and have no gateway, so summing every adapter would double-count. Falls back to all Up adapters.
        /// </summary>
        private static NetworkInterface[] PickNics()
        {
            try
            {
                var up = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel).ToArray();
                var gw = up.Where(HasGateway).ToArray();
                return gw.Length > 0 ? gw : up;
            }
            catch (Exception ex) { Trace.WriteLine("Widget NICs: " + ex.Message); return new NetworkInterface[0]; }
        }

        private static bool HasGateway(NetworkInterface n)
        {
            try
            {
                return n.GetIPProperties().GatewayAddresses.Any(a => a?.Address != null
                    && !a.Address.Equals(IPAddress.Any) && !a.Address.Equals(IPAddress.IPv6Any));
            }
            catch (Exception) { return false; }
        }

        /// <summary>WHEA-Logger events in the System log within the last <paramref name="ms"/> milliseconds; -1 if unreadable.</summary>
        private static int CountWhea(long ms)
        {
            try
            {
                string q = "*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and TimeCreated[timediff(@SystemTime) <= " + ms + "]]]";
                using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, q));
                int n = 0;
                for (var rec = reader.ReadEvent(); rec != null; rec = reader.ReadEvent())
                {
                    rec.Dispose();
                    if (++n >= 9999) break;
                }
                return n;
            }
            catch (Exception ex) { Trace.WriteLine("Widget WHEA: " + ex.Message); return -1; }
        }

        /// <summary>Never blocks the UI: if a tick is mid-flight (e.g. a slow first perf-counter load), that tick releases the counters.</summary>
        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
            if (!Monitor.TryEnter(_gate)) return;
            try { ReleaseCounters(); }
            finally { Monitor.Exit(_gate); }
        }

        private void ReleaseCounters()
        {
            _rd?.Dispose(); _wr?.Dispose();
            _rd = _wr = null;
            _diskFailed = true;
        }
    }
}
