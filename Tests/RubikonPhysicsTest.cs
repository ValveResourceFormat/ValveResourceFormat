using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    public class RubikonPhysicsTest
    {
        [Test]
        public async Task HullVertexPositionsAreReadFromVertexPositions()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "arch_apartment_ixia_01_top_cap_l_01.vmdl_c"));

            var model = (Model)resource.DataBlock!;
            var phys = model.GetEmbeddedPhys();
            await Assert.That(phys).IsNotNull();

            var hull = phys.Parts[0].Shape.Hulls[0].Shape;

            using (Assert.Multiple())
            {
                await Assert.That(hull.GetVertices().Length).IsEqualTo(8);
                await Assert.That(hull.GetVertexPositions().Length).IsEqualTo(8);
                await Assert.That(hull.GetVertexPositions()[0]).IsEqualTo(new Vector3(7.6293945E-06f, 0.00024414062f, 160f));
            }
        }

        [Test]
        public async Task HullVertexPositionsAreReadFromVertices()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "juggernaut.vphys_c"));

            var phys = (PhysAggregateData)resource.DataBlock!;
            var hull = phys.Parts[0].Shape.Hulls[0].Shape;

            using (Assert.Multiple())
            {
                await Assert.That(hull.GetVertices().Length).IsZero();
                await Assert.That(hull.GetVertexPositions().Length).IsEqualTo(8);
                await Assert.That(hull.GetVertexPositions()[0]).IsEqualTo(new Vector3(-14.162005f, 15.413235f, -2.2308178f));
            }
        }
    }
}
