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
            var folder = Directory.CreateTempSubdirectory().FullName;

            try
            {
                await File.WriteAllBytesAsync(Path.Join(folder, "pak01_dir.vpk"), []);
                await File.WriteAllBytesAsync(Path.Join(folder, "dota_dir.vpk"), []);

                using (Assert.Multiple())
                {
                    await Assert.That(PackageFileNames.IsArchiveChunk(Path.Join(folder, "pak01_003.vpk"))).IsTrue();
                    await Assert.That(PackageFileNames.IsArchiveChunk(Path.Join(folder, "dota_683.VPK"))).IsTrue();
                    await Assert.That(PackageFileNames.IsArchiveChunk(Path.Join(folder, "pak01_1000.vpk"))).IsTrue();
                    await Assert.That(PackageFileNames.IsArchiveChunk(Path.Join(folder, "pak01_0001.vpk"))).IsFalse();
                    await Assert.That(PackageFileNames.IsArchiveChunk(Path.Join(folder, "pak01_dir.vpk"))).IsFalse();
                    await Assert.That(PackageFileNames.IsArchiveChunk(Path.Join(folder, "surf_2024.vpk"))).IsFalse();

                    await Assert.That(PackageFileNames.GetDirectoryFile(Path.Join(folder, "pak01_003.vpk"))).IsEqualTo(Path.Join(folder, "pak01_dir.vpk"));
                    await Assert.That(PackageFileNames.GetDirectoryFile("C:/game/pak01_dir.vpk")).IsEqualTo("C:/game/pak01_dir.vpk");
                    await Assert.That(PackageFileNames.GetDirectoryFile("addon.vpk")).IsEqualTo("addon_dir.vpk");
                    await Assert.That(PackageFileNames.GetArchiveChunkFile("C:/game/pak01", 7)).IsEqualTo("C:/game/pak01_007.vpk");

                    await Assert.That(PackageFileNames.TryGetArchiveChunkDirectoryFile(Path.Join(folder, "pak01_003.vpk"), out var archiveDirectoryFile)).IsTrue();
                    await Assert.That(archiveDirectoryFile).IsEqualTo(Path.Join(folder, "pak01_dir.vpk"));
                    await Assert.That(PackageFileNames.TryGetArchiveChunkDirectoryFile(Path.Join(folder, "surf_2024.vpk"), out _)).IsFalse();

                    await Assert.That(PackageFileNames.GetPackageFileNameError(Path.Join(folder, "addon.zip"), splitIntoChunks: false)).IsNotNull();
                    await Assert.That(PackageFileNames.GetPackageFileNameError(Path.Join(folder, "pak01_003.vpk"), splitIntoChunks: false)).IsNotNull();
                    await Assert.That(PackageFileNames.GetPackageFileNameError(Path.Join(folder, "surf_2024.vpk"), splitIntoChunks: false)).IsNull();
                    await Assert.That(PackageFileNames.GetPackageFileNameError(Path.Join(folder, "addon_dir.vpk"), splitIntoChunks: true)).IsNull();
                    await Assert.That(PackageFileNames.GetPackageFileNameError(Path.Join(folder, "addon_dir.vpk"), splitIntoChunks: false)).IsNotNull();
                    await Assert.That(PackageFileNames.GetPackageFileNameError(Path.Join(folder, "addon.vpk"), splitIntoChunks: true)).IsNotNull();
                }
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Test]
        public async Task PackageNames()
        {
            var folder = Directory.CreateTempSubdirectory().FullName;

            try
            {
                await File.WriteAllBytesAsync(Path.Join(folder, "pak01_dir.vpk"), []);

                using (Assert.Multiple())
                {
                    await Assert.That(PackageFileNames.GetPackageName("C:/game/pak01_dir.vpk")).IsEqualTo("C:/game/pak01");
                    await Assert.That(PackageFileNames.GetPackageName("C:/game/pak01_dir")).IsEqualTo("C:/game/pak01");
                    await Assert.That(PackageFileNames.GetPackageName(Path.Join(folder, "pak01_003.vpk"))).IsEqualTo(Path.Join(folder, "pak01"));
                    await Assert.That(PackageFileNames.GetPackageName(Path.Join(folder, "pak01_12345.vpk"))).IsEqualTo(Path.Join(folder, "pak01"));
                    await Assert.That(PackageFileNames.GetPackageName(Path.Join(folder, "surf_2024.vpk"))).IsEqualTo(Path.Join(folder, "surf_2024"));
                    await Assert.That(PackageFileNames.GetPackageName("maps/de_dust2.vpk")).IsEqualTo("maps/de_dust2");
                }
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Test]
        public async Task FilesToPack()
        {
            var folder = Directory.CreateTempSubdirectory().FullName;

            try
            {
                Directory.CreateDirectory(Path.Join(folder, "Materials"));
                Directory.CreateDirectory(Path.Join(folder, ".git"));

                string[] names = ["Materials/Floor.vmat", "readme.txt", ".gitignore", ".git/config", "out_dir.vpk", "out_000.vpk", "out.vpk"];

                foreach (var name in names)
                {
                    await File.WriteAllBytesAsync(Path.Join(folder, name), []);
                }

                var files = PackageFileNames.GetFilesToPack(folder, Path.Join(folder, "out_dir.vpk"), "addon/");

                await Assert.That(string.Join(',', files.Select(static file => file.EntryPath).Order(StringComparer.Ordinal))).IsEqualTo("addon/Materials/Floor.vmat,addon/out.vpk,addon/readme.txt");
                await Assert.That(files[0].PathOnDisk).StartsWith(folder);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
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
