using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Writes m_nTraceMissBehavior on the place-on-ground initializer and operator from the
/// m_bKill flag, which itself survives; only the unprefixed bKill spelling is removed.
/// </summary>
internal sealed class Vpcf32ToVpcf33 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf32";
    public override string ToFormat => "vpcf33";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            var cls = node.GetString("_class", "");

            if (cls is not ("C_INIT_PositionPlaceOnGround" or "C_OP_MovementPlaceOnGround"))
            {
                return;
            }

            var kill = node.GetBool("m_bKill", false);
            node.Remove("bKill");
            node.SetString("m_nTraceMissBehavior", kill
                ? "PARTICLE_TRACE_MISS_BEHAVIOR_KILL"
                : "PARTICLE_TRACE_MISS_BEHAVIOR_TRACE_END");
        });
    }
}
