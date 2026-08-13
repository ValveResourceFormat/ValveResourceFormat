using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Replaces C_OP_PinParticleToCP's m_nParticleNumber with a literal-zero float input appended
/// at the operator tail unless the particle selection is PARTICLE_SELECTION_NUMBER, which
/// keeps the authored value untouched.
/// </summary>
internal sealed class Vpcf55ToVpcf56 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf55";
    public override string ToFormat => "vpcf56";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_OP_PinParticleToCP")
            {
                return;
            }

            if (node.GetString("m_nParticleSelection", "PARTICLE_SELECTION_FIRST") == "PARTICLE_SELECTION_NUMBER")
            {
                return;
            }

            node.Remove("m_nParticleNumber");
            var input = node.SetObject("m_nParticleNumber");
            input.SetString("m_nType", "PF_TYPE_LITERAL");
            input.SetFloat("m_flLiteralValue", 0f);
        });
    }
}
