using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Moves pre-emission operator classes from the root's m_Operators array into
/// m_PreEmissionOperators, which is created as an empty array when absent.
/// </summary>
internal sealed class Vpcf1ToVpcf2 : ParticleUpgradeStep
{
    private static readonly HashSet<string> PreEmissionClasses =
    [
        "C_OP_RemapSpeedtoCP",
        "C_OP_RemapModelVolumetoCP",
        "C_OP_RemapBoundingVolumetoCP",
        "C_OP_RemapAverageScalarValuetoCP",
        "C_OP_RampCPLinearRandom",
        "C_OP_SetParentControlPointsToChildCP",
        "C_OP_SetControlPointPositions",
        "C_OP_SetSingleControlPointPosition",
        "C_OP_SetRandomControlPointPosition",
        "C_OP_SetControlPointOrientation",
        "C_OP_SetControlPointFromObjectScale",
        "C_OP_DistanceBetweenCPsToCP",
        "C_OP_SetControlPointToPlayer",
        "C_OP_SetControlPointToHand",
        "C_OP_SetControlPointToHMD",
        "C_OP_SetControlPointPositionToTimeOfDayValue",
        "C_OP_SetControlPointToCenter",
        "C_OP_StopAfterCPDuration",
        "C_OP_SetControlPointRotation",
        "C_OP_RemapCPtoCP",
        "C_OP_HSVShiftToCP",
        "C_OP_SetControlPointToImpactPoint",
        "C_OP_SetCPOrientationToPointAtCP",
        "C_OP_EnableChildrenFromParentParticleCount",
        "C_OP_DriveCPFromGlobalSoundFloat",
        "C_OP_SetControlPointFieldToWater",
    ];

    public override string FromFormat => "vpcf1";
    public override string ToFormat => "vpcf2";

    public override void Apply(KVObject root)
    {
        var pre = root.Find("m_PreEmissionOperators");

        if (pre == null)
        {
            pre = KVObject.Array();
            root.Add("m_PreEmissionOperators", pre);
        }

        var ops = root.Find("m_Operators");

        if (ops == null)
        {
            return;
        }

        var moved = new List<KVObject>();
        var kept = new List<KVObject>();

        foreach (var element in ops.Elements())
        {
            var isMatch = UpgradeKV.IsObject(element) && PreEmissionClasses.Contains(element.GetString("_class", ""));
            (isMatch ? moved : kept).Add(element);
        }

        if (moved.Count == 0)
        {
            return;
        }

        root.SetMember("m_Operators", KVObject.Array(kept));
        root.SetMember("m_PreEmissionOperators", KVObject.Array([.. moved, .. pre.Elements()]));
    }
}
