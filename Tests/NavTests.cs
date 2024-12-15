using System.IO;
using System.Linq;
using NUnit.Framework;
using ValveResourceFormat.NavMesh;

namespace Tests
{
    public class NavTests
    {
        private static NavMeshFile GetNavMesh(string navMeshName)
        {
            var navMeshPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", navMeshName);
            var navMeshFile = new NavMeshFile();
            navMeshFile.Read(navMeshPath);
            return navMeshFile;
        }

        [Test]
        public void TestNavVersion30_NavGenVersion6()
        {
            var navMeshFile = GetNavMesh("preview_flat.nav");
            Assert.Multiple(() =>
            {
                Assert.That(navMeshFile.Version, Is.EqualTo(30));
                Assert.That(navMeshFile.SubVersion, Is.EqualTo(0));
                Assert.That(navMeshFile.Areas.Count, Is.EqualTo(3));
                Assert.That(navMeshFile.GenerationParams.NavGenVersion, Is.EqualTo(6));
                Assert.That(navMeshFile.GenerationParams.HullParams[2].MaxJumpUpDist, Is.EqualTo(240));
            });
        }

        [Test]
        public void TestNavVersion30_NavGenVersion7()
        {
            var navMeshFile = GetNavMesh("workshop_example_tilemesh.nav");
            Assert.Multiple(() =>
            {
                Assert.That(navMeshFile.Version, Is.EqualTo(30));
                Assert.That(navMeshFile.SubVersion, Is.EqualTo(0));
                Assert.That(navMeshFile.Areas.Count, Is.EqualTo(414));
                Assert.That(navMeshFile.GenerationParams.NavGenVersion, Is.EqualTo(7));
                Assert.That(navMeshFile.GenerationParams.HullParams[2].MaxJumpUpDist, Is.EqualTo(240));
            });
        }

        [Test]
        public void TestNavVersion35()
        {
            var navMeshFile = GetNavMesh("lobby_mapveto.nav");
            Assert.Multiple(() =>
            {
                Assert.That(navMeshFile.Version, Is.EqualTo(35));
                Assert.That(navMeshFile.SubVersion, Is.EqualTo(1));
                Assert.That(navMeshFile.Areas.Count, Is.EqualTo(4));
                Assert.That(navMeshFile.Ladders.Count, Is.EqualTo(0));
                Assert.That(navMeshFile.GenerationParams.NavGenVersion, Is.EqualTo(12));
                Assert.That(navMeshFile.CustomData, Is.Not.Null);
            });
        }
    }
}
