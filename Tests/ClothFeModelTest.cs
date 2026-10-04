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
                await Assert.That(cloth.Fe.CtrlName.Length).IsGreaterThan(0);
                await Assert.That(cloth.Fe.NodeCount).IsEqualTo(8);
                await Assert.That(cloth.Fe.CtrlName.Length).IsEqualTo(8);
                await Assert.That(cloth.Fe.CtrlName[0]).IsEqualTo("back_fur_r0c0");
                await Assert.That(cloth.Fe.CtrlName[7]).IsEqualTo("back_fur_r3c1");
                await Assert.That(cloth.Fe.StaticNodes).IsEqualTo(4);
                await Assert.That(cloth.Fe.RotLockStaticNodes).IsEqualTo(4);

                await Assert.That(cloth.Fe.NodeInvMasses.Length).IsEqualTo(8);
                await Assert.That(cloth.Fe.NodeInvMasses[3]).IsEqualTo(0f);
                await Assert.That(cloth.Fe.NodeInvMasses[4]).IsEqualTo(1f);
                await Assert.That(cloth.Fe.NodeInvMasses[6]).IsEqualTo(0.666667f).Within(1e-6f);

                await Assert.That(cloth.Index.InitPosePositions.Length).IsEqualTo(8);
                await Assert.That(cloth.Index.InitPoseRotations.Length).IsEqualTo(8);
                await Assert.That(cloth.Index.InitPosePositions[0].Z).IsEqualTo(150.352509f).Within(1e-3f);

                await Assert.That(cloth.Index.Rods.Length).IsEqualTo(6);
                await Assert.That(cloth.Index.Rods[0].NodeA).IsEqualTo(3);
                await Assert.That(cloth.Index.Rods[0].NodeB).IsEqualTo(5);
                await Assert.That(cloth.Index.Rods[0].MinDist).IsEqualTo(0.778344f).Within(1e-6f);
                await Assert.That(cloth.Index.Rods[0].MaxDist).IsEqualTo(15.566884f).Within(1e-5f);
                await Assert.That(cloth.Index.Rods[0].Weight0).IsEqualTo(0f);
                await Assert.That(cloth.Index.Rods[0].RelaxationFactor).IsEqualTo(1f);

                await Assert.That(cloth.Index.NodeBases.Count).IsEqualTo(2);
                await Assert.That(cloth.Index.NodeBases[4]).IsEqualTo(new FeModelIndex.NodeBasis(4, 5, 2, 6));
                await Assert.That(cloth.Index.NodeBases[6]).IsEqualTo(new FeModelIndex.NodeBasis(6, 7, 4, 6));

                await Assert.That(cloth.Fe.NodeIntegrator.Length).IsEqualTo(8);
                await Assert.That(cloth.Index.GetIntegrator(0).Gravity).IsEqualTo(700f);
                await Assert.That(cloth.Index.GetIntegrator(6).PointDamping).IsEqualTo(0.071333f).Within(1e-6f);

                await Assert.That(cloth.Fe.CtrlOsOffsets.Length).IsEqualTo(4);
                await Assert.That(cloth.Fe.CtrlOffsets.Length).IsEqualTo(0);
                await Assert.That(cloth.Index.FollowNodeLinks.Count).IsEqualTo(4);
                await Assert.That(cloth.Fe.LegacyStretchForce.Length).IsEqualTo(8);

                await Assert.That(cloth.Index.Quads.Length).IsEqualTo(0);
                await Assert.That(cloth.Index.Tris.Length).IsEqualTo(0);
                await Assert.That(cloth.Index.HasSurfaceElements).IsFalse();
                await Assert.That(cloth.Index.SourceFaces.Length).IsEqualTo(0);
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
                await Assert.That(cloth.Fe.FirstPositionDrivenNode).IsNull();
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
                    await Assert.That(cloth.Index.IsStatic(node)).IsTrue();
                    await Assert.That(cloth.Index.AllowsRotation(node)).IsFalse();
                }

                for (var node = 4; node < 8; node++)
                {
                    await Assert.That(cloth.Index.IsStatic(node)).IsFalse();
                    await Assert.That(cloth.Index.AllowsRotation(node)).IsTrue();
                }

                await Assert.That(cloth.ForcesWorldCollisionOnAllNodes).IsFalse();
                await Assert.That(cloth.Index.WorldCollisionNodes.Count).IsEqualTo(0);
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
                await Assert.That(cloth.Fe.CtrlName.Length).IsEqualTo(24);
                await Assert.That(cloth.Fe.StaticNodes).IsEqualTo(0);
                await Assert.That(cloth.FirstPositionDrivenNode).IsEqualTo(24);
                await Assert.That(cloth.Index.Rods.Length).IsEqualTo(129);
                await Assert.That(cloth.Index.NodeBases.Count).IsEqualTo(6);
                await Assert.That(cloth.Fe.CtrlOffsets.Length).IsEqualTo(18);
                await Assert.That(cloth.Fe.NodeIntegrator.Length).IsEqualTo(24);
                await Assert.That(cloth.Index.InitPosePositions.Length).IsEqualTo(24);

                await Assert.That(cloth.Index.Quads.Length).IsEqualTo(0);
                await Assert.That(cloth.Index.Tris.Length).IsEqualTo(0);
                await Assert.That(cloth.Index.SourceFaces.Length).IsEqualTo(15);
                await Assert.That(cloth.Index.SourceFaces[0].Length).IsEqualTo(4);
                await Assert.That(cloth.Index.SourceSprings.Length).IsEqualTo(0);

                await Assert.That(cloth.Fe.VertexSetNames.Length).IsEqualTo(1);
                await Assert.That(cloth.Fe.DynNodeVertexSet.Length).IsEqualTo(0);
                await Assert.That(cloth.VertexMaps.Count).IsEqualTo(0);

                await Assert.That(cloth.Index.TwistNodes.Count).IsEqualTo(0);
                await Assert.That(cloth.Index.KelagerBends.Count).IsEqualTo(0);
                await Assert.That(cloth.IsImportedCloth).IsFalse();
                await Assert.That(cloth.Index.DefaultGravityScale).IsEqualTo(1f);
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
                    await Assert.That(FeModelIndex.IsProxyNodeName(cloth.Fe.CtrlName[joint])).IsFalse();
                }

                await Assert.That(cloth.Fe.CtrlName[18]).IsEqualTo("wizardSpine1_0");
                await Assert.That(cloth.Fe.CtrlName[21]).IsEqualTo("head1");
                await Assert.That(cloth.Fe.CtrlName[0]).IsEqualTo("$ccwizardSpine1_0_0");

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
                    var integrator = cloth.Index.GetIntegrator(18 + i);
                    await Assert.That(ClothReconstruction.GoalStrengthFromAttraction(integrator.AnimationForceAttraction))
                        .IsEqualTo(expected[i]).Within(1e-4f);
                    await Assert.That(ClothReconstruction.GoalDampingFromAttraction(integrator.AnimationForceAttraction,
                        integrator.AnimationVertexAttraction)).IsEqualTo(0.01f).Within(1e-3f);
                    await Assert.That(integrator.Gravity).IsEqualTo(0f);
                }

                await Assert.That(cloth.Index.GetIntegrator(0).Gravity).IsEqualTo(360f);
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
            var cloth = SyntheticCloth.Parse("""
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
                """);

            using (Assert.Multiple())
            {
                await Assert.That(cloth.Fe.NodeCount).IsEqualTo(-3);
                await Assert.That(cloth.Index.NodeCount).IsEqualTo(0);
                await Assert.That(cloth.Index.InitPosePositions).IsEquivalentTo([Vector3.Zero, new Vector3(0f, 0f, 5f)], CollectionOrdering.Matching);
                await Assert.That(cloth.Fe.VertexMaps[0].VertexCount).IsEqualTo(-1);
                await Assert.That(cloth.Index.VertexMaps[0].Weights.Length).IsEqualTo(0);
                await Assert.That(cloth.Fe.LockToParent[0].Offset).IsEqualTo(Vector3.Zero);
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

        /// <summary><c>m_LocalForce</c> and <c>m_LocalRotation</c> hold one value per dynamic node, after the static nodes.</summary>
        [Test]
        public async Task LocalForceAndRotationAreIndexedByDynamicNode()
        {
            var index = SyntheticCloth.Model(["a", "b", "c"], staticNodes: 1, body: """
                m_LocalForce = [ 0.25, 0.5 ]
                m_LocalRotation = [ 0.75, 1.0 ]
                """).Index;

            using (Assert.Multiple())
            {
                await Assert.That(index.GetLocalForce(0)).IsEqualTo(0f);
                await Assert.That(index.GetLocalForce(1)).IsEqualTo(0.25f);
                await Assert.That(index.GetLocalForce(2)).IsEqualTo(0.5f);
                await Assert.That(index.GetLocalRotation(2)).IsEqualTo(1f);
                await Assert.That(index.GetLocalRotation(3)).IsEqualTo(0f);
            }
        }

        /// <summary>A vertex map from a layout without <c>nScaleSourceNode</c> has no scale source node.</summary>
        [Test]
        public async Task AVertexMapWithoutAScaleSourceNodeReadsMinusOne()
        {
            var fe = SyntheticCloth.Model(["a", "b"], staticNodes: 1, body: """
                m_VertexMaps = [ { sName = "vm" nNameHash = 5 nVertexBase = 1 nVertexCount = 1 nMapOffset = 0 }, ]
                m_VertexMapValues = [ 255 ]
                """).Fe;

            await Assert.That(fe.VertexMaps[0].ScaleSourceNode).IsEqualTo(-1);
        }

        /// <summary>
        /// Unsigned fields keep their bits whether the value is stored as a large unsigned or a negative integer, and a
        /// jiggle bone's all-ones node index reads as -1 instead of failing the parse.
        /// </summary>
        [Test]
        public async Task UnsignedFieldsKeepTheirBitsWhateverTheStoredSign()
        {
            var fe = SyntheticCloth.Model(["a", "b"], staticNodes: 1, body: """
                m_nDynamicNodeFlags = -1
                m_Effects = [ { sName = "wind" nNameHash = -2 nType = 1 }, ]
                m_VertexMaps = [ { sName = "vm" nNameHash = 3000000000 nVertexBase = 1 nVertexCount = 0 nMapOffset = 0 }, ]
                m_JiggleBones = [ { m_nNode = 4294967295 m_nJiggleParent = 4294967295 m_jiggleBone = { m_nFlags = -1 } }, ]
                m_BoneMergeLinks = [ { m_nParentHash = -3 m_nChildNode = 1 }, ]
                """).Fe;

            using (Assert.Multiple())
            {
                await Assert.That(fe.DynamicNodeFlags).IsEqualTo(uint.MaxValue);
                await Assert.That(fe.Effects[0].NameHash).IsEqualTo(uint.MaxValue - 1);
                await Assert.That(fe.VertexMaps[0].NameHash).IsEqualTo(3000000000u);
                await Assert.That(fe.JiggleBones[0].Node).IsEqualTo(-1);
                await Assert.That(fe.JiggleBones[0].JiggleParent).IsEqualTo(-1);
                await Assert.That(fe.JiggleBones[0].JiggleBone.Flags).IsEqualTo(uint.MaxValue);
                await Assert.That(fe.BoneMergeLinks[0].ParentHash).IsEqualTo(uint.MaxValue - 2);
            }
        }

        /// <summary>
        /// A SIMD <c>nNode</c> block reads as rows of four lanes whether stored as rows or as one row-major array, and a
        /// collision plane keeps whichever of <c>flStrength</c> and <c>flStickiness</c> it carries.
        /// </summary>
        [Test]
        public async Task SimdNodesAndCollisionPlanesKeepTheirLayoutAcrossEras()
        {
            var cloth = SyntheticCloth.Model(["a", "b", "c"], staticNodes: 1, body: """
                m_SimdRodsAnim =
                [
                    { nNode = [ [ 0, 1, 1, 1 ], [ 2, 2, 2, 2 ] ] f4Weight0 = [ 0.25, 0.5, 0.5, 0.5 ] },
                    { nNode = [ 0, 1, 1, 1, 2, 2, 2, 2 ] },
                ]
                m_CollisionPlanes =
                [
                    { nCtrlParent = 0 nChildNode = 1 m_Plane = { m_vNormal = [ 0.0, 0.0, 1.0 ] m_flOffset = 2.0 } flStrength = 0.5 },
                    { nCtrlParent = 0 nChildNode = 2 flStickiness = 0.75 },
                ]
                """);

            using (Assert.Multiple())
            {
                await Assert.That(cloth.Fe.SimdRodsAnim[0].Nodes[1]).IsEquivalentTo([2, 2, 2, 2], CollectionOrdering.Matching);
                await Assert.That(cloth.Fe.SimdRodsAnim[1].Nodes[0]).IsEquivalentTo([0, 1, 1, 1], CollectionOrdering.Matching);
                await Assert.That(cloth.Fe.SimdRodsAnim[1].Weight0.Length).IsEqualTo(0);
                await Assert.That(cloth.Index.AnimRods).IsEquivalentTo(
                    [new FeModelIndex.AnimRod(0, 2, 0.25f), new FeModelIndex.AnimRod(1, 2, 0.5f), new FeModelIndex.AnimRod(0, 2, 0.5f)],
                    CollectionOrdering.Matching);

                await Assert.That(cloth.Fe.CollisionPlanes[0].Plane).IsEqualTo(new FeModel.RnPlane(Vector3.UnitZ, 2f));
                await Assert.That(cloth.Fe.CollisionPlanes[0].Strength).IsEqualTo(0.5f);
                await Assert.That(cloth.Fe.CollisionPlanes[1].Stickiness).IsEqualTo(0.75f);
                await Assert.That(cloth.Fe.CollisionPlanes[1].Strength).IsEqualTo(0f);
            }
        }

        /// <summary>A seven-float <c>m_InitPose</c> entry carries no scale and reads like the eight-float form.</summary>
        [Test]
        public async Task SevenAndEightFloatInitPosesReadAlike()
        {
            var fe = SyntheticCloth.Model(["a", "b"], staticNodes: 1, body: """
                m_InitPose =
                [
                    [ 1.0, 2.0, 3.0, 0.0, 0.0, 0.70710677, 0.70710677 ],
                    [ 1.0, 2.0, 3.0, 0.5, 0.0, 0.0, 0.70710677, 0.70710677 ],
                ]
                """).Fe;

            using (Assert.Multiple())
            {
                await Assert.That(fe.InitPose[0].Position).IsEqualTo(new Vector3(1f, 2f, 3f));
                await Assert.That(fe.InitPose[0].Scale).IsEqualTo(1f);
                await Assert.That(fe.InitPose[1].Scale).IsEqualTo(0.5f);
                await Assert.That(fe.InitPose[1].Position).IsEqualTo(fe.InitPose[0].Position);
                await Assert.That(fe.InitPose[1].Orientation).IsEqualTo(fe.InitPose[0].Orientation);
                await Assert.That(fe.InitPose[0].Orientation.W).IsEqualTo(0.70710677f);
            }
        }
    }
}
