using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace BcuHolo.Modules.Sensors
{
    /// <summary>
    /// Installs the bundled PawnIO driver (ThirdParty\PawnIO). Only ever called from the Sensors page button,
    /// after the user confirms. The installer is pinned by hash: any other file is refused.
    /// </summary>
    internal static class PawnIoInstaller
    {
        // PawnIO_setup.exe 2.2.0, matches GitHub's published release digest (audited 2026-11-15). Update both together.
        private const string ExpectedSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";

        public static string SetupPath => Path.Combine(AppContext.BaseDirectory, "ThirdParty", "PawnIO", "PawnIO_setup.exe");

        public static bool IsInstalled()
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            return key != null;
        }

        /// <summary>Returns null on success, otherwise a message for the user.</summary>
        public static async Task<string> InstallAsync()
        {
            if (IsInstalled()) return null;
            if (!File.Exists(SetupPath)) return "The bundled PawnIO installer is missing:\n" + SetupPath;

            // Hold the file open (no write/delete/rename sharing) from the hash check until setup exits,
            // so it can't be swapped between checking and running.
            using var lockStream = new FileStream(SetupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            string hash;
            using (var sha = SHA256.Create()) hash = Convert.ToHexString(sha.ComputeHash(lockStream));
            if (!string.Equals(hash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                return "The bundled PawnIO installer doesn't match the audited file, so it was not run.\n\nExpected " + ExpectedSha256 + "\nFound    " + hash;

            // Never "-unrestricted": that edition accepts unsigned modules.
            var psi = new ProcessStartInfo(SetupPath, "-install -silent") { UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p == null) return "Windows refused to start the PawnIO installer.";
            await p.WaitForExitAsync();
            if (p.ExitCode != 0 || !IsInstalled())
                return $"PawnIO setup failed (exit code 0x{p.ExitCode:X8}). Nothing else was changed.";
            return null;
        }
    }
}
