using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace Tests
{
    public class ClothFeModelTest
    {
        /// <summary>
        /// An old-era rope cloth: eight control nodes in two columns, four pinned, no surface elements
        /// and no <c>m_SkelParents</c>.
        /// </summary>
        private const string RopeClothFixture = "juggernaut.vphys_c";

        /// <summary>
        /// A modern chain cloth: six joints, each carrying a three-node extrude ring, and no static node
        /// at all.
        /// </summary>
        private const string ChainClothFixture = "sw_donkey_10th_anniversary_kv3_v3_zstd.vmdl_c";

        private static readonly int[] RopeClothParents = [-1, -1, -1, -1, 0, 0, 0, 0];
        private static readonly int[] ChainClothJointNodes = [18, 19, 20, 21, 22, 23];
        private static readonly string[] OneUnknownKey = ["m_flNotAKnownKey"];

        private static FeModel LoadFeModel(string fileName)
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", fileName));

            var phys = resource.DataBlock is Model model
                ? model.GetEmbeddedPhys()
                : (PhysAggregateData)resource.DataBlock!;

            return phys!.FeModel!;
        }

        [Test]
        public async Task RopeClothParsesEveryControlArray()
        {
            var feModel = LoadFeModel(RopeClothFixture);

            using (Assert.Multiple())
            {
                await Assert.That(feModel.HasData).IsTrue();
                await Assert.That(feModel.NodeCount).IsEqualTo(8);
                await Assert.That(feModel.CtrlNames.Length).IsEqualTo(8);
                await Assert.That(feModel.CtrlNames[0]).IsEqualTo("back_fur_r0c0");
                await Assert.That(feModel.CtrlNames[7]).IsEqualTo("back_fur_r3c1");
                await Assert.That(feModel.StaticNodeCount).IsEqualTo(4);
                await Assert.That(feModel.RotationLockedStaticNodeCount).IsEqualTo(4);

                await Assert.That(feModel.NodeInvMasses.Length).IsEqualTo(8);
                await Assert.That(feModel.NodeInvMasses[3]).IsEqualTo(0f);
                await Assert.That(feModel.NodeInvMasses[4]).IsEqualTo(1f);
                await Assert.That(feModel.NodeInvMasses[6]).IsEqualTo(0.666667f).Within(1e-6f);

                await Assert.That(feModel.InitPosePositions.Length).IsEqualTo(8);
                await Assert.That(feModel.InitPoseRotations.Length).IsEqualTo(8);
                await Assert.That(feModel.InitPosePositions[0].Z).IsEqualTo(150.352509f).Within(1e-3f);

                await Assert.That(feModel.Rods.Length).IsEqualTo(6);
                await Assert.That(feModel.Rods[0].NodeA).IsEqualTo(3);
                await Assert.That(feModel.Rods[0].NodeB).IsEqualTo(5);
                await Assert.That(feModel.Rods[0].MinDist).IsEqualTo(0.778344f).Within(1e-6f);
                await Assert.That(feModel.Rods[0].MaxDist).IsEqualTo(15.566884f).Within(1e-5f);
                await Assert.That(feModel.Rods[0].Weight0).IsEqualTo(0f);
                await Assert.That(feModel.Rods[0].RelaxationFactor).IsEqualTo(1f);

                await Assert.That(feModel.NodeBases.Count).IsEqualTo(2);
                await Assert.That(feModel.NodeBases[4]).IsEqualTo(new FeModel.NodeBasis(4, 5, 2, 6));
                await Assert.That(feModel.NodeBases[6]).IsEqualTo(new FeModel.NodeBasis(6, 7, 4, 6));

                await Assert.That(feModel.NodeIntegrators.Length).IsEqualTo(8);
                await Assert.That(feModel.GetIntegrator(0).Gravity).IsEqualTo(700f);
                await Assert.That(feModel.GetIntegrator(6).PointDamping).IsEqualTo(0.071333f).Within(1e-6f);

                await Assert.That(feModel.CtrlOsOffsets.Length).IsEqualTo(4);
                await Assert.That(feModel.CtrlOffsets.Length).IsEqualTo(0);
                await Assert.That(feModel.FollowNodeLinks.Count).IsEqualTo(4);
                await Assert.That(feModel.LegacyStretchForce.Length).IsEqualTo(8);

                await Assert.That(feModel.Quads.Length).IsEqualTo(0);
                await Assert.That(feModel.Tris.Length).IsEqualTo(0);
                await Assert.That(feModel.HasSurfaceElements).IsFalse();
                await Assert.That(feModel.SourceFaces.Length).IsEqualTo(0);
                await Assert.That(feModel.CollisionPlanes.Length).IsEqualTo(0);
                await Assert.That(feModel.BuildCollisionCapsules().Count).IsEqualTo(0);
                await Assert.That(feModel.BuildPlanarizeCapsules().Count).IsEqualTo(0);
                await Assert.That(feModel.VertexMaps.Count).IsEqualTo(0);
                await Assert.That(feModel.Effects.Length).IsEqualTo(0);
                await Assert.That(feModel.JiggleBones.Length).IsEqualTo(0);

                await Assert.That(feModel.LocalForce).IsEqualTo(0.386f).Within(1e-6f);
                await Assert.That(feModel.AddWorldCollisionRadius).IsEqualTo(2f);
                await Assert.That(feModel.DefaultSurfaceStretch).IsEqualTo(0f);
                await Assert.That(feModel.StaticNodeFlags).IsEqualTo(3840u);
                await Assert.That(feModel.DynamicNodeFlags).IsEqualTo(7984u);
            }
        }

        /// <summary>
        /// A compile that ships no <c>m_SkelParents</c> still records which node follows which in
        /// <c>m_FollowNodes</c>, and the hierarchy is rebuilt from it.
        /// </summary>
        [Test]
        public async Task RopeClothBuildsItsParentsFromTheFollowNodes()
        {
            var feModel = LoadFeModel(RopeClothFixture);

            await Assert.That(feModel.HasCompiledSkelParents).IsFalse();
            await Assert.That(feModel.SkelParents).IsEquivalentTo(RopeClothParents);
        }

        /// <summary>
        /// The fixture predates <c>m_nFirstPositionDrivenNode</c>. It has no fit matrix, no reverse offset
        /// and no extrude ring, so nothing is back-solved and the derived boundary is the node count.
        /// </summary>
        [Test]
        public async Task RopeClothDerivesFirstPositionDrivenNodeWhenTheKeyIsAbsent()
        {
            var feModel = LoadFeModel(RopeClothFixture);

            using (Assert.Multiple())
            {
                await Assert.That(feModel.Data.ContainsKey("m_nFirstPositionDrivenNode")).IsFalse();
                await Assert.That(feModel.FirstPositionDrivenNode).IsEqualTo(8);
                await Assert.That(feModel.IsPositionDriven(7)).IsFalse();
            }
        }

        /// <summary>
        /// Static nodes lead the control array and the rotation-locked ones lead them in turn, so both
        /// boundaries are read off the counts rather than off any per-node flag.
        /// </summary>
        [Test]
        public async Task RopeClothStaticBoundaryDrivesRotationAndPinning()
        {
            var feModel = LoadFeModel(RopeClothFixture);

            using (Assert.Multiple())
            {
                for (var node = 0; node < 4; node++)
                {
                    await Assert.That(feModel.IsStatic(node)).IsTrue();
                    await Assert.That(feModel.AllowsRotation(node)).IsFalse();
                }

                for (var node = 4; node < 8; node++)
                {
                    await Assert.That(feModel.IsStatic(node)).IsFalse();
                    await Assert.That(feModel.AllowsRotation(node)).IsTrue();
                }

                await Assert.That(feModel.ForcesWorldCollisionOnAllNodes).IsFalse();
                await Assert.That(feModel.WorldCollisionNodes.Count).IsEqualTo(0);
            }
        }

        /// <summary>
        /// A populated <c>m_CtrlOsOffsets</c> with no ctrl offsets, no surface and no fit matrix is the
        /// marker of cloth authored as an imported fx node table.
        /// </summary>
        [Test]
        public async Task RopeClothIsRecognisedAsImportedCloth()
        {
            var feModel = LoadFeModel(RopeClothFixture);

            await Assert.That(feModel.IsImportedCloth).IsTrue();
        }

        [Test]
        public async Task ChainClothParsesTheModernArrays()
        {
            var feModel = LoadFeModel(ChainClothFixture);

            using (Assert.Multiple())
            {
                await Assert.That(feModel.NodeCount).IsEqualTo(24);
                await Assert.That(feModel.CtrlNames.Length).IsEqualTo(24);
                await Assert.That(feModel.StaticNodeCount).IsEqualTo(0);
                await Assert.That(feModel.FirstPositionDrivenNode).IsEqualTo(24);
                await Assert.That(feModel.Rods.Length).IsEqualTo(129);
                await Assert.That(feModel.NodeBases.Count).IsEqualTo(6);
                await Assert.That(feModel.CtrlOffsets.Length).IsEqualTo(18);
                await Assert.That(feModel.NodeIntegrators.Length).IsEqualTo(24);
                await Assert.That(feModel.InitPosePositions.Length).IsEqualTo(24);

                // The surface survives only as authored elements; the compiler kept no solve elements.
                await Assert.That(feModel.Quads.Length).IsEqualTo(0);
                await Assert.That(feModel.Tris.Length).IsEqualTo(0);
                await Assert.That(feModel.SourceFaces.Length).IsEqualTo(15);
                await Assert.That(feModel.SourceFaces[0].Length).IsEqualTo(4);
                await Assert.That(feModel.SourceSprings.Length).IsEqualTo(0);

                // One vertex set is registered but no dynamic node is assigned to it, so no selection
                // can be rebuilt from the registration.
                await Assert.That(feModel.VertexSetNames.Length).IsEqualTo(1);
                await Assert.That(feModel.DynNodeVertexSet.Length).IsEqualTo(0);
                await Assert.That(feModel.VertexMaps.Count).IsEqualTo(0);

                await Assert.That(feModel.TwistNodes.Count).IsEqualTo(0);
                await Assert.That(feModel.KelagerBends.Count).IsEqualTo(0);
                await Assert.That(feModel.IsImportedCloth).IsFalse();
                await Assert.That(feModel.DefaultGravityScale).IsEqualTo(1f);
                await Assert.That(feModel.LocalForce).IsEqualTo(1f);
            }
        }

        /// <summary>
        /// The joints sit at the tail of the control array and their extrude rings lead it, which is what
        /// <c>m_CtrlOffsets</c> records: three ring nodes per joint, all named after the joint's bone.
        /// </summary>
        [Test]
        public async Task ChainClothRingsHangOffTheSixJointNodes()
        {
            var feModel = LoadFeModel(ChainClothFixture);
            var ringSizes = feModel.CtrlOffsets
                .GroupBy(static offset => offset.CtrlParent)
                .ToDictionary(static group => group.Key, static group => group.Count());

            using (Assert.Multiple())
            {
                await Assert.That(ringSizes.Keys.Order()).IsEquivalentTo(ChainClothJointNodes);

                foreach (var (joint, size) in ringSizes)
                {
                    await Assert.That(size).IsEqualTo(3);
                    await Assert.That(FeModel.IsProxyNodeName(feModel.CtrlNames[joint])).IsFalse();
                }

                await Assert.That(feModel.CtrlNames[18]).IsEqualTo("wizardSpine1_0");
                await Assert.That(feModel.CtrlNames[21]).IsEqualTo("head1");
                await Assert.That(feModel.CtrlNames[0]).IsEqualTo("$ccwizardSpine1_0_0");

                // A rope-parent trail is what a compile without m_SkelParents would leave, and this one
                // has neither.
                await Assert.That(feModel.HasCompiledSkelParents).IsFalse();
                await Assert.That(feModel.SkelParents.Length).IsEqualTo(0);
            }
        }

        /// <summary>
        /// The compiler cubes the authored goal strength into <c>flAnimationForceAttraction</c>, so the
        /// six joints' 0.343 / 0.343 / 0.216 / 0.125 / 0.064 / 0.008 are 0.7 / 0.7 / 0.6 / 0.5 / 0.4 / 0.2.
        /// A joint's own node carries no gravity; only the ring it extruded does.
        /// </summary>
        [Test]
        public async Task ChainClothIntegratorsCarryTheGoalPair()
        {
            var feModel = LoadFeModel(ChainClothFixture);
            var expected = new[] { 0.7f, 0.7f, 0.6f, 0.5f, 0.4f, 0.2f };

            using (Assert.Multiple())
            {
                for (var i = 0; i < expected.Length; i++)
                {
                    var integrator = feModel.GetIntegrator(18 + i);
                    await Assert.That(FeModel.GoalStrengthFromAttraction(integrator.ForceAttraction))
                        .IsEqualTo(expected[i]).Within(1e-4f);
                    await Assert.That(FeModel.GoalDampingFromAttraction(integrator.ForceAttraction,
                        integrator.VertexAttraction)).IsEqualTo(0.01f).Within(1e-3f);
                    await Assert.That(integrator.Gravity).IsEqualTo(0f);
                }

                await Assert.That(feModel.GetIntegrator(0).Gravity).IsEqualTo(360f);
            }
        }

        /// <summary>
        /// A physics aggregate whose <c>m_pFeModel</c> is null carries no cloth at all.
        /// </summary>
        [Test]
        public async Task PhysWithoutClothCarriesNoFeModel()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "generic_grip.vphys_c"));

            var phys = (PhysAggregateData)resource.DataBlock!;

            using (Assert.Multiple())
            {
                await Assert.That(phys.Data.ContainsKey("m_pFeModel")).IsTrue();
                await Assert.That(phys.FeModel).IsNull();
            }
        }

        /// <summary>
        /// Every top-level key of a compiled <c>m_pFeModel</c> is either parsed for authoring or listed
        /// as one the compiler regenerates, so a compiler that adds a key is caught rather than silently
        /// dropped. The shipped check is debug-only; this runs it on both cloth fixtures in any build.
        /// </summary>
        [Test]
        public async Task EveryCompiledKeyOfEveryClothFixtureIsAccountedFor()
        {
            using (Assert.Multiple())
            {
                await Assert.That(UnaccountedKeys(LoadFeModel(RopeClothFixture))).IsEmpty();
                await Assert.That(UnaccountedKeys(LoadFeModel(ChainClothFixture))).IsEmpty();
            }
        }

        /// <summary>
        /// The same accounting over a key neither list knows reports it, which is what makes the check
        /// above capable of failing.
        /// </summary>
        [Test]
        public async Task AnUnknownCompiledKeyIsReportedAsUnaccountedFor()
        {
            var feModel = SyntheticCloth.Parse("""
                {
                    m_CtrlName = [ "a" ]
                    m_nNodeCount = 1
                    m_nStaticNodes = 0
                    m_flNotAKnownKey = 1.0
                }
                """);

            await Assert.That(UnaccountedKeys(feModel)).IsEquivalentTo(OneUnknownKey);
        }

        /// <summary>
        /// The keys of a parsed FeModel that are in neither the parsed nor the derived declaration.
        /// </summary>
        private static List<string> UnaccountedKeys(FeModel feModel)
        {
            var parsed = KeyDeclaration("ParsedKeys");
            var derived = KeyDeclaration("DerivedKeys");

            return [.. feModel.Data.Keys.Where(key => !parsed.Contains(key) && !derived.Contains(key))];
        }

        private static HashSet<string> KeyDeclaration(string name)
        {
            var field = typeof(FeModel).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException($"FeModel.{name} is gone; the key accounting test needs it.");

            return (HashSet<string>)field.GetValue(null)!;
        }
    }
}
