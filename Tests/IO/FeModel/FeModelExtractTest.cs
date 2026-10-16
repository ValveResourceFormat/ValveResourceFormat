using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Tests.IO
{
    public class FeModelExtractTest : FeModelTestModels
    {
        /// <summary>A chain cloth: one <c>ClothChain</c> of six joints, each over a three-node extrude ring.</summary>
        private const string ChainClothFixture = "sw_donkey_10th_anniversary_kv3_v3_zstd.vmdl_c";

        private static readonly string[] GeneratedNodePrefixes = ["$cc", "$cloth_m", "$cloth_node_", "$ha_", "$cloth_root"];

        private static Resource LoadFixture(string fileName)
        {
            var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", fileName));
            return resource;
        }

        private static string ExtractValveModel(string fileName)
        {
            using var resource = LoadFixture(fileName);
            return new ModelExtract(resource, new NullFileLoader()).ToValveModel();
        }

        private static int Occurrences(string text, string value)
        {
            var count = 0;
            for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0;
                i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }

        [Test]
        public async Task ChainClothEmitsOneClothChainUnderASoftbody()
        {
            var vmdl = ExtractValveModel(ChainClothFixture);

            using (Assert.Multiple())
            {
                await Assert.That(Occurrences(vmdl, "_class = \"Softbody\"")).IsEqualTo(1);
                await Assert.That(Occurrences(vmdl, "_class = \"ClothParams\"")).IsEqualTo(1);
                await Assert.That(Occurrences(vmdl, "_class = \"ClothChain\"")).IsEqualTo(1);
                await Assert.That(vmdl).Contains("name = \"wizardSpine1_0\"");
                await Assert.That(vmdl).Contains("root_bone = \"wizardSpine1_0\"");
            }
        }

        /// <summary>
        /// The six joints come back in pre-order, five of them with a parent, and <c>head1</c> hangs off the joint its
        /// ring is joined to rather than its skeleton parent.
        /// </summary>
        [Test]
        public async Task ChainClothCarriesItsSixJointsInParentOrder()
        {
            var vmdl = ExtractValveModel(ChainClothFixture);
            var jointNames = vmdl.Split('\n')
                .Select(static line => line.Trim())
                .Where(static line => line.StartsWith("joint_name = ", StringComparison.Ordinal))
                .ToList();

            using (Assert.Multiple())
            {
                await Assert.That(jointNames.Count).IsEqualTo(6);
                await Assert.That(jointNames[0]).IsEqualTo("joint_name = \"wizardSpine1_0\"");
                await Assert.That(jointNames[1]).IsEqualTo("joint_name = \"wizardSpine1_1\"");
                await Assert.That(jointNames[2]).IsEqualTo("joint_name = \"wizardSpine1_2\"");
                await Assert.That(jointNames[3]).IsEqualTo("joint_name = \"head1\"");
                await Assert.That(jointNames[4]).IsEqualTo("joint_name = \"wizardHat2_0\"");
                await Assert.That(jointNames[5]).IsEqualTo("joint_name = \"wizardHat2_1\"");

                await Assert.That(jointNames.Distinct().Count()).IsEqualTo(6);

                await Assert.That(Occurrences(vmdl, "joint_parent = \"")).IsEqualTo(5);

                var head = vmdl.IndexOf("joint_name = \"head1\"", StringComparison.Ordinal);
                var parent = vmdl.IndexOf("joint_parent = \"wizardSpine1_2\"", head, StringComparison.Ordinal);
                await Assert.That(parent).IsGreaterThan(head);
            }
        }

        /// <summary>
        /// The joint rows carry the goal strengths recovered from the compiled integrators and every joint's three-node
        /// ring at radius 5.
        /// </summary>
        [Test]
        public async Task ChainClothJointRowsCarryTheRecoveredPerJointValues()
        {
            var vmdl = ExtractValveModel(ChainClothFixture);

            using (Assert.Multiple())
            {
                await Assert.That(Occurrences(vmdl, "goal_strength = 0.7")).IsEqualTo(2);
                await Assert.That(Occurrences(vmdl, "goal_strength = 0.6")).IsEqualTo(1);
                await Assert.That(Occurrences(vmdl, "goal_strength = 0.5")).IsEqualTo(1);
                await Assert.That(Occurrences(vmdl, "goal_strength = 0.4")).IsEqualTo(1);
                await Assert.That(Occurrences(vmdl, "goal_strength = 0.2")).IsEqualTo(1);

                await Assert.That(Occurrences(vmdl, "goal_damping = 0.01")).IsEqualTo(6);
                await Assert.That(Occurrences(vmdl, "gravity_z = 1.0")).IsEqualTo(6);
                await Assert.That(Occurrences(vmdl, "extrude_sides = 3")).IsEqualTo(6);
                await Assert.That(Occurrences(vmdl, "extrude_radius = 5.0")).IsEqualTo(6);
                await Assert.That(Occurrences(vmdl, "simulate = true")).IsEqualTo(6);
            }
        }

        /// <summary>No generated control node name reaches the emitted source.</summary>
        [Test]
        public async Task ChainClothEmitsNoGeneratedNodeNames()
        {
            var vmdl = ExtractValveModel(ChainClothFixture);

            using (Assert.Multiple())
            {
                foreach (var prefix in GeneratedNodePrefixes)
                {
                    await Assert.That(vmdl).DoesNotContain(prefix);
                }
            }
        }

        /// <summary>A model of one chain tube writes no proxy sheet.</summary>
        [Test]
        public async Task ChainClothOfOneTubeEmitsNoProxySheet()
        {
            using var resource = LoadFixture(ChainClothFixture);
            var extract = new ModelExtract(resource, new NullFileLoader());
            var vmdl = extract.ToValveModel();

            using (Assert.Multiple())
            {
                await Assert.That(extract.Cloth.ProxyMeshes.Count).IsEqualTo(0);
                await Assert.That(Occurrences(vmdl, "_class = \"ClothProxyMeshFile\"")).IsEqualTo(0);
            }
        }

        /// <summary>The solver scalars on <c>ClothParams</c> come straight off the compiled FeModel.</summary>
        [Test]
        public async Task ChainClothParamsCarryTheCompiledScalars()
        {
            var vmdl = ExtractValveModel(ChainClothFixture);

            using (Assert.Multiple())
            {
                await Assert.That(vmdl).Contains("local_force = 1.0");
                await Assert.That(vmdl).Contains("add_world_collision_radius = 2.0");
                await Assert.That(vmdl).Contains("default_gravity_scale = 1.0");
                await Assert.That(vmdl).Contains("default_stretch = 0.0");
                await Assert.That(vmdl).Contains("add_stiffness_rods = true");
                await Assert.That(vmdl).Contains("add_bend_only_rods = false");
                await Assert.That(vmdl).Contains("explicit_masses = false");
            }
        }

        /// <summary>
        /// Two independent extractions of one model produce the same vmdl text and sub files of the same total length.
        /// </summary>
        [Test]
        public async Task ExtractionOfTheSameModelIsDeterministic()
        {
            var (firstVmdl, firstSubFiles) = ExtractWithSubFiles();
            var (secondVmdl, secondSubFiles) = ExtractWithSubFiles();

            using (Assert.Multiple())
            {
                await Assert.That(firstVmdl).IsEqualTo(secondVmdl);
                await Assert.That(firstSubFiles).IsGreaterThan(0);
                await Assert.That(firstSubFiles).IsEqualTo(secondSubFiles);
            }
        }

        private static (string Vmdl, long SubFileLength) ExtractWithSubFiles()
        {
            using var resource = LoadFixture(ChainClothFixture);
            using var content = new ModelExtract(resource, new NullFileLoader()).ToContentFile();

            var length = content.SubFiles.Sum(static file => (long)(file.Extract?.Invoke().Length ?? 0));

            return (Encoding.UTF8.GetString(content.Data!), length);
        }

        /// <summary>Repeated extractions from one loaded resource produce the same source.</summary>
        [Test]
        public async Task RepeatedExtractionFromOneLoadedResourceIsStable()
        {
            using var resource = LoadFixture(ChainClothFixture);

            var first = new ModelExtract(resource, new NullFileLoader()).ToValveModel();
            var second = new ModelExtract(resource, new NullFileLoader()).ToValveModel();
            var third = new ModelExtract(resource, new NullFileLoader()).ToValveModel();

            using (Assert.Multiple())
            {
                await Assert.That(first).Contains("_class = \"ClothChain\"");
                await Assert.That(second).IsEqualTo(first);
                await Assert.That(third).IsEqualTo(first);
            }
        }

        /// <summary>A model with no soft body writes no cloth at all.</summary>
        [Test]
        public async Task ModelWithoutClothEmitsNoSoftbody()
        {
            var vmdl = ExtractValveModel("box_creature_ik_model.vmdl_c");

            using (Assert.Multiple())
            {
                await Assert.That(vmdl).DoesNotContain("_class = \"Softbody\"");
                await Assert.That(vmdl).DoesNotContain("_class = \"ClothChain\"");
                await Assert.That(vmdl).DoesNotContain("_class = \"ClothParams\"");
            }
        }

        /// <summary>With cloth extraction off, a proxy-sheet cloth model writes no cloth nodes and no cloth DMX files.</summary>
        [Test]
        public async Task ClothExtractionOffEmitsNoCloth()
        {
            using var resource = LoadFixture("FeModel/sheet_chain_spring.vmdl_c");
            using var enabled = new ModelExtract(resource, new NullFileLoader()).ToContentFile();
            using var disabled = new ModelExtract(resource, new NullFileLoader()) { ReconstructSoftbody = false }.ToContentFile();
            var vmdl = Encoding.UTF8.GetString(disabled.Data!);

            using (Assert.Multiple())
            {
                await Assert.That(enabled.SubFiles.Any(static file => file.FileName.Contains("_cloth_", StringComparison.Ordinal))).IsTrue();
                await Assert.That(disabled.SubFiles.Any(static file => file.FileName.Contains("_cloth_", StringComparison.Ordinal))).IsFalse();
                await Assert.That(vmdl).DoesNotContain("_class = \"Softbody\"");
                await Assert.That(vmdl).DoesNotContain("_class = \"ClothProxyMeshFile\"");
            }
        }

        /// <summary>A physics file extracted on its own declares its cloth without a model skeleton.</summary>
        [Test]
        public async Task PhysicsFileOnItsOwnEmitsItsCloth()
        {
            var reports = new List<string>();
            var phys = new KVPhysAggregateData(FreeNodeSprungToJoint.ToKV());
            using var content = new ModelExtract(phys, "models/cloth.vmdl") { ProgressReporter = new SynchronousProgress(reports) }
                .ToContentFile();
            var vmdl = Encoding.UTF8.GetString(content.Data!);

            using (Assert.Multiple())
            {
                await Assert.That(reports).IsEmpty();
                await Assert.That(Occurrences(vmdl, "_class = \"Softbody\"")).IsEqualTo(1);
                await Assert.That(content.SubFiles.Any(static file => file.FileName.Contains("_cloth_", StringComparison.Ordinal))).IsFalse();
            }
        }

        /// <summary>A soft body whose node counts and parents name nodes it does not have is refused before reconstruction.</summary>
        [Test]
        public async Task ClothWithOutOfRangeNodeCountsIsRefused()
        {
            var phys = new KVPhysAggregateData(new FeModelBuilder
            {
                Names = ["root", "tip"],
                StaticNodes = 5,
                RotLockStaticNodes = 9,
                FirstPositionDrivenNode = -4,
                InvMasses = [0f, 1f],
                Parents = [-1, 7],
                Positions = [Vector3.Zero, new(0f, 0f, -1f)],
                Rods = [FeModelBuilder.RigidRod(0, 1, 1f)],
            }.ToKV());
            var extract = new ModelExtract(phys, "models/layout.vmdl");

            var exception = Assert.Throws<InvalidDataException>(() => extract.ToContentFile());
            await Assert.That(exception.Message).IsEqualTo("The cloth node counts or parents lie outside its nodes.");
        }

        private sealed class KVPhysAggregateData : PhysAggregateData
        {
            [SetsRequiredMembers]
            public KVPhysAggregateData(KVObject feModel)
            {
                var data = KVObject.Collection();
                data["m_parts"] = KVObject.Array();
                data["m_bindPose"] = KVObject.Array();
                data["m_surfacePropertyHashes"] = KVObject.Array();
                data["m_collisionAttributes"] = KVObject.Array();
                data["m_pFeModel"] = feModel;
                Data = data;
                Resource = null!;
            }
        }

        private sealed class SynchronousProgress(List<string> reports) : IProgress<string>
        {
            public void Report(string value) => reports.Add(value);
        }
    }
}
