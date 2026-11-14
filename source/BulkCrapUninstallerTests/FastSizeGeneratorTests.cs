using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UninstallTools;
using UninstallTools.Factory.InfoAdders;

namespace BulkCrapUninstallerTests
{
    [TestClass]
    public class FastSizeGeneratorTests
    {
        // Guards the late-bound Scripting.FileSystemObject swap: its static ctor swallows
        // failures, so a broken COM binding would otherwise just leave sizes silently empty.
        [TestMethod]
        public void AddMissingInformation_RealFolder_SetsNonZeroSize()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bcu-size-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "blob.bin"), new byte[256 * 1024]);
                var entry = new ApplicationUninstallerEntry { InstallLocation = dir };

                new FastSizeGenerator().AddMissingInformation(entry);

                Assert.IsTrue(entry.EstimatedSize.GetKbSize() >= 256, "Size was " + entry.EstimatedSize);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
