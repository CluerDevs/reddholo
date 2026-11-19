using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BcuHolo.Plugins;

namespace BcuHolo.Modules.Cleanup
{
    public partial class CleanupView : UserControl
    {
        private readonly CleanupModule _module;
        private readonly IHoloHost _host;
        private bool _busy;

        public CleanupView(CleanupModule module, IHoloHost host)
        {
            InitializeComponent();
            _module = module;
            _host = host;
            ItemList.ItemsSource = module.Items;
            foreach (var item in module.Items) item.PropertyChanged += (_, _) => UpdateTotals();
            UpdateTotals();
        }

        private void UpdateTotals()
        {
            var items = _module.Items;
            var picked = items.Where(i => i.Selectable && i.IsChecked && !i.Hidden).ToList();
            TotalText.Text = items.All(i => i.Measured) ? CleanupModule.Fmt(picked.Sum(i => i.SizeBytes ?? 0)) : "…";
            CountText.Text = $"{picked.Count} of {items.Count(i => i.Selectable && !i.Hidden)} items checked";
            CleanButton.IsEnabled = !_busy && picked.Count > 0;
            RescanButton.IsEnabled = !_busy;
        }

        private async Task RunAsync(string title, Func<Task<string>> work)
        {
            _busy = true;
            BusyTitle.Text = title;
            BusyOverlay.Visibility = Visibility.Visible;
            try { UpdateTotals(); ResultText.Text = await work(); }
            catch (Exception ex) { ResultText.Text = "Failed: " + ex.Message; }
            finally
            {
                _busy = false;
                BusyOverlay.Visibility = Visibility.Collapsed;
                UpdateTotals();
            }
        }

        private async void Rescan_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await RunAsync("Measuring…", async () =>
                {
                    await _module.MeasureAllAsync();
                    return "Sizes updated.";
                });
            }
            catch (Exception ex) { ResultText.Text = "Failed: " + ex.Message; }
        }

        private async void Clean_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picked = _module.Items.Where(i => i.Selectable && i.IsChecked && !i.Hidden && i.Measured).ToList();
                if (picked.Count == 0) return;

                var list = string.Join("\n", picked.Select(i => $"• {i.Name}  ({i.SizeText})"));
                var total = CleanupModule.Fmt(picked.Sum(i => i.SizeBytes ?? 0));
                if (!_host.Confirm("Clean selected",
                        $"Clean {picked.Count} item(s), {total}?\n\n{list}\n\nFiles in use are skipped and left alone."))
                    return;

                await RunAsync("Cleaning…", async () =>
                {
                    var (freed, skipped, failed) = await _module.CleanAsync(picked);
                    var text = $"Freed {CleanupModule.Fmt(freed)} · {skipped} files skipped (in use)";
                    return failed.Count == 0 ? text : text + " · failed: " + string.Join(", ", failed);
                });
            }
            catch (Exception ex) { ResultText.Text = "Failed: " + ex.Message; }
        }

        private async void Compact_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if ((sender as FrameworkElement)?.DataContext is not CleanupItem item || !item.Measured) return;

                var message = $"Compact {item.Name}?\n\n" +
                              $"Disk file: {item.DiskPath}\nCurrent size: {item.SizeText}\n\n" +
                              "This shuts down WSL — every Linux terminal and anything running in WSL " +
                              "(including Claude Code sessions) will close.";
                if (!_host.Confirm("Compact WSL disk", message)) return;

                await RunAsync("Shutting down WSL and compacting…", () => _module.CompactAsync(item));
            }
            catch (Exception ex) { ResultText.Text = "Failed: " + ex.Message; }
        }
    }
}
