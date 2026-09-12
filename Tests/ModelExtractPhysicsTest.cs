using System.IO;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace Tests;

public class ModelExtractPhysicsTest
{
    private sealed class TestPhysics : PhysAggregateData
    {
        public TestPhysics(KVObject data)
        {
            Data = data;
        }
    }

    private static TestPhysics LoadPhysics()
    {
        var document = KVDocumentExtensions.ParseKV3(Path.Combine(TestContext.TestDirectory!, "Files", "Physics", "joint_conical.kv3"));
        return new TestPhysics(document.Root) { Resource = null! };
    }

    [Test]
    public async Task RestoresConicalJointFramesAndLimits()
    {
        var physics = LoadPhysics();
        var node = ModelExtract.BuildPhysicsJoint(physics, physics.Data.GetArray("m_joints")[0])!;

        using (Assert.Multiple())
        {
            await Assert.That(node.GetStringProperty("_class")).IsEqualTo("PhysicsJointConical");
            await Assert.That(node.GetStringProperty("parent_body")).IsEqualTo("parent");
            await Assert.That(node.GetStringProperty("child_body")).IsEqualTo("child");
            await Assert.That(node.GetSubCollection("anchor_origin").ToVector3()).IsEqualTo(new Vector3(4, 5, 6));
            await Assert.That(Vector3.Distance(node.GetSubCollection("anchor_angles").ToVector3(), new Vector3(12, 23, 34))).IsLessThan(0.001f);

            // All three offset angles are non-zero so inverse rotation and negated angles differ.
            await Assert.That(Vector3.Distance(node.GetSubCollection("swing_offset_angle").ToVector3(), new Vector3(17, 29, 41))).IsLessThan(0.001f);
            await Assert.That(MathF.Abs(node.GetFloatProperty("swing_limit") - 30)).IsLessThan(0.001f);
            await Assert.That(MathF.Abs(node.GetFloatProperty("min_twist_angle") + 10)).IsLessThan(0.001f);
            await Assert.That(MathF.Abs(node.GetFloatProperty("max_twist_angle") - 40)).IsLessThan(0.001f);
            await Assert.That(node.GetBooleanProperty("enable_swing_limit")).IsTrue();
            await Assert.That(node.GetBooleanProperty("enable_twist_limit")).IsTrue();
            await Assert.That(node.GetBooleanProperty("collision_enabled")).IsFalse();
            await Assert.That(node.GetStringProperty("motion_resistance")).IsEqualTo("friction");
            await Assert.That(node.GetFloatProperty("friction")).IsEqualTo(0.375f);
        }
    }

    [Test]
    public async Task RestoresRevoluteJointAndBodyNameMapping()
    {
        var physics = LoadPhysics();
        physics.Data["m_boneParents"] = MakeArray(1, 0);
        var joint = physics.Data.GetArray("m_joints")[0];
        joint["m_nType"] = 3;
        joint["m_nFlags"] = 2;
        joint["m_bEnableTwistLimit"] = false;
        joint["m_bEnableCollision"] = true;
        joint["m_bIsLinearConstraintDisabled"] = true;
        var node = ModelExtract.BuildPhysicsJoint(physics, joint)!;

        using (Assert.Multiple())
        {
            await Assert.That(node.GetStringProperty("_class")).IsEqualTo("PhysicsJointRevolute");
            await Assert.That(node.GetStringProperty("parent_body")).IsEqualTo("child");
            await Assert.That(node.GetStringProperty("child_body")).IsEqualTo("parent");
            await Assert.That(node.GetBooleanProperty("enable_limit")).IsFalse();
            await Assert.That(node.GetBooleanProperty("collision_enabled")).IsTrue();
            await Assert.That(node.GetBooleanProperty("use_block_solver")).IsTrue();
            await Assert.That(node.GetStringProperty("constraint_space")).IsEqualTo("angular_only");
            await Assert.That(MathF.Abs(node.GetFloatProperty("min_angle") + 10)).IsLessThan(0.001f);
            await Assert.That(MathF.Abs(node.GetFloatProperty("max_angle") - 40)).IsLessThan(0.001f);
            await Assert.That(node.ContainsKey("swing_limit")).IsFalse();
        }
    }

    [Test]
    [Arguments("none", 0f, 0f, 0f)]
    [Arguments("friction", 0.25f, 0f, 0f)]
    [Arguments("elastic", 0f, 5f, 0f)]
    [Arguments("plastic", 0f, 5f, 0.5f)]
    public async Task PreservesMotionResistance(string mode, float friction, float elasticity, float plasticity)
    {
        var physics = LoadPhysics();
        var joint = physics.Data.GetArray("m_joints")[0];
        joint["m_flFriction"] = friction;
        joint["m_flElasticity"] = elasticity;
        joint["m_flPlasticity"] = plasticity;
        var node = ModelExtract.BuildPhysicsJoint(physics, joint)!;

        using (Assert.Multiple())
        {
            await Assert.That(node.GetStringProperty("motion_resistance")).IsEqualTo(mode);
            await Assert.That(node.GetFloatProperty("friction")).IsEqualTo(friction);
            await Assert.That(node.GetFloatProperty("elasticity")).IsEqualTo(elasticity);
            await Assert.That(node.GetFloatProperty("plasticity")).IsEqualTo(plasticity);
        }
    }

    [Test]
    [Arguments(99, 0, 1)]
    [Arguments(4, -1, 1)]
    [Arguments(4, 0, 99)]
    public async Task DoesNotEmitUnsupportedOrUnboundJoints(int type, int parent, int child)
    {
        var physics = LoadPhysics();
        var joint = physics.Data.GetArray("m_joints")[0];
        joint["m_nType"] = type;
        joint["m_nBody1"] = parent;
        joint["m_nBody2"] = child;
        await Assert.That(ModelExtract.BuildPhysicsJoint(physics, joint)).IsNull();
    }

    [Test]
    public async Task PreservesBodyDynamicsWithoutInventingAbsentFields()
    {
        var part = MakeNode("unused",
            ("m_flMass", 25f), ("m_flInertiaScale", 7f),
            ("m_flLinearDamping", 0.1f), ("m_flAngularDamping", 2f),
            ("m_flLinearDrag", 1f), ("m_flAngularDrag", 3f),
            ("m_bOverrideMassCenter", true), ("m_vMassCenterOverride", ToKVArray(new Vector3(1, 2, 3))));
        var node = ModelExtract.BuildPhysicsBodyMarkup(part, "child");
        var legacy = ModelExtract.BuildPhysicsBodyMarkup(KVObject.Collection(), "parent");

        using (Assert.Multiple())
        {
            await Assert.That(node.GetStringProperty("target_body")).IsEqualTo("child");
            await Assert.That(node.GetFloatProperty("mass_override")).IsEqualTo(25f);
            await Assert.That(node.GetFloatProperty("inertia_scale")).IsEqualTo(7f);
            await Assert.That(node.GetFloatProperty("linear_damping")).IsEqualTo(0.1f);
            await Assert.That(node.GetFloatProperty("angular_damping")).IsEqualTo(2f);
            await Assert.That(node.GetFloatProperty("linear_drag")).IsEqualTo(1f);
            await Assert.That(node.GetFloatProperty("angular_drag")).IsEqualTo(3f);
            await Assert.That(node.GetBooleanProperty("use_mass_center_override")).IsTrue();
            await Assert.That(node.GetSubCollection("mass_center_override").ToVector3()).IsEqualTo(new Vector3(1, 2, 3));
            await Assert.That(legacy.ContainsKey("mass_override")).IsFalse();
            await Assert.That(legacy.ContainsKey("angular_damping")).IsFalse();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MergesBodyGameTagsWithoutDuplicateDeclarations(bool hasUnmatchedBody)
    {
        var markups = KVObject.Collection();
        var matched = KVObject.Collection();
        matched.Add("m_TargetBody", "CHILD");
        matched.Add("m_Tag", "limb");
        markups.Add("CHILD", matched);
        if (hasUnmatchedBody)
        {
            var unmatched = KVObject.Collection();
            unmatched.Add("m_TargetBody", "unmatched");
            unmatched.Add("m_Tag", "preserve");
            markups.Add("unmatched", unmatched);
        }

        var keys = KVObject.Collection();
        keys.Add("m_PhysicsBodyMarkupByBoneName", markups);
        var gameList = MakeListNode("GameDataList");
        gameList.Children.Add(MakeNode("GenericGameData", ("game_class", "CPhysicsBodyGameMarkupData"), ("game_keys", keys)));
        gameList.Children.Add(MakeNode("GenericGameData", ("game_class", "unrelated"), ("game_keys", KVObject.Collection())));
        var body = ModelExtract.BuildPhysicsBodyMarkup(KVObject.Collection(), "child");

        ModelExtract.MergePhysicsBodyGameMarkup(MakeArray(gameList.Node), MakeArray(body));

        using (Assert.Multiple())
        {
            await Assert.That(body.GetStringProperty("tag")).IsEqualTo("limb");
            await Assert.That(gameList.Node.GetArray("children").Count).IsEqualTo(hasUnmatchedBody ? 2 : 1);
            await Assert.That(keys.GetSubCollection("m_PhysicsBodyMarkupByBoneName").ContainsKey("CHILD")).IsFalse();
            await Assert.That(keys.GetSubCollection("m_PhysicsBodyMarkupByBoneName").ContainsKey("unmatched")).IsEqualTo(hasUnmatchedBody);
            await Assert.That(gameList.Node.GetArray("children")[hasUnmatchedBody ? 1 : 0].GetStringProperty("game_class")).IsEqualTo("unrelated");
        }
    }

    [Test]
    public async Task DoesNotAddJointListsToStaticModels()
    {
        using var resource = new Resource();
        resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "arch_apartment_ixia_01_top_cap_l_01.vmdl_c"));
        var output = new ModelExtract(resource, new NullFileLoader()).ToValveModel();
        await Assert.That(output.Contains("PhysicsJointList", StringComparison.Ordinal)).IsFalse();
        await Assert.That(output.Contains("PhysicsBodyMarkupList", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments(ModelExtract.ModelExtractType.Default, true)]
    [Arguments(ModelExtract.ModelExtractType.Map_PhysicsToRenderMesh, false)]
    public async Task AddsArticulatedPhysicsOnlyToModelExports(ModelExtract.ModelExtractType type, bool expected)
    {
        using var resource = new Resource();
        resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "arch_apartment_ixia_01_top_cap_l_01.vmdl_c"));
        var target = ((Model)resource.DataBlock!).GetEmbeddedPhys()!;
        var source = LoadPhysics();
        foreach (var key in new[] { "m_parts", "m_joints", "m_boneNames", "m_boneParents", "m_bindPose" })
        {
            target.Data[key] = source.Data[key];
        }

        var output = new ModelExtract(resource, new NullFileLoader()) { Type = type }.ToValveModel();
        using (Assert.Multiple())
        {
            await Assert.That(output.Contains("PhysicsJointList", StringComparison.Ordinal)).IsEqualTo(expected);
            await Assert.That(output.Contains("PhysicsJointConical", StringComparison.Ordinal)).IsEqualTo(expected);
            await Assert.That(output.Contains("PhysicsBodyMarkupList", StringComparison.Ordinal)).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task ExportsLegacyJointRecordsWithoutModernFields()
    {
        using var resource = new Resource();
        resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "juggernaut.vphys_c"));
        var physics = (PhysAggregateData)resource.DataBlock!;
        foreach (var joint in physics.Data.GetArray("m_joints"))
        {
            var node = ModelExtract.BuildPhysicsJoint(physics, joint);
            await Assert.That(node).IsNotNull();
            await Assert.That(node!.ContainsKey("collision_enabled")).IsFalse();
        }
    }
}
