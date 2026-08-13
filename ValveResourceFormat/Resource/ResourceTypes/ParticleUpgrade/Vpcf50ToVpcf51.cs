using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Repairs C_OP_InstantaneousEmitter float inputs whose m_nRandomMode holds the type enum
/// string PF_TYPE_RANDOM_UNIFORM, resetting the mode to PF_RANDOM_MODE_CONSTANT.
/// </summary>
internal sealed class Vpcf50ToVpcf51 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf50";
    public override string ToFormat => "vpcf51";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_OP_InstantaneousEmitter")
            {
                return;
            }

            RepairRandomMode(node, "m_nParticlesToEmit");
            RepairRandomMode(node, "m_flStartTime");
        });
    }

    private static void RepairRandomMode(KVObject node, string name)
    {
        var input = node.Find(name);

        if (input is not { IsCollection: true }
            || input.GetString("m_nType", "PF_TYPE_LITERAL") != "PF_TYPE_RANDOM_UNIFORM")
        {
            return;
        }

        if (input.GetString("m_nRandomMode", "PF_RANDOM_MODE_CONSTANT") == "PF_TYPE_RANDOM_UNIFORM")
        {
            input.SetString("m_nRandomMode", "PF_RANDOM_MODE_CONSTANT");
        }
    }
}
