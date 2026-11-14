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

namespace BcuHolo
{
    public partial class MainWindow : Window
    {
        private List<AppRow> _rows = new();
        private ICollectionView _view;
        private string _driveRoot; // "C:" etc., null = all drives

        public MainWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) => await ScanAsync();
            StateChanged += (_, _) => FitMaximized();

            // Updates and Windows features clutter the list and rarely have sizes; the classic UI still covers them.
            UninstallToolsGlobalConfig.ScanWinUpdates = false;
            UninstallToolsGlobalConfig.ScanWinFeatures = false;
        }

        private async Task ScanAsync()
        {
            RescanButton.IsEnabled = false;
            ScanOverlay.Visibility = Visibility.Visible;
            ScanProgress.IsIndeterminate = true;
            ScanStatus.Text = "Starting…";

            IList<ApplicationUninstallerEntry> entries;
            try
            {
                entries = await Task.Run(() => ApplicationUninstallerFactory.GetUninstallerEntries(p =>
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
            }
            catch (Exception ex)
            {
                ScanStatus.Text = "Scan failed: " + ex.Message;
                RescanButton.IsEnabled = true;
                return;
            }

            var visible = entries
                .Where(e => !e.SystemComponent && !e.IsUpdate && e.UninstallerKind != UninstallerType.WindowsUpdate)
                .ToList();
            var largest = visible.Count == 0 ? 0 : visible.Max(e => e.EstimatedSize.GetKbSize());

            _rows = visible.Select(e => new AppRow(e, largest)).OrderByDescending(r => r.SizeKb).ToList();
            foreach (var r in _rows)
                r.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppRow.IsSelected)) UpdateSelection(); };

            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = o => Matches((AppRow)o);
            AppList.ItemsSource = _view;

            LoadDrives();
            UpdateSelection();

            ScanOverlay.Visibility = Visibility.Collapsed;
            RescanButton.IsEnabled = true;
            AppList.Focus();
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
            var otherKb = Math.Max(0, usedKb - appsKb);
            SplitTitle.Text = $"Where the {AppRow.FormatKb(usedKb)} went";
            AppsTotal.Text = AppRow.FormatKb(appsKb);
            OtherTotal.Text = AppRow.FormatKb(otherKb);
            SplitApps.Width = new GridLength(Math.Max(appsKb, 1), GridUnitType.Star);
            SplitOther.Width = new GridLength(Math.Max(otherKb, 1), GridUnitType.Star);

            var unknown = _rows.Count(r => r.Drive == "?");
            SplitNote.Text = appsKb > usedKb
                ? "App sizes are installer estimates and overshoot here."
                : unknown > 0 && _driveRoot == null ? $"{unknown} apps have no known install folder and aren't counted." : "";
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
            ListCaption.Text = $"· {shown.Count} · {AppRow.FormatKb(shown.Sum(r => r.SizeKb))} · sorted by size";
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

        private async void Rescan_Click(object sender, RoutedEventArgs e) => await ScanAsync();

        private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Max_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // With WindowChrome a maximized window overhangs the screen by the resize border; pad it back in.
        private void FitMaximized()
        {
            var max = WindowState == WindowState.Maximized;
            var t = SystemParameters.WindowResizeBorderThickness;
            Frame.Margin = max ? new Thickness(t.Left + 4, t.Top + 4, t.Right + 4, t.Bottom + 4) : new Thickness(0);
            Frame.BorderThickness = new Thickness(max ? 0 : 1);
            MaxButton.Content = max ? "" : "";
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
        public Brush BarBrush { get; private init; }

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
                BarBrush = (Brush)Application.Current.Resources[used >= 0.9 ? "Danger" : "Accent"],
            };
        }
    }
}
