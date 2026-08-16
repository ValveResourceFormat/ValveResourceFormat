using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SharpGLTF.Schema2;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    public class GltfExtractTest
    {
        [Test]
        public void TestModel()
        {
            using var resource = new Resource();
            var worldPath = Path.Combine(TestContext.TestDirectory!, "Files", "box_creature_ik_model.vmdl_c");
            resource.Read(worldPath);

            var gltf = new GltfModelExporter(new NullFileLoader())
            {
                ExportMaterials = false,
                ProgressReporter = new Progress<string>(progress => { }),
            };
            gltf.Export(resource, null);
        }

        [Test]
        public async Task TestSkinnedAnimatedExport()
        {
            using var resource = new Resource();
            var modelPath = Path.Combine(TestContext.TestDirectory!, "Files", "box_creature_ik_model.vmdl_c");
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
                await Assert.That(sampler).IsNotNull().Because("root_motion bone should have a translation channel");

                var keys = sampler.GetLinearKeys().ToArray();
                var displacement = keys[^1].Value - keys[0].Value;
                await Assert.That(displacement.Length()).IsGreaterThan(1f).Because("root motion should travel the skeleton forward");

                // Each joint's world transform times its inverse-bind matrix is unit-scaled: the conversion is
                // baked into the geometry, so the armature is identity.
                var skin = root.LogicalSkins[0];
                for (var i = 0; i < skin.JointsCount; i++)
                {
                    var (joint, inverseBind) = skin.GetJoint(i);
                    var bind = joint.WorldMatrix * inverseBind;

                    using (Assert.Multiple())
                    {
                        await Assert.That(new Vector3(bind.M11, bind.M12, bind.M13).Length()).IsEqualTo(1f).Within(0.01f).Because($"joint {joint.Name}");
                        await Assert.That(new Vector3(bind.M21, bind.M22, bind.M23).Length()).IsEqualTo(1f).Within(0.01f).Because($"joint {joint.Name}");
                        await Assert.That(new Vector3(bind.M31, bind.M32, bind.M33).Length()).IsEqualTo(1f).Within(0.01f).Because($"joint {joint.Name}");
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
        public async Task TestSkinnedArmatureHasUnitScale()
        {
            await WithExportedGlb("box_creature_ik_model.vmdl_c", async root =>
            {
                var skin = root.LogicalSkins[0];

                using (Assert.Multiple())
                {
                    for (var i = 0; i < skin.JointsCount; i++)
                    {
                        var (joint, _) = skin.GetJoint(i);
                        await Assert.That(WorldScale(joint)).IsLessThan(0.02f).Because($"joint {joint.Name} should be unit-scaled");
                    }

                    foreach (var node in root.LogicalNodes.Where(n => n.Mesh != null))
                    {
                        await Assert.That(WorldScale(node)).IsLessThan(0.02f).Because($"mesh node {node.Name} should be unit-scaled");
                    }
                }
            });
        }

        // Regression guard for issue #1135: bone translation keyframes must be baked into meters (matching
        // the unit-scaled armature), not left in source inches. In-inches channels under a 0.0254 armature
        // scale are exactly what made bones stretch ~39x once transforms were applied.
        [Test]
        public async Task TestSkinnedAnimationStaysMeterScaled()
        {
            await WithExportedGlb("box_creature_ik_model.vmdl_c", async root =>
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

                using (Assert.Multiple())
                {
                    await Assert.That(maxTranslation).IsGreaterThan(0.5f).Because("expected meter-scale motion (baked root motion ~1.2 m)");
                    await Assert.That(maxTranslation).IsLessThan(3f).Because("translations must be in meters, not source inches (~39x larger)");
                }
            });
        }

        // The non-skinned mesh path bakes the conversion into vertex positions and leaves the node at
        // identity (it used to live on the node transform). Verify the geometry is in meters with no residual
        // node scale or placement.
        [Test]
        public async Task TestStaticMeshConversionBakedIntoGeometry()
        {
            await WithExportedGlb("chen_weapon.vmesh_c", async root =>
            {
                var meshNodes = root.LogicalNodes.Where(n => n.Mesh != null).ToList();
                await Assert.That(meshNodes).IsNotEmpty();

                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);

                using (Assert.Multiple())
                {
                    foreach (var node in meshNodes)
                    {
                        await Assert.That(node.WorldMatrix.IsIdentity).IsTrue().Because($"node {node.Name} should be identity; the conversion is baked into the geometry");

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
                    await Assert.That(extent).IsGreaterThan(1f).Because("expected real geometry");
                    await Assert.That(extent).IsLessThan(20f).Because("geometry must be in meters, not source inches (~39x larger)");
                }
            });
        }

        private static float WorldScale(Node node)
        {
            Matrix4x4.Decompose(node.WorldMatrix, out var scale, out _, out _);
            return (scale - Vector3.One).Length();
        }

        private static async Task WithExportedGlb(string fileName, Func<ModelRoot, Task> assert)
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", fileName));

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

                await assert(ModelRoot.Load(outPath));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public async Task TestExportSucceedsWithoutClothAnchor()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "box_creature_ik_model.vmdl_c"));

            // This fixture has no procedural cloth, so the cloth-follow path is a no-op and export is unaffected.
            var model = (Model)resource.DataBlock!;
            await Assert.That(model.Skeleton.ClothSimulationRoot).IsNull();

            var gltf = new GltfModelExporter(new NullFileLoader())
            {
                ExportMaterials = false,
                ProgressReporter = new Progress<string>(progress => { }),
            };
            await Assert.That(() => gltf.Export(resource, null)).ThrowsNothing();
        }

        [Test]
        public void TestMesh()
        {
            using var resource = new Resource();
            var worldPath = Path.Combine(TestContext.TestDirectory!, "Files", "chen_weapon.vmesh_c");
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
            var worldPath = Path.Combine(TestContext.TestDirectory!, "Files", "world.vwrld_c");
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
            var navPath = Path.Combine(TestContext.TestDirectory!, "Files", "workshop_example_tilemesh.nav");
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
            var physPath = Path.Combine(TestContext.TestDirectory!, "Files", "juggernaut.vphys_c");
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
