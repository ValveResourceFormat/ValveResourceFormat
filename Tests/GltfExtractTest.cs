using System.IO;
using System.Linq;
using NUnit.Framework;
using SharpGLTF.Schema2;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.ResourceTypes;

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
        public void TestSkinnedAnimatedExport()
        {
            using var resource = new Resource();
            var modelPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "box_creature_ik_model.vmdl_c");
            resource.Read(modelPath);

            var dir = Path.Combine(Path.GetTempPath(), "vrf_skinned_" + Guid.NewGuid().ToString("N"));
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

                // The root_motion bone has no per-frame bone animation, so any net displacement of its
                // translation channel comes purely from the baked root motion (~47.92 source units forward,
                // ~1.22 m once the source->glTF unit conversion is baked into the export).
                var anim = root.LogicalAnimations.Single(a => a.Name == "box_creature_leggy_walk");
                var rootMotionNode = root.LogicalNodes.Single(n => n.Name == "root_motion");
                var sampler = anim.FindTranslationChannel(rootMotionNode)?.GetTranslationSampler();
                Assert.That(sampler, Is.Not.Null, "root_motion bone should have a translation channel");

                var keys = sampler.GetLinearKeys().ToArray();
                var displacement = keys[^1].Value - keys[0].Value;
                Assert.That(displacement.Length(), Is.GreaterThan(1f), "root motion should travel the skeleton forward");

                // Each joint's world transform times its inverse-bind matrix is unit-scaled: the conversion is
                // baked into the geometry, so the armature is identity.
                var skin = root.LogicalSkins[0];
                for (var i = 0; i < skin.JointsCount; i++)
                {
                    var (joint, inverseBind) = skin.GetJoint(i);
                    var bind = joint.WorldMatrix * inverseBind;

                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(new Vector3(bind.M11, bind.M12, bind.M13).Length(), Is.EqualTo(1f).Within(0.01f), $"joint {joint.Name}");
                        Assert.That(new Vector3(bind.M21, bind.M22, bind.M23).Length(), Is.EqualTo(1f).Within(0.01f), $"joint {joint.Name}");
                        Assert.That(new Vector3(bind.M31, bind.M32, bind.M33).Length(), Is.EqualTo(1f).Within(0.01f), $"joint {joint.Name}");
                    }
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // Regression guard for issue #1135: the source->glTF conversion is baked into geometry and bone
        // transforms instead of living on a node, so the armature carries no scale. If a 0.0254 scale leaked
        // back onto the skeleton or mesh nodes, applying transforms in Blender would blow the skinned mesh up.
        [Test]
        public void TestSkinnedArmatureHasUnitScale()
        {
            WithExportedGlb("box_creature_ik_model.vmdl_c", root =>
            {
                var skin = root.LogicalSkins[0];

                using (Assert.EnterMultipleScope())
                {
                    for (var i = 0; i < skin.JointsCount; i++)
                    {
                        var (joint, _) = skin.GetJoint(i);
                        Assert.That(WorldScale(joint), Is.LessThan(0.02f), $"joint {joint.Name} should be unit-scaled");
                    }

                    foreach (var node in root.LogicalNodes.Where(n => n.Mesh != null))
                    {
                        Assert.That(WorldScale(node), Is.LessThan(0.02f), $"mesh node {node.Name} should be unit-scaled");
                    }
                }
            });
        }

        // Regression guard for issue #1135: bone translation keyframes must be baked into meters (matching
        // the unit-scaled armature), not left in source inches. In-inches channels under a 0.0254 armature
        // scale are exactly what made bones stretch ~39x once transforms were applied.
        [Test]
        public void TestSkinnedAnimationStaysMeterScaled()
        {
            WithExportedGlb("box_creature_ik_model.vmdl_c", root =>
            {
                var anim = root.LogicalAnimations.Single(a => a.Name == "box_creature_leggy_walk");

                var maxTranslation = 0f;
                foreach (var channel in anim.Channels)
                {
                    var sampler = channel.GetTranslationSampler();
                    if (sampler == null)
                    {
                        continue;
                    }

                    foreach (var key in sampler.GetLinearKeys())
                    {
                        maxTranslation = MathF.Max(maxTranslation, MathF.Abs(key.Value.X));
                        maxTranslation = MathF.Max(maxTranslation, MathF.Abs(key.Value.Y));
                        maxTranslation = MathF.Max(maxTranslation, MathF.Abs(key.Value.Z));
                    }
                }

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(maxTranslation, Is.GreaterThan(0.5f), "expected meter-scale motion (baked root motion ~1.2 m)");
                    Assert.That(maxTranslation, Is.LessThan(3f), "translations must be in meters, not source inches (~39x larger)");
                }
            });
        }

        // The non-skinned mesh path bakes the conversion into vertex positions and leaves the node at
        // identity (it used to live on the node transform). Verify the geometry is in meters with no residual
        // node scale or placement, which also guards the aggregate path against applying the conversion twice.
        [Test]
        public void TestStaticMeshConversionBakedIntoGeometry()
        {
            WithExportedGlb("chen_weapon.vmesh_c", root =>
            {
                var meshNodes = root.LogicalNodes.Where(n => n.Mesh != null).ToList();
                Assert.That(meshNodes, Is.Not.Empty);

                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);

                using (Assert.EnterMultipleScope())
                {
                    foreach (var node in meshNodes)
                    {
                        Assert.That(node.WorldMatrix.IsIdentity, Is.True, $"node {node.Name} should be identity; the conversion is baked into the geometry");

                        foreach (var primitive in node.Mesh.Primitives)
                        {
                            foreach (var position in primitive.GetVertexAccessor("POSITION").AsVector3Array())
                            {
                                min = Vector3.Min(min, position);
                                max = Vector3.Max(max, position);
                            }
                        }
                    }

                    var extent = (max - min).Length();
                    Assert.That(extent, Is.GreaterThan(1f), "expected real geometry");
                    Assert.That(extent, Is.LessThan(20f), "geometry must be in meters, not source inches (~39x larger)");
                }
            });
        }

        private static float WorldScale(Node node)
        {
            Matrix4x4.Decompose(node.WorldMatrix, out var scale, out _, out _);
            return (scale - Vector3.One).Length();
        }

        private static void WithExportedGlb(string fileName, Action<ModelRoot> assert)
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", fileName));

            var dir = Path.Combine(Path.GetTempPath(), "vrf_gltf_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                var outPath = Path.Combine(dir, "export.glb");
                new GltfModelExporter(new NullFileLoader())
                {
                    ExportMaterials = false,
                    ProgressReporter = new Progress<string>(_ => { }),
                }.Export(resource, outPath);

                assert(ModelRoot.Load(outPath));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void TestExportSucceedsWithoutClothAnchor()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "box_creature_ik_model.vmdl_c"));

            // This fixture has no procedural cloth, so the cloth-follow path is a no-op and export is unaffected.
            var model = (Model)resource.DataBlock!;
            Assert.That(model.Skeleton.ClothSimulationRoot, Is.Null);

            var gltf = new GltfModelExporter(new NullFileLoader())
            {
                ExportMaterials = false,
                ProgressReporter = new Progress<string>(progress => { }),
            };
            Assert.DoesNotThrow(() => gltf.Export(resource, null));
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
