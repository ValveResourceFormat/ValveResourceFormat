using System.IO;
using System.Linq;
using NUnit.Framework;
using SharpGLTF.Schema2;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.NavMesh;

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
        public void TestRootMotionIsBakedIntoExport()
        {
            using var resource = new Resource();
            var modelPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "box_creature_ik_model.vmdl_c");
            resource.Read(modelPath);

            var dir = Path.Combine(Path.GetTempPath(), "vrf_rootmotion_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var outPath = Path.Combine(dir, "box_creature.glb");

            try
            {
                var gltf = new GltfModelExporter(new NullFileLoader())
                {
                    ExportMaterials = false,
                    ProgressReporter = new Progress<string>(progress => { }),
                };
                gltf.Export(resource, outPath);

                var root = ModelRoot.Load(outPath);
                var anim = root.LogicalAnimations.Single(a => a.Name == "box_creature_leggy_walk");

                // The root_motion bone has no per-frame bone animation, so any net displacement of its
                // translation channel comes purely from the baked root motion (~47.92 source units forward).
                var rootMotionNode = root.LogicalNodes.Single(n => n.Name == "root_motion");
                var sampler = anim.FindTranslationChannel(rootMotionNode)?.GetTranslationSampler();
                Assert.That(sampler, Is.Not.Null, "root_motion bone should have a translation channel");

                var keys = sampler.GetLinearKeys().ToArray();
                var displacement = keys[^1].Value - keys[0].Value;
                Assert.That(displacement.X, Is.GreaterThan(40f), "root motion should travel the skeleton forward");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
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
