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

    [Test]
    public async Task ExportsConicalJointAndBodyMarkup()
    {
        using var resource = new Resource();
        resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "box_creature_model.vmdl_c"));
        var vmdl = new ModelExtract(resource, new NullFileLoader()).ToValveModel();

        using (Assert.Multiple())
        {
            await Assert.That(vmdl).Contains("_class = \"PhysicsJointConical\"");
            await Assert.That(vmdl).Contains("parent_body = \"root\"");
            await Assert.That(vmdl).Contains("child_body = \"sub_root\"");
            await Assert.That(vmdl).Contains("collision_enabled = false");
            await Assert.That(vmdl).Contains("swing_limit = 30.0");
            await Assert.That(vmdl).Contains("min_twist_angle = -15.0");
            await Assert.That(vmdl).Contains("_class = \"PhysicsBodyMarkupList\"");
            await Assert.That(vmdl).Contains("target_body = \"sub_root\"");
        }
    }

    [Test]
    public async Task ExportsLegacyJointRecords()
    {
        using var resource = new Resource();
        resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "juggernaut.vphys_c"));
        var physics = (PhysAggregateData)resource.DataBlock!;
        foreach (var joint in physics.Joints)
        {
            await Assert.That(ModelExtract.BuildPhysicsJoint(physics, joint)).IsNotNull();
        }
    }

    [Test]
    public async Task InvertsSwingOffsetRotationInsteadOfNegatingAngles()
    {
        var document = KVDocumentExtensions.ParseKV3(Path.Combine(TestContext.TestDirectory!, "Files", "Physics", "joint_conical.kv3"));
        var physics = new TestPhysics(document.Root) { Resource = null! };
        var node = ModelExtract.BuildPhysicsJoint(physics, physics.Joints[0])!;

        await Assert.That(Vector3.Distance(node.GetSubCollection("swing_offset_angle").ToVector3(), new Vector3(17, 29, 41))).IsLessThan(0.001f);
    }

    [Test]
    public async Task MovesBodyGameTagsOntoBodyMarkup()
    {
        var matched = KVObject.Collection();
        matched.Add("m_TargetBody", "CHILD");
        matched.Add("m_Tag", "limb");
        var markups = KVObject.Collection();
        markups.Add("CHILD", matched);
        var keys = KVObject.Collection();
        keys.Add("m_PhysicsBodyMarkupByBoneName", markups);

        var gameList = MakeListNode("GameDataList");
        gameList.Children.Add(MakeNode("GenericGameData", ("game_class", "CPhysicsBodyGameMarkupData"), ("game_keys", keys)));
        gameList.Children.Add(MakeNode("GenericGameData", ("game_class", "unrelated"), ("game_keys", KVObject.Collection())));
        var body = MakeNode("PhysicsBodyMarkup", ("target_body", "child"));

        ModelExtract.MergePhysicsBodyGameMarkup(MakeArray(gameList.Node), MakeArray(body));

        var children = gameList.Node.GetArray("children");
        using (Assert.Multiple())
        {
            await Assert.That(body.GetStringProperty("tag")).IsEqualTo("limb");
            await Assert.That(children.Count).IsEqualTo(1);
            await Assert.That(children[0].GetStringProperty("game_class")).IsEqualTo("unrelated");
        }
    }
}
