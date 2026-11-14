using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using UninstallTools;
using UninstallTools.Factory;

namespace BcuHolo
{
    public partial class MainWindow : Window
    {
        private List<AppRow> _rows = new();
        private ICollectionView _view;

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => DarkTitleBar();
            Loaded += async (_, _) => await ScanAsync();

            // Updates and Windows features clutter the list and rarely have sizes; the classic UI still covers them.
            UninstallToolsGlobalConfig.ScanWinUpdates = false;
            UninstallToolsGlobalConfig.ScanWinFeatures = false;
        }

        private async Task ScanAsync()
        {
            RescanButton.IsEnabled = false;
            ScanOverlay.Visibility = Visibility.Visible;
            ScanStatus.Text = "Starting…";
            ShowDrive(0);

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
            foreach (var r in _rows) r.PropertyChanged += (_, _) => UpdateSelection();

            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = o => Matches((AppRow)o);
            AppList.ItemsSource = _view;

            var appsKb = _rows.Sum(r => r.SizeKb);
            ShowDrive(appsKb);
            UpdateCaption();
            UpdateSelection();

            ScanOverlay.Visibility = Visibility.Collapsed;
            RescanButton.IsEnabled = true;
            AppList.Focus();
        }

        private void ShowDrive(long appsKb)
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            long totalKb = drive.TotalSize / 1024, freeKb = drive.TotalFreeSpace / 1024, usedKb = totalKb - freeKb;
            var used = totalKb > 0 ? (double)usedKb / totalKb : 0;

            DriveLabel.Text = "Local disk " + drive.Name.TrimEnd('\\');
            DrivePct.Text = Math.Round(used * 100) + "%";
            DriveFree.Text = AppRow.FormatKb(freeKb);
            DriveUsed.Text = $"{AppRow.FormatKb(usedKb)} / {AppRow.FormatKb(totalKb)}";

            // Ellipse dash lengths are in StrokeThickness units: circumference = π·(132 − 12) / 12.
            var circ = Math.PI * (132 - 12) / 12;
            DriveRing.StrokeDashArray = new DoubleCollection { used * circ, circ };

            // App sizes are publisher/scan estimates, so clamp: they can exceed what the drive reports.
            var otherKb = Math.Max(0, usedKb - appsKb);
            SplitTitle.Text = $"Where the {AppRow.FormatKb(usedKb)} went";
            AppsTotal.Text = appsKb > 0 ? AppRow.FormatKb(appsKb) : "—";
            OtherTotal.Text = appsKb > 0 ? AppRow.FormatKb(otherKb) : "—";
            SplitApps.Width = new GridLength(Math.Max(appsKb, 1), GridUnitType.Star);
            SplitOther.Width = new GridLength(Math.Max(otherKb, 1), GridUnitType.Star);
        }

        private bool Matches(AppRow r)
        {
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
            var shown = _view?.Cast<object>().Count() ?? 0;
            ListCaption.Text = shown == _rows.Count
                ? $"· {_rows.Count} · sorted by size"
                : $"· {shown} of {_rows.Count} · sorted by size";
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

        // Dark native title bar so the window chrome matches (Windows 11; silently ignored on older builds).
        private void DarkTitleBar()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int on = 1, caption = 0x0020100A; // COLORREF 0x00BBGGRR for #0A1020
            DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));      // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
