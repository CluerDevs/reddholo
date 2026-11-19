using System;
using System.Linq;

namespace BcuHolo.Modules.Drivers
{
    /// <summary>One row from Win32_PnPSignedDriver, already converted for display.</summary>
    public sealed class DriverRow
    {
        public string DeviceName { get; init; }
        public string DeviceClass { get; init; }
        public string Manufacturer { get; init; }
        public string Version { get; init; }
        /// <summary>yyyy-MM-dd, or "" when WMI gave no usable date.</summary>
        public string Date { get; init; }
        public string Provider { get; init; }
        public string Inf { get; init; }

        public bool IsDisplay => DeviceClass == "DISPLAY";
        public bool IsMicrosoft => string.Equals(Provider, "Microsoft", StringComparison.OrdinalIgnoreCase);
        public bool IsNvidia => (Manufacturer + " " + DeviceName).Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);

        /// <summary>NVIDIA's public number (e.g. "562.01") for an NVIDIA row, else null.</summary>
        public string NvidiaVersion => IsNvidia ? ToNvidiaNumber(Version) : null;

        /// <summary>"32.0.15.6201  ·  NVIDIA 562.01" for NVIDIA, the plain Windows version otherwise.</summary>
        public string VersionLine => NvidiaVersion is { } nv ? $"{Version}  ·  NVIDIA {nv}" : Version;

        /// <summary>
        /// Windows reports NVIDIA drivers as 32.0.15.6201; NVIDIA's public number is the last two
        /// components concatenated ("156201"), keeping the last five digits ("56201"), with a dot before the last two.
        /// </summary>
        public static string ToNvidiaNumber(string windowsVersion)
        {
            var parts = (windowsVersion ?? "").Split('.');
            if (parts.Length < 4) return null;
            var digits = parts[2] + parts[3];
            if (digits.Length < 3 || !digits.All(char.IsDigit)) return null;
            if (digits.Length > 5) digits = digits.Substring(digits.Length - 5);
            return digits.Insert(digits.Length - 2, ".");
        }
    }

    /// <summary>One pending driver update from Windows Update.</summary>
    public sealed class DriverUpdateRow
    {
        public string Title { get; init; }
        public string Model { get; init; }
        public string VerDate { get; init; }
        public string Class { get; init; }

        public string Detail => string.Join(" · ", new[] { Class, Model, VerDate }.Where(s => !string.IsNullOrEmpty(s)));
    }
}
