using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using BcuHolo.Plugins;
using UninstallTools.Startup;

namespace BcuHolo.Modules.Startup
{
    public sealed class StartupModule : IHoloModule
    {
        private bool _started;
        private bool _viewCreated;

        public string Id => "startup";
        public string Title => "Startup";
        public string Glyph => "";
        public int Order => 60;

        /// <summary>Shared by the page and the dashboard tile. Loaded once, reloaded on demand.</summary>
        public ObservableCollection<StartupRow> Rows { get; } = new();
        public bool IsLoading { get; private set; }

        /// <summary>Set when a whole startup source failed. Per-entry failures live in StartupRow.Error.</summary>
        public string LoadError { get; private set; }

        /// <summary>Raised on the UI thread after a load, a toggle or a delete.</summary>
        public event Action Changed;

        public FrameworkElement CreateView(IHoloHost host)
        {
            EnsureLoaded();
            _viewCreated = true;
            EnsureIcons();
            return new StartupView(this, host);
        }

        public FrameworkElement CreateTile(IHoloHost host)
        {
            EnsureLoaded();
            return new StartupTile(this, host);
        }

        private void EnsureLoaded()
        {
            if (_started) return;
            _started = true;
            _ = ReloadAsync();
        }

        /// <summary>Call from the UI thread. Enumeration runs on the thread pool; the await resumes on the UI thread.</summary>
        public async Task ReloadAsync()
        {
            if (IsLoading) return;
            IsLoading = true;
            Changed?.Invoke();

            var (rows, error) = await Task.Run(() => Collect());
            Rows.Clear();
            foreach (var row in rows) Rows.Add(row);
            LoadError = error;

            IsLoading = false;
            Changed?.Invoke();
            if (_viewCreated) EnsureIcons();
        }

        /// <summary>
        /// Icons are only shown on the page, so they are extracted once a view exists instead of for the dashboard tile.
        /// Extraction runs on the pool (bitmaps come back frozen); assignment resumes on the UI thread.
        /// </summary>
        private async void EnsureIcons()
        {
            var todo = new List<StartupRow>();
            foreach (var row in Rows)
                if (!row.IconQueued) { row.IconQueued = true; todo.Add(row); }
            if (todo.Count == 0) return;

            var icons = await Task.Run(() => todo.ConvertAll(r =>
            {
                try { return StartupRow.LoadIcon(r.Entry.CommandFilePath); }
                catch { return null; } // async void: never let one entry take the app down
            }));
            for (int i = 0; i < todo.Count; i++)
                if (icons[i] != null) todo[i].Icon = icons[i];
        }

        public void Remove(StartupRow row)
        {
            Rows.Remove(row);
            RaiseChanged();
        }

        private void RaiseChanged() => Changed?.Invoke();

        /// <summary>
        /// Runs off the UI thread. Walks the factories one at a time: GetAllStartupItems() is one lazy Concat,
        /// so a single throwing source (Task Scheduler COM, say) would abort everything after it.
        /// </summary>
        private (List<StartupRow> Rows, string Error) Collect()
        {
            var rows = new List<StartupRow>();
            var errors = new List<string>();

            foreach (var factory in StartupManager.Factories.Values)
            {
                try
                {
                    foreach (var entry in factory())
                    {
                        try { rows.Add(new StartupRow(entry, RaiseChanged)); }
                        catch (Exception ex) { errors.Add(ex.Message); }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                }
            }

            return (rows, errors.Count == 0 ? null : "Some startup sources failed to load: " + string.Join("; ", errors));
        }
    }
}
