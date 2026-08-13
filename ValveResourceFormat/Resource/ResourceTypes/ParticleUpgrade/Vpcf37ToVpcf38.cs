using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Removes m_bViewModelEffect from every particle system definition, writing
/// m_nViewModelEffect as inheritable-true only when the flag was set on a definition that is
/// not also a screen-space effect.
/// </summary>
internal sealed class Vpcf37ToVpcf38 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf37";
    public override string ToFormat => "vpcf38";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "CParticleSystemDefinition")
            {
                return;
            }

            var viewModel = node.GetBool("m_bViewModelEffect", false);
            var screenSpace = node.GetBool("m_bScreenSpaceEffect", false);
            node.Remove("m_bViewModelEffect");

            if (viewModel && !screenSpace)
            {
                node.SetString("m_nViewModelEffect", "INHERITABLE_BOOL_TRUE");
            }
        });
    }
}
