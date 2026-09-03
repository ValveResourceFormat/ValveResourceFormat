using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the model doc nodes for the jiggle bones a compiled FeModel carries.
/// </summary>
partial class ModelExtract
{
    [Flags]
    private enum FeJiggleBoneFlags
    {
        Flexible = 0x1,
        Rigid = 0x2,
        YawConstraint = 0x4,
        PitchConstraint = 0x8,
        AngleConstraint = 0x10,
        LengthConstraint = 0x20,
        BaseSpring = 0x40,
        InvertAxes = 0x80,
        Collision = 0x300,
    }

    private enum JiggleBoneType
    {
        Rigid = 0,
        Flexible = 1,
        Neither = 2,
    }

    static KVObject? ProcessJiggleBone(FeModel.IndexedJiggleBone indexedJiggleBone, string[] controlNames)
    {
        var nodeIndex = indexedJiggleBone.Node;
        if (nodeIndex < 0 || nodeIndex >= controlNames.Length)
        {
            return null;
        }

        var jiggleBone = indexedJiggleBone.Bone;
        var flags = (FeJiggleBoneFlags)jiggleBone.Flags;

        var type = JiggleBoneType.Neither;
        if (flags.HasFlag(FeJiggleBoneFlags.Rigid))
        {
            type = JiggleBoneType.Rigid;
        }
        else if (flags.HasFlag(FeJiggleBoneFlags.Flexible))
        {
            type = JiggleBoneType.Flexible;
        }

        var name = controlNames[nodeIndex];

        return MakeNode("JiggleBone",
            ("name", name),
            ("jiggle_root_bone", name),
            ("jiggle_type", (int)type),
            ("has_yaw_constraint", flags.HasFlag(FeJiggleBoneFlags.YawConstraint)),
            ("has_pitch_constraint", flags.HasFlag(FeJiggleBoneFlags.PitchConstraint)),
            ("has_angle_constraint", flags.HasFlag(FeJiggleBoneFlags.AngleConstraint)),
            ("has_base_spring", flags.HasFlag(FeJiggleBoneFlags.BaseSpring)),
            ("allow_flex_length", !flags.HasFlag(FeJiggleBoneFlags.LengthConstraint)),
            ("invert_axes", flags.HasFlag(FeJiggleBoneFlags.InvertAxes)),
            ("has_collision", (flags & FeJiggleBoneFlags.Collision) != 0),
            ("length", jiggleBone.Length),
            ("tip_mass", jiggleBone.TipMass),
            ("angle_limit", float.RadiansToDegrees(jiggleBone.AngleLimit)),
            ("min_yaw", float.RadiansToDegrees(jiggleBone.MinYaw)),
            ("max_yaw", float.RadiansToDegrees(jiggleBone.MaxYaw)),
            ("yaw_friction", jiggleBone.YawFriction),
            ("yaw_bounce", jiggleBone.YawBounce),
            ("min_pitch", float.RadiansToDegrees(jiggleBone.MinPitch)),
            ("max_pitch", float.RadiansToDegrees(jiggleBone.MaxPitch)),
            ("pitch_friction", jiggleBone.PitchFriction),
            ("pitch_bounce", jiggleBone.PitchBounce),
            ("base_mass", jiggleBone.BaseMass),
            ("base_stiffness", jiggleBone.BaseStiffness),
            ("base_damping", jiggleBone.BaseDamping),
            ("base_left_min", jiggleBone.BaseMinLeft),
            ("base_left_max", jiggleBone.BaseMaxLeft),
            ("base_left_friction", jiggleBone.BaseLeftFriction),
            ("base_up_min", jiggleBone.BaseMinUp),
            ("base_up_max", jiggleBone.BaseMaxUp),
            ("base_up_friction", jiggleBone.BaseUpFriction),
            ("base_forward_min", jiggleBone.BaseMinForward),
            ("base_forward_max", jiggleBone.BaseMaxForward),
            ("base_forward_friction", jiggleBone.BaseForwardFriction),
            ("yaw_stiffness", jiggleBone.YawStiffness),
            ("yaw_damping", jiggleBone.YawDamping),
            ("pitch_stiffness", jiggleBone.PitchStiffness),
            ("pitch_damping", jiggleBone.PitchDamping),
            ("along_stiffness", jiggleBone.AlongStiffness),
            ("along_damping", jiggleBone.AlongDamping),
            ("radius0", jiggleBone.Radius0),
            ("radius1", jiggleBone.Radius1),
            ("point0", ToKVArray(jiggleBone.Point0)),
            ("point1", ToKVArray(jiggleBone.Point1))
        );
    }

    static KVObject? ExtractJiggleBones(FeModel? feModel)
    {
        if (feModel is null || feModel.JiggleBones.Length == 0)
        {
            return null;
        }

        var children = KVObject.Array();

        foreach (var indexedJiggleBone in feModel.JiggleBones)
        {
            var node = ProcessJiggleBone(indexedJiggleBone, feModel.CtrlNames);
            if (node != null)
            {
                children.Add(node);
            }
        }

        return children.Count > 0 ? MakeNode("JiggleBoneList", ("children", children)) : null;
    }
}
