using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat.IO;

namespace Tests.IO
{
    public class GameFolderLocatorTest
    {
        [Test]
        public async Task Test()
        {
            // This test is essentially just verifying that none of these paths crash on the CI because it has no Steam
            var steamPath = GameFolderLocator.SteamPath;
            GameFolderLocator.FindSteamGameByAppId(10);
            using (Assert.Multiple())
            {
                await Assert.That(GameFolderLocator.FindSteamLibraryFolderPaths()).IsNotNull();
                await Assert.That(GameFolderLocator.FindAllSteamGames()).IsNotNull();
            }
        }

        [Test]
        public async Task SteamAppPathRequiresAppId()
        {
            using (Assert.Multiple())
            {
                await Assert.That(GameFolderLocator.IsSteamAppPath("steam:730/game/csgo/pak01_dir.vpk")).IsTrue();
                await Assert.That(GameFolderLocator.IsSteamAppPath("STEAM:730")).IsTrue();
                await Assert.That(GameFolderLocator.IsSteamAppPath("steam")).IsFalse();
                await Assert.That(GameFolderLocator.IsSteamAppPath("C:/steam/pak01_dir.vpk")).IsFalse();

                await Assert.That(GameFolderLocator.TryResolveSteamAppPath("C:/game/pak01_dir.vpk", out var resolvedPath, out _)).IsTrue();
                await Assert.That(resolvedPath).IsEqualTo("C:/game/pak01_dir.vpk");

                foreach (var path in (string[])["steam:cs2/game", "steam:-730/game", "steam: 730/game", "steam:/game", "steam:999999999/game"])
                {
                    await Assert.That(GameFolderLocator.TryResolveSteamAppPath(path, out resolvedPath, out var error)).IsFalse();
                    await Assert.That(resolvedPath).IsEqualTo(path);
                    await Assert.That(error).IsNotNull();
                }
            }
        }

        [Test]
        public async Task VpkLinkParse()
        {
            var (packagePaths, innerPath) = VpkLink.Parse("vpk:steam:730/game/csgo/pak01_dir.vpk:maps/de_dust2.vpk:maps/de_dust2/entities/default_ents.vents_c");
            var (noPackagePaths, noPackageInnerPath) = VpkLink.Parse("vpk:C:/Some%2520Game/file.vmdl_c");

            using (Assert.Multiple())
            {
                await Assert.That(VpkLink.IsVpkLink("VPK:file")).IsTrue();
                await Assert.That(packagePaths).IsEquivalentTo(["steam:730/game/csgo/pak01_dir.vpk", "maps/de_dust2.vpk"]);
                await Assert.That(innerPath).IsEqualTo("maps/de_dust2/entities/default_ents.vents_c");
                await Assert.That(noPackagePaths).IsEmpty();
                await Assert.That(noPackageInnerPath).IsEqualTo("C:/Some%20Game/file.vmdl_c");
                await Assert.That(VpkLink.EscapePath("100%.txt")).IsEqualTo("100%25.txt");
            }
        }

        [Test]
        public async Task GetSteamAppPath()
        {
            var steamApps = Path.Join(Path.GetTempPath(), "VrfSteamAppPathTest", "steamapps");
            var gamePath = Path.Join(steamApps, "common", string.Concat("Some Game", Path.DirectorySeparatorChar));
            var otherGamePath = Path.Join(steamApps, "common", string.Concat("Some Game 2", Path.DirectorySeparatorChar));

            GameFolderLocator.SteamLibraryGameInfo[] games =
            [
                new(123, "Some Game", steamApps, gamePath),
                new(456, "Some Game 2", steamApps, otherGamePath),
            ];

            using (Assert.Multiple())
            {
                await Assert.That(GameFolderLocator.GetSteamAppPath(Path.Join(gamePath, "game", "dir", "pak01_dir.vpk"), games)).IsEqualTo("steam:123/game/dir/pak01_dir.vpk");
                await Assert.That(GameFolderLocator.GetSteamAppPath(Path.Join(otherGamePath, "pak01_dir.vpk"), games)).IsEqualTo("steam:456/pak01_dir.vpk");
                await Assert.That(GameFolderLocator.GetSteamAppPath(Path.Join(steamApps, "common", "Elsewhere", "pak01_dir.vpk"), games)).IsNull();
            }
        }
    }
}
