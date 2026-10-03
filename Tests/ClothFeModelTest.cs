using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace Tests
{
    public class ClothFeModelTest
    {
        /// <summary>
        /// A rope cloth: eight control nodes in two columns, four pinned, no surface elements and no <c>m_SkelParents</c>.
        /// </summary>
        private const string RopeClothFixture = "juggernaut.vphys_c";

        /// <summary>A chain cloth: six joints, each carrying a three-node extrude ring, and no static node.</summary>
        private const string ChainClothFixture = "sw_donkey_10th_anniversary_kv3_v3_zstd.vmdl_c";

        private static readonly int[] RopeClothParents = [-1, -1, -1, -1, 0, 0, 0, 0];
        private static readonly int[] ChainClothJointNodes = [18, 19, 20, 21, 22, 23];

        private static ClothReconstruction LoadFeModel(string fileName)
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", fileName));

            var phys = resource.DataBlock is Model model
                ? model.GetEmbeddedPhys()
                : (PhysAggregateData)resource.DataBlock!;

            return new ClothReconstruction(phys!.FeModel!);
        }

        [Test]
        public async Task RopeClothParsesEveryControlArray()
        {
            var cloth = LoadFeModel(RopeClothFixture);

            using (Assert.Multiple())
            {
                await Assert.That(cloth.Fe.HasData).IsTrue();
                await Assert.That(cloth.Fe.NodeCount).IsEqualTo(8);
                await Assert.That(cloth.Fe.CtrlNames.Length).IsEqualTo(8);
                await Assert.That(cloth.Fe.CtrlNames[0]).IsEqualTo("back_fur_r0c0");
                await Assert.That(cloth.Fe.CtrlNames[7]).IsEqualTo("back_fur_r3c1");
                await Assert.That(cloth.Fe.StaticNodeCount).IsEqualTo(4);
                await Assert.That(cloth.Fe.RotationLockedStaticNodeCount).IsEqualTo(4);

                await Assert.That(cloth.Fe.NodeInvMasses.Length).IsEqualTo(8);
                await Assert.That(cloth.Fe.NodeInvMasses[3]).IsEqualTo(0f);
                await Assert.That(cloth.Fe.NodeInvMasses[4]).IsEqualTo(1f);
                await Assert.That(cloth.Fe.NodeInvMasses[6]).IsEqualTo(0.666667f).Within(1e-6f);

                await Assert.That(cloth.Fe.InitPosePositions.Length).IsEqualTo(8);
                await Assert.That(cloth.Fe.InitPoseRotations.Length).IsEqualTo(8);
                await Assert.That(cloth.Fe.InitPosePositions[0].Z).IsEqualTo(150.352509f).Within(1e-3f);

                await Assert.That(cloth.Fe.Rods.Length).IsEqualTo(6);
                await Assert.That(cloth.Fe.Rods[0].NodeA).IsEqualTo(3);
                await Assert.That(cloth.Fe.Rods[0].NodeB).IsEqualTo(5);
                await Assert.That(cloth.Fe.Rods[0].MinDist).IsEqualTo(0.778344f).Within(1e-6f);
                await Assert.That(cloth.Fe.Rods[0].MaxDist).IsEqualTo(15.566884f).Within(1e-5f);
                await Assert.That(cloth.Fe.Rods[0].Weight0).IsEqualTo(0f);
                await Assert.That(cloth.Fe.Rods[0].RelaxationFactor).IsEqualTo(1f);

                await Assert.That(cloth.Fe.NodeBases.Count).IsEqualTo(2);
                await Assert.That(cloth.Fe.NodeBases[4]).IsEqualTo(new FeModel.NodeBasis(4, 5, 2, 6));
                await Assert.That(cloth.Fe.NodeBases[6]).IsEqualTo(new FeModel.NodeBasis(6, 7, 4, 6));

                await Assert.That(cloth.Fe.NodeIntegrators.Length).IsEqualTo(8);
                await Assert.That(cloth.Fe.GetIntegrator(0).Gravity).IsEqualTo(700f);
                await Assert.That(cloth.Fe.GetIntegrator(6).PointDamping).IsEqualTo(0.071333f).Within(1e-6f);

                await Assert.That(cloth.Fe.CtrlOsOffsets.Length).IsEqualTo(4);
                await Assert.That(cloth.Fe.CtrlOffsets.Length).IsEqualTo(0);
                await Assert.That(cloth.Fe.FollowNodeLinks.Count).IsEqualTo(4);
                await Assert.That(cloth.Fe.LegacyStretchForce.Length).IsEqualTo(8);

                await Assert.That(cloth.Fe.Quads.Length).IsEqualTo(0);
                await Assert.That(cloth.Fe.Tris.Length).IsEqualTo(0);
                await Assert.That(cloth.Fe.HasSurfaceElements).IsFalse();
                await Assert.That(cloth.Fe.SourceFaces.Length).IsEqualTo(0);
                await Assert.That(cloth.Fe.CollisionPlanes.Length).IsEqualTo(0);
                await Assert.That(cloth.BuildCollisionCapsules().Count).IsEqualTo(0);
                await Assert.That(cloth.BuildPlanarizeCapsules().Count).IsEqualTo(0);
                await Assert.That(cloth.VertexMaps.Count).IsEqualTo(0);
                await Assert.That(cloth.Fe.Effects.Length).IsEqualTo(0);
                await Assert.That(cloth.Fe.JiggleBones.Length).IsEqualTo(0);

                await Assert.That(cloth.Fe.LocalForce).IsEqualTo(0.386f).Within(1e-6f);
                await Assert.That(cloth.Fe.AddWorldCollisionRadius).IsEqualTo(2f);
                await Assert.That(cloth.Fe.DefaultSurfaceStretch).IsEqualTo(0f);
                await Assert.That(cloth.Fe.StaticNodeFlags).IsEqualTo(3840u);
                await Assert.That(cloth.Fe.DynamicNodeFlags).IsEqualTo(7984u);
            }
        }

        /// <summary>Without <c>m_SkelParents</c> the node hierarchy is rebuilt from <c>m_FollowNodes</c>.</summary>
        [Test]
        public async Task RopeClothBuildsItsParentsFromTheFollowNodes()
        {
            var cloth = LoadFeModel(RopeClothFixture);

            await Assert.That(cloth.HasCompiledSkelParents).IsFalse();
            await Assert.That(cloth.SkelParents).IsEquivalentTo(RopeClothParents);
        }

        /// <summary>
        /// Without <c>m_nFirstPositionDrivenNode</c>, a model with no fit matrix, reverse offset or extrude ring derives
        /// the node count as the boundary.
        /// </summary>
        [Test]
        public async Task RopeClothDerivesFirstPositionDrivenNodeWhenTheKeyIsAbsent()
        {
            var cloth = LoadFeModel(RopeClothFixture);

            using (Assert.Multiple())
            {
                await Assert.That(cloth.Fe.Data.ContainsKey("m_nFirstPositionDrivenNode")).IsFalse();
                await Assert.That(cloth.FirstPositionDrivenNode).IsEqualTo(8);
                await Assert.That(cloth.IsPositionDriven(7)).IsFalse();
            }
        }

        /// <summary>The static and rotation-locked boundaries are read off the node counts.</summary>
        [Test]
        public async Task RopeClothStaticBoundaryDrivesRotationAndPinning()
        {
            var cloth = LoadFeModel(RopeClothFixture);

            using (Assert.Multiple())
            {
                for (var node = 0; node < 4; node++)
                {
                    await Assert.That(cloth.Fe.IsStatic(node)).IsTrue();
                    await Assert.That(cloth.Fe.AllowsRotation(node)).IsFalse();
                }

                for (var node = 4; node < 8; node++)
                {
                    await Assert.That(cloth.Fe.IsStatic(node)).IsFalse();
                    await Assert.That(cloth.Fe.AllowsRotation(node)).IsTrue();
                }

                await Assert.That(cloth.ForcesWorldCollisionOnAllNodes).IsFalse();
                await Assert.That(cloth.Fe.WorldCollisionNodes.Count).IsEqualTo(0);
            }
        }

        /// <summary>
        /// A populated <c>m_CtrlOsOffsets</c> with no ctrl offsets, no surface and no fit matrix reads as imported cloth.
        /// </summary>
        [Test]
        public async Task RopeClothIsRecognisedAsImportedCloth()
        {
            var cloth = LoadFeModel(RopeClothFixture);

            await Assert.That(cloth.IsImportedCloth).IsTrue();
        }

        [Test]
        public async Task ChainClothParsesTheModernArrays()
        {
            var cloth = LoadFeModel(ChainClothFixture);

            using (Assert.Multiple())
            {
                await Assert.That(cloth.Fe.NodeCount).IsEqualTo(24);
                await Assert.That(cloth.Fe.CtrlNames.Length).IsEqualTo(24);
                await Assert.That(cloth.Fe.StaticNodeCount).IsEqualTo(0);
                await Assert.That(cloth.FirstPositionDrivenNode).IsEqualTo(24);
                await Assert.That(cloth.Fe.Rods.Length).IsEqualTo(129);
                await Assert.That(cloth.Fe.NodeBases.Count).IsEqualTo(6);
                await Assert.That(cloth.Fe.CtrlOffsets.Length).IsEqualTo(18);
                await Assert.That(cloth.Fe.NodeIntegrators.Length).IsEqualTo(24);
                await Assert.That(cloth.Fe.InitPosePositions.Length).IsEqualTo(24);

                await Assert.That(cloth.Fe.Quads.Length).IsEqualTo(0);
                await Assert.That(cloth.Fe.Tris.Length).IsEqualTo(0);
                await Assert.That(cloth.Fe.SourceFaces.Length).IsEqualTo(15);
                await Assert.That(cloth.Fe.SourceFaces[0].Length).IsEqualTo(4);
                await Assert.That(cloth.Fe.SourceSprings.Length).IsEqualTo(0);

                await Assert.That(cloth.Fe.VertexSetNames.Length).IsEqualTo(1);
                await Assert.That(cloth.Fe.DynNodeVertexSet.Length).IsEqualTo(0);
                await Assert.That(cloth.VertexMaps.Count).IsEqualTo(0);

                await Assert.That(cloth.Fe.TwistNodes.Count).IsEqualTo(0);
                await Assert.That(cloth.Fe.KelagerBends.Count).IsEqualTo(0);
                await Assert.That(cloth.IsImportedCloth).IsFalse();
                await Assert.That(cloth.Fe.DefaultGravityScale).IsEqualTo(1f);
                await Assert.That(cloth.Fe.LocalForce).IsEqualTo(1f);
            }
        }

        /// <summary>
        /// <c>m_CtrlOffsets</c> hangs a three-node ring off each of the six joint nodes at the tail of the control array.
        /// </summary>
        [Test]
        public async Task ChainClothRingsHangOffTheSixJointNodes()
        {
            var cloth = LoadFeModel(ChainClothFixture);
            var ringSizes = cloth.Fe.CtrlOffsets
                .GroupBy(static offset => offset.CtrlParent)
                .ToDictionary(static group => group.Key, static group => group.Count());

            using (Assert.Multiple())
            {
                await Assert.That(ringSizes.Keys.Order()).IsEquivalentTo(ChainClothJointNodes);

                foreach (var (joint, size) in ringSizes)
                {
                    await Assert.That(size).IsEqualTo(3);
                    await Assert.That(FeModel.IsProxyNodeName(cloth.Fe.CtrlNames[joint])).IsFalse();
                }

                await Assert.That(cloth.Fe.CtrlNames[18]).IsEqualTo("wizardSpine1_0");
                await Assert.That(cloth.Fe.CtrlNames[21]).IsEqualTo("head1");
                await Assert.That(cloth.Fe.CtrlNames[0]).IsEqualTo("$ccwizardSpine1_0_0");

                await Assert.That(cloth.HasCompiledSkelParents).IsFalse();
                await Assert.That(cloth.SkelParents.Length).IsEqualTo(0);
            }
        }

        /// <summary>
        /// The joints' goal strengths read back from their force attractions, and only the rings carry gravity.
        /// </summary>
        [Test]
        public async Task ChainClothIntegratorsCarryTheGoalPair()
        {
            var cloth = LoadFeModel(ChainClothFixture);
            var expected = new[] { 0.7f, 0.7f, 0.6f, 0.5f, 0.4f, 0.2f };

            using (Assert.Multiple())
            {
                for (var i = 0; i < expected.Length; i++)
                {
                    var integrator = cloth.Fe.GetIntegrator(18 + i);
                    await Assert.That(ClothReconstruction.GoalStrengthFromAttraction(integrator.ForceAttraction))
                        .IsEqualTo(expected[i]).Within(1e-4f);
                    await Assert.That(ClothReconstruction.GoalDampingFromAttraction(integrator.ForceAttraction,
                        integrator.VertexAttraction)).IsEqualTo(0.01f).Within(1e-3f);
                    await Assert.That(integrator.Gravity).IsEqualTo(0f);
                }

                await Assert.That(cloth.Fe.GetIntegrator(0).Gravity).IsEqualTo(360f);
            }
        }

        /// <summary>A physics aggregate whose <c>m_pFeModel</c> is null carries no cloth at all.</summary>
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
        /// A negative node count, a short pose, a negative vertex count and a short offset read as empty or zero values.
        /// </summary>
        [Test]
        public async Task MalformedCountsAndShortVectorsReadAsEmpty()
        {
            var fe = SyntheticCloth.Parse("""
                {
                m_CtrlName = [ "a", "b" ]
                m_nNodeCount = -3
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0 ]
                m_InitPose = [ [ 1.0, 2.0 ], [ 0.0, 0.0, 5.0, 1.0, 0.0, 0.0, 0.0, 1.0 ] ]
                m_VertexMapValues = [ 255 ]
                m_VertexMaps = [ { sName = "m" nNameHash = 1 nVertexBase = 0 nVertexCount = -1 nMapOffset = 0 vCenterOfMass = [ 0.0 ] flVolumetricSolveStrength = 0.0 nScaleSourceNode = -1 }, ]
                m_LockToParent = [ { vOffset = [ 1.0 ] nCtrlParent = 0 nCtrlChild = 1 }, ]
                }
                """).Fe;

            using (Assert.Multiple())
            {
                await Assert.That(fe.NodeCount).IsEqualTo(0);
                await Assert.That(fe.InitPosePositions).IsEquivalentTo([Vector3.Zero, new Vector3(0f, 0f, 5f)], CollectionOrdering.Matching);
                await Assert.That(fe.VertexMaps[0].Weights.Length).IsEqualTo(0);
                await Assert.That(fe.LockToParent[0].Offset).IsEqualTo(Vector3.Zero);
            }
        }

        /// <summary>A twist naming a negative node adopts nothing; the rope still parents its run.</summary>
        [Test]
        public async Task ATwistWithANegativeNodeLeavesTheRopeParents()
        {
            var cloth = SyntheticCloth.Model(["a", "b"], staticNodes: 1, body: """
                m_nRopeCount = 1
                m_Ropes = [ 3, 0, 1 ]
                m_Twists = [ { nNodeOrient = -1 nNodeEnd = 1 flTwistRelax = 0.0 flSwingRelax = 0.0 }, ]
                """);

            await Assert.That(cloth.SkelParents).IsEquivalentTo([-1, 0], CollectionOrdering.Matching);
        }

        /// <summary>A follow node that would close a cycle with a rope run is not adopted.</summary>
        [Test]
        public async Task AFollowNodeThatClosesARopeCycleIsNotAdopted()
        {
            var cloth = SyntheticCloth.Model(["a", "b"], staticNodes: 1, body: """
                m_nRopeCount = 1
                m_Ropes = [ 3, 0, 1 ]
                m_FollowNodes = [ { nParentNode = 1 nChildNode = 0 flWeight = 1.0 }, ]
                """);

            await Assert.That(cloth.SkelParents).IsEquivalentTo([-1, 0], CollectionOrdering.Matching);
        }

        /// <summary>A negative entry of an unsigned array keeps its bits instead of failing the parse.</summary>
        [Test]
        public async Task ANegativeUnsignedEntryKeepsItsBits()
        {
            var fe = SyntheticCloth.Model(["a", "b"], staticNodes: 1, body: "m_VertexSetNames = [ -1, 7 ]").Fe;

            await Assert.That(fe.VertexSetNames).IsEquivalentTo([uint.MaxValue, 7u], CollectionOrdering.Matching);
        }
    }
}
