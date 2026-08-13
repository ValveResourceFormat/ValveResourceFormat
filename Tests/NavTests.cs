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
            using (Assert.EnterMultipleScope())
            {
                Assert.That(navMeshFile.Version, Is.EqualTo(30));
                Assert.That(navMeshFile.SubVersion, Is.Zero);
                Assert.That(navMeshFile.Areas, Has.Count.EqualTo(3));
                Assert.That(navMeshFile.GenerationParams?.NavGenVersion, Is.EqualTo(6));
                Assert.That(navMeshFile.GenerationParams?.HullParams[2].MaxJumpUpDist, Is.EqualTo(240));
            }
        }

        [Test]
        public void TestNavVersion30_NavGenVersion7()
        {
            var navMeshFile = GetNavMesh("workshop_example_tilemesh.nav");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(navMeshFile.Version, Is.EqualTo(30));
                Assert.That(navMeshFile.SubVersion, Is.Zero);
                Assert.That(navMeshFile.Areas, Has.Count.EqualTo(414));
                Assert.That(navMeshFile.GenerationParams?.NavGenVersion, Is.EqualTo(7));
                Assert.That(navMeshFile.GenerationParams?.HullParams[2].MaxJumpUpDist, Is.EqualTo(240));
            }
        }

        [Test]
        public void TestNavVersion35()
        {
            var navMeshFile = GetNavMesh("lobby_mapveto.nav");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(navMeshFile.Version, Is.EqualTo(35));
                Assert.That(navMeshFile.SubVersion, Is.EqualTo(1));
                Assert.That(navMeshFile.Areas, Has.Count.EqualTo(4));
                Assert.That(navMeshFile.Ladders, Is.Empty);
                Assert.That(navMeshFile.GenerationParams?.NavGenVersion, Is.EqualTo(12));
                Assert.That(navMeshFile.GenerationParams?.GravityFollowsRotation, Is.False);
                Assert.That(navMeshFile.MovableMeshIds, Is.Empty);
                Assert.That(navMeshFile.TransformedBounds, Is.Empty);
                Assert.That(navMeshFile.Areas.Values.Select(area => area.MovableMeshId), Is.All.EqualTo(NavMeshFile.NoMovableMesh));
                Assert.That(navMeshFile.CustomData, Is.Not.Null);
                Assert.That(navMeshFile.CustomData?.Header?.Format.Name, Is.EqualTo("navmeshcustomdata1"));
            }
        }
    }
}
