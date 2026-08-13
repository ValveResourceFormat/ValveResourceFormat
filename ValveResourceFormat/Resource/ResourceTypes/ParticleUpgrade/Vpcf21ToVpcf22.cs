using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Converts C_OP_RenderTrails radius taper and scale scalars into literal float inputs,
/// storing the reciprocal taper and the taper-premultiplied scale. A non-literal radius
/// scale input or a zero scale with non-zero taper leaves the renderer untouched.
/// </summary>
internal sealed class Vpcf21ToVpcf22 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf21";
    public override string ToFormat => "vpcf22";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_OP_RenderTrails")
            {
                return;
            }

            var taper = node.GetFloat("m_flRadiusTaper", 1f);
            var radiusScale = node.Find("m_flRadiusScale");
            float scale;

            if (radiusScale is { IsCollection: true })
            {
                if (radiusScale.GetString("m_nType", "PF_TYPE_LITERAL") != "PF_TYPE_LITERAL")
                {
                    return;
                }

                scale = radiusScale.GetFloat("m_flLiteralValue", 1f);
            }
            else
            {
                scale = node.GetFloat("m_flRadiusScale", 1f);
            }

            float newTaper;
            float newScale;

            if (taper == 0f)
            {
                newTaper = 100f;
                newScale = scale * 0.01f;
            }
            else if (scale != 0f)
            {
                newTaper = 1f / taper;
                newScale = scale * taper;
            }
            else
            {
                return;
            }

            node.Remove("m_flRadiusTaper");
            node.Remove("m_flRadiusScale");

            var taperInput = node.SetObject("m_flRadiusTaper");
            taperInput.SetString("m_nType", "PF_TYPE_LITERAL");
            taperInput.SetFloat("m_flLiteralValue", newTaper);

            var scaleInput = node.SetObject("m_flRadiusScale");
            scaleInput.SetString("m_nType", "PF_TYPE_LITERAL");
            scaleInput.SetFloat("m_flLiteralValue", newScale);
        });
    }
}
