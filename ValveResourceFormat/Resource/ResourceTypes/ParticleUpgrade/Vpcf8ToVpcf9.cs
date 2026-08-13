using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Bumps m_nBehaviorVersion from 5 to 6 and deletes m_nFirstMultipleOverride_BackwardCompat
/// when every root initializer matches the multiple-override predicate.
/// </summary>
internal sealed class Vpcf8ToVpcf9 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf8";
    public override string ToFormat => "vpcf9";

    public override void Apply(KVObject root)
    {
        if (root.GetInt("m_nBehaviorVersion", 0) != 5)
        {
            return;
        }

        foreach (var element in root.ElementsOf("m_Initializers"))
        {
            if (!Vpcf7ToVpcf8.IsMultipleOverride(element))
            {
                return;
            }
        }

        root.SetInt("m_nBehaviorVersion", 6);
        root.Remove("m_nFirstMultipleOverride_BackwardCompat");
    }
}
