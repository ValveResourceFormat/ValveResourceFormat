using System.IO;
using System.Linq;
using NUnit.Framework;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    [TestFixture]
    public class GltfExtractTest
    {
        [Test]
        public void TestModel()
        {
            using var resource = new Resource();
            var worldPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "box_creature_ik_model.vmdl_c");
            resource.Read(worldPath);

            var gltf = new GltfModelExporter(new NullFileLoader())
            {
                ExportMaterials = false,
                ProgressReporter = new Progress<string>(progress => { }),
            };
            gltf.Export(resource, null);
        }

        [Test]
        public void TestMesh()
        {
            using var resource = new Resource();
            var worldPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "chen_weapon.vmesh_c");
            resource.Read(worldPath);

            var gltf = new GltfModelExporter(new NullFileLoader())
            {
                ExportMaterials = false,
                ProgressReporter = new Progress<string>(progress => { }),
            };
            gltf.Export(resource, null);
        }

        [Test]
        public void TestWorld()
        {
            using var resource = new Resource();
            var worldPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "world.vwrld_c");
            resource.Read(worldPath);

            var gltf = new GltfModelExporter(new NullFileLoader())
            {
                ExportMaterials = false,
                ProgressReporter = new Progress<string>(progress => { }),
            };
            gltf.Export(resource, null);
        }

        [Test]
        public void TestNavMesh()
        {
            var navPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "workshop_example_tilemesh.nav");
            var navMeshFile = new NavMeshFile();
            navMeshFile.Read(navPath);

            var gltf = new GltfModelExporter(new NullFileLoader())
            {
                ExportMaterials = false,
                ProgressReporter = new Progress<string>(progress => { }),
            };
            gltf.Export(navMeshFile, navPath, (string?)null);
        }

        [Test]
        public void TestPhysicsCollisionMesh()
        {
            using var resource = new Resource();
            var physPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "juggernaut.vphys_c");
            resource.Read(physPath);

            var gltf = new GltfModelExporter(new NullFileLoader())
            {
                ExportMaterials = true,
                ProgressReporter = new Progress<string>(progress => { }),
            };
            gltf.Export(resource, null);
        }
    }
}
