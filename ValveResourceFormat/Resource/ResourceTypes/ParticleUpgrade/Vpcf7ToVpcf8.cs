using System.Linq;
using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Stable-partitions the root's m_Initializers so multiple-override initializers move to the
/// tail, and records the split index in m_nFirstMultipleOverride_BackwardCompat.
/// </summary>
internal sealed class Vpcf7ToVpcf8 : ParticleUpgradeStep
{
    private static readonly HashSet<string> AlwaysOverrideClasses =
    [
        "C_INIT_CreateOnModelAtHeight",
        "C_INIT_SetHitboxToClosest",
        "C_INIT_PositionOffset",
        "C_INIT_PositionOffsetToCP",
        "C_INIT_PositionPlaceOnGround",
        "C_INIT_VelocityFromNormal",
        "C_INIT_VelocityRandom",
        "C_INIT_InitialVelocityNoise",
        "C_INIT_InitialVelocityFromHitbox",
        "C_INIT_VelocityRadialRandom",
        "C_INIT_RandomVectorComponent",
        "C_INIT_AddVectorToVector",
        "C_INIT_SequenceFromCP",
        "C_INIT_PositionWarp",
        "C_INIT_RemapScalar",
        "C_INIT_RemapParticleCountToScalar",
        "C_INIT_InheritVelocity",
        "C_INIT_VelocityFromCP",
        "C_INIT_AgeNoise",
        "C_INIT_SequenceLifeTime",
        "C_INIT_RemapScalarToVector",
        "C_INIT_OffsetVectorToVector",
        "C_INIT_InitialRepulsionVelocity",
        "C_INIT_RandomYawFlip",
        "C_INIT_RemapCPtoScalar",
        "C_INIT_RemapCPtoVector",
        "C_INIT_RemapTransformtoVector",
        "C_INIT_InheritFromParentParticles",
        "C_INIT_DistanceToCPInit",
        "C_INIT_LifespanFromVelocity",
        "C_INIT_ModelCull",
        "C_INIT_RtEnvCull",
        "C_INIT_NormalOffset",
        "C_INIT_RemapSpeedToScalar",
        "C_OP_InitSetSnapshotOnCP",
        "C_INIT_SetRigidAttachment",
        "C_INIT_RemapInitialVisibilityScalar",
        "C_INIT_GlobalScale",
        "C_INIT_MakeShapes",
        "C_INIT_RemapNamedModelElementToScalar",
    ];

    /// <summary>
    /// Matches initializers that act as multiple overrides: class membership alone for most
    /// classes, plus field-conditioned entries for epitrochoid offset, trail bias and
    /// CP-snapshot attribute writes.
    /// </summary>
    internal static bool IsMultipleOverride(KVObject element)
    {
        if (!UpgradeKV.IsObject(element))
        {
            return false;
        }

        return element.GetString("_class", "") switch
        {
            "C_INIT_CreateInEpitrochoid" => element.GetBool("m_bOffsetExistingPos", false),
            "C_INIT_MoveBetweenPoints" => element.GetBool("m_bTrailBias", false),
            "C_INIT_InitFromCPSnapshot" => element.GetInt("m_nAttributeToWrite", 0) == 2,
            var cls => AlwaysOverrideClasses.Contains(cls),
        };
    }

    public override string FromFormat => "vpcf7";
    public override string ToFormat => "vpcf8";

    public override void Apply(KVObject root)
    {
        var initializers = root.Find("m_Initializers");

        if (initializers == null)
        {
            return;
        }

        var elements = initializers.Elements();
        var matching = elements.Where(IsMultipleOverride).ToList();

        if (matching.Count == 0)
        {
            return;
        }

        var nonMatching = elements.Where(element => !IsMultipleOverride(element)).ToList();
        root.SetMember("m_Initializers", KVObject.Array([.. nonMatching, .. matching]));
        root.SetInt("m_nFirstMultipleOverride_BackwardCompat", nonMatching.Count);
    }
}
