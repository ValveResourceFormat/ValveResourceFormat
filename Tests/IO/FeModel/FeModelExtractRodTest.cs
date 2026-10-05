using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static Tests.IO.FeModelBuilder;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace Tests.IO
{
    /// <summary>Declaring springs, clusters and the rods a chain or sheet does not regenerate.</summary>
    public class FeModelExtractRodTest : FeModelTestModels
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

        private static ClothReconstruction ClusterTieChain(int ties) => (ThreeJointRope with
        {
            Rods = [.. ThreeJointRope.Rods!, .. Enumerable.Repeat(Rod(1, 2, 12f, 48f), ties)],
        }).Reconstruct();

        /// <summary>
        /// A rod from a free node to a chain joint is re-declared as a <c>ClothSpring</c> where a two-corner source
        /// element records it and as a two-member cluster where none does; without chain joints nothing is declared.
        /// </summary>
        [Test]
        public async Task ASpringFromAFreeNodeToAChainJointIsDeclared()
        {
            static ClothReconstruction Model(params int[] sourceElems) => new FeModelBuilder
            {
                Names = ["coattail_0_L", "coattail_0_R", "coattail_1_R"],
                StaticNodes = 2,
                InvMasses = [0f, 0f, 0.006141f],
                Parents = [-1, -1, 1],
                Positions = [new(4f, 0f, 60f), new(-4f, 0f, 60f), new(-4f, 0f, 51.5f)],
                SourceElems = sourceElems,
                Rods = [RigidRod(0, 2, 11.854138f), RigidRod(1, 2, 8.5f)],
            }.Reconstruct();

            static string[] Ties(ClothReconstruction cloth, HashSet<int>? chainJoints)
            {
                var clothChildren = KVObject.Array();
                var softbodyChildren = KVObject.Array();
                ClothExtract.AddFreeClothNodesAndSprings(clothChildren, softbodyChildren, cloth, [1, 2], static _ => true,
                    [], chainJoints: chainJoints);
                return [.. softbodyChildren.Select(static child => child.Value).Select(static child =>
                    child.GetStringProperty("_class") == "ClothSpring"
                        ? "spring:" + child.GetStringProperty("cloth_node_0") + "|" + child.GetStringProperty("cloth_node_1")
                        : "cluster:" + string.Join("|", child.GetSubCollection("chain").GetArray("joints")
                            .Select(static joint => joint.GetStringProperty("joint_name"))))];
            }

            var recorded = Model(0, 1, 0, 0, 0, 2);
            var unrecorded = Model(0, 0, 0, 0);

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
            var cloth = FreeNodeSprungToJoint.Reconstruct();
            var joints = cloth.BuildBoneChains().SelectMany(static chain => chain.Joints).Select(static joint => joint.Node).ToHashSet();

            var withJoints = KVObject.Array();
            ClothExtract.AddFreeClothNodesAndSprings(KVObject.Array(), withJoints, cloth, joints, static _ => true, [], chainJoints: joints);
            var withoutJoints = KVObject.Array();
            ClothExtract.AddFreeClothNodesAndSprings(KVObject.Array(), withoutJoints, cloth, joints, static _ => true, []);

            static (string, string)[] Springs(KVObject children) => [.. children
                .Select(static child => child.Value)
                .Where(static node => node.GetStringProperty("_class") == "ClothSpring")
                .Select(static node => (node.GetStringProperty("cloth_node_0"), node.GetStringProperty("cloth_node_1")))];

            using (Assert.Multiple())
            {
                await Assert.That(Springs(withJoints).Count(static s => s == ("node_b", "coattail_1_L"))).IsEqualTo(1);
                await Assert.That(Springs(withJoints).Count(static s => s.Item1 == "coattail_1_L" || s.Item2 == "coattail_1_L")).IsEqualTo(1);
                await Assert.That(Springs(withoutJoints).Any(static s => s.Item1 == "coattail_1_L" || s.Item2 == "coattail_1_L")).IsFalse();
            }
        }

        /// <summary>
        /// A banded rod between two joints' ring nodes is declared as a two-member <c>ClothSelfCollisionCluster</c>
        /// whose name has no <c>$</c>; the band closed onto the rest length or between one joint's rings declares
        /// nothing.
        /// </summary>
        [Test]
        public async Task ARingRingClusterTieIsItsTwoMemberClusterUnderANameWithoutADollar()
        {
            var tiedModel = RingClusterTie(Rod(2, 4, 12f, 48f));
            var tied = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(tied, tiedModel, tiedModel.BuildBoneChains());

            var restBandModel = RingClusterTie(Rod(2, 4, 8.4f, 8.503419f));
            var restBand = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(restBand, restBandModel, restBandModel.BuildBoneChains());

            var sameJointModel = RingClusterTie(Rod(2, 3, 12f, 48f));
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

        /// <summary>
        /// A coattail of four joints, the static first with a one-node ring and the rest with two-node rings, its rigid rods
        /// followed by <paramref name="tie"/>.
        /// </summary>
        private static ClothReconstruction RingClusterTie(FeRodConstraint tie) => new FeModelBuilder
        {
            Names = ["coattail_0_L", "$cccoattail_0_L_0", "$cccoattail_1_L_0", "$cccoattail_1_L_1", "$cccoattail_2_L_0", "$cccoattail_2_L_1",
                "$cccoattail_end_L_0", "$cccoattail_end_L_1", "coattail_1_L", "coattail_2_L", "coattail_end_L"],
            StaticNodes = 2,
            InvMasses = [0f, 0f, 0.002634f, 0.003111f, 0.002588f, 0.003142f, 0.005709f, 0.005709f, 1f, 1f, 1f],
            Parents = [-1, 0, 8, 8, 9, 9, 10, 10, 0, 8, 9],
            Poses =
            [
                Pose(-8.915481f, 4.000124f, 65.447983f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-10.723646f, 4.561181f, 66.092773f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-13.473376f, 4.824905f, 58.146507f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-9.917552f, 3.709337f, 56.693367f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-16.587204f, 5.195801f, 50.242416f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-13.028829f, 4.079931f, 48.795761f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-19.686529f, 5.562407f, 42.336063f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-16.128153f, 4.446536f, 40.889408f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-11.695464f, 4.267121f, 57.419937f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-14.808016f, 4.637866f, 49.519089f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-17.907341f, 5.004471f, 41.612736f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
            ],
            Rods =
            [
                RigidRod(0, 2, 8.646747f, 1f, 0f),
                RigidRod(0, 3, 8.816575f, 1f, 0f),
                RigidRod(1, 2, 8.412711f, 1f, 0f),
                RigidRod(1, 3, 9.472289f, 1f, 0f),
                RigidRod(2, 3, 4f),
                RigidRod(2, 4, 8.503419f),
                RigidRod(2, 5, 9.390903f),
                RigidRod(3, 4, 9.397265f),
                RigidRod(3, 5, 8.496444f),
                RigidRod(4, 5, 4.000001f),
                RigidRod(4, 6, 8.500037f),
                RigidRod(4, 7, 9.394195f),
                RigidRod(5, 6, 9.394169f),
                RigidRod(5, 7, 8.500037f),
                RigidRod(6, 7, 4.000002f),
                tie,
            ],
        }.Reconstruct();

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
            var tieModel = CrossChainTieModel(12f, 48f);
            var recorded = CrossChainTieModel(12f, 48f, 0, 1, 0, 0, 2, 3);
            var rest = CrossChainTieModel(20f, 20f);

            using (Assert.Multiple())
            {
                await Assert.That(SurplusClasses(recorded)).DoesNotContain("ClothSelfCollisionCluster");

                await Assert.That(SurplusClasses(rest)).IsEquivalentTo(SpringOnly, CollectionOrdering.Matching);

                await Assert.That(SurplusClasses(tieModel)).IsEquivalentTo(ClusterOnly, CollectionOrdering.Matching);
                await Assert.That(SurplusClusterMembers(tieModel)).IsEquivalentTo(ReversedTie, CollectionOrdering.Matching);
            }
        }

        private static readonly string[] SpringOnly = ["ClothSpring"];

        private static readonly string[] ClusterOnly = ["ClothSelfCollisionCluster"];

        private static readonly string[] ReversedTie = ["b1", "a1"];

        private static string[] SurplusClusterMembers(ClothReconstruction cloth)
        {
            var children = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(children, cloth, cloth.BuildBoneChains());
            return [.. children.Where(static c => c.Value.GetStringProperty("_class") == "ClothSelfCollisionCluster")
                .SelectMany(static c => c.Value.GetSubCollection("chain").GetArray("joints"))
                .Select(static j => j.GetStringProperty("joint_name"))];
        }

        private static string[] SurplusClasses(ClothReconstruction cloth)
        {
            var children = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(children, cloth, cloth.BuildBoneChains());
            return [.. children.Select(static c => c.Value.GetStringProperty("_class"))];
        }

        /// <summary>
        /// Two one-joint chains 20 apart with a banded rod between their joints and the given source elements.
        /// </summary>
        private static ClothReconstruction CrossChainTieModel(float min, float max, params int[] sourceElems) => new FeModelBuilder
        {
            Names = ["rootA", "rootB", "a1", "b1"],
            StaticNodes = 2,
            Parents = [-1, -1, 0, 1],
            Positions = [new(0f, 0f, 0f), new(20f, 0f, 0f), new(0f, 0f, -10f), new(20f, 0f, -10f)],
            Rods = [RigidRod(0, 2, 10f), RigidRod(1, 3, 10f), Rod(2, 3, min, max)],
            SourceElems = sourceElems,
        }.Reconstruct();

        /// <summary>
        /// Fold-weighted rods on a face-kept sheet read as <c>add_stiffness_rods</c> and are derived; even-split rods
        /// leave the switch off.
        /// </summary>
        [Test]
        public async Task AFaceKeptSheetsFoldsAreTheSwitchsAndNoSprings()
        {
            var folded = FoldedSheetModel(0.666667f).Reconstruct();
            var declared = FoldedSheetModel(0.5f).Reconstruct();

            static (bool Switch, HashSet<(int, int)> Derived) Read(ClothReconstruction cloth)
            {
                var proxies = cloth.BuildProxyMeshes().Select(static (proxy, i) => new ClothExtract.ClothProxyFile($"p{i}.dmx", $"p{i}", proxy)).ToList();
                var rods = ClothExtract.ClothRodsFromSurface(cloth, proxies);
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
            var sheet = FaceKeptSheetCorner.Reconstruct();
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

        /// <summary>A face-kept sheet of four quads around node 7 under three static nodes, with six banded rods.</summary>
        private static FeModelBuilder FaceKeptSheetCorner => new()
        {
            Names = ["$cloth_m2p42", "$cloth_m2p45", "$cloth_m2p49", "$cloth_m2p46", "$cloth_m2p47", "$cloth_m2p48", "$cloth_m2p50", "$cloth_m2p51",
                "$cloth_m2p52"],
            StaticNodes = 3,
            InvMasses = [0f, 0f, 0f, 0.004359f, 0.004359f, 0.002214f, 0.003712f, 0.001881f, 0.003712f],
            Positions =
            [
                new(-27.650145f, 10.560019f, 205.05894f),
                new(-27.650145f, -10.560019f, 205.05894f),
                new(-27.33801f, 0f, 195.58267f),
                new(-43.832478f, 6.48951f, 203.6097f),
                new(-43.832478f, -6.48951f, 203.6097f),
                new(-43.35944f, 0f, 198.29076f),
                new(-35.769325f, 8.53872f, 204.58925f),
                new(-35.299297f, 0f, 197.24841f),
                new(-35.769325f, -8.53872f, 204.58925f),
            ],
            Rods =
            [
                Rod(0, 3, 3.35389f, 16.753178f, 0f),
                Rod(2, 5, 2.432684f, 16.255886f, 0f),
                Rod(2, 4, 10.834126f, 19.47035f, 0f),
                Rod(1, 5, 11.755301f, 20.102926f, 0f),
                Rod(6, 8, 0f, 22.364f),
                Rod(3, 4, 0f, 16.776804f),
            ],
            Quads = [Quad(2, 0, 6, 7), Quad(1, 2, 7, 8), Quad(4, 8, 7, 5), Quad(3, 5, 7, 6)],
        };

        /// <summary>
        /// A clique of equal bands over two chains' ring nodes is declared as one cluster of all its members at half
        /// the band; the same clique within one joint declares none.
        /// </summary>
        [Test]
        public async Task AClusterCliqueAcrossTwoChainsRingsIsOneCluster()
        {
            var model = ClusterCliqueRings.Reconstruct();
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
        private static FeModelBuilder ClusterCliqueRings => new()
        {
            Names = ["a", "b", "$cca_0", "$cca_1", "$ccb_0", "$ccb_1"],
            StaticNodes = 2,
            InvMasses = [0f, 0f, 0.01f, 0.01f, 0.01f, 0.01f],
            Parents = [-1, -1, 0, 0, 1, 1],
            Positions = [new(0f, 0f, 0f), new(10f, 0f, 0f), new(0f, 3f, -5f), new(0f, -3f, -5f), new(10f, 3f, -5f), new(10f, -3f, -5f)],
            Rods = [Rod(2, 3, 8f, 16f), Rod(2, 4, 8f, 16f), Rod(2, 5, 8f, 16f), Rod(3, 4, 8f, 16f), Rod(3, 5, 8f, 16f), Rod(4, 5, 8f, 16f)],
        };

        /// <summary>
        /// A second rigid copy of a span with no two-corner source element is an unrecorded span copy; a recorded
        /// spring, a banded copy, a lone rod, or a model without <c>m_SkelParents</c> is not.
        /// </summary>
        [Test]
        public async Task ASecondRigidSpanCopyWithNoSourceElementIsAClusterRod()
        {
            var rigid = RigidRod(0, 1, 10f);
            var doubled = (RodPair with { Rods = [rigid, rigid] }).Reconstruct();
            var sprung = (RodPair with { Rods = [rigid, rigid], SourceElems = [0, 1, 0, 0, 0, 1] }).Reconstruct();
            var banded = (RodPair with { Rods = [rigid, Rod(0, 1, 8f, 10f)] }).Reconstruct();
            var single = (RodPair with { Rods = [rigid] }).Reconstruct();
            var unparented = (RodPair with { Rods = [rigid, rigid], Parents = null }).Reconstruct();

            static bool SpanCopy(ClothReconstruction cloth, int rod)
                => ClothExtract.IsUnrecordedSpanCopy(cloth, cloth.Index.Rods[rod], ClothExtract.RodCountsByPair(cloth).Entries);

            using (Assert.Multiple())
            {
                await Assert.That(SpanCopy(sprung, 1)).IsFalse();
                await Assert.That(SpanCopy(banded, 1)).IsFalse();
                await Assert.That(SpanCopy(single, 0)).IsFalse();
                await Assert.That(SpanCopy(unparented, 1)).IsFalse();

                await Assert.That(SpanCopy(doubled, 1)).IsTrue();
            }
        }

        /// <summary>A static node and its child 10 away.</summary>
        private static FeModelBuilder RodPair => new()
        {
            Names = ["a", "b"],
            StaticNodes = 1,
            Parents = [-1, 0],
            Positions = [new(0f, 0f, 0f), new(10f, 0f, 0f)],
        };

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
        private static ClothReconstruction FoldedClusterClique(bool faces) => (ClusterCliqueRings with
        {
            InvMasses = [0f, 0f, 0.01f, 0.02f, 0.01f, 0.02f],
            Quads = faces ? [Quad(0, 1, 4, 2), Quad(1, 0, 3, 5)] : [],
            Rods = [.. ClusterCliqueRings.Rods!, Rod(2, 3, 1f, 12f, 0.333333f), Rod(4, 5, 1f, 12f, 0.333333f)],
        }).Reconstruct();

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

            var sprung = Extract("FeModel/sheet_chain_spring.vmdl_c");
            var plain = Extract("FeModel/sheet_chain_nospring.vmdl_c");

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
