using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Converts C_OP_RepeatedTriggerChildGroup min/max scalar pairs into varying random-uniform
/// float inputs for refire time, cluster size and cooldown.
/// </summary>
internal sealed class Vpcf11ToVpcf12 : ParticleUpgradeStep
{
    private static readonly (string Name, string Min, string Max)[] Pairs =
    [
        ("m_flClusterRefireTime", "m_flClusterRefireTimeMin", "m_flClusterRefireTimeMax"),
        ("m_flClusterSize", "m_nClusterSizeMin", "m_nClusterSizeMax"),
        ("m_flClusterCooldown", "m_flClusterCooldownMin", "m_flClusterCooldownMax"),
    ];

    public override string FromFormat => "vpcf11";
    public override string ToFormat => "vpcf12";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_OP_RepeatedTriggerChildGroup")
            {
                return;
            }

            var values = new (float Min, float Max)[Pairs.Length];

            for (var i = 0; i < Pairs.Length; i++)
            {
                values[i] = (node.GetFloat(Pairs[i].Min, 0f), node.GetFloat(Pairs[i].Max, 0f));
            }

            foreach (var (_, min, max) in Pairs)
            {
                node.Remove(min);
                node.Remove(max);
            }

            for (var i = 0; i < Pairs.Length; i++)
            {
                var input = node.SetObject(Pairs[i].Name);
                input.SetString("m_nType", "PF_TYPE_RANDOM_UNIFORM");
                input.SetFloat("m_flRandomMin", values[i].Min);
                input.SetFloat("m_flRandomMax", values[i].Max);
                input.SetString("m_nRandomMode", "PF_RANDOM_MODE_VARYING");
            }
        });
    }
}
