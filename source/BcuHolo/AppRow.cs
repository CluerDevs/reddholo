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
            SizeText = SizeKb > 0 ? FormatKb(SizeKb) : "—";

            // Bar fill as proportional star columns; tiny apps still show a sliver.
            var share = largestKb > 0 ? (double)SizeKb / largestKb : 0;
            BarFill = new GridLength(Math.Max(share, 0.02), GridUnitType.Star);
            BarRest = new GridLength(1 - Math.Min(share, 0.98), GridUnitType.Star);

            Initials = string.Concat(Name.Split(new[] { ' ', '-', '_', '.', ':' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetterOrDigit(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0])));
            Icon = ToImage(entry);
        }

        public ApplicationUninstallerEntry Entry { get; }
        public long SizeKb { get; }
        public string Name { get; }
        public string Publisher { get; }
        public string Kind { get; }
        public string SizeText { get; }
        public string Initials { get; }
        public ImageSource Icon { get; }
        public GridLength BarFill { get; }
        public GridLength BarRest { get; }

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
            _ => "App",
        };

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
