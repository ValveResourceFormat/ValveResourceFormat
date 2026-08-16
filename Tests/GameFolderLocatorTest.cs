using System.Threading.Tasks;
using ValveResourceFormat.IO;

namespace Tests
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
    }
}
