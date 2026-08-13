using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Migrates C_INIT_CreateSpiralSphere: the control point folds into m_TransformInput with no
/// negative clamp, a non-zero m_nDensity becomes a literal m_flDensity input, and a consumed
/// m_nOverrideCP other than minus one rebuilds radius, density and both speeds as CP-component
/// mult inputs on that control point.
/// </summary>
internal sealed class Vpcf64ToVpcf65 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf64";
    public override string ToFormat => "vpcf65";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_INIT_CreateSpiralSphere")
            {
                return;
            }

            var cp = unchecked((int)node.TakeInt("m_nControlPointNumber", 0));
            Vpcf45ToVpcf46.FillTransformInput(node.MergeObject("m_TransformInput"), cp);

            var density = node.TakeInt("m_nDensity", 0);

            if (density != 0)
            {
                var input = node.SetObject("m_flDensity");
                input.SetString("m_nType", "PF_TYPE_LITERAL");
                input.SetFloat("m_flLiteralValue", density);
            }

            if (!node.ContainsKey("m_nOverrideCP"))
            {
                return;
            }

            var overrideCP = unchecked((int)node.TakeInt("m_nOverrideCP", 0));

            if (overrideCP == -1)
            {
                return;
            }

            var radius = node.GetFloat("m_flInitialRadius", 0f);
            var speedMin = node.GetFloat("m_flInitialSpeedMin", 0f);
            var speedMax = node.GetFloat("m_flInitialSpeedMax", 0f);
            node.Remove("m_flInitialRadius");
            node.Remove("m_flInitialSpeedMin");
            node.Remove("m_flInitialSpeedMax");

            SetCpComponent(node.SetObject("m_flInitialRadius"), overrideCP, 0, radius);
            SetCpComponent(node.SetObject("m_flDensity"), overrideCP, 1, density);
            SetCpComponent(node.SetObject("m_flInitialSpeedMin"), overrideCP, 2, speedMin);
            SetCpComponent(node.SetObject("m_flInitialSpeedMax"), overrideCP, 2, speedMax);
        });
    }

    private static void SetCpComponent(KVObject input, int cp, int component, float factor)
    {
        input.SetString("m_nType", "PF_TYPE_CONTROL_POINT_COMPONENT");
        input.SetInt("m_nControlPoint", cp);
        input.SetInt("m_nVectorComponent", component);
        input.SetString("m_nMapType", "PF_MAP_TYPE_MULT");
        input.SetFloat("m_flMultFactor", factor);
    }
}
