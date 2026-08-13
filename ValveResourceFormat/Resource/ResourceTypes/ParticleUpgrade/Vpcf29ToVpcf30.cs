using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Converts integer m_nOpEndCapState values on every object into PARTICLE_ENDCAP strings in
/// place, forcing out-of-range values to off, then replaces m_flHitBoxScale on the hitbox
/// initializers with the m_vecHitBoxScale literal vector holding two-times-scale-minus-one.
/// </summary>
internal sealed class Vpcf29ToVpcf30 : ParticleUpgradeStep
{
    private static readonly HashSet<string> HitboxClasses =
    [
        "C_INIT_CreateOnModel",
        "C_INIT_CreateOnModelAtHeight",
        "C_INIT_SetHitboxToClosest",
        "C_INIT_SetHitboxToModel",
    ];

    public override string FromFormat => "vpcf29";
    public override string ToFormat => "vpcf30";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            var value = node.Find("m_nOpEndCapState");

            if (value == null)
            {
                return;
            }

            var state = node.GetInt("m_nOpEndCapState", 0);

            if (state is < -1 or > 1)
            {
                node.SetString("m_nOpEndCapState", "PARTICLE_ENDCAP_ENDCAP_OFF");
            }
            else if (value.ValueType != KVValueType.String)
            {
                node.SetString("m_nOpEndCapState", state switch
                {
                    -1 => "PARTICLE_ENDCAP_ALWAYS_ON",
                    0 => "PARTICLE_ENDCAP_ENDCAP_OFF",
                    _ => "PARTICLE_ENDCAP_ENDCAP_ON",
                });
            }
        });

        UpgradeKV.WalkObjects(root, static node =>
        {
            if (!HitboxClasses.Contains(node.GetString("_class", "")))
            {
                return;
            }

            var scale = node.GetFloat("m_flHitBoxScale", 1f);
            node.Remove("m_flHitBoxScale");

            var doubled = (scale - 0.5f) + (scale - 0.5f);
            var input = node.SetObject("m_vecHitBoxScale");
            input.SetString("m_nType", "PVEC_TYPE_LITERAL");
            input.SetFloatArray("m_vLiteralValue", doubled, doubled, doubled);
        });
    }
}
