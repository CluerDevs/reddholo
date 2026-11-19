using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using UninstallTools;
using UninstallTools.Factory;
using UninstallTools.Junk;
using UninstallTools.Junk.Confidence;
using UninstallTools.Junk.Containers;
using UninstallTools.Uninstaller;

namespace BcuHolo.Modules.Apps
{
    public partial class AppsView : UserControl
    {
        private List<AppRow> _rows = new();
        private IList<ApplicationUninstallerEntry> _allEntries = new List<ApplicationUninstallerEntry>();
        private ICollectionView _view;
        private string _driveRoot; // "C:" etc., null = all drives

        private bool _scanned;

        public AppsView()
        {
            InitializeComponent();
            // The shell caches this view, so Loaded fires on every visit; scan only the first time.
            Loaded += async (_, _) =>
            {
                if (_scanned) return;
                _scanned = true;
                try { await ScanAsync(); }
                catch (Exception ex) { ShowError("Scan failed", ex); }
            };

            // Updates and Windows features clutter the list and rarely have sizes; the classic UI still covers them.
            UninstallToolsGlobalConfig.ScanWinUpdates = false;
            UninstallToolsGlobalConfig.ScanWinFeatures = false;
        }

        private void ShowError(string title, Exception ex) =>
            MessageBox.Show(Window.GetWindow(this), title + ": " + ex.Message, "reddholo", MessageBoxButton.OK, MessageBoxImage.Error);

        private void EndOverlay()
        {
            StopButton.Visibility = Visibility.Collapsed;
            ScanOverlay.Visibility = Visibility.Collapsed;
            RescanButton.IsEnabled = true;
        }

        private async Task ScanAsync()
        {
            ShowOverlay("Scanning installed apps…");
            try
            {
                var entries = await Task.Run(() => ApplicationUninstallerFactory.GetUninstallerEntries(p =>
                    Dispatcher.BeginInvoke(() =>
                    {
                        ScanStatus.Text = p.Inner?.Message ?? p.Message;
                        if (p.TotalCount > 0)
                        {
                            ScanProgress.IsIndeterminate = false;
                            ScanProgress.Maximum = p.TotalCount;
                            ScanProgress.Value = p.CurrentCount;
                        }
                    })));

                var visible = entries
                    .Where(e => !e.SystemComponent && !e.IsUpdate && e.UninstallerKind != UninstallerType.WindowsUpdate)
                    .ToList();
                var largest = visible.Count == 0 ? 0 : visible.Max(e => e.EstimatedSize.GetKbSize());

                _allEntries = entries;
                _rows = visible.Select(e => new AppRow(e, largest)).OrderByDescending(r => r.SizeKb).ToList();
                foreach (var r in _rows)
                    r.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppRow.IsSelected)) UpdateSelection(); };

                _view = CollectionViewSource.GetDefaultView(_rows);
                _view.Filter = o => Matches((AppRow)o);
                AppList.ItemsSource = _view;

                _bigFiles = await Task.Run(() => FindBigFiles(FixedDrives()));
                LoadDrives();
                UpdateSelection();
                AppList.Focus();
            }
            catch (Exception ex)
            {
                ShowError("Scan failed", ex);
            }
            finally
            {
                EndOverlay();
            }
        }

        private void ShowOverlay(string title)
        {
            RescanButton.IsEnabled = false;
            SelectionBar.Visibility = Visibility.Collapsed;
            OverlayTitle.Text = title;
            ScanOverlay.Visibility = Visibility.Visible;
            ScanProgress.IsIndeterminate = true;
            ScanStatus.Text = "Starting…";
            StopButton.Visibility = Visibility.Collapsed;
        }

        private bool Confirm(string text, string title) =>
            MessageBox.Show(Window.GetWindow(this), text, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

        private static string Bullets(IEnumerable<string> items, int max = 15)
        {
            var list = items.ToList();
            return string.Join("\n", list.Take(max).Select(i => "• " + i)) + (list.Count > max ? $"\n…and {list.Count - max} more" : "");
        }

        /// <summary>
        /// Same flow as classic BCU: run each app's own uninstaller (quiet when possible), then scan for
        /// leftovers of the ones that completed and delete those only after a second confirmation.
        /// </summary>
        private async void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            try { await UninstallFlowAsync(); }
            catch (Exception ex) { ShowError("Uninstall failed", ex); }
            finally
            {
                _stopUninstall = null;
                EndOverlay();
                UpdateSelection();
            }
        }

        private Action _stopUninstall;

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            StopButton.IsEnabled = false;
            _stopUninstall?.Invoke();
        }

        private async Task UninstallFlowAsync()
        {
            var picked = _rows.Where(r => r.IsSelected).ToList();
            if (picked.Count == 0) return;
            var dry = DryRun.IsChecked == true;
            var dryNote = dry ? "DRY RUN: nothing will be removed.\n\n" : "";

            if (!Confirm($"{dryNote}Uninstall {picked.Count} app(s), {AppRow.FormatKb(picked.Sum(r => r.SizeKb))}?\n\n" +
                         Bullets(picked.Select(r => $"{r.Name}  ({r.SizeText})")) +
                         "\n\nEach app's own uninstaller runs (quietly when possible). Leftovers are listed afterwards for a separate OK.",
                    dry ? "Uninstall (dry run)" : "Uninstall"))
                return;

            ShowOverlay(dry ? "Simulating uninstall…" : "Uninstalling…");
            var targets = picked.Select(r => new BulkUninstallEntry(r.Entry, r.Entry.QuietUninstallPossible,
                !r.Entry.IsValid ? UninstallStatus.Invalid
                : r.Entry.IsProtected ? UninstallStatus.Protected
                : UninstallStatus.Waiting)).ToList();

            var finished = new TaskCompletionSource();
            StopButton.IsEnabled = true;
            StopButton.Visibility = Visibility.Visible;
            using (var task = UninstallManager.CreateBulkUninstallTask(targets,
                       new BulkUninstallConfiguration(ignoreProtection: false, preferQuiet: true, simulate: dry,
                           autoKillStuckQuiet: true, retryFailedQuiet: true)))
            {
                task.OnStatusChanged += (_, _) => Dispatcher.BeginInvoke(() =>
                {
                    var done = targets.Count(x => x.Finished);
                    ScanProgress.IsIndeterminate = false;
                    ScanProgress.Maximum = targets.Count;
                    ScanProgress.Value = done;
                    var running = targets.FirstOrDefault(x => x.IsRunning);
                    ScanStatus.Text = $"{done} of {targets.Count}" + (running != null ? " · " + running.UninstallerEntry.DisplayName : "");
                    if (task.Finished) finished.TrySetResult();
                });
                // Aborted skips apps not yet started; an uninstaller already running can't be interrupted.
                _stopUninstall = () => { task.Aborted = true; finished.TrySetResult(); };
                try { task.Start(); }
                catch (Exception ex) { finished.TrySetException(ex); }
                await finished.Task;
            }
            _stopUninstall = null;
            StopButton.Visibility = Visibility.Collapsed;

            var removed = targets.Where(x => x.CurrentStatus == UninstallStatus.Completed).Select(x => x.UninstallerEntry).ToList();
            var notRemoved = targets.Where(x => x.CurrentStatus != UninstallStatus.Completed)
                .Select(x => $"{x.UninstallerEntry.DisplayName}: {x.CurrentStatus}{(x.CurrentError != null ? " – " + x.CurrentError.Message : "")}").ToList();
            var summary = $"{dryNote}Uninstalled {removed.Count} of {targets.Count}." +
                          (notRemoved.Count > 0 ? "\n\nNot removed:\n" + Bullets(notRemoved) : "");

            List<IJunkResult> junk = new();
            if (removed.Count > 0)
            {
                OverlayTitle.Text = "Looking for leftovers…";
                ScanProgress.IsIndeterminate = true;
                var all = _allEntries;
                try
                {
                    // Only "Good" or better, matching what classic BCU pre-selects; lower-confidence hits are often other apps' files.
                    junk = await Task.Run(() => JunkManager
                        .FindJunk(removed, all.Where(a => a.RegKeyStillExists()).ToList(),
                            p => Dispatcher.BeginInvoke(() => ScanStatus.Text = p.Inner?.Message ?? p.Message))
                        .Where(j => j.Confidence.GetConfidence() >= ConfidenceLevel.Good)
                        .ToList());
                }
                catch (Exception ex)
                {
                    summary += "\n\nLeftover scan failed: " + ex.Message;
                }
            }

            if (junk.Count == 0)
            {
                MessageBox.Show(Window.GetWindow(this), summary + (removed.Count > 0 ? "\n\nNo leftovers found." : ""), "Uninstall", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (Confirm($"{summary}\n\nFound {junk.Count} leftover item(s):\n{Bullets(junk.Select(j => j.ToString()), 20)}\n\nDelete them permanently?", "Clean leftovers"))
            {
                OverlayTitle.Text = dry ? "Simulating cleanup…" : "Cleaning leftovers…";
                // Run commands and stop startup entries before deleting the files they point at (same order as classic BCU).
                var ordered = junk.OrderByDescending(j => j is RunProcessJunk).ThenByDescending(j => j is StartupJunkNode).ToList();
                List<string> errors;
                try
                {
                    errors = await Task.Run(() =>
                    {
                        var failed = new List<string>();
                        foreach (var j in ordered)
                        {
                            if (dry) continue;
                            try { j.Delete(); }
                            catch (Exception ex) { failed.Add($"{j}: {ex.Message}"); }
                        }
                        return failed;
                    });
                }
                catch (Exception ex)
                {
                    ShowError("Leftover cleanup failed", ex);
                    errors = new List<string>();
                    ordered = new List<IJunkResult>();
                }
                MessageBox.Show(Window.GetWindow(this), $"{(dry ? "Dry run: would have removed" : "Removed")} {ordered.Count - errors.Count} leftover item(s)." +
                                      (errors.Count > 0 ? "\n\nCouldn't remove:\n" + Bullets(errors) : ""),
                    "Clean leftovers", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            await ScanAsync();
        }

        private static List<DriveInfo> FixedDrives() =>
            DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).ToList();

        private void LoadDrives()
        {
            var drives = FixedDrives();
            var items = new List<DriveItem> { DriveItem.All(drives, _rows.Count) };
            items.AddRange(drives.Select(DriveItem.For));

            var keep = _driveRoot ?? Root(new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!));
            DriveList.ItemsSource = items;
            DriveList.SelectedItem = items.FirstOrDefault(i => i.Root == keep) ?? items[0];
        }

        private static string Root(DriveInfo d) => d.Name.Substring(0, 2).ToUpperInvariant();

        private void Drive_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (DriveList.SelectedItem is not DriveItem item) return;
            _driveRoot = item.Root;
            _view?.Refresh();
            UpdateCaption();
            ShowOverview();
        }

        private void ShowOverview()
        {
            var drives = FixedDrives().Where(d => _driveRoot == null || Root(d) == _driveRoot).ToList();
            var roots = drives.Select(Root).ToHashSet();
            long totalKb = drives.Sum(d => d.TotalSize) / 1024, freeKb = drives.Sum(d => d.TotalFreeSpace) / 1024;
            long usedKb = totalKb - freeKb;
            var used = totalKb > 0 ? (double)usedKb / totalKb : 0;

            DriveLabel.Text = _driveRoot == null ? $"All {drives.Count} drives" : $"Local disk {_driveRoot}";
            DrivePct.Text = Math.Round(used * 100) + "%";
            DriveFree.Text = AppRow.FormatKb(freeKb);
            DriveUsed.Text = $"{AppRow.FormatKb(usedKb)} / {AppRow.FormatKb(totalKb)}";

            // Ellipse dash lengths are in StrokeThickness units: circumference = π·(132 − 12) / 12.
            var circ = Math.PI * (132 - 12) / 12;
            DriveRing.StrokeDashArray = new DoubleCollection { used * circ, circ };

            // Only apps whose install folder is on these drives count toward their used space.
            var appsKb = _rows.Where(r => roots.Contains(r.Drive)).Sum(r => r.SizeKb);
            var wsl = _bigFiles.Where(f => f.IsWsl && roots.Contains(f.Drive)).ToList();
            var sys = _bigFiles.Where(f => !f.IsWsl && roots.Contains(f.Drive)).ToList();
            long wslKb = wsl.Sum(f => f.Kb), sysKb = sys.Sum(f => f.Kb);
            var otherKb = Math.Max(0, usedKb - appsKb - wslKb - sysKb);

            SplitTitle.Text = $"Where the {AppRow.FormatKb(usedKb)} went";
            AppsTotal.Text = AppRow.FormatKb(appsKb);
            WslTotal.Text = AppRow.FormatKb(wslKb);
            SysTotal.Text = AppRow.FormatKb(sysKb);
            OtherTotal.Text = AppRow.FormatKb(otherKb);
            WslLegend.Visibility = wslKb > 0 ? Visibility.Visible : Visibility.Collapsed;
            SysLegend.Visibility = sysKb > 0 ? Visibility.Visible : Visibility.Collapsed;
            SplitApps.Width = new GridLength(Math.Max(appsKb, 1), GridUnitType.Star);
            SplitWsl.Width = new GridLength(wslKb, GridUnitType.Star);
            SplitSys.Width = new GridLength(sysKb, GridUnitType.Star);
            SplitOther.Width = new GridLength(Math.Max(otherKb, 1), GridUnitType.Star);

            var notes = new List<string>();
            if (wsl.Count + sys.Count > 0)
                notes.Add(string.Join(" · ", wsl.Concat(sys).OrderByDescending(f => f.Kb).Select(f => $"{f.Name} {AppRow.FormatKb(f.Kb)}")));
            var unknown = _rows.Count(r => r.Drive == "?");
            if (appsKb > usedKb) notes.Add("App sizes are installer estimates and overshoot here.");
            else if (unknown > 0 && _driveRoot == null) notes.Add($"{unknown} apps have no known install folder and aren't counted.");
            SplitNote.Text = string.Join("\n", notes);
        }

        private List<BigFile> _bigFiles = new();

        private sealed record BigFile(string Name, string Path, long Kb, bool IsWsl)
        {
            public string Drive => AppRow.DriveOf(Path);
        }

        /// <summary>WSL distro disks (from the Lxss registry) plus hiberfil/pagefile/swapfile on every fixed drive.</summary>
        private static List<BigFile> FindBigFiles(IEnumerable<DriveInfo> drives)
        {
            var found = new List<BigFile>();
            using (var lxss = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss"))
            {
                foreach (var id in lxss?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var k = lxss.OpenSubKey(id);
                    var basePath = (k?.GetValue("BasePath") as string)?.Replace(@"\\?\", "");
                    if (string.IsNullOrEmpty(basePath)) continue;
                    var vhd = new FileInfo(Path.Combine(basePath, k.GetValue("VhdFileName") as string ?? "ext4.vhdx"));
                    if (vhd.Exists) found.Add(new BigFile("WSL " + (k.GetValue("DistributionName") ?? id), vhd.FullName, vhd.Length / 1024, true));
                }
            }
            foreach (var d in drives)
            {
                // Enumerate rather than stat: pagefile/hiberfil are locked and can fail a direct FileInfo lookup.
                try
                {
                    foreach (var f in d.RootDirectory.EnumerateFiles("*.sys"))
                        if (f.Name is "hiberfil.sys" or "pagefile.sys" or "swapfile.sys")
                            found.Add(new BigFile($"{f.Name} ({Root(d)})", f.FullName, f.Length / 1024, false));
                }
                catch (Exception) { /* unreadable root: skip */ }
            }
            return found;
        }

        private bool Matches(AppRow r)
        {
            if (_driveRoot != null && r.Drive != _driveRoot) return false;
            var q = SearchBox.Text.Trim();
            if (q.Length > 0 && r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                             && r.Publisher.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            if (FilterLarge.IsChecked == true) return r.SizeKb >= 1024 * 1024;
            if (FilterMs.IsChecked == true) return r.Publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);
            if (FilterStore.IsChecked == true) return r.Entry.UninstallerKind == UninstallerType.StoreApp;
            return true;
        }

        private void UpdateCaption()
        {
            var shown = _view?.Cast<AppRow>().ToList() ?? new List<AppRow>();
            var largest = shown.Count == 0 ? 0 : shown.Max(r => r.SizeKb);
            foreach (var r in shown) r.SetScale(largest);
            ListCaption.Text = $"· {shown.Count} · {AppRow.FormatKb(shown.Sum(r => r.SizeKb))}";
        }

        private void UpdateSelection()
        {
            var sel = _rows.Where(r => r.IsSelected).ToList();
            SelectionBar.Visibility = sel.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            SelCount.Text = $"{sel.Count} selected";
            SelSize.Text = AppRow.FormatKb(sel.Sum(r => r.SizeKb));
            UninstallButton.Content = $"Uninstall {sel.Count} + clean leftovers";
        }

        private void Search_Changed(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _view?.Refresh();
            UpdateCaption();
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;
            _view?.Refresh();
            UpdateCaption();
        }

        private void Row_Click(object sender, MouseButtonEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is AppRow r) r.IsSelected = !r.IsSelected;
        }

        private void List_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && AppList.SelectedItem is AppRow r)
            {
                r.IsSelected = !r.IsSelected;
                e.Handled = true;
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.IsSelected = false;
        }

        private async void Rescan_Click(object sender, RoutedEventArgs e)
        {
            try { await ScanAsync(); }
            catch (Exception ex) { ShowError("Scan failed", ex); }
        }
    }

    /// <summary>One card in the Drives rail.</summary>
    public sealed class DriveItem
    {
        public string Root { get; private init; }
        public string Title { get; private init; }
        public string Detail { get; private init; }
        public GridLength UsedFill { get; private init; }
        public GridLength UsedRest { get; private init; }
        public bool Full { get; private init; }

        public static DriveItem All(IReadOnlyCollection<DriveInfo> drives, int apps) =>
            Make(null, "All drives", $"{apps} apps", drives.Sum(d => d.TotalSize), drives.Sum(d => d.TotalFreeSpace));

        public static DriveItem For(DriveInfo d)
        {
            var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Local disk" : d.VolumeLabel;
            return Make(d.Name.Substring(0, 2).ToUpperInvariant(), $"{d.Name.Substring(0, 2)} {label}",
                $"{AppRow.FormatKb(d.TotalFreeSpace / 1024)} free", d.TotalSize, d.TotalFreeSpace);
        }

        private static DriveItem Make(string root, string title, string detail, long total, long free)
        {
            var used = total > 0 ? (double)(total - free) / total : 0;
            return new DriveItem
            {
                Root = root,
                Title = title,
                Detail = detail,
                UsedFill = new GridLength(Math.Max(used, 0.01), GridUnitType.Star),
                UsedRest = new GridLength(Math.Max(1 - used, 0.01), GridUnitType.Star),
                // Nearly-full drives turn orange so they stand out in the rail.
                Full = used >= 0.9,
            };
        }
    }
}
