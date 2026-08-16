using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.NavMesh;

namespace Tests
{
    public class NavTests
    {
        private static NavMeshFile GetNavMesh(string navMeshName)
        {
            var navMeshPath = Path.Combine(TestContext.TestDirectory!, "Files", navMeshName);
            var navMeshFile = new NavMeshFile();
            navMeshFile.Read(navMeshPath);
            return navMeshFile;
        }

        [Test]
        public async Task TestNavVersion30_NavGenVersion6()
        {
            var navMeshFile = GetNavMesh("preview_flat.nav");
            using (Assert.Multiple())
            {
                await Assert.That(navMeshFile.Version).IsEqualTo((uint)30);
                await Assert.That(navMeshFile.SubVersion).IsZero();
                await Assert.That(navMeshFile.Areas).Count().IsEqualTo(3);
                await Assert.That(navMeshFile.GenerationParams?.NavGenVersion).IsEqualTo(6);
                await Assert.That(navMeshFile.GenerationParams?.HullParams[2].MaxJumpUpDist).IsEqualTo(240);
            }
        }

        [Test]
        public async Task TestNavVersion30_NavGenVersion7()
        {
            var navMeshFile = GetNavMesh("workshop_example_tilemesh.nav");
            using (Assert.Multiple())
            {
                await Assert.That(navMeshFile.Version).IsEqualTo((uint)30);
                await Assert.That(navMeshFile.SubVersion).IsZero();
                await Assert.That(navMeshFile.Areas).Count().IsEqualTo(414);
                await Assert.That(navMeshFile.GenerationParams?.NavGenVersion).IsEqualTo(7);
                await Assert.That(navMeshFile.GenerationParams?.HullParams[2].MaxJumpUpDist).IsEqualTo(240);
            }
        }

        [Test]
        public async Task TestNavVersion35()
        {
            var navMeshFile = GetNavMesh("lobby_mapveto.nav");
            using (Assert.Multiple())
            {
                await Assert.That(navMeshFile.Version).IsEqualTo((uint)35);
                await Assert.That(navMeshFile.SubVersion).IsEqualTo((uint)1);
                await Assert.That(navMeshFile.Areas).Count().IsEqualTo(4);
                await Assert.That(navMeshFile.Ladders).IsEmpty();
                await Assert.That(navMeshFile.GenerationParams?.NavGenVersion).IsEqualTo(12);
                await Assert.That(navMeshFile.GenerationParams?.GravityFollowsRotation).IsFalse();
                await Assert.That(navMeshFile.MovableMeshIds).IsEmpty();
                await Assert.That(navMeshFile.TransformedBounds).IsEmpty();
                await Assert.That(navMeshFile.Areas.Values.Select(area => area.MovableMeshId)).All(id => id == NavMeshFile.NoMovableMesh);
                await Assert.That(navMeshFile.CustomData).IsNotNull();
                await Assert.That(navMeshFile.CustomData?.Header?.Format.Name).IsEqualTo("navmeshcustomdata1");
            }
        }
    }
}
