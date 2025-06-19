using NUnit.Framework;
using ValveResourceFormat.IO;

namespace Tests
{
    [TestFixture]
    public class GameFolderLocatorTest
    {
        [Test]
        public void Test()
        {
            // This test is essentially just verifying that none of these paths crash on the CI because it has no Steam
            var steamPath = GameFolderLocator.SteamPath;
            GameFolderLocator.FindSteamGameByAppId(10);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(GameFolderLocator.FindSteamLibraryFolderPaths(), Is.Not.Null);
                Assert.That(GameFolderLocator.FindAllSteamGames(), Is.Not.Null);
            }
        }
    }
}
