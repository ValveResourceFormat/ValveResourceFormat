using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Forces m_bForceLoopingAnimation to true on every C_OP_RenderModels renderer at the root,
/// overwriting an authored value.
/// </summary>
internal sealed class Vpcf15ToVpcf16 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf15";
    public override string ToFormat => "vpcf16";

    public override void Apply(KVObject root)
    {
        foreach (var element in root.ElementsOf("m_Renderers"))
        {
            if (UpgradeKV.IsObject(element) && element.GetString("_class", "") == "C_OP_RenderModels")
            {
                element.SetBool("m_bForceLoopingAnimation", true);
            }
        }
    }
}
