using System.IO;
using System.Linq;
using System.Text;
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
    [Arguments(null, null, false)]
    [Arguments("PELVIS", null, false)]
    [Arguments("alias", "PELVIS", false)]
    [Arguments(null, null, true)]
    public async Task PreservesBodyPropertiesWithGameMarkup(string? key, string? target, bool hasUnmatchedMarkup)
    {
        using var resource = new Resource();
        resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "ctm_sas.vmdl_c"));
        var model = (Model)resource.DataBlock!;
        var physics = model.GetEmbeddedPhys()!;
        var gameData = model.KeyValues.GetSubCollection("CPhysicsBodyGameMarkupData");
        var markups = gameData.GetSubCollection("m_PhysicsBodyMarkupByBoneName");
        if (key != null)
        {
            var replacement = KVObject.Collection();
            foreach (var (name, value) in markups)
            {
                if (name != "pelvis")
                {
                    replacement.Add(name!, value);
                    continue;
                }

                var markup = KVObject.Collection();
                markup.Add("m_Tag", "test_tag");
                if (target != null)
                {
                    markup.Add("m_TargetBody", target);
                }

                replacement.Add(key, markup);
            }

            markups = replacement;
            gameData["m_PhysicsBodyMarkupByBoneName"] = markups;
        }

        if (hasUnmatchedMarkup)
        {
            var unmatched = KVObject.Collection();
            unmatched.Add("m_TargetBody", "unmatched_body");
            unmatched.Add("m_Tag", "unmatched_tag");
            markups.Add("unmatched_body", unmatched);
            gameData.Add("extra_metadata", 42);
        }

        var tags = markups.ToDictionary(entry => entry.Value.GetStringProperty("m_TargetBody", entry.Key!),
            entry => entry.Value.GetStringProperty("m_Tag"), StringComparer.OrdinalIgnoreCase);
        var originalKeyValues = model.KeyValues.ToKV3String();
        var extract = new ModelExtract(resource, new NullFileLoader());
        var text = extract.ToValveModel();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var children = KVDocumentExtensions.ParseKV3(stream).Root.GetSubCollection("rootNode").GetArray("children");
        var bodyList = children.Single(node => node.GetStringProperty("_class") == "PhysicsBodyMarkupList");
        var bodies = bodyList.GetArray("children");
        await Assert.That(bodies.Count).IsEqualTo(15);
        var joints = children.Single(node => node.GetStringProperty("_class") == "PhysicsJointList").GetArray("children");
        await Assert.That(joints.Count).IsEqualTo(14);
        var parts = physics.Data.GetArray("m_parts");
        for (var i = 0; i < parts.Count; i++)
        {
            var name = physics.GetParentBoneName(i);
            var body = bodies.Single(node => node.GetStringProperty("target_body") == name);
            var part = parts[i];
            await Assert.That(body.GetStringProperty("tag")).IsEqualTo(tags[name]);
            await Assert.That(body.GetFloatProperty("mass_override")).IsEqualTo(part.GetFloatProperty("m_flMass"));
            await Assert.That(body.GetFloatProperty("inertia_scale")).IsEqualTo(part.GetFloatProperty("m_flInertiaScale"));
            await Assert.That(body.GetFloatProperty("linear_damping")).IsEqualTo(part.GetFloatProperty("m_flLinearDamping"));
            await Assert.That(body.GetFloatProperty("angular_damping")).IsEqualTo(part.GetFloatProperty("m_flAngularDamping"));
            await Assert.That(body.GetFloatProperty("linear_drag")).IsEqualTo(part.GetFloatProperty("m_flLinearDrag"));
            await Assert.That(body.GetFloatProperty("angular_drag")).IsEqualTo(part.GetFloatProperty("m_flAngularDrag"));
            await Assert.That(body.GetBooleanProperty("use_mass_center_override")).IsEqualTo(part.GetBooleanProperty("m_bOverrideMassCenter"));
            await Assert.That(body.GetSubCollection("mass_center_override").ToVector3()).IsEqualTo(part.GetSubCollection("m_vMassCenterOverride").ToVector3());
        }

        if (hasUnmatchedMarkup)
        {
            var gameList = children.Single(node => node.GetStringProperty("_class") == "GameDataList");
            var remainingData = gameList.GetArray("children")
                .Single(node => node.GetStringProperty("game_class") == "CPhysicsBodyGameMarkupData")
                .GetSubCollection("game_keys");
            var remaining = remainingData.GetSubCollection("m_PhysicsBodyMarkupByBoneName");
            await Assert.That(remaining.Count).IsEqualTo(1);
            await Assert.That(remaining.GetSubCollection("unmatched_body").GetStringProperty("m_Tag")).IsEqualTo("unmatched_tag");
            await Assert.That(remainingData.GetInt32Property("extra_metadata")).IsEqualTo(42);
        }
        else
        {
            await Assert.That(text).DoesNotContain("CPhysicsBodyGameMarkupData");
        }

        await Assert.That(extract.ToValveModel()).IsEqualTo(text);
        await Assert.That(model.KeyValues.ToKV3String()).IsEqualTo(originalKeyValues);
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
