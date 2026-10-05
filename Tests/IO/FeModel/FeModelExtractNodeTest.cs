using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static Tests.IO.FeModelBuilder;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace Tests.IO
{
    /// <summary>Declaring cloth nodes, jiggle bones, collision shapes and the cloth skeleton.</summary>
    public class FeModelExtractNodeTest : FeModelTestModels
    {
        /// <summary>
        /// A <c>leader_type</c> 1 follower names the known bone whose string token is its parent hash; an unmatched
        /// hash declares nothing.
        /// </summary>
        [Test]
        public async Task ABoneMergeFollowerNamesTheBoneWhoseTokenIsItsParentHash()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["coattail_0_L", "coattail_1_L", "coattail_2_L"],
                StaticNodes = 1,
                Parents = [-1, 0, 1],
                Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
                BoneMergeLinks = [new(ValveResourceFormat.Utils.StringToken.Get("spine_2"), 2), new(12345, 1)],
            }.Reconstruct();
            cloth = WithSkeleton(cloth, boneNames: ["pelvis", "spine_2", "coattail_0_L", "coattail_1_L", "coattail_2_L"]);
            var children = KVObject.Array();
            ClothExtract.AddClothFollowBones(children, cloth,
                new HashSet<string>(["coattail_1_L", "coattail_2_L"], StringComparer.OrdinalIgnoreCase));

            await Assert.That(children.Count).IsEqualTo(1);
            var follow = children.ElementAt(0).Value;

            using (Assert.Multiple())
            {
                await Assert.That(follow.GetInt32Property("leader_type")).IsEqualTo(1);
                await Assert.That(follow.GetStringProperty("leader_bone")).IsEqualTo("spine_2");
                await Assert.That(follow.GetStringProperty("follower_bone")).IsEqualTo("coattail_2_L");
            }
        }

        /// <summary>
        /// <c>stiffness_on_ragdoll</c> and <c>cloth_sleep_enabled</c> come back from the model's key values, and
        /// neither is declared without them.
        /// </summary>
        [Test]
        public async Task SoftbodyKeysComeBackFromTheModelKeyValues()
        {
            var keyValues = KVObject.Collection();
            keyValues.Add("cloth_stiffness_on_ragdoll", 0.5f);
            keyValues.Add("cloth_sleep_enabled", true);
            var softbody = KVObject.Collection();
            ClothExtract.AddSoftbodyModelKeyValues(softbody, keyValues);
            var bare = KVObject.Collection();
            ClothExtract.AddSoftbodyModelKeyValues(bare, KVObject.Collection());

            using (Assert.Multiple())
            {
                await Assert.That(softbody.GetFloatProperty("stiffness_on_ragdoll")).IsEqualTo(0.5f);
                await Assert.That(softbody.GetBooleanProperty("cloth_sleep_enabled")).IsTrue();
                await Assert.That(bare.Count).IsEqualTo(0);
            }
        }

        /// <summary>
        /// Capsules sorted into priority groups are declared in their parent bones' node order; without groups the
        /// array order is kept.
        /// </summary>
        [Test]
        public async Task ShapesSortedIntoPriorityGroupsAreDeclaredInTheirParentBonesOrder()
        {
            var grouped = ClothExtract.AddClothCollisionShapes(KVObject.Array(), (PriorityCapsules with
            {
                RigidColliderPriorities = [new(0, 0, 0, 0, 0, null), new(1, 0, 0, 0, 0, null), new(2, 0, 0, 0, 0, null)],
            }).Reconstruct());
            var ungrouped = ClothExtract.AddClothCollisionShapes(KVObject.Array(), PriorityCapsules.Reconstruct());

            using (Assert.Multiple())
            {
                await Assert.That(grouped).IsEquivalentTo(DeclaredCapsules, CollectionOrdering.Matching);
                await Assert.That(ungrouped).IsEquivalentTo(ArrayOrderCapsules, CollectionOrdering.Matching);
            }
        }

        private static readonly string[] DeclaredCapsules = ["spine_2_clothCapsule", "pelvis_clothCapsule"];

        private static readonly string[] ArrayOrderCapsules = ["pelvis_clothCapsule", "spine_2_clothCapsule"];

        /// <summary>Three static bones, pelvis and spine_2 each parenting a capsule, with no priority groups.</summary>
        private static FeModelBuilder PriorityCapsules => new()
        {
            Names = ["spine_2", "pelvis", "coattail_0_L"],
            StaticNodes = 3,
            Parents = [-1, -1, -1],
            Positions = [new(0f, 0f, 40f), new(0f, 0f, 30f), new(-8f, 4f, 65f)],
            TaperedCapsuleRigids = [Capsule(1), Capsule(0)],
            RigidColliderPriorities = [],
        };

        /// <summary>A capsule of radius 4 along Z on <paramref name="node"/>, with collision mask 15 and no vertex map.</summary>
        private static FeTaperedCapsuleRigid Capsule(int node) => new([new(0f, 0f, -4f, 4f), new(0f, 0f, 4f, 4f)], node, 15, 65535, 0, null);

        /// <summary>
        /// The exported skeleton puts every cloth control bone on its <c>m_InitPose</c> position, where the compiled
        /// bone table alone does not.
        /// </summary>
        [Test]
        public async Task AClothControlBoneIsEmittedOnItsRecordedRestPosition()
        {
            var (corrected, compiled) = RestPoseDistances("sw_donkey_10th_anniversary_kv3_v3_zstd");

            using (Assert.Multiple())
            {
                await Assert.That(corrected).IsLessThan(1e-4f);
                await Assert.That(compiled).IsGreaterThan(1e-3f);
            }
        }

        /// <summary>
        /// A model with no cloth registers no rest-position correction.
        /// </summary>
        [Test]
        public async Task AModelWithoutClothEmitsItsCompiledSkeleton()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "townsfolk_03.vmdl_c"));
            var extract = new ModelExtract(resource, new NullFileLoader());

            using (Assert.Multiple())
            {
                await Assert.That(((Model)resource.DataBlock!).Skeleton.Bones.Length).IsEqualTo(56);
                await Assert.That(extract.Cloth.RestBonePositions.Count).IsEqualTo(0);
                await Assert.That(extract.Cloth.ProxyRestBonePositions.Count).IsEqualTo(0);
            }
        }

        /// <summary>
        /// The worst distance between a real cloth control bone's <c>m_InitPose</c> and the world position
        /// its emitted joint chain accumulates to, with the rest-pose correction and with the compiled
        /// bone transforms.
        /// </summary>
        private static (float Corrected, float Compiled) RestPoseDistances(string modelName)
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", modelName + ".vmdl_c"));
            var model = (Model)resource.DataBlock!;
            var extract = new ModelExtract(resource, new NullFileLoader());
            var cloth = new ClothReconstruction(model.GetEmbeddedPhys()!.FeModel!);

            var targets = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            for (var node = 0; node < cloth.Fe.CtrlName.Length && node < cloth.Index.InitPosePositions.Length; node++)
            {
                var name = cloth.Fe.CtrlName[node];
                if (!string.IsNullOrEmpty(name) && !cloth.IsGeneratedNodeName(name))
                {
                    targets.TryAdd(name, cloth.Index.InitPosePositions[node]);
                }
            }

            var corrected = 0f;
            var compiled = 0f;
            void Walk(Bone bone, Vector3 correctedParent, Vector3 compiledParent, Quaternion parentRotation)
            {
                var here = correctedParent
                    + Vector3.Transform(ModelExtract.BonePosition(bone, extract.Cloth.RestBonePositions), parentRotation);
                var asCompiled = compiledParent + Vector3.Transform(bone.Position, parentRotation);
                var rotation = parentRotation * bone.Angle;

                if (targets.TryGetValue(bone.Name, out var target))
                {
                    corrected = Math.Max(corrected, Vector3.Distance(here, target));
                    compiled = Math.Max(compiled, Vector3.Distance(asCompiled, target));
                }

                foreach (var child in bone.Children)
                {
                    Walk(child, here, asCompiled, rotation);
                }
            }

            foreach (var root in model.Skeleton.Roots)
            {
                Walk(root, Vector3.Zero, Vector3.Zero, Quaternion.Identity);
            }

            return (corrected, compiled);
        }

        /// <summary>
        /// A rotation-locked static ClothNode declares its node-base preset: <c>transform_alignment</c> 3 when X0 and a
        /// Y reference are the node itself, otherwise 4 with the stored references; a free static node keeps 0.
        /// </summary>
        [Test]
        public async Task AStaticClothNodeDeclaresThePresetItsNodeBaseCompiledFrom()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["spine", "hip", "pin", "a", "b", "c", "d"],
                StaticNodes = 3,
                RotLockStaticNodes = 2,
                Positions = [new(0f, 0f, 60f), new(0f, 0f, 40f), new(0f, 5f, 40f), new(-4f, 4f, 55f), new(-4f, -4f, 55f),
                    new(-6f, 4f, 45f), new(-6f, -4f, 45f)],
                Rods = [RigidRod(2, 3, 15.56f), RigidRod(2, 4, 17.94f)],
                NodeBases = [NodeBase(0, 0, 3, 4, 0), NodeBase(1, 3, 4, 6, 5), NodeBase(2, 2, 3, 2, 4), NodeBase(3, 4, 3, 3, 5)],
            }.Reconstruct();

            var spine = ClothExtract.MakeClothNode(cloth, "spine", 0, isStaticNode: true);
            var hip = ClothExtract.MakeClothNode(cloth, "hip", 1, isStaticNode: true);
            var pin = ClothExtract.MakeClothNode(cloth, "pin", 2, isStaticNode: true);
            var crossed = cloth.ClothNodeBasisPreset(3);

            using (Assert.Multiple())
            {
                await Assert.That(spine.GetInt32Property("transform_alignment")).IsEqualTo(3);
                await Assert.That(spine.GetStringProperty("node_base_x0")).IsEqualTo(string.Empty);
                await Assert.That(spine.GetStringProperty("node_base_x1")).IsEqualTo("a");
                await Assert.That(spine.GetStringProperty("node_base_y0")).IsEqualTo(string.Empty);
                await Assert.That(spine.GetStringProperty("node_base_y1")).IsEqualTo("b");
                await Assert.That(hip.GetInt32Property("transform_alignment")).IsEqualTo(4);
                await Assert.That(hip.GetStringProperty("node_base_x0")).IsEqualTo("a");
                await Assert.That(hip.GetStringProperty("node_base_x1")).IsEqualTo("b");
                await Assert.That(hip.GetStringProperty("node_base_y0")).IsEqualTo("d");
                await Assert.That(hip.GetStringProperty("node_base_y1")).IsEqualTo("c");
                await Assert.That(pin.GetInt32Property("transform_alignment")).IsEqualTo(0);
                await Assert.That(crossed?.TransformAlignment).IsEqualTo(4);
                await Assert.That(crossed?.References).IsEqualTo(new FeModelIndex.NodeBasis(4, 3, 3, 5));
                await Assert.That(cloth.ClothNodeBasisPreset(5) is null).IsTrue();
            }
        }

        /// <summary>
        /// A rotated <c>$cloth_node_</c> element gets its <c>angles</c> back relative to its bone with a zero origin,
        /// and its effects are declared in its frame; an unrotated element keeps angles 0 and an offset origin.
        /// </summary>
        [Test]
        public async Task ARotatedClothNodeGetsItsAnglesBackAndItsEffectsInItsFrame()
        {
            var bone = Quaternion.Normalize(new Quaternion(-0.435361f, -0.55719f, -0.55719f, 0.435361f));
            var turned = bone * EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(0f, 90f, 0f));
            var cloth = new FeModelBuilder
            {
                Names = ["spine_2", "$cloth_node_node_spine", "$cloth_node_node_flat", "pelvis"],
                StaticNodes = 4,
                Parents = [-1, 0, 0, -1],
                Poses = [new(Vector3.Zero, 1f, bone), new(Vector3.Zero, 1f, turned), new(Vector3.Zero, 1f, bone), Pose(0f, 0f, -5f)],
                CtrlOffsets = [Offset(0, 1, 0f, 0f, 0f), Offset(0, 2, 0f, 0f, 0f)],
                Effects = [Gravity("gravity0", 1, 0, -0.353553f, 0.353553f, 0f), Gravity("gravity1", 2, 3, -0.353553f, 0.353553f, 0f)],
            }.Reconstruct();
            var anchors = cloth.Fe.CtrlOffsets.ToDictionary(static offset => offset.CtrlChild);
            var rotatedFound = ClothExtract.TryResolveClothNodeAnchor(cloth, anchors, 1, out var rotated);
            var flatFound = ClothExtract.TryResolveClothNodeAnchor(cloth, anchors, 2, out var flat);
            var (rotatedRoot, rotatedOrigin, rotatedAngles) = rotated;
            var (_, flatOrigin, flatAngles) = flat;
            var element = ClothExtract.MakeClothNode(cloth, rotatedRoot, 1, isStaticNode: true, elementName: "node_spine",
                origin: rotatedOrigin, angles: rotatedAngles);

            using (Assert.Multiple())
            {
                await Assert.That(rotatedFound).IsTrue();
                await Assert.That(rotatedRoot).IsEqualTo("spine_2");
                await Assert.That(Vector3.Distance(rotatedAngles, new Vector3(0f, 90f, 0f))).IsLessThan(1e-3f);
                await Assert.That(rotatedOrigin).IsEqualTo(Vector3.Zero);
                await Assert.That(Vector3.Distance(element.GetSubCollection("angles").ToVector3(), new Vector3(0f, 90f, 0f)))
                    .IsLessThan(1e-3f);
                await Assert.That(flatFound).IsTrue();
                await Assert.That(flatAngles).IsEqualTo(Vector3.Zero);
                await Assert.That(flatOrigin.Length()).IsGreaterThan(1e-3f);
            }

            static KVObject StaticNode(string name, string root, float yaw) => KVHelpers.MakeNode("ClothNode",
                ("name", name), ("cloth_node_root_bone", root), ("is_static_node", true),
                ("angles", KVHelpers.ToKVArray(new Vector3(0f, yaw, 0f))));
            var softbodyChildren = KVObject.Array();
            softbodyChildren.Add(StaticNode("spine_2", "spine_2", 0f));
            softbodyChildren.Add(StaticNode("node_spine", "spine_2", 90f));
            softbodyChildren.Add(StaticNode("pelvis", "pelvis", 0f));

            ClothExtract.AddClothEffects(softbodyChildren, cloth, new HashSet<string>());

            var nodes = softbodyChildren.Select(static child => child.Value).ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(nodes.Length).IsEqualTo(3);
                await Assert.That(nodes[0].ContainsKey("children")).IsFalse();
                await Assert.That(nodes[1].GetArray("children")[0].GetStringProperty("name")).IsEqualTo("gravity0");
                await Assert.That(Vector3.Distance(nodes[1].GetArray("children")[0].GetSubCollection("angles").ToVector3(),
                    new Vector3(0f, 45f, 0f))).IsLessThan(1e-3f);
                await Assert.That(nodes[2].GetArray("children")[0].GetStringProperty("name")).IsEqualTo("gravity1");
                await Assert.That(Vector3.Distance(nodes[2].GetArray("children")[0].GetSubCollection("angles").ToVector3(),
                    new Vector3(0f, 135f, 0f))).IsLessThan(1e-3f);
            }
        }

        /// <summary>
        /// A colliding jiggle bone declares false for each <c>cloth_collision_layer</c> its mask leaves out; a full
        /// mask or a non-colliding bone declares no layers.
        /// </summary>
        [Test]
        public async Task AJiggleBoneDeclaresTheCollisionLayersItsMaskLeavesOut()
        {
            static KVObject? Declare(uint flags, int mask) => ClothExtract.ProcessJiggleBone(
                new FeModel.FeIndexedJiggleBone(0, -1, default(FeModel.FeJiggleBone) with { Flags = flags, Length = 5f, CollisionMask = mask }),
                ["tophat"]);

            var leftOut = Declare(802, 13)!;
            var allLayers = Declare(802, 15)!;
            var noCollision = Declare(0x22, 0)!;

            using (Assert.Multiple())
            {
                await Assert.That(leftOut.GetBooleanProperty("has_collision")).IsTrue();
                await Assert.That(leftOut.GetBooleanProperty("cloth_collision_layer0")).IsTrue();
                await Assert.That(leftOut.GetBooleanProperty("cloth_collision_layer1")).IsFalse();
                await Assert.That(leftOut.GetBooleanProperty("cloth_collision_layer2")).IsTrue();
                await Assert.That(leftOut.GetBooleanProperty("cloth_collision_layer3")).IsTrue();
                await Assert.That(allLayers.ContainsKey("cloth_collision_layer1")).IsFalse();
                await Assert.That(noCollision.GetBooleanProperty("has_collision")).IsFalse();
                await Assert.That(noCollision.ContainsKey("cloth_collision_layer0")).IsFalse();
            }
        }

        /// <summary>
        /// A jiggle-bone model with nonzero extra iterations keeps its ClothParams; zero counts or no jiggle bone do
        /// not.
        /// </summary>
        [Test]
        public async Task AJiggleBoneModelKeepsTheClothParamsItsIterationCountsRecord()
        {
            static ClothReconstruction Model(int extraIterations, params FeIndexedJiggleBone[] jiggleBones) => new FeModelBuilder
            {
                Names = ["tophat"],
                Parents = [-1],
                Positions = [new(0f, 0f, 60f)],
                ExtraIterations = extraIterations,
                ExtraGoalIterations = extraIterations,
                JiggleBones = jiggleBones,
            }.Reconstruct();

            using (Assert.Multiple())
            {
                await Assert.That(ClothExtract.HasJiggleBoneClothParams(Model(1, TophatJiggle))).IsTrue();
                await Assert.That(ClothExtract.HasJiggleBoneClothParams(Model(0, TophatJiggle))).IsFalse();
                await Assert.That(ClothExtract.HasJiggleBoneClothParams(Model(1))).IsFalse();
            }
        }

        /// <summary>
        /// A simulated bone in the unnamed vertex set with a jiggle bone is left to the jiggle bone; without one it
        /// stays a lone cloth node.
        /// </summary>
        [Test]
        public async Task ABoneOnlyItsJiggleBoneDeclaresIsNotALoneClothNode()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["tophat", "tail"],
                Parents = [-1, -1],
                Positions = [new(0f, 0f, 60f), new(10f, 0f, 40f)],
                VertexSetNames = [0, 2103756403],
                DynNodeVertexSet = [0, 1],
                JiggleBones = [TophatJiggle],
            }.Reconstruct();

            using (Assert.Multiple())
            {
                await Assert.That(ClothExtract.IsDeclaredByItsJiggleBone(cloth, 0)).IsTrue();
                await Assert.That(ClothExtract.IsDeclaredByItsJiggleBone(cloth, 1)).IsFalse();
            }
        }

        /// <summary>A jiggle bone on node 0 of length 5.</summary>
        private static FeIndexedJiggleBone TophatJiggle => new(0, 0, default(FeJiggleBone) with { Flags = 38, Length = 5f });

        /// <summary>
        /// A lone simulated root whose stray radius is relaxed to zero is a <c>ClothNode</c>; a partly relaxed record,
        /// no record, or a control-node ancestor keeps a <c>ClothChain</c>.
        /// </summary>
        [Test]
        public async Task AStrayRecordRelaxedToZeroIsALoneClothNode()
        {
            static string[] Classes(ClothReconstruction cloth, Func<string, bool> hasControlAncestor)
            {
                var clothChildren = KVObject.Array();
                ClothExtract.AddFreeClothNodesAndSprings(clothChildren, KVObject.Array(), cloth, [], static _ => true, [],
                    bareStaticReparented: hasControlAncestor);
                return [.. clothChildren.Select(static child => child.Value.GetStringProperty("_class"))];
            }

            var cloth = new FeModelBuilder
            {
                Names = ["spine_2", "tail", "ear"],
                Parents = [-1, -1, -1],
                Positions = [new(0f, 0f, 60f), new(10f, 0f, 40f), new(-10f, 0f, 40f)],
                AnimStrayRadii = [new([0, 0], 7f, 0f), new([1, 1], 7f, 0.25f)],
            }.Reconstruct();

            using (Assert.Multiple())
            {
                await Assert.That(Classes(cloth, static _ => false)).IsEquivalentTo(["ClothNode", "ClothChain", "ClothChain"]);
                await Assert.That(Classes(cloth, static _ => true)).IsEquivalentTo(["ClothChain", "ClothChain", "ClothChain"]);
            }
        }

        /// <summary>
        /// A static root in <c>m_LockToGoal</c> is declared as a one-joint chain; an unlocked root or a free node stays
        /// a ClothNode.
        /// </summary>
        [Test]
        public async Task AStaticRootTheOriginalLocksToItsGoalIsASingleJointChain()
        {
            static ClothReconstruction Model(params int[] locks) => new FeModelBuilder
            {
                Names = ["tophat", "$cloth_node_sb_tw_end"],
                StaticNodes = 1,
                RotLockStaticNodes = 0,
                InvMasses = [0f, 312.5f],
                Parents = [-1, 0],
                Positions = [new(0f, 0f, 0f), new(0f, 0f, -4f)],
                LockToGoal = locks,
            }.Reconstruct();

            var locked = Model(0);
            var free = Model();

            using (Assert.Multiple())
            {
                await Assert.That(ClothExtract.LoneNodeIsJointChain(locked, 0, bareStatic: false, bareStaticReparented: false)).IsTrue();
                await Assert.That(ClothExtract.LoneNodeIsJointChain(free, 0, bareStatic: false, bareStaticReparented: false)).IsFalse();
                await Assert.That(ClothExtract.LoneNodeIsJointChain(free, 1, bareStatic: false, bareStaticReparented: false)).IsFalse();
            }
        }

        /// <summary>
        /// A capsule parent bone at the ClothNode goal and gravity defaults is declared as a bare static
        /// <c>ClothNode</c>; other goal values, bones already declared or chain joints, and bones no shape names are
        /// not.
        /// </summary>
        [Test]
        public async Task AShapeParentAtTheClothNodeDefaultsIsDeclaredABareStaticClothNode()
        {
            var cloth = ShapeParentIntegrators.Reconstruct();
            var (folder, folderChildren) = KVHelpers.MakeListNode("Folder");
            folderChildren.Add(EffectParentNode("head", isStatic: true));
            var joint = KVObject.Collection();
            joint.Add("joint_name", "neck_0");
            var joints = KVObject.Array();
            joints.Add(joint);
            var chain = KVObject.Collection();
            chain.Add("joints", joints);
            var softbodyChildren = KVObject.Array();
            softbodyChildren.Add(folder);
            softbodyChildren.Add(KVHelpers.MakeNode("ClothChain", ("name", "neck_0"), ("root_bone", "neck_0"), ("chain", chain)));

            ClothExtract.AddShapeParentDefaultClothNodes(softbodyChildren, cloth);

            var added = softbodyChildren.Select(static child => child.Value).Skip(2).ToArray();
            var bones = added.Select(static node => node.GetStringProperty("cloth_node_root_bone")).ToArray();
            string[] declaredBones = ["pelvis"];
            string[] undeclaredBones = ["spine_2", "clavicle_L", "head", "neck_0", "hand_R"];

            using (Assert.Multiple())
            {
                await Assert.That(bones).IsEquivalentTo(declaredBones, CollectionOrdering.Matching);
                await Assert.That(added.Select(static node => node.GetStringProperty("name")).ToArray())
                    .IsEquivalentTo(declaredBones, CollectionOrdering.Matching);
                await Assert.That(added.Count(static node => node.GetStringProperty("_class") == "ClothNode"
                    && node.GetBooleanProperty("is_static_node") && !node.ContainsKey("goal_strength"))).IsEqualTo(1);

                await Assert.That(bones.Intersect(undeclaredBones).Count()).IsEqualTo(0);
                await Assert.That(added.Count(static node => node.ContainsKey("goal_strength"))).IsEqualTo(0);
                await Assert.That(folderChildren.Count).IsEqualTo(1);
            }
        }

        /// <summary>
        /// Six static bones, five parenting a capsule: pelvis, head, neck_0 and hand_R at the ClothNode goal defaults,
        /// spine_2 at no goal and clavicle_L at other goal values.
        /// </summary>
        private static FeModelBuilder ShapeParentIntegrators => new()
        {
            Names = ["pelvis", "spine_2", "clavicle_L", "head", "neck_0", "hand_R"],
            StaticNodes = 6,
            Parents = [-1, 0, 1, 1, 1, 2],
            Positions = [new(0f, 0f, 30f), new(0f, 0f, 40f), new(4f, 0f, 50f), new(0f, 0f, 60f), new(0f, 0f, 55f), new(8f, 0f, 45f)],
            NodeIntegrator =
            [
                new(0f, 0.216f, 0.797273f, 360f),
                new(0f, 0f, 0f, 360f),
                new(0f, 0.125f, 0.173846f, 360f),
                new(0f, 0.216f, 0.797273f, 360f),
                new(0f, 0.216f, 0.797273f, 360f),
                new(0f, 0.216f, 0.797273f, 360f),
            ],
            TaperedCapsuleRigids = [Capsule(0), Capsule(1), Capsule(2), Capsule(3), Capsule(4)],
            RigidColliderPriorities = [],
        };

        /// <summary>
        /// Contesting planarized shapes are declared smallest first: the five-plane sphere precedes the six-plane one.
        /// </summary>
        [Test]
        public async Task ThePlanarizedShapeOwningFewestPlanesIsDeclaredFirst()
        {
            var order = ClothExtract.PlanarizedShapesInClaimOrder(TwoPlanarizedSpheres());

            using (Assert.Multiple())
            {
                await Assert.That(order.Count).IsEqualTo(2);
                await Assert.That(order[0].GetStringProperty("parent_bone")).IsEqualTo("boneB");
                await Assert.That(order[1].GetStringProperty("parent_bone")).IsEqualTo("boneA");
            }
        }

        /// <summary>A six-plane sphere on <c>boneA</c> and a five-plane one on <c>boneB</c>.</summary>
        private static ClothReconstruction TwoPlanarizedSpheres()
        {
            Vector3[] axes = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY,
                Vector3.UnitZ, -Vector3.UnitZ];
            var bigB = new Vector3(100f, 0f, 0f);
            List<Vector3> positions = [Vector3.Zero, bigB];
            List<FeCollisionPlane> planes = [];
            var node = 2;
            foreach (var axis in axes)
            {
                positions.Add(axis * 7f);
                planes.Add(SpherePlane(0, node++, axis));
            }

            foreach (var axis in axes.Take(5))
            {
                positions.Add(bigB + (axis * 7f));
                planes.Add(SpherePlane(1, node++, axis));
            }

            return new FeModelBuilder
            {
                Names = ["boneA", "boneB", .. Enumerable.Range(2, 11).Select(static i => $"n{i}")],
                Parents = [-1, -1, .. Enumerable.Repeat(0, 6), .. Enumerable.Repeat(1, 5)],
                Positions = [.. positions],
                CollisionPlanes = [.. planes],
                VertexMapValues = [.. Enumerable.Repeat((byte)255, 11)],
                VertexMaps = [VertexMap("setA", 1, 0, 2, 6), VertexMap("setB", 2, 6, 8, 5)],
            }.Reconstruct();
        }

        private static FeCollisionPlane SpherePlane(int parent, int node, Vector3 normal) => new(parent, node, new RnPlane(normal, 5f), 0f, 0f);

        /// <summary>
        /// A ClothNode on an <c>m_Ropes</c> run states alignment 2 for an element node and 1 for a bone node; nodes on
        /// no run, or a model with no ropes, keep 0.
        /// </summary>
        [Test]
        public async Task AClothNodeOnARopeRunStatesTheAlignmentThatLetsItRope()
        {
            var roped = RopeClothNodeModel([3, 0, 2], 1);
            var bare = RopeClothNodeModel([], 0);

            using (Assert.Multiple())
            {
                await Assert.That(RopeAlignments(bare)).IsEquivalentTo(NoRopeAlignments, CollectionOrdering.Matching);

                await Assert.That(RopeAlignments(roped)).IsEquivalentTo(RopedAlignments, CollectionOrdering.Matching);
            }
        }

        private static readonly int[] NoRopeAlignments = [0, 0, 0];

        private static readonly int[] RopedAlignments = [1, 0, 2];

        private static int[] RopeAlignments(ClothReconstruction cloth) =>
        [
            ClothExtract.MakeClothNode(cloth, "joint1", 0, isStaticNode: true).GetInt32Property("transform_alignment"),
            ClothExtract.MakeClothNode(cloth, "joint1", 1, elementName: "clothNode_joint2").GetInt32Property("transform_alignment"),
            ClothExtract.MakeClothNode(cloth, "joint1", 2, elementName: "clothNode_joint3").GetInt32Property("transform_alignment"),
        ];

        /// <summary>A static bone and two element nodes under it, the second on a rope to the bone.</summary>
        private static ClothReconstruction RopeClothNodeModel(int[] ropes, int ropeCount) => new FeModelBuilder
        {
            Names = ["joint1", "$cloth_node_clothNode_joint2", "$cloth_node_clothNode_joint3"],
            StaticNodes = 1,
            Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f), new(10f, 0f, -10f)],
            RopeCount = ropeCount,
            Ropes = ropes,
        }.Reconstruct();

        /// <summary>
        /// A proxy control bone turned from its bind rotation is written at its recorded rotation, its child turned
        /// back to keep its world rotation; a bone whose record agrees writes nothing.
        /// </summary>
        [Test]
        public async Task AProxyControlBoneIsWrittenAtItsRecordedRestRotation()
        {
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f);
            var root = new Bone(0, "root", Vector3.Zero, Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            var cape = new Bone(1, "cape", new Vector3(0f, 0f, -10f), Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            var tip = new Bone(2, "tip", new Vector3(0f, 0f, -10f), Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            cape.SetParent(root);
            tip.SetParent(cape);

            var into = new Dictionary<string, Quaternion>(StringComparer.OrdinalIgnoreCase);
            var turned = ClothReconstruction.ProxyRestRotations([root],
                new Dictionary<string, Quaternion> { ["root"] = Quaternion.Identity, ["cape"] = turn }, into);

            using (Assert.Multiple())
            {
                await Assert.That(into.Keys.Order()).IsEquivalentTo(["cape", "tip"]);
                await Assert.That(MathF.Abs(Quaternion.Dot(turned.GetValueOrDefault("cape"), turn))).IsEqualTo(1f).Within(1e-6f);
                await Assert.That(MathF.Abs(Quaternion.Dot(turn * into.GetValueOrDefault("tip"), Quaternion.Identity)))
                    .IsEqualTo(1f).Within(1e-6f);

                await Assert.That(into.ContainsKey("root")).IsFalse();
            }
        }

        /// <summary>
        /// Proxy control bones are written at their recorded rest positions at any distance; bones already there or
        /// without a record keep their offsets.
        /// </summary>
        [Test]
        public async Task ALoneProxyControlBoneFarFromItsBindPoseIsWrittenAtItsRestPosition()
        {
            var root = new Bone(0, "root", Vector3.Zero, Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            var cape = new Bone(1, "cape", new Vector3(0f, 0f, -10f), Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            var tip = new Bone(2, "tip", new Vector3(0f, 0f, -10f), Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            var tail = new Bone(3, "tail", new Vector3(5f, 0f, 0f), Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            cape.SetParent(root);
            tip.SetParent(cape);
            tail.SetParent(root);

            var into = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            ClothReconstruction.ProxyRestPositions([root], new Dictionary<string, Vector3>
            {
                ["root"] = Vector3.Zero,
                ["cape"] = new Vector3(0f, 0f, -10.5f),
                ["tail"] = new Vector3(5f, 0f, -7.5f),
            }, new Dictionary<string, Quaternion>(), into);

            using (Assert.Multiple())
            {
                await Assert.That(into.GetValueOrDefault("tail")).IsEqualTo(new Vector3(5f, 0f, -7.5f));

                await Assert.That(into.GetValueOrDefault("cape")).IsEqualTo(new Vector3(0f, 0f, -10.5f));
                await Assert.That(into.ContainsKey("root")).IsFalse();
                await Assert.That(into.ContainsKey("tip")).IsFalse();
            }
        }

        /// <summary>
        /// A culled cloth bone is nested under its compiled parent's DMX joint in that joint's frame; without compiled
        /// parents, or with a parent that is no joint, it stays a root joint.
        /// </summary>
        [Test]
        public async Task ACulledClothBoneIsNestedUnderItsCompiledParentJoint()
        {
            static (DmeModel Model, DmeJoint Parent, DmeJoint Culled) Append(int[]? parents, string parentName)
            {
                var cloth = new FeModelBuilder
                {
                    Names = ["root", parentName, "collar_1"],
                    StaticNodes = 3,
                    Parents = parents,
                    Positions = [new(0f, 0f, 10f), new(0f, 5f, 10f), new(0f, 9f, 10f)],
                }.Reconstruct();

                var dmeModel = new DmeModel();
                var root = new DmeJoint { Name = "root" };
                root.Transform.Position = new Vector3(0f, 0f, 10f);
                root.Transform.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
                var parent = new DmeJoint { Name = "collar_0" };
                parent.Transform.Position = new Vector3(5f, 0f, 0f);
                root.Children.Add(parent);
                dmeModel.Children.Add(root);
                dmeModel.JointList.Add(root);
                dmeModel.JointList.Add(parent);

                var boneIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["root"] = 0, ["collar_0"] = 1 };
                ClothExtract.AppendCulledClothBoneJoints(dmeModel, boneIndexByName, cloth, [new(2, "collar_1")]);
                return (dmeModel, parent, dmeModel.JointList.OfType<DmeJoint>().Single(joint => joint.Name == "collar_1"));
            }

            var unparented = Append(null, "collar_0");
            var foreign = Append([-1, 0, 1], "ghost");
            var nested = Append([-1, 0, 1], "collar_0");

            using (Assert.Multiple())
            {
                await Assert.That(unparented.Model.Children.Select(static c => c.Name)).Contains("collar_1");
                await Assert.That(unparented.Culled.Transform.Position).IsEqualTo(new Vector3(0f, 9f, 10f));
                await Assert.That(foreign.Model.Children.Select(static c => c.Name)).Contains("collar_1");
                await Assert.That(foreign.Parent.Children.Count).IsEqualTo(0);

                await Assert.That(nested.Model.Children.Select(static c => c.Name)).DoesNotContain("collar_1");
                await Assert.That(nested.Parent.Children.Select(static c => c.Name)).IsEquivalentTo(["collar_1"]);
                await Assert.That(Vector3.Distance(nested.Culled.Transform.Position, new Vector3(4f, 0f, 0f))).IsLessThan(1e-4f);
            }
        }

        /// <summary>
        /// A proxy joint whose compiled parent is another joint but not a DAG ancestor is moved under it at the same
        /// model-space transform; no compiled parents, an ancestor parent, or a descendant parent move nothing.
        /// </summary>
        [Test]
        public async Task AProxyJointIsNestedUnderItsCompiledParentJoint()
        {
            static (DmeModel Model, DmeJoint Pelvis, DmeJoint Upper, DmeJoint Lower) Nest(int[]? parents)
            {
                var cloth = new FeModelBuilder
                {
                    Names = ["pelvis", "leg_upper", "leg_lower"],
                    StaticNodes = 3,
                    Parents = parents,
                    Positions = [new(0f, 0f, 40f), new(0f, 5f, 38f), new(0f, 5f, 18f)],
                }.Reconstruct();

                var dmeModel = new DmeModel();
                var pelvis = new DmeJoint { Name = "pelvis" };
                pelvis.Transform.Position = new Vector3(0f, 0f, 40f);
                pelvis.Transform.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
                var upper = new DmeJoint { Name = "leg_upper" };
                upper.Transform.Position = new Vector3(0f, -2f, 5f);
                var lower = new DmeJoint { Name = "leg_lower" };
                lower.Transform.Position = new Vector3(0f, -22f, 5f);
                pelvis.Children.Add(upper);
                pelvis.Children.Add(lower);
                dmeModel.Children.Add(pelvis);
                dmeModel.JointList.Add(pelvis);
                dmeModel.JointList.Add(upper);
                dmeModel.JointList.Add(lower);

                ClothExtract.NestProxyJointsUnderCompiledParents(dmeModel, cloth);
                return (dmeModel, pelvis, upper, lower);
            }

            var unparented = Nest(null);
            var ancestral = Nest([-1, 0, 0]);
            var cyclic = Nest([2, 0, 0]);
            var nested = Nest([-1, 0, 1]);

            using (Assert.Multiple())
            {
                await Assert.That(unparented.Pelvis.Children.Select(static c => c.Name)).IsEquivalentTo(["leg_upper", "leg_lower"]);
                await Assert.That(ancestral.Pelvis.Children.Select(static c => c.Name)).IsEquivalentTo(["leg_upper", "leg_lower"]);
                await Assert.That(cyclic.Model.Children.Select(static c => c.Name)).IsEquivalentTo(["pelvis"]);
                await Assert.That(cyclic.Pelvis.Children.Select(static c => c.Name)).IsEquivalentTo(["leg_upper", "leg_lower"]);

                await Assert.That(nested.Pelvis.Children.Select(static c => c.Name)).IsEquivalentTo(["leg_upper"]);
                await Assert.That(nested.Upper.Children.Select(static c => c.Name)).IsEquivalentTo(["leg_lower"]);
                await Assert.That(Vector3.Distance(nested.Lower.Transform.Position, new Vector3(0f, -20f, 0f))).IsLessThan(1e-4f);
            }
        }

        /// <summary>
        /// A proxy control bone 0.57 degrees off its bind rotation is turned onto its record; one 0.18 degrees off is
        /// not.
        /// </summary>
        [Test]
        public async Task AProxyControlBoneHalfADegreeOffItsBindRotationIsTurned()
        {
            var root = new Bone(0, "root", Vector3.Zero, Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            var neck = new Bone(1, "neck", new Vector3(0f, 0f, 10f), Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            var fish = new Bone(2, "fish", new Vector3(5f, 0f, 0f), Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            neck.SetParent(root);
            fish.SetParent(root);

            var into = new Dictionary<string, Quaternion>(StringComparer.OrdinalIgnoreCase);
            var turned = ClothReconstruction.ProxyRestRotations([root], new Dictionary<string, Quaternion>
            {
                ["root"] = Quaternion.Identity,
                ["neck"] = Quaternion.CreateFromAxisAngle(Vector3.UnitX, float.DegreesToRadians(0.5717f)),
                ["fish"] = Quaternion.CreateFromAxisAngle(Vector3.UnitX, float.DegreesToRadians(0.1835f)),
            }, into);

            using (Assert.Multiple())
            {
                await Assert.That(turned.Keys.Order()).IsEquivalentTo(["neck"]);

                await Assert.That(into.ContainsKey("fish")).IsFalse();
            }
        }

        /// <summary>
        /// A chain joint whose node base names a free cloth node is declared a <c>ClothNode</c> at alignment 3, with
        /// every joint a static ClothNode in node order; bases naming no free cloth node declare none.
        /// </summary>
        [Test]
        public async Task AChainJointBasedThroughAFreeClothNodeIsDeclaredAClothNode()
        {
            var chain = new BoneChain { RootBone = "a0" };
            chain.Joints.Add(new BoneChainJoint { Node = 2, Name = "a0", ParentNode = -1, InvMass = 1f });
            chain.Joints.Add(new BoneChainJoint { Node = 3, Name = "a1", ParentNode = 2, InvMass = 1f });
            chain.Joints.Add(new BoneChainJoint { Node = 4, Name = "a2", ParentNode = 3, InvMass = 1f });

            var viaClothNode = ClothExtract.ChainJointClothNodes(ChainOverFreeClothNode(1), [chain]).ToList();
            var viaBone = ClothExtract.ChainJointClothNodes(ChainOverFreeClothNode(0), [chain]).ToList();

            using (Assert.Multiple())
            {
                await Assert.That(viaClothNode.Select(static node => node.GetStringProperty("name")))
                    .IsEquivalentTo(["a0", "a1", "a2"], CollectionOrdering.Matching);
                await Assert.That(viaClothNode.Select(static node => node.GetStringProperty("cloth_node_root_bone")))
                    .IsEquivalentTo(["a0", "a1", "a2"], CollectionOrdering.Matching);
                await Assert.That(viaClothNode.Count(static node => node.ContainsKey("transform_alignment"))).IsEqualTo(2);
                await Assert.That(viaClothNode.Count > 0 && viaClothNode[0].GetInt32Property("transform_alignment") == 3).IsTrue();
                await Assert.That(viaClothNode.Count > 0 ? viaClothNode[0].GetStringProperty("node_base_x1") : null).IsEqualTo("side");
                await Assert.That(viaClothNode.Count > 0 ? viaClothNode[0].GetStringProperty("node_base_y1") : null).IsEqualTo("a1");
                await Assert.That(viaBone).IsEmpty();
            }
        }

        /// <summary>
        /// A chain a0 - a1 - a2 under a static head and a free cloth node, with a0 and a1 based through node <paramref
        /// name="xNode"/>.
        /// </summary>
        private static ClothReconstruction ChainOverFreeClothNode(int xNode) => new FeModelBuilder
        {
            Names = ["head", "$cloth_node_side", "a0", "a1", "a2"],
            StaticNodes = 2,
            RotLockStaticNodes = 2,
            Parents = [-1, 0, 0, 2, 3],
            Positions = [new(0f, 0f, 0f), new(0f, -50f, 0f), new(0f, 0f, -5f), new(0f, 0f, -10f), new(0f, 0f, -15f)],
            NodeBases = [NodeBase(2, 2, xNode, 3, 2), NodeBase(3, 3, xNode, 4, 3)],
        }.Reconstruct();

        /// <summary>
        /// A ClothNode with a node base but fewer than two rod neighbours declares alignment 4 with its references; a
        /// node two rods tie to keeps 0.
        /// </summary>
        [Test]
        public async Task ADynamicClothNodeNoRodTiesToTwoNodesDeclaresItsBasisPreset()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["root", "a", "b", "c", "d", "flap", "strap"],
                StaticNodes = 1,
                Positions = [new(0f, 0f, 60f), new(-4f, 4f, 55f), new(-4f, -4f, 55f), new(-6f, 4f, 45f), new(-6f, -4f, 45f),
                    new(-5f, 0f, 50f), new(-3f, 0f, 58f)],
                Rods = [RigidRod(1, 2, 8f), RigidRod(3, 4, 8f), RigidRod(1, 3, 10f), RigidRod(6, 1, 5f), RigidRod(6, 2, 5f)],
                NodeBases = [NodeBase(5, 4, 1, 2, 3), NodeBase(6, 4, 1, 2, 3)],
            }.Reconstruct();

            var flap = ClothExtract.MakeClothNode(cloth, "flap", 5);
            var strap = ClothExtract.MakeClothNode(cloth, "strap", 6);

            using (Assert.Multiple())
            {
                await Assert.That(ClothExtract.RodNeighbourCount(cloth, 6)).IsEqualTo(2);
                await Assert.That(strap.GetInt32Property("transform_alignment")).IsEqualTo(0);

                await Assert.That(flap.GetInt32Property("transform_alignment")).IsEqualTo(4);
                await Assert.That(flap.GetStringProperty("node_base_x0")).IsEqualTo("d");
                await Assert.That(flap.GetStringProperty("node_base_x1")).IsEqualTo("a");
                await Assert.That(flap.GetStringProperty("node_base_y0")).IsEqualTo("b");
                await Assert.That(flap.GetStringProperty("node_base_y1")).IsEqualTo("c");
            }
        }
    }
}
