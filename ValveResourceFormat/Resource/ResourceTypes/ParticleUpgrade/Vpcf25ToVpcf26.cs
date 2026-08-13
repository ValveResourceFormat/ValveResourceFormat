using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Converts the integer m_nCollisionMode on C_OP_WorldTraceConstraint root constraints into
/// its COLLISION_MODE enum string, re-appended at the object tail.
/// </summary>
internal sealed class Vpcf25ToVpcf26 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf25";
    public override string ToFormat => "vpcf26";

    public override void Apply(KVObject root)
    {
        foreach (var element in root.ElementsOf("m_Constraints"))
        {
            if (!UpgradeKV.IsObject(element) || element.GetString("_class", "") != "C_OP_WorldTraceConstraint")
            {
                continue;
            }

            if (!element.ContainsKey("m_nCollisionMode"))
            {
                continue;
            }

            var mode = element.GetInt("m_nCollisionMode", 0);
            element.Remove("m_nCollisionMode");
            element.SetString("m_nCollisionMode", mode switch
            {
                1 => "COLLISION_MODE_PER_FRAME_PLANESET",
                2 => "COLLISION_MODE_INITIAL_TRACE_DOWN",
                3 or 4 => "COLLISION_MODE_USE_NEAREST_TRACE",
                _ => "COLLISION_MODE_PER_PARTICLE_TRACE",
            });
        }
    }
}
