using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    public class ModelExtractTest
    {
        /// <summary>
        /// A vmesh has no model to read mesh groups, LODs, materials or a skeleton from, and map
        /// extraction reaches this path for a scene object that carries a bare renderable.
        /// </summary>
        [Test]
        public async Task ExtractsAMeshWithNoModelBehindIt()
        {
            using var resource = TestFixtures.Load("chen_weapon.vmesh_c");

            using var content = new ModelExtract((Mesh)resource.DataBlock!, "models/heroes/chen/chen_weapon.vmesh").ToContentFile();
            var vmdl = Encoding.UTF8.GetString(content.Data!);

            using (Assert.Multiple())
            {
                await Assert.That(content.FileName).IsEqualTo("models/heroes/chen/chen_weapon.vmdl");
                await Assert.That(vmdl).Contains("_class = \"RenderMeshFile\"");
                await Assert.That(vmdl).Contains("filename = \"models/heroes/chen/chen_weapon.dmx\"");

                // Nothing a model would have contributed may be invented for a lone mesh.
                await Assert.That(vmdl).DoesNotContain("BodyGroupList");
                await Assert.That(vmdl).DoesNotContain("LODGroupList");
                await Assert.That(vmdl).DoesNotContain("MaterialGroupList");

                await Assert.That(content.SubFiles.Select(subFile => subFile.FileName))
                    .IsEquivalentTo(["chen_weapon.dmx"]);
                await Assert.That(content.SubFiles.Single().Extract!.Invoke()).IsNotEmpty();
            }
        }

        /// <summary>
        /// A model doc list node is created the first time a section writes into it, so the order the
        /// sections run in is the order the reader sees, and a list nothing wrote is absent rather than
        /// empty. Pinned per model because which sections run at all depends on what the model carries.
        /// </summary>
        [Test]
        public async Task WritesOnlyTheDocSectionsTheModelFills()
        {
            using (Assert.Multiple())
            {
                await Assert.That(RootSections("necro_archer.vmdl_c")).IsEquivalentTo(
                    ["BoneMarkupList", "RenderMeshList", "BodyGroupList", "AttachmentList", "PoseParamList", "AnimationList", "HitboxSetList", "Skeleton"],
                    CollectionOrdering.Matching);

                await Assert.That(RootSections("box_creature_ik_model.vmdl_c")).IsEquivalentTo(
                    ["BoneMarkupList", "RenderMeshList", "AttachmentList", "WeightListList", "AnimationList", "IKData", "GameDataList", "Skeleton"],
                    CollectionOrdering.Matching);

                await Assert.That(RootSections("alyx_hand_left.vmdl_c")).IsEquivalentTo(
                    ["BoneMarkupList", "RenderMeshList", "AttachmentList", "WeightListList", "PoseParamList", "AnimationList", "IKData", "HitboxSetList", "Skeleton", "PhysicsBodyMarkupList", "PhysicsShapeList"],
                    CollectionOrdering.Matching);

                // Only the LOD fixture reaches LODGroupList, and it has no skeleton to write.
                await Assert.That(RootSections("lod_test.vmdl_c")).IsEquivalentTo(
                    ["BoneMarkupList", "RenderMeshList", "LODGroupList"],
                    CollectionOrdering.Matching);

                // Every mesh of this one is an external reference the null loader cannot resolve, so
                // nothing downstream of the meshes has anything to write.
                await Assert.That(RootSections("alchemist.vmdl_c")).IsEquivalentTo(
                    ["BoneMarkupList", "Skeleton"],
                    CollectionOrdering.Matching);
            }
        }

        /// <summary>
        /// A blend has no animation of its own: the compiler resolves it into a sequence that fetches
        /// the animations it blends, positioned along a pose parameter, and it comes back as the node
        /// listing them. A sequence that fetches nothing at all is the bind pose instead.
        /// </summary>
        [Test]
        public async Task RebuildsOneDimensionalBlendsAndBindPoses()
        {
            using var resource = TestFixtures.Load("necro_archer.vmdl_c");
            var model = (Model)resource.DataBlock!;

            var blendSequence = model.GetAllAnimations(new NullFileLoader())
                .OfType<SequenceAnimation>()
                .Single(anim => anim.Name == "archer_turns");

            var document = TestFixtures.ParseKV3(new ModelExtract(resource, new NullFileLoader()).ToValveModel());
            var animationList = TestFixtures.FindNode(document, "AnimationList")!;
            var blend = TestFixtures.FindNode(animationList, "1DBlend")!;
            var proxies = blend.GetArray("blendList");

            using (Assert.Multiple())
            {
                await Assert.That(blendSequence.IsBlend).IsTrue();
                await Assert.That(blendSequence.Fetch!.Value.LocalReferenceArray).Count().IsEqualTo(3);
                await Assert.That(blendSequence.Fetch!.Value.PoseKeyArray).IsEquivalentTo([-1f, 0f, 1f]);

                await Assert.That(blend.GetStringProperty("name")).IsEqualTo("archer_turns");
                await Assert.That(blend.GetStringProperty("poseParam")).IsEqualTo("turn");
                await Assert.That(proxies.Select(proxy => proxy.GetStringProperty("name")))
                    .IsEquivalentTo(["@archer_turns_lookFrame_0", "@archer_turns_lookFrame_1", "@archer_turns_lookFrame_2"]);
                await Assert.That(proxies.Select(proxy => proxy.GetFloatProperty("weight")))
                    .IsEquivalentTo([-1f, 0f, 1f]);

                await Assert.That(TestFixtures.FindNode(animationList, "AnimBindPose")?.GetStringProperty("name"))
                    .IsEqualTo("bindPose");
            }
        }

        /// <summary>
        /// A two dimensional blend spreads its animations over a grid of two pose parameters. The
        /// compiler takes each dimension's size from the length of its weight list and walks the grid
        /// row first, so a row has to come back in that order.
        /// </summary>
        [Test]
        public async Task RebuildsTwoDimensionalBlendGrids()
        {
            var blend = TestFixtures.FindNode(TestFixtures.ExtractValveModelDocument("gem_lich.vmdl_c"), "2DBlend")!;
            var rows = blend.GetArray("blend_anim_list");

            using (Assert.Multiple())
            {
                await Assert.That(blend.GetStringProperty("row_pose_param_name")).IsEqualTo("up_down");
                await Assert.That(blend.GetStringProperty("col_pose_param_name")).IsEqualTo("left_right");
                await Assert.That(blend.GetFloatArray("row_weight_list")).IsEquivalentTo([-1f, 0f, 1f]);
                await Assert.That(blend.GetFloatArray("col_weight_list")).IsEquivalentTo([-1f, 0f, 1f]);
                await Assert.That(rows).Count().IsEqualTo(3);
                await Assert.That(string.Join(',', rows[0].Select(cell => cell.Value)))
                    .IsEqualTo("@gem_lina_coordinates_right_up,@gem_lina_coordinates_up,@gem_lina_coordinates_left_up");
            }
        }

        /// <summary>
        /// A sequence applies a named weight list rather than the default one every animation gets, and
        /// carries every activity past the first as the modifier node the compiler folds back in.
        /// </summary>
        [Test]
        public async Task KeepsWeightListsAndActivityModifiers()
        {
            var animationList = TestFixtures.FindNode(TestFixtures.ExtractValveModelDocument("alyx_hand_left.vmdl_c"), "AnimationList")!;

            var thumb = TestFixtures.FindNamed(animationList, "@grab_thumb");
            var cylinder = TestFixtures.FindNamed(animationList, "cylinder_ik_pose");

            using (Assert.Multiple())
            {
                await Assert.That(thumb?.GetStringProperty("weight_list_name")).IsEqualTo("wl_thumb");
                await Assert.That(cylinder?.GetStringProperty("activity_name")).IsEqualTo("ACT_CYLINDER");
                await Assert.That(TestFixtures.FindNode(cylinder!, "ActivityModifier")?.GetStringProperty("activity_name"))
                    .IsEqualTo("ACT_NEUTRAL_REF_POSE");
            }
        }

        /// <summary>
        /// Each IK joint owns the joints below it, so the second nests inside the first and closes
        /// before its parent writes its own bone key. ModelDoc registers some chain keys with a trailing
        /// space and others behind an m_Data prefix, and ignores every other spelling, so those have to
        /// survive serialization verbatim.
        /// </summary>
        [Test]
        public async Task RebuildsIKChainsWithTheKeySpellingsModelDocReadsBack()
        {
            var vmdl = TestFixtures.ExtractValveModel("box_creature_ik_model.vmdl_c");

            var parent = vmdl.IndexOf("bone = \"upper_leg_r\"", StringComparison.Ordinal);
            var child = vmdl.IndexOf("bone = \"foot_r\"", StringComparison.Ordinal);

            using (Assert.Multiple())
            {
                await Assert.That(vmdl).Contains("_class = \"IKData\"");
                await Assert.That(vmdl).Contains("_class = \"IKChain\"");
                await Assert.That(vmdl).Contains("name = \"IK_chain_new_foot_r\"");
                await Assert.That(vmdl).Contains("name = \"IK_chain_new_foot_l\"");
                await Assert.That(vmdl).Contains("_class = \"IKChainJoint\"");
                await Assert.That(vmdl).Contains("m_Name = \"foot_r_ik_target\"");

                await Assert.That(child).IsGreaterThan(0);
                await Assert.That(parent).IsGreaterThan(child);

                await Assert.That(vmdl).Contains("\"m_DefaultSolverSettings.m_SolverType \" = \"IKSOLVER_Fabrik\"");
                await Assert.That(vmdl).Contains("m_Data.m_bParentJointRequiresAlignment = ");
                await Assert.That(vmdl).Contains("m_Data.m_EndEffectorFixedOffsetAttachment = ");
                await Assert.That(vmdl).Contains("m_Data.m_DefaultTargetSettings.m_AnimgraphParameterNamePosition = ");

                // The pole vector keys postdate this model's compiler, and a key the compiled block
                // does not carry is not written back.
                await Assert.That(vmdl).DoesNotContain("m_PoleVectorForAxis");
            }
        }

        /// <summary>
        /// Joint constraints and target settings come back on the chain, while the three fields the
        /// compiler fills in itself have no ModelDoc attribute and must not be written.
        /// </summary>
        [Test]
        public async Task RebuildsHingeConstraintsAndOmitsCompilerDerivedFields()
        {
            var vmdl = TestFixtures.ExtractValveModel("alyx_hand_left.vmdl_c");

            using (Assert.Multiple())
            {
                await Assert.That(vmdl).Contains("_class = \"IKJointConstraint_Hinge\"");
                await Assert.That(vmdl).Contains("hinge_axis = \"Up\"");
                await Assert.That(vmdl).Contains("max_radians = 1.745329");
                await Assert.That(vmdl).Contains("constrained_joint = \"\"");

                await Assert.That(vmdl).Contains("m_DefaultTargetSettings.m_TargetSource = \"Animgraph Parameter\"");
                await Assert.That(vmdl).Contains("m_Name = \"fingertip_thumb\"");
                await Assert.That(vmdl).Contains("m_DefaultSolverSettings.m_SolverType \" = \"IKSOLVER_CCD\"");

                await Assert.That(vmdl).DoesNotContain("m_qFixupRotation");
                await Assert.That(vmdl).DoesNotContain("m_ParentJoint");
                await Assert.That(vmdl).DoesNotContain("m_TargetCoordSystem");
            }
        }

        /// <summary>
        /// Models authored against the legacy rig carry their chains in the control rig and leave the
        /// m_IKChains entries without joints. ModelDoc rejects a chain in that state, so the jointless
        /// entry is dropped and the rig it belongs to is written instead. A model with neither gets no
        /// IKData at all.
        /// </summary>
        [Test]
        public async Task FallsBackToTheLegacyRigAndOmitsIKDataWithoutChains()
        {
            var legacy = TestFixtures.ExtractValveModel("box_creature_model.vmdl_c");

            using (Assert.Multiple())
            {
                await Assert.That(legacy).DoesNotContain("_class = \"IKChain\"");
                await Assert.That(legacy).DoesNotContain("_class = \"IKChainJoint\"");

                await Assert.That(legacy).Contains("_class = \"IKData\"");
                await Assert.That(legacy).Contains("_class = \"IKRigSimple\"");
                await Assert.That(legacy).Contains("_class = \"IKChainOld\"");
                await Assert.That(legacy).Contains("name = \"test\"");
                await Assert.That(legacy).Contains("solver = \"IKSOLVER_Fabrik\"");

                await Assert.That(TestFixtures.ExtractValveModel("lod_test.vmdl_c")).DoesNotContain("_class = \"IKData\"");
            }
        }

        /// <summary>
        /// Root motion is written to the model level transform channel, not baked into the bones, so a
        /// clip that travels keeps its root track intact and one that does not travel writes no such
        /// channel at all.
        /// </summary>
        [Test]
        public async Task WritesRootMotionToTheModelChannelOnly()
        {
            using var resource = TestFixtures.Load("box_creature_ik_model.vmdl_c");
            var model = (Model)resource.DataBlock!;

            var walkChannels = DmxAnimationChannels(model, "box_creature_leggy_walk");
            var idle = model.GetAllAnimations(new NullFileLoader()).First(anim => anim.Name == "box_creature_leggy_idle");
            var idleChannels = DmxAnimationChannels(model, "box_creature_leggy_idle");

            var modelRoot = RootChannelValues(walkChannels, "");
            var rootMotionBone = RootChannelValues(walkChannels, "root_motion");

            using (Assert.Multiple())
            {
                // The walk travels its full ~47.92 source-unit displacement on the model channel.
                await Assert.That((modelRoot[^1] - modelRoot[0]).X).IsEqualTo(47.92f).Within(0.1f);

                // The root_motion bone itself carries no baked travel, only its raw per-frame data.
                await Assert.That((rootMotionBone[^1] - rootMotionBone[0]).X).IsLessThan(1.0f);

                await Assert.That(idle.HasMovementData()).IsFalse();
                await Assert.That(idleChannels.Cast<Datamodel.Element>().Any(channel => channel.Name is "_p" or "_o")).IsFalse();
            }
        }

        /// <summary>
        /// The LOD structure comes back as one LODGroup per declared level carrying that level's switch
        /// threshold, so a recompile rebuilds the original switch distances.
        /// </summary>
        [Test]
        public async Task EmitsALodGroupPerLevel()
        {
            var vmdl = TestFixtures.ExtractValveModel("lod_test.vmdl_c");

            using (Assert.Multiple())
            {
                await Assert.That(vmdl).Contains("LODGroupList");
                await Assert.That(vmdl).Contains("_class = \"LODGroup\"");
                await Assert.That(vmdl).Contains("mesh_references");
                await Assert.That(vmdl).Contains("switch_threshold = 0");
                await Assert.That(vmdl).Contains("switch_threshold = 5");
                await Assert.That(vmdl).Contains("switch_threshold = 20");
            }
        }

        /// <summary>
        /// A model whose every mesh sits in every LOD level still needs one LODGroup per declared
        /// level. The compiler ignores the whole LOD section unless at least two of them are present,
        /// so collapsing the meshes into a lone LODGroupAll loses the switch distances entirely.
        /// </summary>
        [Test]
        public async Task EmitsALodGroupPerLevelEvenWhenNoLevelHasItsOwnMesh()
        {
            var lodGroups = TestFixtures.FindNode(
                TestFixtures.ExtractValveModelDocument("stone_tranquility_helm.vmdl_c"), "LODGroupList")!
                .GetArray("children");

            var perLevel = lodGroups.Where(group => group.GetStringProperty("_class") == "LODGroup").ToList();
            var all = lodGroups.Where(group => group.GetStringProperty("_class") == "LODGroupAll").ToList();

            using (Assert.Multiple())
            {
                await Assert.That(perLevel.Select(group => group.GetFloatProperty("switch_threshold")))
                    .IsEquivalentTo([0f, 1f], CollectionOrdering.Matching);

                // The mesh belongs to every level, so it is named once by LODGroupAll and the
                // per-level groups carry no references of their own.
                await Assert.That(perLevel.TrueForAll(group => group.GetArray("mesh_references").Count == 0)).IsTrue();
                await Assert.That(all).Count().IsEqualTo(1);
                await Assert.That(all[0].GetArray("mesh_references")).Count().IsEqualTo(1);
            }
        }

        private static string[] RootSections(string fileName)
            => [.. TestFixtures.ExtractValveModelDocument(fileName)
                .GetSubCollection("rootNode")
                .GetArray("children")
                .Select(child => child.GetStringProperty("_class"))];

        private static Datamodel.ElementArray DmxAnimationChannels(Model model, string animationName)
        {
            var anim = model.GetAllAnimations(new NullFileLoader()).First(a => a.Name == animationName);

            using var ms = new MemoryStream(ModelExtract.ToDmxAnim(model, anim));
            // Eager load so deferred attributes don't read from the stream after it is disposed.
            var dm = Datamodel.Datamodel.Load(ms, Datamodel.Codecs.DeferredMode.Disabled);

            var clip = (Datamodel.Element)((Datamodel.ElementArray)((Datamodel.Element)dm.Root!["animationList"]!)["animations"]!)[0]!;

            return (Datamodel.ElementArray)clip["channels"]!;
        }

        private static Datamodel.Vector3Array RootChannelValues(Datamodel.ElementArray channels, string boneName)
        {
            var channel = channels.Cast<Datamodel.Element>().Single(c => c.Name == $"{boneName}_p");
            var layer = (Datamodel.Element)((Datamodel.ElementArray)((Datamodel.Element)channel["log"]!)["layers"]!)[0]!;

            return (Datamodel.Vector3Array)layer["values"]!;
        }
    }
}
