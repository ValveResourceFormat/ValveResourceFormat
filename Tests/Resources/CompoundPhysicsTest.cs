using System.IO;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests.Resources
{
    public class CompoundPhysicsTest
    {
        [Test]
        public async Task BareCompoundChildrenUseExistingShapeParsers()
        {
            using var resource = TestFixtures.Load("juggernaut.vphys_c");
            var hull = ((PhysAggregateData)resource.DataBlock!).Parts[0].Shape.Hulls[0].Shape;
            var compound = Compound(7, 2, "compound");
            var children = compound.GetSubCollection("m_Compound");
            children.GetSubCollection("m_Spheres").Add(Sphere(100));
            children.GetSubCollection("m_Capsules").Add(Capsule());
            children.GetSubCollection("m_Hulls").Add(hull.Data);
            children.GetSubCollection("m_Meshes").Add(Mesh());
            var shape = new Shape(ShapeData(Array(compound)));

            using (Assert.Multiple())
            {
                await Assert.That(shape.Spheres.Length).IsEqualTo(1);
                await Assert.That(shape.Capsules.Length).IsEqualTo(1);
                await Assert.That(shape.Hulls.Length).IsEqualTo(1);
                await Assert.That(shape.Meshes.Length).IsEqualTo(1);
                await Assert.That(shape.Spheres[0].Shape.Center).IsEqualTo(new Vector3(100, 2, 3));
                await Assert.That(shape.Capsules[0].Shape.Center[1]).IsEqualTo(new Vector3(4, 5, 6));
                await Assert.That(shape.Hulls[0].Shape.GetVertexPositions()[0]).IsEqualTo(hull.GetVertexPositions()[0]);
                await Assert.That(shape.Meshes[0].Shape.GetVertices().Length).IsEqualTo(3);
                await Assert.That(shape.Meshes[0].Shape.GetTriangles().Length).IsEqualTo(1);
                await Assert.That(shape.Spheres[0].CollisionAttributeIndex).IsEqualTo(7);
                await Assert.That(shape.Capsules[0].SurfacePropertyIndex).IsEqualTo(2);
                await Assert.That(shape.Hulls[0].UserFriendlyName).IsEqualTo("compound");
                await Assert.That(shape.Meshes[0].HitGroupName).IsEqualTo("group");
            }
        }

        [Test]
        public async Task TopLevelShapesAndMultipleCompoundsAreKeptInOrder()
        {
            var first = Compound(1, 2, "first");
            first.GetSubCollection("m_Compound").GetSubCollection("m_Spheres").Add(Sphere(100));
            var second = Compound(3, 4, "second");
            second.GetSubCollection("m_Compound").GetSubCollection("m_Spheres").Add(Sphere(200));
            var data = ShapeData(Array(first, second));
            var top = KVObject.Collection();
            top.Add("m_nCollisionAttributeIndex", 5);
            top.Add("m_nSurfacePropertyIndex", 6);
            top.Add("m_Sphere", Sphere(300));
            data.GetSubCollection("m_spheres").Add(top);
            var shape = new Shape(data);

            using (Assert.Multiple())
            {
                await Assert.That(shape.Spheres.Length).IsEqualTo(3);
                await Assert.That(shape.Spheres[0].Shape.Center.X).IsEqualTo(300);
                await Assert.That(shape.Spheres[1].Shape.Center.X).IsEqualTo(100);
                await Assert.That(shape.Spheres[2].Shape.Center.X).IsEqualTo(200);
                await Assert.That(shape.Spheres[0].CollisionAttributeIndex).IsEqualTo(5);
                await Assert.That(shape.Spheres[1].CollisionAttributeIndex).IsEqualTo(1);
                await Assert.That(shape.Spheres[2].CollisionAttributeIndex).IsEqualTo(3);
                await Assert.That(shape.Spheres[2].SurfacePropertyIndex).IsEqualTo(4);
            }
        }

        [Test]
        public async Task MissingAndEmptyCompoundArraysRemainCompatible()
        {
            var legacy = new Shape(ShapeData());
            var empty = new Shape(ShapeData(Array()));

            using (Assert.Multiple())
            {
                await Assert.That(legacy.Spheres).IsEmpty();
                await Assert.That(legacy.Capsules).IsEmpty();
                await Assert.That(legacy.Hulls).IsEmpty();
                await Assert.That(legacy.Meshes).IsEmpty();
                await Assert.That(empty.Hulls).IsEmpty();
            }
        }

        [Test]
        public async Task MissingCompoundPayloadDoesNotSilentlyDropGeometry()
        {
            await Assert.That(() => new Shape(ShapeData(Array(KVObject.Collection()))))
                .Throws<InvalidDataException>();
        }

        private static KVObject ShapeData(KVObject? compounds = null)
        {
            var data = KVObject.Collection();
            foreach (var name in new[] { "m_spheres", "m_capsules", "m_hulls", "m_meshes", "m_CollisionAttributeIndices" })
            {
                data.Add(name, KVObject.Array());
            }

            if (compounds != null)
            {
                data.Add("m_compounds", compounds);
            }

            return data;
        }

        private static KVObject Compound(int collisionAttribute, int surfaceProperty, string name)
        {
            var data = KVObject.Collection();
            data.Add("m_nCollisionAttributeIndex", collisionAttribute);
            data.Add("m_nSurfacePropertyIndex", surfaceProperty);
            data.Add("m_UserFriendlyName", name);
            data.Add("m_sHitGroupName", "group");
            var compound = KVObject.Collection();
            foreach (var kind in new[] { "Spheres", "Capsules", "Hulls", "Meshes" })
            {
                compound.Add("m_" + kind, KVObject.Array());
            }

            data.Add("m_Compound", compound);
            return data;
        }

        private static KVObject Array(params KVObject[] values)
        {
            var array = KVObject.Array();
            foreach (var value in values)
            {
                array.Add(value);
            }

            return array;
        }

        private static KVObject Vector(float x, float y, float z)
        {
            return Array(x, y, z);
        }

        private static KVObject Sphere(float x)
        {
            var data = KVObject.Collection();
            data.Add("m_vCenter", Vector(x, 2, 3));
            data.Add("m_flRadius", 4f);
            return data;
        }

        private static KVObject Capsule()
        {
            var data = KVObject.Collection();
            data.Add("m_vCenter", Array(Vector(1, 2, 3), Vector(4, 5, 6)));
            data.Add("m_flRadius", 4f);
            return data;
        }

        private static KVObject Mesh()
        {
            var data = KVObject.Collection();
            data.Add("m_vMin", Vector(0, 0, 0));
            data.Add("m_vMax", Vector(1, 1, 0));
            data.Add("m_vOrthographicAreas", Vector(1, 1, 1));
            data.Add("m_Materials", KVObject.Array());
            data.Add("m_Vertices", Array(Vector(0, 0, 0), Vector(1, 0, 0), Vector(0, 1, 0)));
            var triangle = KVObject.Collection();
            triangle.Add("m_nIndex", Array(0, 1, 2));
            data.Add("m_Triangles", Array(triangle));
            return data;
        }
    }
}
