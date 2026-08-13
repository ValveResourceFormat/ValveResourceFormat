using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Runs three document walks: C_INIT_RingWave scalars become float inputs keyed on the two
/// override CPs, C_INIT_CreateWithinSphere scalars and local-speed vectors become inputs when
/// a scale CP is set, and C_OP_CurlNoiseForce's m_useCurl flag becomes the curl noise type.
/// </summary>
internal sealed class Vpcf26ToVpcf27 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf26";
    public override string ToFormat => "vpcf27";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") == "C_INIT_RingWave")
            {
                ConvertRingWave(node);
            }
        });

        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") == "C_INIT_CreateWithinSphere")
            {
                ConvertCreateWithinSphere(node);
            }
        });

        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") == "C_OP_CurlNoiseForce" && node.GetBool("m_useCurl", false))
            {
                node.Remove("m_useCurl");
                node.SetString("m_nNoiseType", "PARTICLE_DIR_NOISE_CURL");
            }
        });
    }

    private static void SetLiteral(KVObject input, float value)
    {
        input.SetString("m_nType", "PF_TYPE_LITERAL");
        input.SetFloat("m_flLiteralValue", value);
    }

    private static void SetCpComponent(KVObject input, long cp, int component, float factor)
    {
        input.SetString("m_nType", "PF_TYPE_CONTROL_POINT_COMPONENT");
        input.SetInt("m_nControlPoint", (int)cp);
        input.SetInt("m_nVectorComponent", component);
        input.SetString("m_nMapType", "PF_MAP_TYPE_MULT");
        input.SetFloat("m_flMultFactor", factor);
    }

    private static void ConvertRingWave(KVObject node)
    {
        var radius = node.GetFloat("m_flInitialRadius", 0f);
        var thickness = node.GetFloat("m_flThickness", 0f);
        var speedMin = node.GetFloat("m_flInitialSpeedMin", 0f);
        var speedMax = node.GetFloat("m_flInitialSpeedMax", 0f);
        var roll = node.GetFloat("m_flRoll", 0f);
        var pitch = node.GetFloat("m_flPitch", 0f);
        var yaw = node.GetFloat("m_flYaw", 0f);
        var overrideCP = node.GetInt("m_nOverrideCP", -1);
        var overrideCP2 = node.GetInt("m_nOverrideCP2", -1);

        node.Remove("m_flInitialRadius");
        node.Remove("m_flThickness");
        node.Remove("m_flInitialSpeedMin");
        node.Remove("m_flInitialSpeedMax");
        node.Remove("m_flRoll");
        node.Remove("m_flPitch");
        node.Remove("m_flYaw");
        node.Remove("m_nOverrideCP");
        node.Remove("m_nOverrideCP2");

        var radiusInput = node.SetObject("m_flInitialRadius");
        var thicknessInput = node.SetObject("m_flThickness");
        var speedMinInput = node.SetObject("m_flInitialSpeedMin");
        var speedMaxInput = node.SetObject("m_flInitialSpeedMax");
        var rollInput = node.SetObject("m_flRoll");
        var pitchInput = node.SetObject("m_flPitch");
        var yawInput = node.SetObject("m_flYaw");

        if (overrideCP <= -1)
        {
            SetLiteral(radiusInput, radius);
            SetLiteral(thicknessInput, thickness);
            SetLiteral(speedMinInput, speedMin);
            SetLiteral(speedMaxInput, speedMax);
        }
        else
        {
            SetCpComponent(radiusInput, overrideCP, 0, radius);
            SetCpComponent(thicknessInput, overrideCP, 1, thickness);
            SetCpComponent(speedMinInput, overrideCP, 2, speedMin);
            SetCpComponent(speedMaxInput, overrideCP, 2, speedMax);
        }

        if (overrideCP2 <= -1)
        {
            SetLiteral(rollInput, roll);
            SetLiteral(pitchInput, pitch);
            SetLiteral(yawInput, yaw);
        }
        else
        {
            SetCpComponent(pitchInput, overrideCP2, 0, pitch);
            SetCpComponent(yawInput, overrideCP2, 1, yaw);
            SetCpComponent(rollInput, overrideCP2, 2, roll);
        }
    }

    private static void ConvertCreateWithinSphere(KVObject node)
    {
        var scaleCP = node.GetInt("m_nScaleCP", -1);

        if (scaleCP == -1)
        {
            return;
        }

        var radiusMin = node.GetFloat("m_fRadiusMin", 0f);
        var radiusMax = node.GetFloat("m_fRadiusMax", 0f);
        var speedMin = node.GetFloat("m_fSpeedMin", 0f);
        var speedMax = node.GetFloat("m_fSpeedMax", 0f);
        var localMin = node.GetFloat3("m_LocalCoordinateSystemSpeedMin", Vector3.Zero);
        var localMax = node.GetFloat3("m_LocalCoordinateSystemSpeedMax", Vector3.Zero);

        node.Remove("m_fRadiusMin");
        node.Remove("m_fRadiusMax");
        node.Remove("m_fSpeedMin");
        node.Remove("m_fSpeedMax");
        node.Remove("m_LocalCoordinateSystemSpeedMin");
        node.Remove("m_LocalCoordinateSystemSpeedMax");

        var radiusMinInput = node.SetObject("m_fRadiusMin");
        var radiusMaxInput = node.SetObject("m_fRadiusMax");
        var speedMinInput = node.SetObject("m_fSpeedMin");
        var speedMaxInput = node.SetObject("m_fSpeedMax");
        var localMinInput = node.SetObject("m_LocalCoordinateSystemSpeedMin");
        var localMaxInput = node.SetObject("m_LocalCoordinateSystemSpeedMax");

        if (scaleCP <= -1)
        {
            SetLiteral(radiusMinInput, radiusMin);
            SetLiteral(radiusMaxInput, radiusMax);
            SetLiteral(speedMinInput, speedMin);
            SetLiteral(speedMaxInput, speedMax);

            localMinInput.SetString("m_nType", "PVEC_TYPE_LITERAL");
            localMinInput.SetFloatArray("m_vLiteralValue", localMin.X, localMin.Y, localMin.Z);
            localMaxInput.SetString("m_nType", "PVEC_TYPE_LITERAL");
            localMaxInput.SetFloatArray("m_vLiteralValue", localMax.X, localMax.Y, localMax.Z);
        }
        else
        {
            SetCpComponent(radiusMinInput, scaleCP, 0, radiusMin);
            SetCpComponent(radiusMaxInput, scaleCP, 0, radiusMax);
            SetCpComponent(speedMinInput, scaleCP, 1, speedMin);
            SetCpComponent(speedMaxInput, scaleCP, 1, speedMax);
            SetFloatComponents(localMinInput, scaleCP, localMin);
            SetFloatComponents(localMaxInput, scaleCP, localMax);
        }
    }

    private static void SetFloatComponents(KVObject input, long cp, Vector3 value)
    {
        input.SetString("m_nType", "PVEC_TYPE_FLOAT_COMPONENTS");
        SetCpComponent(input.SetObject("m_FloatComponentX"), cp, 2, value.X);
        SetCpComponent(input.SetObject("m_FloatComponentY"), cp, 2, value.Y);
        SetCpComponent(input.SetObject("m_FloatComponentZ"), cp, 2, value.Z);
    }
}
