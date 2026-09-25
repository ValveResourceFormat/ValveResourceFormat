using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    /// <summary>Declaring springs, clusters and the rods a chain or sheet does not regenerate.</summary>
    public class ClothExtractRodTest : ClothTestFixtures
    {
        /// <summary>
        /// A banded rod beside a chain's rigid span is re-declared as a two-member cluster at half its band per member;
        /// two banded copies stay springs.
        /// </summary>
        [Test]
        public async Task AClusterTieBesideAChainSpanIsItsTwoMemberCluster()
        {
            var tiedModel = ClusterTieChain(1);
            var tied = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(tied, tiedModel, tiedModel.BuildBoneChains());
            var doubledModel = ClusterTieChain(2);
            var doubled = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(doubled, doubledModel, doubledModel.BuildBoneChains());

            await Assert.That(tied.Count).IsEqualTo(1);
            var cluster = tied.ElementAt(0).Value;
            var joints = cluster.GetSubCollection("chain").GetArray("joints")!.ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(cluster.GetStringProperty("_class")).IsEqualTo("ClothSelfCollisionCluster");
                await Assert.That(joints.Select(static joint => joint.GetStringProperty("joint_name")).ToArray())
                    .IsEquivalentTo(ClusterTieMembers, CollectionOrdering.Matching);
                await Assert.That(joints[0].GetFloatProperty("collision_radius")).IsEqualTo(6f);
                await Assert.That(joints[0].GetFloatProperty("stray_radius")).IsEqualTo(24f);
                await Assert.That(doubled.Select(static child => child.Value.GetStringProperty("_class")).ToArray())
                    .IsEquivalentTo(ClusterTieControlClasses, CollectionOrdering.Matching);
            }
        }

        private static readonly string[] ClusterTieMembers = ["j1", "j2"];

        private static readonly string[] ClusterTieControlClasses = ["ClothSpring", "ClothSpring"];

        private static FeModel ClusterTieChain(int ties) => SyntheticCloth.Model(
            ["root", "j1", "j2"], staticNodes: 1, parents: [-1, 0, 1],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
            body: $$"""
                m_Rods =
                [
                    {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                    {{string.Concat(Enumerable.Repeat(SyntheticCloth.BandedRod(1, 2, 12f, 48f, 1f), ties))}}
                ]
                """);

        /// <summary>
        /// A rod from a free node to a chain joint is re-declared as a <c>ClothSpring</c> where a two-corner source
        /// element records it and as a two-member cluster where none does; without chain joints nothing is declared.
        /// </summary>
        [Test]
        public async Task ASpringFromAFreeNodeToAChainJointIsDeclared()
        {
            static FeModel Model(string sourceElems) => SyntheticCloth.Model(
                ["coattail_0_L", "coattail_0_R", "coattail_1_R"], staticNodes: 2, parents: [-1, -1, 1],
                    invMasses: "0.0, 0.0, 0.006141",
                poses: [new(4f, 0f, 60f), new(-4f, 0f, 60f), new(-4f, 0f, 51.5f)],
                body: $$"""
                    m_SourceElems = [ {{sourceElems}} ]
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 2, 11.854138f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 8.5f, 1f)}}
                    ]
                    """);

            static string[] Ties(FeModel feModel, HashSet<int>? chainJoints)
            {
                KVObject clothChildren = KVObject.Array();
                KVObject softbodyChildren = KVObject.Array();
                ClothExtract.AddFreeClothNodesAndSprings(clothChildren, softbodyChildren, feModel, [1, 2], static _ => true,
                    [], chainJoints: chainJoints);
                return [.. softbodyChildren.Select(static child => child.Value).Select(static child =>
                    child.GetStringProperty("_class") == "ClothSpring"
                        ? "spring:" + child.GetStringProperty("cloth_node_0") + "|" + child.GetStringProperty("cloth_node_1")
                        : "cluster:" + string.Join("|", child.GetSubCollection("chain").GetArray("joints")
                            .Select(static joint => joint.GetStringProperty("joint_name"))))];
            }

            FeModel recorded = Model("0, 1, 0, 0, 0, 2");
            FeModel unrecorded = Model("0, 0, 0, 0");

            using (Assert.Multiple())
            {
                await Assert.That(Ties(recorded, [1, 2])).IsEquivalentTo(["spring:coattail_0_L|coattail_1_R"]);
                await Assert.That(Ties(unrecorded, [1, 2])).IsEquivalentTo(["cluster:coattail_0_L|coattail_1_R"]);
                await Assert.That(Ties(unrecorded, null)).IsEmpty();
            }
        }

        /// <summary>
        /// A free node's spring to a chain joint names the joint once; without chain joints the rod is not declared.
        /// </summary>
        [Test]
        public async Task AFreeNodesSpringToAChainJointNamesTheJoint()
        {
            var feModel = FreeNodeSprungToJoint;
            var joints = feModel.BuildBoneChains().SelectMany(static chain => chain.Joints).Select(static joint => joint.Node).ToHashSet();

            var withJoints = KVObject.Array();
            ClothExtract.AddFreeClothNodesAndSprings(KVObject.Array(), withJoints, feModel, joints, static _ => true, [], chainJoints: joints);
            var withoutJoints = KVObject.Array();
            ClothExtract.AddFreeClothNodesAndSprings(KVObject.Array(), withoutJoints, feModel, joints, static _ => true, []);

            static (string, string)[] Springs(KVObject children) => children
                .Select(static child => child.Value)
                .Where(static node => node.GetStringProperty("_class") == "ClothSpring")
                .Select(static node => (node.GetStringProperty("cloth_node_0"), node.GetStringProperty("cloth_node_1")))
                .ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(Springs(withJoints).Count(static s => s == ("node_b", "coattail_1_L"))).IsEqualTo(1);
                await Assert.That(Springs(withJoints).Count(static s => s.Item1 == "coattail_1_L" || s.Item2 == "coattail_1_L")).IsEqualTo(1);
                await Assert.That(Springs(withoutJoints).Any(static s => s.Item1 == "coattail_1_L" || s.Item2 == "coattail_1_L")).IsFalse();
            }
        }

        private static FeModel FreeNodeSprungToJoint => SyntheticCloth.Load("cloth_chain_free_node_spring.kv3");

        /// <summary>
        /// A banded rod between two joints' ring nodes is declared as a two-member <c>ClothSelfCollisionCluster</c>
        /// whose name has no <c>$</c>; the band closed onto the rest length or between one joint's rings declares
        /// nothing.
        /// </summary>
        [Test]
        public async Task ARingRingClusterTieIsItsTwoMemberClusterUnderANameWithoutADollar()
        {
            var tiedModel = SyntheticCloth.Parse(RingClusterTieText);
            var tied = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(tied, tiedModel, tiedModel.BuildBoneChains());

            var restBandModel = SyntheticCloth.Parse(RingClusterTieText.Replace(
                "{ nNode = [ 2, 4 ] flMaxDist = 48.0 flMinDist = 12.0",
                "{ nNode = [ 2, 4 ] flMaxDist = 8.503419 flMinDist = 8.4",
                StringComparison.Ordinal));
            var restBand = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(restBand, restBandModel, restBandModel.BuildBoneChains());

            var sameJointModel = SyntheticCloth.Parse(RingClusterTieText.Replace(
                "{ nNode = [ 2, 4 ] flMaxDist = 48.0 flMinDist = 12.0",
                "{ nNode = [ 2, 3 ] flMaxDist = 48.0 flMinDist = 12.0",
                StringComparison.Ordinal));
            var sameJoint = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(sameJoint, sameJointModel, sameJointModel.BuildBoneChains());

            var emitted = tied.Select(static child => child.Value).ToArray();

            static IEnumerable<KVObject> Members(IEnumerable<KVObject> nodes)
                => nodes.SelectMany(static node => node.GetSubCollection("chain").GetArray("joints")!);

            using (Assert.Multiple())
            {
                await Assert.That(emitted.Select(static node => node.GetStringProperty("_class")).ToArray())
                    .IsEquivalentTo(RingClusterTieClasses, CollectionOrdering.Matching);
                await Assert.That(emitted.Select(static node => node.GetStringProperty("name")).ToArray())
                    .IsEquivalentTo(RingClusterTieNames, CollectionOrdering.Matching);
                await Assert.That(Members(emitted).Select(static joint => joint.GetStringProperty("joint_name")).ToArray())
                    .IsEquivalentTo(RingClusterTieMembers, CollectionOrdering.Matching);
                await Assert.That(Members(emitted).Select(static joint => joint.GetFloatProperty("collision_radius")).ToArray())
                    .IsEquivalentTo(RingClusterTieRadii, CollectionOrdering.Matching);
                await Assert.That(Members(emitted).Select(static joint => joint.GetFloatProperty("stray_radius")).ToArray())
                    .IsEquivalentTo(RingClusterTieStrays, CollectionOrdering.Matching);

                await Assert.That(restBand.Count).IsEqualTo(0);
                await Assert.That(sameJoint.Count).IsEqualTo(0);
            }
        }

        private static readonly string[] RingClusterTieClasses = ["ClothSelfCollisionCluster"];

        private static readonly string[] RingClusterTieNames = ["cluster_cccoattail_1_L_0_cccoattail_2_L_0"];

        private static readonly string[] RingClusterTieMembers = ["$cccoattail_1_L_0", "$cccoattail_2_L_0"];

        private static readonly float[] RingClusterTieRadii = [6f, 6f];

        private static readonly float[] RingClusterTieStrays = [24f, 24f];

        private static string RingClusterTieText => SyntheticCloth.Fixture("cloth_chain_ring_cluster_tie.kv3");

        /// <summary>
        /// A <c>ClothSelfCollisionCluster</c>'s member table states its members, version 0 and its own <c>attrs</c>
        /// defaults.
        /// </summary>
        [Test]
        public async Task AClusterMemberTableStatesItsOwnDefaults()
        {
            var cluster = ClothExtract.MakeClothSelfCollisionCluster(
                "cluster_0", ["j1", "j2"], radius: 6f, strayRadius: 24f);
            var chain = cluster.GetSubCollection("chain");

            static string[] Keys(KVObject table) => table is null ? [] : [.. table.Select(a => a.Key)];
            static float[] Numbers(KVObject table) => table is null
                ? []
                : [table.GetSubCollection("stiffness").GetFloatProperty("default"),
                   table.GetSubCollection("stray_radius").GetFloatProperty("default"),
                   table.GetSubCollection("collision_radius").GetFloatProperty("default")];

            string[] expectedMembers = ["j1", "j2"];
            string[] expectedKeys = ["joint_name", "stiffness", "stray_radius", "collision_radius"];
            float[] expectedNumbers = [1f, 2f, 2f];

            using (Assert.Multiple())
            {
                await Assert.That(chain.GetSubCollection("joints")
                    .Select(j => ((KVObject)j.Value!).GetStringProperty("joint_name")))
                    .IsEquivalentTo(expectedMembers);
                await Assert.That(chain.GetInt32Property("version")).IsEqualTo(0);
                await Assert.That(Keys(chain.GetSubCollection("attrs"))).IsEquivalentTo(expectedKeys);
                await Assert.That(Numbers(chain.GetSubCollection("attrs"))).IsEquivalentTo(expectedNumbers);
            }
        }

        /// <summary>
        /// A cross-chain surplus rod off its rest distance with no two-corner source element is a two-member cluster,
        /// members reversed; a recorded pair or a rod at rest distance stays a spring.
        /// </summary>
        [Test]
        public async Task AnUnrecordedOffRestSurplusRodIsAClusterNotASpring()
        {
            var law = CrossChainTieModel(12f, 48f, string.Empty);
            var recorded = CrossChainTieModel(12f, 48f, "0, 1, 0, 0, 2, 3");
            var rest = CrossChainTieModel(20f, 20f, string.Empty);

            using (Assert.Multiple())
            {
                await Assert.That(SurplusClasses(recorded)).DoesNotContain("ClothSelfCollisionCluster");

                await Assert.That(SurplusClasses(rest)).IsEquivalentTo(SpringOnly, CollectionOrdering.Matching);

                await Assert.That(SurplusClasses(law)).IsEquivalentTo(ClusterOnly, CollectionOrdering.Matching);
                await Assert.That(SurplusClusterMembers(law)).IsEquivalentTo(ReversedTie, CollectionOrdering.Matching);
            }
        }

        private static readonly string[] SpringOnly = ["ClothSpring"];

        private static readonly string[] ClusterOnly = ["ClothSelfCollisionCluster"];

        private static readonly string[] ReversedTie = ["b1", "a1"];

        private static string[] SurplusClusterMembers(FeModel feModel)
        {
            var children = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(children, feModel, feModel.BuildBoneChains());
            return children.Where(static c => c.Value.GetStringProperty("_class") == "ClothSelfCollisionCluster")
                .SelectMany(static c => c.Value.GetSubCollection("chain").GetArray("joints"))
                .Select(static j => j.GetStringProperty("joint_name")).ToArray();
        }

        private static string[] SurplusClasses(FeModel feModel)
        {
            var children = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(children, feModel, feModel.BuildBoneChains());
            return children.Select(static c => c.Value.GetStringProperty("_class")).ToArray();
        }

        /// <summary>
        /// Two one-joint chains 20 apart with a banded rod between their joints and the given source elements.
        /// </summary>
        private static FeModel CrossChainTieModel(float min, float max, string sourceElems) => SyntheticCloth.Model(
            ["rootA", "rootB", "a1", "b1"], staticNodes: 2, parents: [-1, -1, 0, 1],
            poses: [new(0f, 0f, 0f), new(20f, 0f, 0f), new(0f, 0f, -10f), new(20f, 0f, -10f)],
            body: $$"""
                m_Rods =
                [
                    {{SyntheticCloth.RigidRod(0, 2, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(1, 3, 10f, 1f)}}
                    {{SyntheticCloth.BandedRod(2, 3, min, max, 1f)}}
                ]
                m_SourceElems = [ {{sourceElems}} ]
                """);

        /// <summary>
        /// Fold-weighted rods on a face-kept sheet read as <c>add_stiffness_rods</c> and are derived; even-split rods
        /// leave the switch off.
        /// </summary>
        [Test]
        public async Task AFaceKeptSheetsFoldsAreTheSwitchsAndNoSprings()
        {
            var folded = FoldedSheetModel(0.666667f);
            var declared = FoldedSheetModel(0.5f);

            static (bool Switch, HashSet<(int, int)> Derived) Read(FeModel feModel)
            {
                var proxies = feModel.BuildProxyMeshes().Select(static (proxy, i) => new ClothExtract.ClothProxyFile($"p{i}.dmx", $"p{i}", proxy)).ToList();
                var rods = ClothExtract.ClothRodsFromSurface(feModel, proxies);
                return (rods.GeneratesBendRods, rods.Derived);
            }

            var (declaredSwitch, declaredDerived) = Read(declared);
            var (foldedSwitch, foldedDerived) = Read(folded);

            using (Assert.Multiple())
            {
                await Assert.That(declaredSwitch).IsFalse();
                await Assert.That(declaredDerived.Contains((2, 6))).IsFalse();

                await Assert.That(foldedSwitch).IsTrue();
                await Assert.That(foldedDerived.Contains((2, 6)) && foldedDerived.Contains((3, 7))).IsTrue();
            }
        }

        /// <summary>
        /// A face-kept sheet's folds are predicted in the corner order the compiler meets its faces in, including the
        /// crosswise pairs a mid-cycle static order folds.
        /// </summary>
        [Test]
        public async Task AFaceKeptSheetFoldsInTheCornerOrderTheCompilerMeetsItsFacesIn()
        {
            var sheet = FaceKeptSheetCorner;
            var proxies = sheet.BuildProxyMeshes().Select(static (proxy, i) => new ClothExtract.ClothProxyFile($"p{i}.dmx", $"p{i}", proxy)).ToList();
            var rods = ClothExtract.ClothRodsFromSurface(sheet, proxies);
            var (derived, bend) = (rods.Derived, rods.GeneratesBendRods);

            using (Assert.Multiple())
            {
                await Assert.That(bend).IsTrue();
                await Assert.That(derived.Contains((2, 5)) && derived.Contains((0, 3))).IsTrue();

                await Assert.That(derived.Contains((1, 5)) && derived.Contains((2, 4))).IsTrue();
            }
        }

        private static FeModel FaceKeptSheetCorner => SyntheticCloth.Load("cloth_sheet_face_kept_corner.kv3");

        /// <summary>
        /// A clique of equal bands over two chains' ring nodes is declared as one cluster of all its members at half
        /// the band; the same clique within one joint declares none.
        /// </summary>
        [Test]
        public async Task AClusterCliqueAcrossTwoChainsRingsIsOneCluster()
        {
            var model = ClusterCliqueRings;
            var across = KVObject.Array();
            var covered = ClothExtract.AddRingClusterCliques(across, model, new Dictionary<int, int> { [2] = 0, [3] = 0, [4] = 1, [5] = 1 });
            var single = KVObject.Array();
            var none = ClothExtract.AddRingClusterCliques(single, model, new Dictionary<int, int> { [2] = 0, [3] = 0, [4] = 0, [5] = 0 });

            using (Assert.Multiple())
            {
                await Assert.That(single.Count).IsEqualTo(0);
                await Assert.That(none.Count).IsEqualTo(0);

                await Assert.That(covered.Count).IsEqualTo(6);
                await Assert.That(across.Select(static child => string.Join("|", child.Value.GetSubCollection("chain").GetArray("joints")
                    .Select(static joint => $"{joint.GetStringProperty("joint_name")}:{joint.GetFloatProperty("collision_radius")}:{joint.GetFloatProperty("stray_radius")}"))).ToArray())
                    .IsEquivalentTo(ClusterCliqueMembers, CollectionOrdering.Matching);
            }
        }

        private static readonly string[] ClusterCliqueMembers = ["$cca_0:4:8|$cca_1:4:8|$ccb_0:4:8|$ccb_1:4:8"];

        /// <summary>Joints a and b with two ring nodes each and the six 8 / 16 bands of a four-member cluster.</summary>
        private static FeModel ClusterCliqueRings => SyntheticCloth.Model(
            ["a", "b", "$cca_0", "$cca_1", "$ccb_0", "$ccb_1"], staticNodes: 2, parents: [-1, -1, 0, 0, 1, 1],
                invMasses: "0.0, 0.0, 0.01, 0.01, 0.01, 0.01",
            poses: [new(0f, 0f, 0f), new(10f, 0f, 0f), new(0f, 3f, -5f), new(0f, -3f, -5f), new(10f, 3f, -5f), new(10f, -3f, -5f)],
            body: $$"""
                m_Rods =
                [
                    {{SyntheticCloth.BandedRod(2, 3, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(2, 4, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(2, 5, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(3, 4, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(3, 5, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(4, 5, 8f, 16f, 1f)}}
                ]
                """);

        /// <summary>
        /// A second rigid copy of a span with no two-corner source element is an unrecorded span copy; a recorded
        /// spring, a banded copy, a lone rod, or a model without <c>m_SkelParents</c> is not.
        /// </summary>
        [Test]
        public async Task ASecondRigidSpanCopyWithNoSourceElementIsAClusterRod()
        {
            static FeModel Pair(string rods, string sourceElems, string skelParents = "m_SkelParents = [ -1, 0 ]") => SyntheticCloth.Model(
                ["a", "b"], staticNodes: 1, poses: [new(0f, 0f, 0f), new(10f, 0f, 0f)], body: $$"""
                    m_Rods = [ {{rods}} ]
                    {{sourceElems}}
                    {{skelParents}}
                    """);

            var rigid = SyntheticCloth.RigidRod(0, 1, 10f, 1f);
            var doubled = Pair(rigid + rigid, string.Empty);
            var sprung = Pair(rigid + rigid, "m_SourceElems = [ 0, 1, 0, 0, 0, 1 ]");
            var banded = Pair(rigid + SyntheticCloth.BandedRod(0, 1, 8f, 10f, 1f), string.Empty);
            var single = Pair(rigid, string.Empty);
            var unparented = Pair(rigid + rigid, string.Empty, string.Empty);

            static bool SpanCopy(FeModel feModel, int rod)
                => ClothExtract.IsUnrecordedSpanCopy(feModel, feModel.Rods[rod], ClothExtract.RodCountsByPair(feModel).Entries);

            using (Assert.Multiple())
            {
                await Assert.That(SpanCopy(sprung, 1)).IsFalse();
                await Assert.That(SpanCopy(banded, 1)).IsFalse();
                await Assert.That(SpanCopy(single, 0)).IsFalse();
                await Assert.That(SpanCopy(unparented, 1)).IsFalse();

                await Assert.That(SpanCopy(doubled, 1)).IsTrue();
            }
        }

        /// <summary>
        /// A fold-weighted record beside a cluster band leaves the clique one cluster; the same records on unfolded
        /// pairs break it.
        /// </summary>
        [Test]
        public async Task AFoldBesideAClusterBandLeavesTheCliqueOneCluster()
        {
            var ringOwner = new Dictionary<int, int> { [2] = 0, [3] = 0, [4] = 1, [5] = 1 };
            var folded = KVObject.Array();
            ClothExtract.AddRingClusterCliques(folded, FoldedClusterClique(faces: true), ringOwner);
            var unfolded = KVObject.Array();
            ClothExtract.AddRingClusterCliques(unfolded, FoldedClusterClique(faces: false), ringOwner);

            using (Assert.Multiple())
            {
                await Assert.That(unfolded.Count).IsEqualTo(0);

                await Assert.That(folded.Select(static child => string.Join("|", child.Value.GetSubCollection("chain").GetArray("joints")
                    .Select(static joint => joint.GetStringProperty("joint_name")))).ToArray())
                    .IsEquivalentTo(FoldedCliqueMembers, CollectionOrdering.Matching);
            }
        }

        private static readonly string[] FoldedCliqueMembers = ["$cca_0|$cca_1|$ccb_0|$ccb_1"];

        /// <summary>
        /// <see cref="ClusterCliqueRings"/> with unequal ring masses and a fold-weighted record on each ring pair;
        /// <paramref name="faces"/> adds the quads that fold them.
        /// </summary>
        private static FeModel FoldedClusterClique(bool faces) => SyntheticCloth.Model(
            ["a", "b", "$cca_0", "$cca_1", "$ccb_0", "$ccb_1"], staticNodes: 2, parents: [-1, -1, 0, 0, 1, 1],
                invMasses: "0.0, 0.0, 0.01, 0.02, 0.01, 0.02",
            poses: [new(0f, 0f, 0f), new(10f, 0f, 0f), new(0f, 3f, -5f), new(0f, -3f, -5f), new(10f, 3f, -5f), new(10f, -3f, -5f)],
            body: $$"""
                m_Quads = [ {{(faces ? "{ nNode = [ 0, 1, 4, 2 ] }, { nNode = [ 1, 0, 3, 5 ] }" : string.Empty)}} ]
                m_Rods =
                [
                    {{SyntheticCloth.BandedRod(2, 3, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(2, 4, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(2, 5, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(3, 4, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(3, 5, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(4, 5, 8f, 16f, 1f)}}
                    { nNode = [ 2, 3 ] flMinDist = 1.0 flMaxDist = 12.0 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 5 ] flMinDist = 1.0 flMaxDist = 12.0 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                ]
                """);

        /// <summary>
        /// On the proxy sheet route an authored <c>ClothSpring</c> between two chain joints is re-declared; the model
        /// without it declares none.
        /// </summary>
        [Test]
        public async Task AnAuthoredSpringBetweenChainJointsIsReDeclaredOnTheSheetRoute()
        {
            static string Extract(string fixture)
            {
                using var resource = new Resource();
                resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", fixture));
                return new ModelExtract(resource, new NullFileLoader()).ToValveModel();
            }

            var sprung = Extract("cloth_sheet_chain_spring.vmdl_c");
            var plain = Extract("cloth_sheet_chain_nospring.vmdl_c");

            using (Assert.Multiple())
            {
                await Assert.That(sprung).Contains("ClothProxyMeshFile");
                await Assert.That(plain).Contains("ClothProxyMeshFile");
                await Assert.That(plain).DoesNotContain("_class = \"ClothSpring\"");

                await Assert.That(sprung).Contains("_class = \"ClothSpring\"");
                await Assert.That(sprung).Contains("cloth_node_0 = \"coattail_1_L\"");
                await Assert.That(sprung).Contains("cloth_node_1 = \"coattail_1_R\"");
            }
        }
    }
}
