using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static Tests.IO.FeModelBuilder;

namespace Tests.IO
{
    /// <summary>Declaring cloth effects, stiff hinges and anti-tunnel constructs.</summary>
    public class FeModelExtractEffectTest : FeModelTestModels
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
            => [.. children.Select(static c => c.Value.GetStringProperty("source_node"))];

        private static string[] AntiTunnelTargets(KVObject children, int index)
            => [.. children.ElementAt(index).Value.GetSubCollection("data").GetSubCollection("nodes").Select(static n => n.Key)];

        /// <summary>Two probes whose target slices are laid out in the reverse of the probe order.</summary>
        private static ClothReconstruction SwappedAntiTunnelProbes() => (ProbedColumn with
        {
            Names = [.. ProbedColumn.Names, "tip"],
            Parents = [.. ProbedColumn.Parents!, 0],
            Positions = [.. ProbedColumn.Positions!, new(0f, 8f, -30f)],
            AntiTunnelTargetNodes = [1, 2, 3, 4],
            AntiTunnelProbes = [new(1f, 1, 5, 1, 3, 1f, 0f, 0f), new(1f, 0, 4, 3, 0, 1f, 0f, 0f)],
        }).Reconstruct();

        /// <summary>One probe whose target slice is not in ascending node order.</summary>
        private static ClothReconstruction ShuffledAntiTunnelTargets() => (ProbedColumn with
        {
            AntiTunnelTargetNodes = [3, 1, 2],
            AntiTunnelProbes = [new(1f, 0, 4, 3, 0, 1f, 0f, 0f)],
        }).Reconstruct();

        /// <summary>A static root over a column of a, b and c, with a body node beside c.</summary>
        private static FeModelBuilder ProbedColumn => new()
        {
            Names = ["root", "a", "b", "c", "body"],
            StaticNodes = 1,
            Parents = [-1, 0, 0, 0, 0],
            Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f), new(0f, 4f, -30f)],
        };

        /// <summary>
        /// An effect declares <c>cloth_effect_version</c> from its <c>Version</c> parameter, and none without it.
        /// </summary>
        [Test]
        public async Task AClothEffectVersionIsItsVersionParameter()
        {
            var versioned = ClothWindEffect(2);
            var unversioned = ClothWindEffect(null);
            var maps = new HashSet<string>();
            var node = ClothExtract.MakeClothEffect(versioned, versioned.Fe.Effects.First(), maps);
            var plain = ClothExtract.MakeClothEffect(unversioned, unversioned.Fe.Effects.First(), maps);

            using (Assert.Multiple())
            {
                await Assert.That(node!.GetInt32Property("cloth_effect_version")).IsEqualTo(2);
                await Assert.That(plain!.ContainsKey("cloth_effect_version")).IsFalse();
            }
        }

        private static ClothReconstruction ClothWindEffect(int? version)
        {
            var parameters = Object(("Strength", Floats(70.400002f, 0f, 0f)), ("AirToCloth", 0.249439f), ("LocalSpace", 0f),
                ("Choppiness", 1f), ("Vortices", KVObject.Array([Object(("MaxSpeed", 123.200005f), ("MaxCell", 32f))])));
            if (version is { } v)
            {
                parameters["Version"] = v;
            }

            return (LoneStaticNode with { Effects = [Effect("wind0", 1456486, 1, parameters)] }).Reconstruct();
        }

        /// <summary>One unnamed static node at the origin.</summary>
        private static FeModelBuilder LoneStaticNode => new()
        {
            NodeCount = 1,
            StaticNodes = 1,
            InvMasses = [0f],
            Positions = [Vector3.Zero],
        };

        /// <summary>
        /// A stiffen effect declares its <c>BoneOverlay</c> parameter beside <c>Stiffness</c>, and none without it.
        /// </summary>
        [Test]
        public async Task AStiffenEffectsBoneOverlayIsItsOwnParameter()
        {
            var overlaid = ClothStiffenEffect(0.5f);
            var plain = ClothStiffenEffect(null);
            var maps = new HashSet<string>();
            var node = ClothExtract.MakeClothEffect(overlaid, overlaid.Fe.Effects.First(), maps);
            var bare = ClothExtract.MakeClothEffect(plain, plain.Fe.Effects.First(), maps);

            using (Assert.Multiple())
            {
                await Assert.That(node!.GetFloatProperty("BoneOverlay")).IsEqualTo(0.5f);
                await Assert.That(node!.GetFloatProperty("Stiffness")).IsEqualTo(2f);
                await Assert.That(bare!.ContainsKey("BoneOverlay")).IsFalse();
            }
        }

        private static ClothReconstruction ClothStiffenEffect(float? overlay)
        {
            var parameters = Object(("Stiffness", 2f));
            if (overlay is { } boneOverlay)
            {
                parameters["BoneOverlay"] = boneOverlay;
            }

            return (LoneStaticNode with { Effects = [Effect("stiffen0", 1, 3, parameters)] }).Reconstruct();
        }

        /// <summary>
        /// An effect recording a <c>Node</c> is declared under the static ClothNode rooted on that bone, with its
        /// <c>strength</c> and <c>angles</c>; an effect with no <c>Node</c> stays at the top level.
        /// </summary>
        [Test]
        public async Task AnEffectRecordingANodeIsDeclaredUnderThatStaticClothNode()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["spine_2", "coattail_0_L"],
                StaticNodes = 2,
                Parents = [-1, 0],
                Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f)],
                Effects = [Gravity("gravity0", 1, 0, 0.353553f, 0.353553f, -0f), Gravity("gravity1", 2, null, 0f, 0f, -2f)],
            }.Reconstruct();
            var (folder, folderChildren) = KVHelpers.MakeListNode("Folder");
            folderChildren.Add(EffectParentNode("spine_2", isStatic: true));
            var softbodyChildren = KVObject.Array();
            softbodyChildren.Add(folder);

            ClothExtract.AddClothEffects(softbodyChildren, cloth, new HashSet<string>());

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
            var cloth = new FeModelBuilder
            {
                Names = ["spine_2", "coattail_0_L", "coattail_1_L", "$cccoattail_1_L_0"],
                StaticNodes = 2,
                Parents = [-1, 0, 1, 2],
                Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 2f, -20f)],
                Effects =
                [
                    Gravity("gravity_root", 1, 1, 1f, 0f, 0f),
                    Gravity("gravity_root2", 2, 1, 0f, 1f, 0f),
                    Gravity("gravity_joint", 3, 2, 0f, 0f, 1f),
                    Gravity("gravity_static", 4, 0, 1f, 0f, 0f),
                    Gravity("gravity_top", 5, null, 1f, 0f, 0f),
                    Gravity("gravity_generated", 6, 3, 1f, 0f, 0f),
                ],
            }.Reconstruct();
            var softbodyChildren = KVObject.Array();
            softbodyChildren.Add(EffectParentNode("spine_2", isStatic: true));
            softbodyChildren.Add(EffectParentNode("coattail_1_L", isStatic: false));

            ClothExtract.AddClothEffects(softbodyChildren, cloth, new HashSet<string>());

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
            static ClothReconstruction Model(params uint[] bytecode) => new FeModelBuilder
            {
                Names = ["spine_2", "coattail_0_L", "coattail_1_L"],
                StaticNodes = 2,
                Parents = [-1, -1, 1],
                Positions = [new(0f, 0f, 0f), new(0f, 5f, 0f), new(0f, 5f, -8f)],
                AntiTunnelBytecode = bytecode,
            }.Reconstruct();

            var withBytecode = KVObject.Array();
            ClothExtract.AddClothAntiTunnelGroup(withBytecode, Model(131072, 805306368, 2, 131073, 196609), ["spine_2_clothCapsule"],
                ["coattail_0_L", "coattail_0_L"]);
            var withoutBytecode = KVObject.Array();
            ClothExtract.AddClothAntiTunnelGroup(withoutBytecode, Model(), ["spine_2_clothCapsule"], ["coattail_0_L"]);

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
            var cloth = new FeModelBuilder
            {
                Names = ["spine_2", "$cloth_node_hinge_n0", "$cloth_node_hinge_n1", "$cloth_node_hinge_n2", "coattail_0_L", "coattail_1_L",
                    "coattail_2_L"],
                StaticNodes = 3,
                InvMasses = [0f, 0f, 0f, 1f, 0f, 1f, 1f],
                Parents = [-1, 0, 0, 0, -1, 4, 5],
                Positions = [new(0f, 0f, 0f), new(0f, 0f, -4f), new(0f, 0f, -8f), new(0f, 0f, -12f), new(0f, 5f, 0f), new(0f, 5f, -8f),
                    new(0f, 5f, -16f)],
                KelagerBends =
                [
                    KelagerBend(1, 2, 3, 1.652419f, [-0f, 1f, 2f]),
                    KelagerBend(1, 2, 3, 2.981424f, [-0f, 1f, 2f]),
                    KelagerBend(5, 4, 6, 0.5f, [-2f, 1f, 1f]),
                ],
            }.Reconstruct();

            var softbodyChildren = KVObject.Array();
            ClothExtract.AddClothStiffHinges(softbodyChildren, cloth);
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
            var local = WindInLocalSpace(0.469f);
            var world = WindInLocalSpace(0f);
            var maps = new HashSet<string>();
            var node = ClothExtract.MakeClothEffect(local, local.Fe.Effects.First(), maps);
            var plain = ClothExtract.MakeClothEffect(world, world.Fe.Effects.First(), maps);

            using (Assert.Multiple())
            {
                await Assert.That(node!.GetFloatProperty("local_space")).IsEqualTo(0.469f).Within(1e-6f);
                await Assert.That(plain!.ContainsKey("local_space")).IsFalse();
            }
        }

        private static ClothReconstruction WindInLocalSpace(float localSpace) => (LoneStaticNode with
        {
            Effects =
            [
                Effect("ClothEffectWind", 2353092209, 1, Object(("Strength", Floats(-281.600006f, 0f, 0f)), ("AirToCloth", 0.5f),
                    ("LocalSpace", localSpace), ("Choppiness", 8f),
                    ("Vortices", KVObject.Array([Object(("MaxSpeed", 211.199997f), ("MaxCell", 1658.880005f))])))),
            ],
        }).Reconstruct();

        /// <summary>
        /// An effect names a selection the document declares in a <c>ClothVertexMap</c> or a joint's <c>vertex_map</c>;
        /// one declared nowhere is not named.
        /// </summary>
        [Test]
        public async Task AnEffectNamesASelectionTheDocumentDeclares()
        {
            var cloth = StiffenOnSelection();

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
                ClothExtract.AddClothEffects(document, cloth, new HashSet<string>());
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

        private static ClothReconstruction StiffenOnSelection() => new FeModelBuilder
        {
            NodeCount = 2,
            StaticNodes = 1,
            InvMasses = [0f, 1f],
            Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f)],
            VertexMaps = [VertexMap("sail_vm", 7, 0, 1, 1)],
            VertexMapValues = [255],
            Effects = [Effect("stiffen0", 1, 3, Object(("Stiffness", 1f), ("VertexMap", 7)))],
        }.Reconstruct();

        /// <summary>
        /// Vortices compiled at zero speed read <c>time_multiplier</c> 0 with a positive vortex speed; moving vortices
        /// keep 1.
        /// </summary>
        [Test]
        public async Task VorticesCompiledAtZeroSpeedAreAZeroTimeMultiplier()
        {
            var moving = WindVortexEffect(new(70.400002f, 0f, 0f), 1f, 123.200005f);
            var stilled = WindVortexEffect(new(-0f, -0f, -0f), 0f, 0f);
            var maps = new HashSet<string>();
            var movingNode = ClothExtract.MakeClothEffect(moving, moving.Fe.Effects.First(), maps)!;
            var stilledNode = ClothExtract.MakeClothEffect(stilled, stilled.Fe.Effects.First(), maps)!;

            using (Assert.Multiple())
            {
                await Assert.That(movingNode.GetFloatProperty("time_multiplier")).IsEqualTo(1f);
                await Assert.That(movingNode.GetFloatProperty("vortex_max_speed_mph")).IsEqualTo(7f).Within(1e-4f);

                await Assert.That(stilledNode.GetFloatProperty("time_multiplier")).IsEqualTo(0f);
                await Assert.That(stilledNode.GetFloatProperty("vortex_max_speed_mph")).IsGreaterThan(0f);
                await Assert.That(stilledNode.GetInt32Property("vortex_count")).IsEqualTo(2);
            }
        }

        private static ClothReconstruction WindVortexEffect(Vector3 strength, float choppiness, float maxSpeed) => (LoneStaticNode with
        {
            Effects =
            [
                Effect("wind0", 1456486, 1, Object(("Strength", Floats(strength.X, strength.Y, strength.Z)), ("AirToCloth", 0.249439f),
                    ("Choppiness", choppiness),
                    ("Vortices", KVObject.Array([Object(("MaxSpeed", maxSpeed), ("MaxCell", 80f)),
                        Object(("MaxSpeed", maxSpeed), ("MaxCell", 80f))])))),
            ],
        }).Reconstruct();
    }
}
