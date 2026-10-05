using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValvePak;
using ValveResourceFormat.NavMesh;

namespace Tests.Formats
{
    public class NavTest
    {
        private static NavMeshFile GetNavMesh(string navMeshName)
        {
            var navMeshPath = TestFixtures.Path(navMeshName);
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
                await Assert.That(navMeshFile.IsAnalyzed).IsFalse();
                await Assert.That(navMeshFile.PlaceNames).IsEmpty();
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
                await Assert.That(navMeshFile.GenerationParams?.HullCount).IsEqualTo(1);
                await Assert.That(navMeshFile.GenerationParams?.HullParams).Count().IsEqualTo(1);
                await Assert.That(navMeshFile.IsAnalyzed).IsTrue();
                await Assert.That(navMeshFile.PlaceNames).IsEmpty();
                await Assert.That(navMeshFile.Areas.Values).All(area => area.CornerGravity == null);
                await Assert.That(navMeshFile.MovableMeshIds).IsEmpty();
                await Assert.That(navMeshFile.TransformedBounds).IsEmpty();
                await Assert.That(navMeshFile.Areas.Values.Select(area => area.MovableMeshId)).All(id => id == NavMeshFile.NoMovableMesh);
                await Assert.That(navMeshFile.CustomData).IsNotNull();
                await Assert.That(navMeshFile.CustomData?.Header?.Format.Name).IsEqualTo("navmeshcustomdata1");
                await Assert.That(navMeshFile.Areas.Values.Sum(area => area.HidingSpots.Length)).IsEqualTo(8);

                var hidingSpot = navMeshFile.Areas[4].HidingSpots[0];
                await Assert.That(hidingSpot.Id).IsEqualTo(6u);
                await Assert.That(hidingSpot.Position).IsEqualTo(new Vector3(1709f, -3090.4998f, 4f));
                await Assert.That(hidingSpot.Flags).IsEqualTo(NavHidingSpotFlags.InCover);
            }
        }

        [Test]
        public async Task TestNavVersion36()
        {
            using var package = new Package();
            package.Read(TestFixtures.Path("point_template_test.vpk"));
            package.ReadEntry(package.FindEntry("maps/point_template_test.nav")!, out var bytes);

            var navMeshFile = new NavMeshFile();
            navMeshFile.Read(new MemoryStream(bytes));

            using (Assert.Multiple())
            {
                await Assert.That(navMeshFile.Version).IsEqualTo((uint)36);
                await Assert.That(navMeshFile.GenerationParams?.NavGenVersion).IsEqualTo(13);
                await Assert.That(navMeshFile.MovableMeshSettings).IsNotNull();
                await Assert.That(navMeshFile.MovableMeshGravityFollowsRotation).IsEmpty();
                await Assert.That(navMeshFile.GameData?.Root).IsEmpty();
                await Assert.That(navMeshFile.ExtraGenerationParams).IsNotNull();
                await Assert.That(navMeshFile.GenerationParams?.HullParams).Count().IsEqualTo(1);
                await Assert.That(navMeshFile.GenerationParams?.HullParams[0].Name).IsEqualTo("player");
                await Assert.That(navMeshFile.GenerationParams?.HullParams[0].FlowMapEnabled).IsFalse();
                await Assert.That(navMeshFile.GenerationParams?.HullParams[0].FlowMapNodeMaxRadius).IsEqualTo(400f);
            }
        }
    }
}
