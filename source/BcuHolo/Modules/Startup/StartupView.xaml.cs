using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BcuHolo.Plugins;
using UninstallTools.Startup;

namespace BcuHolo.Modules.Startup
{
    public partial class StartupView : UserControl
    {
        private readonly StartupModule _module;
        private readonly IHoloHost _host;
        private readonly ICollectionView _view;

        public StartupView(StartupModule module, IHoloHost host)
        {
            InitializeComponent();
            _module = module;
            _host = host;

            _view = CollectionViewSource.GetDefaultView(module.Rows);
            _view.Filter = o => Matches((StartupRow)o);
            ItemList.ItemsSource = _view;

            // Deferred: a toggle raises this from inside its own binding update, and refreshing the filter there would pull the row out mid-click.
            module.Changed += () => Dispatcher.BeginInvoke(new Action(OnModuleChanged));
            OnModuleChanged();
        }

        private void OnModuleChanged()
        {
            _view.Refresh();

            var rows = _module.Rows;
            var loading = _module.IsLoading && rows.Count == 0;
            CountOn.Text = loading ? "…" : $"{rows.Count(r => r.Enabled)} on";
            CountTotal.Text = loading ? "" : $" / {rows.Count}";
            LoadingText.Visibility = _module.IsLoading ? Visibility.Visible : Visibility.Collapsed;
            LoadErrorText.Text = _module.LoadError;
            LoadErrorText.Visibility = _module.LoadError == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private bool Matches(StartupRow r)
        {
            var q = SearchBox.Text.Trim();
            if (q.Length > 0 && r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                             && r.Company.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            if (FilterOn.IsChecked == true) return r.Enabled;
            if (FilterOff.IsChecked == true) return !r.Enabled;
            if (FilterServices.IsChecked == true) return r.Kind == "Service";
            if (FilterTasks.IsChecked == true) return r.Kind == "Task";
            return true;
        }

        private void Search_Changed(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _view?.Refresh();
        }

        private void Filter_Changed(object sender, RoutedEventArgs e) => _view?.Refresh();

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await _module.ReloadAsync();

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: StartupRow row }) return;

            var backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BCU-Holo", "StartupBackups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            if (!_host.Confirm("Delete startup entry",
                    $"Delete \"{row.Name}\" ({row.Kind}, {row.Location}) from startup?\n\n" +
                    $"A backup is saved first to:\n{backupDir}"))
                return;

            var error = await Task.Run(() => DeleteEntry(row.Entry, backupDir));
            if (error != null)
            {
                row.Error = error;
                return;
            }
            _module.Remove(row);
        }

        /// <summary>Backs up first. If the backup throws, nothing is deleted.</summary>
        private static string DeleteEntry(StartupEntryBase entry, string backupDir)
        {
            try
            {
                Directory.CreateDirectory(backupDir);
                entry.CreateBackup(backupDir);
            }
            catch (Exception ex)
            {
                return "Backup failed, not deleted: " + ex.Message;
            }

            try
            {
                entry.Delete();
                return null;
            }
            catch (Exception ex)
            {
                return "Delete failed: " + ex.Message;
            }
        }
    }
}
