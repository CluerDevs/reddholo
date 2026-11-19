using System;
using System.ComponentModel;
using System.Windows;

namespace BcuHolo.Modules.Cleanup
{
    /// <summary>What one clean run freed and how many files it had to leave alone.</summary>
    public readonly record struct CleanResult(long Freed, int Skipped);

    /// <summary>One cleanup row. Measure and Clean are plain delegates so the list stays declarative.</summary>
    public sealed class CleanupItem : INotifyPropertyChanged
    {
        private bool _checked;
        private long? _size;
        private bool _hidden;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Name { get; init; }
        public string Description { get; init; }
        public string Risk { get; init; }
        public bool DefaultChecked { get; init; }

        /// <summary>WSL disks: own "Compact…" button, never part of "Clean selected".</summary>
        public bool Compactable { get; init; }

        /// <summary>Hide the row when it measures 0 bytes (e.g. no hiberfil.sys).</summary>
        public bool HideWhenZero { get; init; }

        /// <summary>Full path of the VHDX for compactable items.</summary>
        public string DiskPath { get; init; }

        public Func<long> Measure { get; init; }
        public Func<CleanResult> Clean { get; init; }

        public bool Selectable => !Compactable;
        public bool Measured => _size.HasValue;
        public long? SizeBytes => _size;

        public bool IsChecked
        {
            get => _checked;
            set { if (_checked == value) return; _checked = value; Raise(nameof(IsChecked)); }
        }

        public bool Hidden
        {
            get => _hidden;
            private set
            {
                if (_hidden == value) return;
                _hidden = value;
                Raise(nameof(Hidden));
                Raise(nameof(ItemVisibility));
            }
        }

        public Visibility ItemVisibility => Hidden ? Visibility.Collapsed : Visibility.Visible;
        public Visibility CheckVisibility => Compactable ? Visibility.Collapsed : Visibility.Visible;
        public Visibility CompactVisibility => Compactable ? Visibility.Visible : Visibility.Collapsed;

        public string SizeText => !_size.HasValue ? "measuring…"
            : Compactable ? CleanupModule.Fmt(_size.Value) + " (disk file)"
            : CleanupModule.Fmt(_size.Value);

        public void SetSize(long bytes)
        {
            _size = bytes;
            Raise(nameof(SizeBytes));
            Raise(nameof(SizeText));
            Raise(nameof(Measured));
            Hidden = HideWhenZero && bytes == 0;
        }

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
