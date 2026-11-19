using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using BcuHolo.Plugins;

namespace BcuHolo.Modules.Cleanup
{
    public sealed class CleanupModule : IHoloModule
    {
        private static readonly EnumerationOptions Opts = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint, // never follow junctions or symlinks
        };

        private readonly List<CleanupItem> _items;
        private IHoloHost _host;
        private Task _firstScan;

        public CleanupModule() => _items = BuildItems();

        public string Id => "cleanup";
        public string Title => "Cleanup";
        public string Glyph => "\uE74D";
        public int Order => 30;

        public IReadOnlyList<CleanupItem> Items => _items;

        public FrameworkElement CreateView(IHoloHost host)
        {
            Ensure(host);
            return new CleanupView(this, host);
        }

        public FrameworkElement CreateTile(IHoloHost host)
        {
            Ensure(host);
            return BuildTile(host);
        }

        public static string Fmt(long bytes) => AppRow.FormatKb(bytes / 1024);

        /// <summary>Measures everything once, on first use by the tile or the view.</summary>
        private void Ensure(IHoloHost host)
        {
            _host ??= host;
            _firstScan ??= MeasureAllAsync();
        }

        internal async Task MeasureAllAsync()
        {
            await Task.WhenAll(_items.Select(MeasureOne));
            _host?.SetBadge(Id, Fmt(DefaultTotal()));
        }

        private static async Task MeasureOne(CleanupItem item)
        {
            item.SetSize(await Task.Run(item.Measure));
        }

        /// <summary>Cleans the given items one by one, then re-measures each so the rows show what is left.</summary>
        internal async Task<(long Freed, int Skipped, List<string> Failed)> CleanAsync(IEnumerable<CleanupItem> picked)
        {
            long freed = 0;
            int skipped = 0;
            var failed = new List<string>();
            foreach (var item in picked)
            {
                try
                {
                    var r = await Task.Run(item.Clean);
                    freed += r.Freed;
                    skipped += r.Skipped;
                }
                catch (Exception)
                {
                    failed.Add(item.Name);
                }
                await MeasureOne(item);
            }
            return (freed, skipped, failed);
        }

        /// <summary>Shuts WSL down, compacts one VHDX with diskpart, and reports the size change.</summary>
        internal async Task<string> CompactAsync(CleanupItem item)
        {
            var before = item.SizeBytes ?? 0;
            string error = null;
            try { await Task.Run(() => CompactVhdx(item.DiskPath)); }
            catch (Exception ex) { error = ex.Message; }

            var after = await Task.Run(item.Measure);
            item.SetSize(after);
            return error != null
                ? $"Compact failed: {error}. WSL was shut down; the disk is now {Fmt(after)}."
                : $"Compacted {item.Name}: {Fmt(before)} -> {Fmt(after)} (freed {Fmt(Math.Max(0, before - after))}).";
        }

        private long DefaultTotal() => _items.Where(i => i.DefaultChecked && !i.Hidden).Sum(i => i.SizeBytes ?? 0);

        // ---- Definitions ----

        private static List<CleanupItem> BuildItems()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var items = new List<CleanupItem>
            {
                Folders("NVIDIA shader cache", "Rebuilt automatically; games may stutter briefly on first launch.",
                    "Safe to delete. The driver regenerates it.", true,
                    new[] { Path.Combine(local, "NVIDIA", "DXCache"), Path.Combine(local, "NVIDIA", "GLCache") }),
                Folders("DirectX shader cache", "Compiled shaders for DirectX games and apps.",
                    "Safe to delete. Rebuilt on demand.", true,
                    new[] { Path.Combine(local, "D3DSCache") }),
                Folders("Temporary files", "Temp folders: only files not written in the last 24 hours.",
                    "Apps still holding a file keep it.", true,
                    new[] { Path.GetTempPath(), Path.Combine(windows, "Temp") }, OldEnough),
                Folders("Windows Update download cache", "Update packages Windows has already downloaded.",
                    "Pending updates download again if needed.", true,
                    new[] { Path.Combine(windows, "SoftwareDistribution", "Download") }),
                new CleanupItem
                {
                    Name = "Recycle Bin",
                    Description = "Everything in the Recycle Bin on all drives.",
                    Risk = "Permanent. Deleted items cannot be restored afterwards.",
                    Measure = RecycleBytes,
                    Clean = CleanRecycleBin,
                },
                new CleanupItem
                {
                    Name = "Hibernation file",
                    Description = "Turns off hibernate and Fast Startup. Turn back on with: powercfg /hibernate on",
                    Risk = "Discards the hibernate state. Boot is slightly slower without Fast Startup.",
                    HideWhenZero = true,
                    Measure = HiberBytes,
                    Clean = CleanHibernate,
                },
            };

            items.AddRange(WslDisks().Select(d => new CleanupItem
            {
                Name = "WSL virtual disk (compact): " + d.Distro,
                Description = "Shrinks the disk file. Only space Linux has already freed is returned.",
                Risk = "Shuts WSL down first. The freed amount is known only after the run.",
                Compactable = true,
                HideWhenZero = true,
                DiskPath = d.Vhdx,
                Measure = () => FileBytes(d.Vhdx),
            }));

            foreach (var i in items) i.IsChecked = i.DefaultChecked;
            return items;
        }

        private static CleanupItem Folders(string name, string desc, string risk, bool defaultChecked,
            string[] roots, Func<FileInfo, bool> keep = null) => new()
        {
            Name = name,
            Description = desc,
            Risk = risk,
            DefaultChecked = defaultChecked,
            Measure = () => FolderBytes(roots, keep),
            Clean = () => CleanFolders(roots, keep),
        };

        private static bool OldEnough(FileInfo f) => f.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-24);

        // ---- Folder contents ----

        private static IEnumerable<string> Normalize(IEnumerable<string> roots) =>
            roots.Select(r => Path.GetFullPath(r).TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase);

        private static List<FileInfo> ListFiles(IEnumerable<string> roots, Func<FileInfo, bool> keep)
        {
            var files = new List<FileInfo>();
            foreach (var root in Normalize(roots))
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    files.AddRange(new DirectoryInfo(root).EnumerateFiles("*", Opts).Where(f => keep == null || keep(f)));
                }
                catch (Exception)
                {
                    // Root became unreadable mid-walk: keep what was listed so far.
                }
            }
            return files;
        }

        private static List<string> SubDirs(IEnumerable<string> roots)
        {
            var dirs = new List<string>();
            foreach (var root in Normalize(roots))
            {
                if (!Directory.Exists(root)) continue;
                try { dirs.AddRange(new DirectoryInfo(root).EnumerateDirectories("*", Opts).Select(d => d.FullName)); }
                catch (Exception) { }
            }
            return dirs;
        }

        private static long FolderBytes(string[] roots, Func<FileInfo, bool> keep)
        {
            // Streamed, not via ListFiles: temp/shader caches can hold tens of thousands of files, and Length
            // is already filled in from the directory enumeration.
            long total = 0;
            foreach (var root in Normalize(roots))
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", Opts))
                        if (keep == null || keep(f)) total += f.Length;
                }
                catch (Exception)
                {
                    // Root became unreadable mid-walk: keep what was counted so far.
                }
            }
            return total;
        }

        /// <summary>Deletes matching files, then empty subfolders deepest first. The root folders themselves stay.</summary>
        private static CleanResult CleanFolders(string[] roots, Func<FileInfo, bool> keep)
        {
            long freed = 0;
            int skipped = 0;
            foreach (var f in ListFiles(roots, keep))
            {
                try
                {
                    var len = f.Length;
                    f.Delete();
                    freed += len;
                }
                catch (Exception)
                {
                    skipped++; // locked, in use, or read-only: leave it and count it
                }
            }

            // Longer path = deeper, so this removes children before their parents.
            foreach (var d in SubDirs(roots).OrderByDescending(d => d.Length))
            {
                try { Directory.Delete(d, false); } // non-recursive: only succeeds when empty
                catch (Exception) { }
            }
            return new CleanResult(freed, skipped);
        }

        // ---- Recycle Bin ----

        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        private static long RecycleBytes()
        {
            // Default (natural) layout matches the native struct on x64: 24 bytes. Pack=4 would give 20 and break the call.
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
            return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
        }

        private static CleanResult CleanRecycleBin()
        {
            var before = RecycleBytes();
            // 0x7 = SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND. HRESULT ignored; the re-query is the truth.
            SHEmptyRecycleBin(IntPtr.Zero, null, 0x7);
            return new CleanResult(Math.Max(0, before - RecycleBytes()), 0);
        }

        // ---- Hibernation ----

        private static long HiberBytes()
        {
            try { return new DirectoryInfo(@"C:\").EnumerateFiles("hiberfil.sys").FirstOrDefault()?.Length ?? 0; }
            catch (Exception) { return 0; } // the file is locked; enumeration still reports its length
        }

        private static CleanResult CleanHibernate()
        {
            var before = HiberBytes();
            var (code, _) = Run("powercfg.exe", "/hibernate", "off");
            if (code != 0) throw new InvalidOperationException($"powercfg exited {code}");
            return new CleanResult(Math.Max(0, before - HiberBytes()), 0);
        }

        // ---- WSL ----

        private static List<(string Distro, string Vhdx)> WslDisks()
        {
            var disks = new List<(string Distro, string Vhdx)>();
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
                if (root == null) return disks;
                foreach (var guid in root.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(guid);
                    var basePath = k?.GetValue("BasePath") as string;
                    if (string.IsNullOrEmpty(basePath)) continue;
                    if (basePath.StartsWith(@"\\?\", StringComparison.Ordinal)) basePath = basePath.Substring(4);
                    var file = k.GetValue("VhdFileName") as string ?? "ext4.vhdx";
                    var distro = k.GetValue("DistributionName") as string ?? guid;
                    disks.Add((distro, Path.Combine(basePath, file)));
                }
            }
            catch (Exception) { }
            return disks;
        }

        private static void CompactVhdx(string vhdx)
        {
            Run("wsl.exe", "--shutdown");
            Thread.Sleep(3000); // a short grace period for the VM service to release the VHDX; not verified on this box
            var script = Path.Combine(Path.GetTempPath(), "bcu-compact-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(script,
                    $"select vdisk file=\"{vhdx}\"\r\nattach vdisk readonly\r\ncompact vdisk\r\ndetach vdisk\r\n");
                var (code, output) = Run("diskpart.exe", "/s", script);
                if (code != 0)
                {
                    // A failed compact can leave the disk attached, which would stop WSL from starting. Best-effort detach.
                    File.WriteAllText(script, $"select vdisk file=\"{vhdx}\"\r\ndetach vdisk\r\n");
                    Run("diskpart.exe", "/s", script);
                    var last = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
                    throw new InvalidOperationException("diskpart: " + last);
                }
            }
            finally
            {
                try { File.Delete(script); } catch (Exception) { }
            }
        }

        // ---- Shared helpers ----

        private static long FileBytes(string path)
        {
            try { return new FileInfo(path).Length; }
            catch (Exception) { return 0; }
        }

        private static (int Code, string Output) Run(string exe, params string[] args)
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output);
        }

        // ---- Dashboard tile ----

        private static T R<T>(string key) => (T)Application.Current.Resources[key];

        private FrameworkElement BuildTile(IHoloHost host)
        {
            var total = new TextBlock
            {
                Text = "…",
                FontFamily = R<FontFamily>("Display"),
                FontSize = 40,
                FontWeight = FontWeights.Bold,
            };
            total.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
            var rows = new StackPanel { Margin = new Thickness(0, 12, 0, 16) };
            var button = new Button { Style = R<Style>("Primary"), Content = "Review & clean" };
            button.Click += (_, _) => host.Navigate(Id);

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "CLEANUP", Style = R<Style>("Caps") });
            panel.Children.Add(total);
            panel.Children.Add(rows);
            panel.Children.Add(button);

            void Refresh()
            {
                total.Text = _items.All(i => i.Measured) ? Fmt(DefaultTotal()) : "…";
                rows.Children.Clear();
                foreach (var i in _items.Where(i => i.DefaultChecked && !i.Hidden)
                             .OrderByDescending(i => i.SizeBytes ?? 0).Take(4))
                {
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var name = new TextBlock { Text = i.Name, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0) };
                    var size = new TextBlock { Text = i.SizeText, FontFamily = R<FontFamily>("Mono"), HorizontalAlignment = HorizontalAlignment.Right };
                    Grid.SetColumn(size, 1);
                    grid.Children.Add(name);
                    grid.Children.Add(size);
                    var row = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 8, 0, 8), Child = grid };
                    row.SetResourceReference(Border.BorderBrushProperty, "RowLine");
                    rows.Children.Add(row);
                }
            }

            foreach (var i in _items) i.PropertyChanged += (_, _) => Refresh();
            Refresh();

            return new Border { Style = R<Style>("DashTile"), Child = panel };
        }
    }
}
