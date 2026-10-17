using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.IO;

namespace Tests.IO
{
    public class PackageFileNamesTest
    {
        [Test]
        public async Task ArchiveChunks()
        {
            using (Assert.Multiple())
            {
                await Assert.That(PackageFileNames.IsArchiveChunk("C:/game/pak01_003.vpk")).IsTrue();
                await Assert.That(PackageFileNames.IsArchiveChunk("dota_683.VPK")).IsTrue();
                await Assert.That(PackageFileNames.IsArchiveChunk("pak01_dir.vpk")).IsFalse();
                await Assert.That(PackageFileNames.IsArchiveChunk("de_dust2.vpk")).IsFalse();
                await Assert.That(PackageFileNames.IsArchiveChunk("_003.vpk")).IsFalse();

                await Assert.That(PackageFileNames.GetDirectoryFile("C:/game/pak01_003.vpk")).IsEqualTo("C:/game/pak01_dir.vpk");
                await Assert.That(PackageFileNames.GetDirectoryFile("C:/game/pak01_dir.vpk")).IsEqualTo("C:/game/pak01_dir.vpk");
                await Assert.That(PackageFileNames.GetDirectoryFile("addon.vpk")).IsEqualTo("addon_dir.vpk");
                await Assert.That(PackageFileNames.GetArchiveChunkFile("C:/game/pak01", 7)).IsEqualTo("C:/game/pak01_007.vpk");
            }
        }

        [Test]
        public async Task PackageNames()
        {
            using (Assert.Multiple())
            {
                await Assert.That(PackageFileNames.GetPackageName("C:/game/pak01_dir.vpk")).IsEqualTo("C:/game/pak01");
                await Assert.That(PackageFileNames.GetPackageName("C:/game/pak01_dir")).IsEqualTo("C:/game/pak01");
                await Assert.That(PackageFileNames.GetPackageName("C:/game/pak01_003.vpk")).IsEqualTo("C:/game/pak01");
                await Assert.That(PackageFileNames.GetPackageName("maps/de_dust2.vpk")).IsEqualTo("maps/de_dust2");
            }
        }

        [Test]
        public async Task PakNumbers()
        {
            var folder = Directory.CreateTempSubdirectory().FullName;

            try
            {
                foreach (var name in (string[])["pak00_dir.vpk", "pak03_dir.vpk", "pak01_dir.vpk", "pak01_000.vpk", "pakxx_dir.vpk", "pak100_dir.vpk"])
                {
                    await File.WriteAllBytesAsync(Path.Join(folder, name), []);
                }

                using (Assert.Multiple())
                {
                    await Assert.That(PackageFileNames.GetPakNumber("pak01_dir.vpk")).IsEqualTo(1);
                    await Assert.That(PackageFileNames.GetPakNumber("PAK42_DIR.VPK")).IsEqualTo(42);
                    await Assert.That(PackageFileNames.GetPakNumber("pak1_dir.vpk")).IsEqualTo(-1);
                    await Assert.That(PackageFileNames.GetPakNumber("abc01_dir.vpk")).IsEqualTo(-1);

                    await Assert.That(string.Join(',', PackageFileNames.EnumeratePakDirectoryFiles(folder).Select(Path.GetFileName)))
                        .IsEqualTo("pak01_dir.vpk,pak03_dir.vpk");
                }
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
