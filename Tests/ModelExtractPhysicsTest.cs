using System.IO;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

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
}
