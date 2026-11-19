using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UninstallTools.Startup;
using BrowserHelperEntry = UninstallTools.Startup.Browser.BrowserHelperEntry;
using ServiceEntry = UninstallTools.Startup.Service.ServiceEntry;
using TaskEntry = UninstallTools.Startup.Task.TaskEntry;
using NormalEntry = UninstallTools.Startup.Normal.StartupEntry;

namespace BcuHolo.Modules.Startup
{
    /// <summary>One startup entry: a thin view over BCU's StartupEntryBase.</summary>
    public sealed class StartupRow : INotifyPropertyChanged
    {
        private readonly Action _changed;
        private bool _enabled;
        private string _error;

        public StartupRow(StartupEntryBase entry, Action changed)
        {
            Entry = entry;
            _changed = changed;

            Name = new[] { entry.ProgramNameTrimmed, entry.ProgramName, entry.Command }
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "(unnamed)";
            Company = entry.Company ?? "";
            Location = entry.ParentShortName ?? "";
            FullLocation = string.IsNullOrEmpty(entry.ParentLongName) ? Location : entry.ParentLongName;
            Kind = KindOf(entry);
            IsMicrosoft = Company.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);
            Initials = string.Concat(Name.Split(new[] { ' ', '-', '_', '.', ':' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetterOrDigit(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0])));
            Icon = LoadIcon(entry.CommandFilePath);

            try
            {
                _enabled = !entry.Disabled;
            }
            catch (Exception ex)
            {
                _error = "Couldn't read state: " + ex.Message;
            }
        }

        public StartupEntryBase Entry { get; }
        public string Name { get; }
        public string Company { get; }
        public string Location { get; }
        public string FullLocation { get; }
        public string Kind { get; }
        public bool IsMicrosoft { get; }
        public string Initials { get; }
        /// <summary>Null when the file has no icon; the view then shows the initials tile.</summary>
        public ImageSource Icon { get; }

        /// <summary>True when the entry runs at startup. Writes through to the engine.</summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                try
                {
                    Entry.Disabled = !value;
                    _enabled = value;
                    Error = null;
                }
                catch (Exception ex)
                {
                    Error = $"Couldn't {(value ? "enable" : "disable")}: {ex.Message}";
                }

                // Always notify: on failure this makes the toggle snap back to the real state.
                Raise(nameof(Enabled));
                _changed();
            }
        }

        public string Error
        {
            get => _error;
            set
            {
                _error = value;
                Raise(nameof(Error));
                Raise(nameof(ErrorVisibility));
                Raise(nameof(CompanyVisibility));
            }
        }

        public Visibility ErrorVisibility => _error == null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility CompanyVisibility => _error == null ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static string KindOf(StartupEntryBase e) => e switch
        {
            ServiceEntry _ => "Service",
            TaskEntry _ => "Task",
            BrowserHelperEntry _ => "Browser",
            NormalEntry n => n.IsRegKey ? "Run key" : "Startup folder",
            _ => "Run key",
        };

        /// <summary>Runs on the worker thread, so the bitmap is frozen before the UI touches it.</summary>
        private static ImageSource LoadIcon(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;
                var img = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                img.Freeze();
                return img;
            }
            catch
            {
                return null; // fall back to the initials tile
            }
        }
    }
}
