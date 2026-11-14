using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UninstallTools;

namespace BcuHolo
{
    /// <summary>One row in the app list: a thin view over BCU's ApplicationUninstallerEntry.</summary>
    public sealed class AppRow : INotifyPropertyChanged
    {
        private bool _isSelected;

        public AppRow(ApplicationUninstallerEntry entry, long largestKb)
        {
            Entry = entry;
            SizeKb = Math.Max(0, entry.EstimatedSize.GetKbSize());
            Name = entry.DisplayNameTrimmed ?? entry.DisplayName ?? "(unnamed)";
            Publisher = entry.PublisherTrimmed ?? "";
            Kind = KindLabel(entry.UninstallerKind);
            Drive = DriveOf(entry.InstallLocation);
            SizeText = SizeKb > 0 ? FormatKb(SizeKb) : "—";

            SetScale(largestKb);

            Initials = string.Concat(Name.Split(new[] { ' ', '-', '_', '.', ':' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetterOrDigit(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0])));
            Icon = ToImage(entry);
        }

        public ApplicationUninstallerEntry Entry { get; }
        public long SizeKb { get; }
        public string Name { get; }
        public string Publisher { get; }
        /// <summary>Installer type, or null for plain apps (the chip is hidden then).</summary>
        public string Kind { get; }
        /// <summary>"C:", "D:", … or "?" when the install location is unknown.</summary>
        public string Drive { get; }
        public string SizeText { get; }
        public string Initials { get; }
        public ImageSource Icon { get; }
        public GridLength BarFill { get; private set; }
        public GridLength BarRest { get; private set; }

        /// <summary>Size bars are relative to the largest app currently shown.</summary>
        public void SetScale(long largestKb)
        {
            // Proportional star columns; tiny apps still show a sliver.
            var share = largestKb > 0 ? Math.Min(1, (double)SizeKb / largestKb) : 0;
            BarFill = new GridLength(Math.Max(share, 0.02), GridUnitType.Star);
            BarRest = new GridLength(Math.Max(1 - share, 0.0001), GridUnitType.Star);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BarFill)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BarRest)));
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public static string FormatKb(long kb)
        {
            if (kb >= 1024L * 1024) return (kb / 1024d / 1024).ToString("0.0") + " GB";
            if (kb >= 1024) return (kb / 1024d).ToString("0") + " MB";
            return kb + " KB";
        }

        private static string KindLabel(UninstallerType t) => t switch
        {
            UninstallerType.Steam => "Steam",
            UninstallerType.StoreApp => "Store app",
            UninstallerType.Msiexec => "MSI",
            UninstallerType.WindowsFeature => "Win feature",
            UninstallerType.Chocolatey => "Chocolatey",
            UninstallerType.Oculus => "Oculus",
            _ => null,
        };

        public static string DriveOf(string path)
        {
            try
            {
                var root = string.IsNullOrWhiteSpace(path) ? null : System.IO.Path.GetPathRoot(path);
                return root is { Length: >= 2 } && root[1] == ':' ? root.Substring(0, 2).ToUpperInvariant() : "?";
            }
            catch (ArgumentException)
            {
                return "?";
            }
        }

        private static ImageSource ToImage(ApplicationUninstallerEntry entry)
        {
            try
            {
                var icon = entry.GetIcon();
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
