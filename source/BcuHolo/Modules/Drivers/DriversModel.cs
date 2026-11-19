using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using BcuHolo.Plugins;

namespace BcuHolo.Modules.Drivers
{
    /// <summary>
    /// State shared by the Drivers page and its dashboard tile (one instance, owned by DriversModule).
    /// Background tasks only return plain data; every property write happens after an await on the UI context.
    /// </summary>
    public sealed class DriversModel : INotifyPropertyChanged
    {
        private readonly List<DriverRow> _all = new();
        private Task _loadTask;
        private Task _checkTask;
        private bool _showMicrosoft;
        private bool _checking;
        private string _biosLine = "BIOS: loading…";
        private string _boardLine = "";
        private string _message = "";
        private DateTime? _lastChecked;
        private int? _updateCount;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Set by DriversModule when the shell creates the view or tile.</summary>
        public IHoloHost Host { get; set; }

        public ObservableCollection<DriverRow> Gpus { get; } = new();
        public ObservableCollection<DriverRow> Rows { get; } = new();
        public ObservableCollection<DriverUpdateRow> Updates { get; } = new();

        public string BiosLine => _biosLine;
        public string BoardLine => _boardLine;
        public bool CanCheck => !_checking;
        public string CountText => $"{Rows.Count + Gpus.Count} shown of {_all.Count} drivers";
        public string GpuName => Gpus.Count == 0 ? "No graphics driver listed" : Gpus[0].DeviceName;
        public string GpuVersion => Gpus.Count == 0 ? "" : Gpus[0].VersionLine;
        public string EmptyText => _updateCount == 0 ? "No driver updates found." : "";
        public string StatusText =>
            _checking ? "Checking Windows Update…"
            : _message != "" ? _message
            : _lastChecked is { } t ? $"Last checked {t:HH:mm}"
            : "";

        public string UpdateTileText => _updateCount switch
        {
            null => "Updates: not checked",
            0 => "Drivers up to date",
            var n => $"Updates: {n} available",
        };

        /// <summary>Theme resource key for the update line colour; the view applies it with SetResourceReference.</summary>
        public string UpdateBrushKey => _updateCount switch
        {
            null => "Muted",
            0 => "Good",
            _ => "Danger",
        };

        /// <summary>Off by default: Microsoft-provided inbox drivers are hidden.</summary>
        public bool ShowMicrosoft
        {
            get => _showMicrosoft;
            set
            {
                if (_showMicrosoft == value) return;
                _showMicrosoft = value;
                Refilter();
            }
        }

        /// <summary>Local WMI read, once per model. Safe to call from both the page and the tile.</summary>
        public Task LoadLocalAsync() => _loadTask ??= LoadLocalCore();

        /// <summary>Windows Update search. Only runs when the user presses a Check button.</summary>
        public Task CheckUpdatesAsync()
        {
            if (!_checking) _checkTask = CheckCore();
            return _checkTask;
        }

        /// <summary>Opens a URL or ms-settings link in the shell. Never installs anything.</summary>
        public void Open(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _message = $"Could not open {target}: {ex.Message}";
                Changed();
            }
        }

        private async Task LoadLocalCore()
        {
            try
            {
                var rows = await Task.Run(() => ReadDrivers());
                var (bios, board) = await Task.Run(() => ReadPlatform());
                _all.Clear();
                _all.AddRange(rows);
                _biosLine = bios;
                _boardLine = board;
                Refilter();
            }
            catch (Exception ex)
            {
                _message = "Driver scan failed: " + ex.Message;
                Changed();
            }
        }

        private async Task CheckCore()
        {
            _checking = true;
            _message = "";
            Changed();
            try
            {
                var found = await Task.Run(() => SearchUpdates());
                Updates.Clear();
                foreach (var u in found) Updates.Add(u);
                _updateCount = found.Count;
                _lastChecked = DateTime.Now;
                Host?.SetBadge("drivers", _updateCount > 0 ? _updateCount.ToString() : null);
            }
            catch (Exception ex)
            {
                _message = "Check failed: " + ex.Message;
            }
            finally
            {
                _checking = false;
                Changed();
            }
        }

        private void Refilter()
        {
            Rows.Clear();
            Gpus.Clear();
            foreach (var r in _all.Where(r => _showMicrosoft || !r.IsMicrosoft))
            {
                if (r.IsDisplay) Gpus.Add(r);
                else Rows.Add(r);
            }
            Changed();
        }

        private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

        // ---- Background work (no UI objects, no bindings) ----

        private static List<DriverRow> ReadDrivers()
        {
            var list = new List<DriverRow>();
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DeviceClass, Manufacturer, DriverVersion, DriverDate, DriverProviderName, InfName FROM Win32_PnPSignedDriver");
            using var results = searcher.Get();
            foreach (ManagementBaseObject o in results)
            {
                var name = Str(o, "DeviceName");
                if (string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new DriverRow
                {
                    DeviceName = name,
                    DeviceClass = Str(o, "DeviceClass") ?? "OTHER",
                    Manufacturer = Str(o, "Manufacturer"),
                    Version = Str(o, "DriverVersion"),
                    Date = DateOf(Str(o, "DriverDate")),
                    Provider = Str(o, "DriverProviderName"),
                    Inf = Str(o, "InfName"),
                });
            }
            return list.OrderBy(r => r.IsDisplay ? 0 : 1).ThenBy(r => r.DeviceClass).ThenBy(r => r.DeviceName).ToList();
        }

        private static (string Bios, string Board) ReadPlatform()
        {
            try
            {
                string bios = "", board = "";
                using (var s = new ManagementObjectSearcher("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
                using (var res = s.Get())
                    foreach (ManagementBaseObject o in res)
                        bios = "BIOS " + Join(Str(o, "SMBIOSBIOSVersion"), Str(o, "Manufacturer"), DateOf(Str(o, "ReleaseDate")));
                using (var s = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                using (var res = s.Get())
                    foreach (ManagementBaseObject o in res)
                        board = "Board " + Join(Str(o, "Manufacturer"), Str(o, "Product"));
                return (bios, board);
            }
            catch (Exception ex)
            {
                return ("BIOS unavailable: " + ex.Message, "");
            }
        }

        /// <summary>Late-bound so the build needs no COM reference. Runs on a thread-pool (MTA) thread.</summary>
        private static List<DriverUpdateRow> SearchUpdates()
        {
            var list = new List<DriverUpdateRow>();
            dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", true));
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic result = searcher.Search("IsInstalled=0 and Type='Driver'");
            dynamic updates = result.Updates;
            int n = (int)updates.Count;
            for (int i = 0; i < n; i++)
            {
                dynamic u = updates.Item(i);
                list.Add(new DriverUpdateRow
                {
                    Title = Read(() => (string)u.Title),
                    Model = Read(() => (string)u.DriverModel),
                    VerDate = Read(() => ((DateTime)u.DriverVerDate).ToString("yyyy-MM-dd")),
                    Class = Read(() => (string)u.DriverClass),
                });
            }
            return list;
        }

        private static string Read(Func<string> get)
        {
            try { return get() ?? ""; }
            catch (Exception) { return ""; } // non-driver or partially filled updates throw on some properties
        }

        private static string Str(ManagementBaseObject o, string prop) => o[prop]?.ToString();

        private static string DateOf(string wmi)
        {
            if (string.IsNullOrEmpty(wmi)) return "";
            try { return ManagementDateTimeConverter.ToDateTime(wmi).ToString("yyyy-MM-dd"); }
            catch (Exception) { return ""; }
        }

        private static string Join(params string[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
