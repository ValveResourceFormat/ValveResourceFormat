using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    /// <summary>Declaring cloth effects, stiff hinges and anti-tunnel constructs.</summary>
    public class ClothExtractEffectTest : ClothTestFixtures
    {
        /// <summary>
        /// Anti-tunnel probes are declared in the order of their target slices in <c>m_AntiTunnelTargetNodes</c>, here
        /// the reverse of <c>m_AntiTunnelProbes</c>.
        /// </summary>
        [Test]
        public async Task AntiTunnelProbesAreDeclaredInTheOrderTheirTargetsAreConcatenated()
        {
            var children = KVObject.Array();
            ClothExtract.AddClothAntiTunnelProbes(children, SwappedAntiTunnelProbes(), null);

            using (Assert.Multiple())
            {
                await Assert.That(AntiTunnelSources(children))
                    .IsEquivalentTo(SwappedProbeSources, CollectionOrdering.Matching);
                await Assert.That(AntiTunnelTargets(children, 0))
                    .IsEquivalentTo(SwappedProbeFirstTargets, CollectionOrdering.Matching);
                await Assert.That(AntiTunnelTargets(children, 1))
                    .IsEquivalentTo(SwappedProbeSecondTargets, CollectionOrdering.Matching);
            }
        }

        /// <summary>
        /// A probe's targets keep their compiled slice order.
        /// </summary>
        [Test]
        public async Task AnAntiTunnelProbeKeepsTheSliceOrderOfItsTargets()
        {
            var children = KVObject.Array();
            ClothExtract.AddClothAntiTunnelProbes(children, ShuffledAntiTunnelTargets(), null);

            await Assert.That(AntiTunnelTargets(children, 0))
                .IsEquivalentTo(ShuffledProbeTargets, CollectionOrdering.Matching);
        }

        private static readonly string[] SwappedProbeSources = ["body", "tip"];

        private static readonly string[] SwappedProbeFirstTargets = ["a", "b", "c"];

        private static readonly string[] SwappedProbeSecondTargets = ["body"];

        private static readonly string[] ShuffledProbeTargets = ["c", "a", "b"];

        private static string[] AntiTunnelSources(KVObject children)
            => children.Select(static c => c.Value.GetStringProperty("source_node")).ToArray();

        private static string[] AntiTunnelTargets(KVObject children, int index)
            => children.ElementAt(index).Value.GetSubCollection("data").GetSubCollection("nodes")
                .Select(static n => n.Key).ToArray();

        /// <summary>Two probes whose target slices are laid out in the reverse of the probe order.</summary>
        private static FeModel SwappedAntiTunnelProbes() => SyntheticCloth.Model(
            ["root", "a", "b", "c", "body", "tip"], staticNodes: 1, parents: [-1, 0, 0, 0, 0, 0],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f), new(0f, 4f, -30f), new(0f, 8f, -30f)],
            body: """
                m_AntiTunnelTargetNodes = [ 1, 2, 3, 4 ]
                m_AntiTunnelProbes =
                [
                    { flWeight = 1.0 nFlags = 1 nProbeNode = 5 nCount = 1 nBegin = 3
                      flActivationDistance = 1.0 flCurvatureRadius = 0.0 flBias = 0.0 },
                    { flWeight = 1.0 nFlags = 0 nProbeNode = 4 nCount = 3 nBegin = 0
                      flActivationDistance = 1.0 flCurvatureRadius = 0.0 flBias = 0.0 },
                ]
                """);

        /// <summary>One probe whose target slice is not in ascending node order.</summary>
        private static FeModel ShuffledAntiTunnelTargets() => SyntheticCloth.Model(
            ["root", "a", "b", "c", "body"], staticNodes: 1, parents: [-1, 0, 0, 0, 0],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f), new(0f, 4f, -30f)],
            body: """
                m_AntiTunnelTargetNodes = [ 3, 1, 2 ]
                m_AntiTunnelProbes =
                [
                    { flWeight = 1.0 nFlags = 0 nProbeNode = 4 nCount = 3 nBegin = 0
                      flActivationDistance = 1.0 flCurvatureRadius = 0.0 flBias = 0.0 },
                ]
                """);

        /// <summary>
        /// An effect declares <c>cloth_effect_version</c> from its <c>Version</c> parameter, and none without it.
        /// </summary>
        [Test]
        public async Task AClothEffectVersionIsItsVersionParameter()
        {
            var versioned = ClothWindEffect("Version = 2");
            var unversioned = ClothWindEffect(string.Empty);
            var maps = new HashSet<string>();
            var node = ClothExtract.MakeClothEffect(versioned, versioned.Effects.First(), maps);
            var plain = ClothExtract.MakeClothEffect(unversioned, unversioned.Effects.First(), maps);

            using (Assert.Multiple())
            {
                await Assert.That(node!.GetInt32Property("cloth_effect_version")).IsEqualTo(2);
                await Assert.That(plain!.ContainsKey("cloth_effect_version")).IsFalse();
            }
        }

        private static FeModel ClothWindEffect(string version) => SyntheticCloth.Parse($$"""
            {
                m_nNodeCount = 1
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0 ]
                m_InitPose = [ {{SyntheticCloth.Pose(0f, 0f, 0f)}} ]
                m_Effects =
                [
                    {
                        sName = "wind0"
                        nNameHash = 1456486
                        nType = 1
                        m_Params =
                        {
                            Strength = [ 70.400002, 0.0, 0.0 ]
                            AirToCloth = 0.249439
                            LocalSpace = 0.0
                            Choppiness = 1.0
                            Vortices = [ { MaxSpeed = 123.200005 MaxCell = 32.0 } ]
                            {{version}}
                        }
                    },
                ]
            }
            """);

        /// <summary>
        /// A stiffen effect declares its <c>BoneOverlay</c> parameter beside <c>Stiffness</c>, and none without it.
        /// </summary>
        [Test]
        public async Task AStiffenEffectsBoneOverlayIsItsOwnParameter()
        {
            var overlaid = ClothStiffenEffect("BoneOverlay = 0.5");
            var plain = ClothStiffenEffect(string.Empty);
            var maps = new HashSet<string>();
            var node = ClothExtract.MakeClothEffect(overlaid, overlaid.Effects.First(), maps);
            var bare = ClothExtract.MakeClothEffect(plain, plain.Effects.First(), maps);

            using (Assert.Multiple())
            {
                await Assert.That(node!.GetFloatProperty("BoneOverlay")).IsEqualTo(0.5f);
                await Assert.That(node!.GetFloatProperty("Stiffness")).IsEqualTo(2f);
                await Assert.That(bare!.ContainsKey("BoneOverlay")).IsFalse();
            }
        }

        private static FeModel ClothStiffenEffect(string overlay) => SyntheticCloth.Parse($$"""
            {
                m_nNodeCount = 1
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0 ]
                m_InitPose = [ {{SyntheticCloth.Pose(0f, 0f, 0f)}} ]
                m_Effects =
                [
                    {
                        sName = "stiffen0"
                        nNameHash = 1
                        nType = 3
                        m_Params =
                        {
                            Stiffness = 2.0
                            {{overlay}}
                        }
                    },
                ]
            }
            """);

        /// <summary>
        /// An effect recording a <c>Node</c> is declared under the static ClothNode rooted on that bone, with its
        /// <c>strength</c> and <c>angles</c>; an effect with no <c>Node</c> stays at the top level.
        /// </summary>
        [Test]
        public async Task AnEffectRecordingANodeIsDeclaredUnderThatStaticClothNode()
        {
            var feModel = SyntheticCloth.Model(
                ["spine_2", "coattail_0_L"], staticNodes: 2, parents: [-1, 0],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -10f)],
                body: """
                    m_Effects =
                    [
                        { sName = "gravity0" nNameHash = 1 nType = 4 m_Params = { Node = 0 Strength = [ 0.353553, 0.353553, -0.0 ] } },
                        { sName = "gravity1" nNameHash = 2 nType = 4 m_Params = { Strength = [ 0.0, 0.0, -2.0 ] } },
                    ]
                    """);
            var (folder, folderChildren) = KVHelpers.MakeListNode("Folder");
            folderChildren.Add(EffectParentNode("spine_2", isStatic: true));
            var softbodyChildren = KVObject.Array();
            softbodyChildren.Add(folder);

            ClothExtract.AddClothEffects(softbodyChildren, feModel, new HashSet<string>());

            var nested = folderChildren.ElementAt(0).Value.GetArray("children");
            var top = softbodyChildren.Select(static child => child.Value).Skip(1).ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(nested.Count).IsEqualTo(1);
                await Assert.That(nested[0].GetStringProperty("_class")).IsEqualTo("ClothEffectAddGravity");
                await Assert.That(nested[0].GetStringProperty("name")).IsEqualTo("gravity0");
                await Assert.That(nested[0].GetFloatProperty("strength")).IsEqualTo(0.5f).Within(1e-5f);
                await Assert.That(Vector3.Distance(nested[0].GetSubCollection("angles").ToVector3(), new Vector3(0f, 45f, 0f)))
                    .IsLessThan(1e-3f);
                await Assert.That(top.Select(static child => child.GetStringProperty("name")).ToArray())
                    .IsEquivalentTo(["gravity1"], CollectionOrdering.Matching);
                await Assert.That(top[0].GetFloatProperty("strength")).IsEqualTo(2f).Within(1e-5f);
                await Assert.That(Vector3.Distance(top[0].GetSubCollection("angles").ToVector3(), new Vector3(90f, 0f, 0f)))
                    .IsLessThan(1e-3f);
            }
        }

        /// <summary>
        /// An effect whose bone has no emitted static ClothNode is declared under a bare static one per bone; effects
        /// on an emitted static node join it, and effects with no or a generated <c>Node</c> stay at the top level.
        /// </summary>
        [Test]
        public async Task AnEffectWhoseNodeHasNoStaticClothNodeGetsABareStaticOne()
        {
            var feModel = SyntheticCloth.Model(
                ["spine_2", "coattail_0_L", "coattail_1_L", "$cccoattail_1_L_0"], staticNodes: 2, parents: [-1, 0, 1, 2],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 2f, -20f)],
                body: """
                    m_Effects =
                    [
                        { sName = "gravity_root" nNameHash = 1 nType = 4 m_Params = { Node = 1 Strength = [ 1.0, 0.0, 0.0 ] } },
                        { sName = "gravity_root2" nNameHash = 2 nType = 4 m_Params = { Node = 1 Strength = [ 0.0, 1.0, 0.0 ] } },
                        { sName = "gravity_joint" nNameHash = 3 nType = 4 m_Params = { Node = 2 Strength = [ 0.0, 0.0, 1.0 ] } },
                        { sName = "gravity_static" nNameHash = 4 nType = 4 m_Params = { Node = 0 Strength = [ 1.0, 0.0, 0.0 ] } },
                        { sName = "gravity_top" nNameHash = 5 nType = 4 m_Params = { Strength = [ 1.0, 0.0, 0.0 ] } },
                        { sName = "gravity_generated" nNameHash = 6 nType = 4 m_Params = { Node = 3 Strength = [ 1.0, 0.0, 0.0 ] } },
                    ]
                    """);
            var softbodyChildren = KVObject.Array();
            softbodyChildren.Add(EffectParentNode("spine_2", isStatic: true));
            softbodyChildren.Add(EffectParentNode("coattail_1_L", isStatic: false));

            ClothExtract.AddClothEffects(softbodyChildren, feModel, new HashSet<string>());

            var top = softbodyChildren.Select(static child => child.Value).ToArray();
            static string[] Names(IEnumerable<KVObject> nodes) => [.. nodes.Select(static node => node.GetStringProperty("name"))];

            await Assert.That(Names(top)).IsEquivalentTo(
                ["spine_2", "coattail_1_L", "coattail_0_L_effects", "coattail_1_L_effects", "gravity_top", "gravity_generated"],
                CollectionOrdering.Matching);

            using (Assert.Multiple())
            {
                foreach (var (bare, bone) in new[] { (top[2], "coattail_0_L"), (top[3], "coattail_1_L") })
                {
                    await Assert.That(bare.GetStringProperty("_class")).IsEqualTo("ClothNode");
                    await Assert.That(bare.GetStringProperty("cloth_node_root_bone")).IsEqualTo(bone);
                    await Assert.That(bare.GetBooleanProperty("is_static_node")).IsTrue();
                }

                await Assert.That(Names(top[2].GetArray("children"))).IsEquivalentTo(["gravity_root", "gravity_root2"],
                    CollectionOrdering.Matching);
                await Assert.That(Names(top[3].GetArray("children"))).IsEquivalentTo(["gravity_joint"]);
                await Assert.That(Names(top[0].GetArray("children"))).IsEquivalentTo(["gravity_static"]);
                await Assert.That(top[1].ContainsKey("children")).IsFalse();
            }
        }

        /// <summary>
        /// Anti-tunnel bytecode on a sheetless model declares a <c>ClothAntiTunnelColliderGroup</c> naming the capsule
        /// and each chain once; no bytecode declares no group.
        /// </summary>
        [Test]
        public async Task AChainModelWithAntiTunnelBytecodeDeclaresItsColliderGroup()
        {
            static FeModel Model(string bytecode) => SyntheticCloth.Model(
                ["spine_2", "coattail_0_L", "coattail_1_L"], staticNodes: 2, parents: [-1, -1, 1],
                poses: [new(0f, 0f, 0f), new(0f, 5f, 0f), new(0f, 5f, -8f)],
                body: $$"""
                    m_AntiTunnelBytecode = [ {{bytecode}} ]
                    """);

            var withBytecode = KVObject.Array();
            ClothExtract.AddClothAntiTunnelGroup(withBytecode, Model("131072, 805306368, 2, 131073, 196609"), ["spine_2_clothCapsule"],
                ["coattail_0_L", "coattail_0_L"]);
            var withoutBytecode = KVObject.Array();
            ClothExtract.AddClothAntiTunnelGroup(withoutBytecode, Model(string.Empty), ["spine_2_clothCapsule"], ["coattail_0_L"]);

            var groups = withBytecode.Select(static child => child.Value).ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(groups.Length).IsEqualTo(1);
                await Assert.That(groups[0].GetStringProperty("_class")).IsEqualTo("ClothAntiTunnelColliderGroup");
                await Assert.That(groups[0].GetSubCollection("data").GetSubCollection("nodes").Select(static member => member.Key))
                    .IsEquivalentTo(["spine_2_clothCapsule", "coattail_0_L"], CollectionOrdering.Matching);
                await Assert.That(withoutBytecode.Count).IsEqualTo(0);
            }
        }

        /// <summary>
        /// A Kelager bend over three free cloth nodes comes back as a <c>ClothStiffHinge</c> with its <c>max_angle</c>;
        /// a bend over chain joints does not.
        /// </summary>
        [Test]
        public async Task ABendOverFreeClothNodesComesBackAsAStiffHinge()
        {
            var feModel = SyntheticCloth.Model(
                ["spine_2", "$cloth_node_hinge_n0", "$cloth_node_hinge_n1", "$cloth_node_hinge_n2", "coattail_0_L", "coattail_1_L", "coattail_2_L"],
                    staticNodes: 3, parents: [-1, 0, 0, 0, -1, 4, 5], invMasses: "0.0, 0.0, 0.0, 1.0, 0.0, 1.0, 1.0",
                poses: [new(0f, 0f, 0f), new(0f, 0f, -4f), new(0f, 0f, -8f), new(0f, 0f, -12f), new(0f, 5f, 0f), new(0f, 5f, -8f),
                    new(0f, 5f, -16f)],
                body: """
                    m_KelagerBends =
                    [
                        { flWeight = [ -0.0, 1.0, 2.0 ] flHeight0 = 1.652419 nNode = [ 1, 2, 3 ] nReserved = 0 },
                        { flWeight = [ -0.0, 1.0, 2.0 ] flHeight0 = 2.981424 nNode = [ 1, 2, 3 ] nReserved = 0 },
                        { flWeight = [ -2.0, 1.0, 1.0 ] flHeight0 = 0.5 nNode = [ 5, 4, 6 ] nReserved = 0 },
                    ]
                    """);

            var softbodyChildren = KVObject.Array();
            ClothExtract.AddClothStiffHinges(softbodyChildren, feModel);
            var hinges = softbodyChildren.Select(static child => child.Value).ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(hinges.Length).IsEqualTo(2);
                foreach (var (hinge, angle) in hinges.Zip([30f, 90f]))
                {
                    await Assert.That(hinge.GetStringProperty("_class")).IsEqualTo("ClothStiffHinge");
                    await Assert.That(hinge.GetStringProperty("cloth_node_0")).IsEqualTo("hinge_n0");
                    await Assert.That(hinge.GetStringProperty("cloth_node_1")).IsEqualTo("hinge_n1");
                    await Assert.That(hinge.GetStringProperty("cloth_node_2")).IsEqualTo("hinge_n2");
                    await Assert.That(MathF.Abs(hinge.GetFloatProperty("max_angle") - angle)).IsLessThan(1e-2f);
                }
            }
        }

        /// <summary>
        /// A wind effect declares <c>local_space</c> from its <c>LocalSpace</c> parameter; zero declares no key.
        /// </summary>
        [Test]
        public async Task AWindEffectsLocalSpaceIsItsLocalSpaceParameter()
        {
            var local = WindInLocalSpace("0.469");
            var world = WindInLocalSpace("0.0");
            var maps = new HashSet<string>();
            var node = ClothExtract.MakeClothEffect(local, local.Effects.First(), maps);
            var plain = ClothExtract.MakeClothEffect(world, world.Effects.First(), maps);

            using (Assert.Multiple())
            {
                await Assert.That(node!.GetFloatProperty("local_space")).IsEqualTo(0.469f).Within(1e-6f);
                await Assert.That(plain!.ContainsKey("local_space")).IsFalse();
            }
        }

        private static FeModel WindInLocalSpace(string localSpace) => SyntheticCloth.Parse($$"""
            {
                m_nNodeCount = 1
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0 ]
                m_InitPose = [ {{SyntheticCloth.Pose(0f, 0f, 0f)}} ]
                m_Effects =
                [
                    {
                        sName = "ClothEffectWind"
                        nNameHash = 2353092209
                        nType = 1
                        m_Params =
                        {
                            Strength = [ -281.600006, 0.0, 0.0 ]
                            AirToCloth = 0.5
                            LocalSpace = {{localSpace}}
                            Choppiness = 8.0
                            Vortices = [ { MaxSpeed = 211.199997 MaxCell = 1658.880005 } ]
                        }
                    },
                ]
            }
            """);

        /// <summary>
        /// An effect names a selection the document declares in a <c>ClothVertexMap</c> or a joint's <c>vertex_map</c>;
        /// one declared nowhere is not named.
        /// </summary>
        [Test]
        public async Task AnEffectNamesASelectionTheDocumentDeclares()
        {
            var feModel = StiffenOnSelection();

            var map = KVObject.Collection();
            map.Add("_class", "ClothVertexMap");
            map.Add("name", "sail_vm");
            var folderChildren = KVObject.Array();
            folderChildren.Add(map);
            var folder = KVObject.Collection();
            folder.Add("_class", "Folder");
            folder.Add("children", folderChildren);
            var container = KVObject.Array();
            container.Add(folder);

            var joint = KVObject.Collection();
            joint.Add("joint_name", "sail_top");
            joint.Add("vertex_map", "sail_vm=0.5");
            var joints = KVObject.Array();
            joints.Add(joint);
            var table = KVObject.Collection();
            table.Add("joints", joints);
            var chain = KVObject.Collection();
            chain.Add("_class", "ClothChain");
            chain.Add("chain", table);
            var jointDocument = KVObject.Array();
            jointDocument.Add(chain);

            var bare = KVObject.Array();

            foreach (var document in new[] { container, jointDocument, bare })
            {
                ClothExtract.AddClothEffects(document, feModel, new HashSet<string>());
            }

            static KVObject Effect(KVObject document)
                => document.Select(static child => child.Value).First(static child => child.GetStringProperty("_class") == "ClothEffectStiffen");

            using (Assert.Multiple())
            {
                await Assert.That(Effect(container).GetStringProperty("vertex_map")).IsEqualTo("sail_vm");
                await Assert.That(Effect(jointDocument).GetStringProperty("vertex_map")).IsEqualTo("sail_vm");
                await Assert.That(Effect(bare).ContainsKey("vertex_map")).IsFalse();
            }
        }

        private static FeModel StiffenOnSelection() => SyntheticCloth.Parse($$"""
            {
                m_nNodeCount = 2
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                ]
                m_VertexMaps = [ {{VertexMapEntry("sail_vm", 7, 0, 1, 1)}} ]
                m_VertexMapValues = [ 255 ]
                m_Effects =
                [
                    {
                        sName = "stiffen0"
                        nNameHash = 1
                        nType = 3
                        m_Params =
                        {
                            Stiffness = 1.0
                            VertexMap = 7
                        }
                    },
                ]
            }
            """);

        /// <summary>
        /// Vortices compiled at zero speed read <c>time_multiplier</c> 0 with a positive vortex speed; moving vortices
        /// keep 1.
        /// </summary>
        [Test]
        public async Task VorticesCompiledAtZeroSpeedAreAZeroTimeMultiplier()
        {
            var moving = WindVortexEffect("[ 70.400002, 0.0, 0.0 ]", "1.0", "123.200005");
            var stilled = WindVortexEffect("[ -0.0, -0.0, -0.0 ]", "0.0", "0.0");
            var maps = new HashSet<string>();
            var movingNode = ClothExtract.MakeClothEffect(moving, moving.Effects.First(), maps)!;
            var stilledNode = ClothExtract.MakeClothEffect(stilled, stilled.Effects.First(), maps)!;

            using (Assert.Multiple())
            {
                await Assert.That(movingNode.GetFloatProperty("time_multiplier")).IsEqualTo(1f);
                await Assert.That(movingNode.GetFloatProperty("vortex_max_speed_mph")).IsEqualTo(7f).Within(1e-4f);

                await Assert.That(stilledNode.GetFloatProperty("time_multiplier")).IsEqualTo(0f);
                await Assert.That(stilledNode.GetFloatProperty("vortex_max_speed_mph")).IsGreaterThan(0f);
                await Assert.That(stilledNode.GetInt32Property("vortex_count")).IsEqualTo(2);
            }
        }

        private static FeModel WindVortexEffect(string strength, string choppiness, string maxSpeed) => SyntheticCloth.Parse($$"""
            {
                m_nNodeCount = 1
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0 ]
                m_InitPose = [ {{SyntheticCloth.Pose(0f, 0f, 0f)}} ]
                m_Effects =
                [
                    {
                        sName = "wind0"
                        nNameHash = 1456486
                        nType = 1
                        m_Params =
                        {
                            Strength = {{strength}}
                            AirToCloth = 0.249439
                            Choppiness = {{choppiness}}
                            Vortices = [ { MaxSpeed = {{maxSpeed}} MaxCell = 80.0 }, { MaxSpeed = {{maxSpeed}} MaxCell = 80.0 } ]
                        }
                    },
                ]
            }
            """);
    }
}
