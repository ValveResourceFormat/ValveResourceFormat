using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace Tests
{
    public class ClothExtractTest
    {
        /// <summary>
        /// A modern chain cloth: one <c>ClothChain</c> of six joints over a three-node extrude ring each,
        /// plus the chain grid the export writes as authoring convenience.
        /// </summary>
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
        /// The joints come back in pre-order with the parent each one hangs off. This chain branches:
        /// <c>head1</c> hangs off <c>wizardSpine1_1</c>, not off the joint written before it.
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

                // The root carries no joint_parent, so five of the six rows do.
                await Assert.That(Occurrences(vmdl, "joint_parent = \"")).IsEqualTo(5);

                var head = vmdl.IndexOf("joint_name = \"head1\"", StringComparison.Ordinal);
                var parent = vmdl.IndexOf("joint_parent = \"wizardSpine1_1\"", head, StringComparison.Ordinal);
                await Assert.That(parent).IsGreaterThan(head);
            }
        }

        /// <summary>
        /// The per-joint rows carry the values recovered from the compiled integrators and extrude ring:
        /// the goal strengths are the cube roots of the shipped force attractions, and every joint keeps
        /// the three-node ring at radius 5.
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

        /// <summary>
        /// The compiler's own generated control nodes have no authored counterpart, so none of their
        /// names may reach the emitted source.
        /// </summary>
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

        /// <summary>
        /// A chain-phase model has no proxy sheet at all; the only mesh the cloth writes is the chain
        /// grid, which is emitted disabled and never reaches the compiled physics.
        /// </summary>
        [Test]
        public async Task ChainClothEmitsADisabledChainGridAndNoProxySheet()
        {
            using var resource = LoadFixture(ChainClothFixture);
            var extract = new ModelExtract(resource, new NullFileLoader());
            var vmdl = extract.ToValveModel();

            using (Assert.Multiple())
            {
                await Assert.That(extract.ClothProxyMeshesToExtract.Count).IsEqualTo(0);
                await Assert.That(extract.ClothChainGridsToExtract.Count).IsEqualTo(1);
                await Assert.That(extract.ClothChainGridsToExtract[0].Name).IsEqualTo("cloth_grid");
                await Assert.That(extract.ClothChainGridsToExtract[0].FileName).EndsWith("_cloth_grid.dmx");

                await Assert.That(Occurrences(vmdl, "_class = \"ClothProxyMeshFile\"")).IsEqualTo(1);
                await Assert.That(vmdl).Contains("name = \"cloth_grid\"");
                await Assert.That(Occurrences(vmdl, "disabled = true")).IsEqualTo(1);
            }
        }

        /// <summary>
        /// The solver scalars on <c>ClothParams</c> come straight off the compiled FeModel.
        /// </summary>
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
        /// Two independent extractions of one model produce the same source. Only the emitted vmdl text
        /// is compared byte for byte: the proxy DMX writer assigns fresh element GUIDs on every run, so
        /// its output is compared by length instead.
        /// </summary>
        [Test]
        public async Task ExtractionOfTheSameModelIsDeterministic()
        {
            var (firstVmdl, firstGrid) = ExtractWithGrid();
            var (secondVmdl, secondGrid) = ExtractWithGrid();

            using (Assert.Multiple())
            {
                await Assert.That(firstVmdl).IsEqualTo(secondVmdl);
                await Assert.That(firstGrid).IsGreaterThan(0);
                await Assert.That(firstGrid).IsEqualTo(secondGrid);
            }
        }

        private static (string Vmdl, int GridLength) ExtractWithGrid()
        {
            using var resource = LoadFixture(ChainClothFixture);
            using var content = new ModelExtract(resource, new NullFileLoader()).ToContentFile();

            var grid = content.SubFiles.Single(static file =>
                file.FileName.EndsWith("_cloth_grid.dmx", StringComparison.Ordinal));

            return (Encoding.UTF8.GetString(content.Data!), grid.Extract!().Length);
        }

        /// <summary>
        /// The cloth phase mutates the parsed FeModel before the Softbody tree is built, and the parse is
        /// cached on the resource, so a second extraction from one loaded resource has to land on the
        /// same source as the first.
        /// </summary>
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

        /// <summary>
        /// A model with no soft body writes no cloth at all.
        /// </summary>
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
    }
}
