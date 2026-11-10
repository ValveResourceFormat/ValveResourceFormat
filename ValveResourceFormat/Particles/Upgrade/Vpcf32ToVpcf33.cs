using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Upgrade;

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
            var cls = node.GetStringProperty("_class", "");

            if (cls is not ("C_INIT_PositionPlaceOnGround" or "C_OP_MovementPlaceOnGround"))
            {
                return;
            }

            var kill = node.GetParticleBooleanProperty("m_bKill");
            node.Remove("bKill");
            node.SetString("m_nTraceMissBehavior", kill
                ? "PARTICLE_TRACE_MISS_BEHAVIOR_KILL"
                : "PARTICLE_TRACE_MISS_BEHAVIOR_TRACE_END");
        });
    }
}
