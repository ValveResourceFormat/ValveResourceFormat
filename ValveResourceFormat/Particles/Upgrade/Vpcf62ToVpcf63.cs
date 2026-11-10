using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Upgrade;

/// <summary>
/// Folds C_OP_RemapAverageScalarValuetoCP's four remap scalars into a single m_flOutputRemap
/// input object holding an invalid-typed remap mapping.
/// </summary>
internal sealed class Vpcf62ToVpcf63 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf62";
    public override string ToFormat => "vpcf63";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetStringProperty("_class", "") != "C_OP_RemapAverageScalarValuetoCP")
            {
                return;
            }

            var inputMin = node.GetFloatProperty("m_flInputMin");
            var inputMax = node.GetFloatProperty("m_flInputMax", 1f);
            var outputMin = node.GetFloatProperty("m_flOutputMin");
            var outputMax = node.GetFloatProperty("m_flOutputMax", 1f);
            node.Remove("m_flInputMin");
            node.Remove("m_flInputMax");
            node.Remove("m_flOutputMin");
            node.Remove("m_flOutputMax");

            var remap = node.SetObject("m_flOutputRemap");
            remap.SetString("m_nType", "PF_TYPE_INVALID");
            remap.SetString("m_nMapType", "PF_MAP_TYPE_REMAP");
            remap.SetFloat("m_flInput0", inputMin);
            remap.SetFloat("m_flInput1", inputMax);
            remap.SetFloat("m_flOutput0", outputMin);
            remap.SetFloat("m_flOutput1", outputMax);
        });
    }
}
