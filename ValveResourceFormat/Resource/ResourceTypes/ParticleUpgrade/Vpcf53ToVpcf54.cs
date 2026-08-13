using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Removes m_nViewModelEffect from particle system definitions that are screen-space effects
/// with the viewmodel flag set to INHERITABLE_BOOL_TRUE; the screen-space bool itself stays.
/// </summary>
internal sealed class Vpcf53ToVpcf54 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf53";
    public override string ToFormat => "vpcf54";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "CParticleSystemDefinition")
            {
                return;
            }

            if (node.GetBool("m_bScreenSpaceEffect", false)
                && node.GetString("m_nViewModelEffect", "INHERITABLE_BOOL_INHERIT") == "INHERITABLE_BOOL_TRUE")
            {
                node.Remove("m_nViewModelEffect");
            }
        });
    }
}
