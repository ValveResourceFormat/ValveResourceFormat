using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    [TestFixture]
    public class RubikonPhysicsTest
    {
        [Test]
        public void HullVertexPositionsAreReadFromVertexPositions()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "arch_apartment_ixia_01_top_cap_l_01.vmdl_c"));

            var model = (Model)resource.DataBlock!;
            var phys = model.GetEmbeddedPhys();
            Assert.That(phys, Is.Not.Null);

            var hull = phys.Parts[0].Shape.Hulls[0].Shape;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(hull.GetVertices().Length, Is.EqualTo(8));
                Assert.That(hull.GetVertexPositions().Length, Is.EqualTo(8));
                Assert.That(hull.GetVertexPositions()[0], Is.EqualTo(new Vector3(7.6293945E-06f, 0.00024414062f, 160f)));
            }
        }

        [Test]
        public void HullVertexPositionsAreReadFromVertices()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "juggernaut.vphys_c"));

            var phys = (PhysAggregateData)resource.DataBlock!;
            var hull = phys.Parts[0].Shape.Hulls[0].Shape;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(hull.GetVertices().Length, Is.Zero);
                Assert.That(hull.GetVertexPositions().Length, Is.EqualTo(8));
                Assert.That(hull.GetVertexPositions()[0], Is.EqualTo(new Vector3(-14.162005f, 15.413235f, -2.2308178f)));
            }
        }
    }
}
