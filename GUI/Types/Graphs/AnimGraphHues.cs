using GUI.Types.Graphs.Core;

namespace GUI.Types.Graphs;

/// <summary>
/// Functional bucket an animation graph node belongs to. AG1 and AG2 describe the same domain
/// with different class vocabularies, so both map onto this one set and a node of a given kind
/// reads the same colour in either viewer.
/// </summary>
enum AnimGraphCategory
{
    Other,
    Comment,
    StateMachine,
    Additive,
    BoneMask,
    Output,
    Timing,
    Constraint,
    Blend,
    Blend2D,
    Input,
    Selector,
    Motion,
    Clip,
    AimMatrix,

    /// <summary>Points at another file. Reserved, never shared with a category or a data type.</summary>
    ExternalReference,
}

/// <summary>
/// The pose and value kinds an animation graph wire carries. AG2 bakes these into every node
/// constructor as pin types; AG1 expresses the same things through its parameter types.
/// </summary>
enum AnimGraphValueKind
{
    Pose,
    Float,
    Bool,
    Id,
    Target,
    BoneMask,
    Vector,
    Quaternion,
    Unknown,
}

/// <summary>
/// The shared animation graph palette: node headers are coloured by <see cref="AnimGraphCategory"/>,
/// sockets and wires by <see cref="AnimGraphValueKind"/>.
/// </summary>
static class AnimGraphHues
{
    public static GraphHue HueOf(AnimGraphCategory category) => category switch
    {
        AnimGraphCategory.Comment => GraphHue.Neutral,
        AnimGraphCategory.StateMachine => GraphHue.Slate,
        AnimGraphCategory.Additive => GraphHue.Maroon,
        AnimGraphCategory.BoneMask => GraphHue.Orange,
        AnimGraphCategory.Output => GraphHue.Amber,
        AnimGraphCategory.Timing => GraphHue.Olive,
        AnimGraphCategory.Constraint => GraphHue.Green,
        AnimGraphCategory.Blend => GraphHue.Emerald,
        AnimGraphCategory.Blend2D => GraphHue.Teal,
        AnimGraphCategory.Input => GraphHue.Cyan,
        AnimGraphCategory.Selector => GraphHue.Blue,
        AnimGraphCategory.Motion => GraphHue.Indigo,
        AnimGraphCategory.Clip => GraphHue.Purple,
        AnimGraphCategory.AimMatrix => GraphHue.Magenta,
        AnimGraphCategory.ExternalReference => GraphHue.Pink,
        _ => GraphHue.Neutral,
    };

    public static GraphHue HueOf(AnimGraphValueKind kind) => kind switch
    {
        AnimGraphValueKind.Pose => GraphHue.Green,
        AnimGraphValueKind.Float => GraphHue.Olive,
        AnimGraphValueKind.Bool => GraphHue.Blue,
        AnimGraphValueKind.Id => GraphHue.Amber,
        AnimGraphValueKind.Target => GraphHue.Cyan,
        AnimGraphValueKind.BoneMask => GraphHue.Orange,
        AnimGraphValueKind.Vector => GraphHue.Emerald,
        AnimGraphValueKind.Quaternion => GraphHue.Maroon,
        _ => GraphHue.Neutral,
    };

    /// <summary>The legend both animation graph viewers advertise, in reading order.</summary>
    public static IEnumerable<GraphLegendEntry> Legend()
    {
        yield return new("Pose flow", HueOf(AnimGraphValueKind.Pose), GraphLegendKind.Wire);
        yield return new("Clip / sequence", HueOf(AnimGraphCategory.Clip));
        yield return new("Blend", HueOf(AnimGraphCategory.Blend));
        yield return new("Blend 2D", HueOf(AnimGraphCategory.Blend2D));
        yield return new("Additive / layer", HueOf(AnimGraphCategory.Additive));
        yield return new("IK / constraint", HueOf(AnimGraphCategory.Constraint));
        yield return new("Aim / lean matrix", HueOf(AnimGraphCategory.AimMatrix));
        yield return new("Selector / choice", HueOf(AnimGraphCategory.Selector));
        yield return new("State machine", HueOf(AnimGraphCategory.StateMachine));
        yield return new("Motion / movement", HueOf(AnimGraphCategory.Motion));
        yield return new("Input / source pose", HueOf(AnimGraphCategory.Input));
        yield return new("Bone mask", HueOf(AnimGraphCategory.BoneMask));
        yield return new("Timing / speed", HueOf(AnimGraphCategory.Timing));
        yield return new("Output / root", HueOf(AnimGraphCategory.Output));
        yield return new("Subgraph / referenced file", HueOf(AnimGraphCategory.ExternalReference));
        yield return new("Transition", GraphHue.Slate, GraphLegendKind.DashedWire);
    }

    /// <summary>
    /// Buckets a compiled AG1 class (CXxxUpdateNode). The editor schema's CXxxAnimNode names are
    /// normalised to the compiled spelling before they get here.
    /// </summary>
    public static AnimGraphCategory CategoryOfAG1(string compiledClass) => compiledClass switch
    {
        "CSequenceUpdateNode" or "CSingleFrameUpdateNode" or "CCycleControlClipUpdateNode"
            or "CChoreoUpdateNode" or "CDirectPlaybackUpdateNode" => AnimGraphCategory.Clip,

        "CBlendUpdateNode" or "CDirectionalBlendUpdateNode" => AnimGraphCategory.Blend,
        "CBlend2DUpdateNode" => AnimGraphCategory.Blend2D,

        "CAddUpdateNode" or "CSubtractUpdateNode" => AnimGraphCategory.Additive,
        "CBoneMaskUpdateNode" => AnimGraphCategory.BoneMask,

        "CSolveIKChainUpdateNode" or "CTwoBoneIKUpdateNode" or "CFootPinningUpdateNode"
            or "CLookAtUpdateNode" or "CHitReactUpdateNode" or "CFootLockUpdateNode"
            or "CJiggleBoneUpdateNode" or "CFollowAttachmentUpdateNode"
            or "CSlowDownOnSlopesUpdateNode" => AnimGraphCategory.Constraint,

        "CAimMatrixUpdateNode" or "CLeanMatrixUpdateNode" => AnimGraphCategory.AimMatrix,

        "CSelectorUpdateNode" or "CChoiceUpdateNode" => AnimGraphCategory.Selector,
        "CStateMachineUpdateNode" => AnimGraphCategory.StateMachine,

        "CMotionMatchingUpdateNode" or "CMoverUpdateNode" or "CStopAtGoalUpdateNode"
            or "CPathHelperUpdateNode" or "CSetFacingUpdateNode" or "CTurnHelperUpdateNode"
            or "CFollowPathUpdateNode" => AnimGraphCategory.Motion,

        "CSkeletalInputUpdateNode" or "CInputStreamUpdateNode" => AnimGraphCategory.Input,

        "CSpeedScaleUpdateNode" or "CCycleControlUpdateNode"
            or "CFootStepTriggerUpdateNode" => AnimGraphCategory.Timing,

        "CRootUpdateNode" => AnimGraphCategory.Output,
        "CSubGraphUpdateNode" => AnimGraphCategory.ExternalReference,
        "CCommentUpdateNode" => AnimGraphCategory.Comment,

        _ => AnimGraphCategory.Other,
    };

    /// <summary>
    /// Buckets an AG2 node type, the class name with its CNm prefix and Node::CDefinition suffix
    /// already stripped by the viewer.
    /// </summary>
    public static AnimGraphCategory CategoryOfAG2(string nodeType) => nodeType switch
    {
        "Clip" or "ClipSelector" or "AnimationClipSelector" or "ParameterizedClipSelector"
            or "ParameterizedAnimationClipSelector" or "AnimationPose" => AnimGraphCategory.Clip,

        "Blend1D" or "ParameterizedBlend" or "VelocityBlend" or "BoneMaskBlend" => AnimGraphCategory.Blend,
        "Blend2D" => AnimGraphCategory.Blend2D,

        "LayerBlend" or "_LayerDefinition_" => AnimGraphCategory.Additive,

        "BoneMask" or "BoneMaskSelector" or "BoneMaskSwitch" or "BoneMaskValue"
            or "FixedWeightBoneMask" => AnimGraphCategory.BoneMask,

        "FootIK" or "TwoBoneIK" or "ChainLookat" or "FollowBone" or "SnapWeapon" => AnimGraphCategory.Constraint,

        "AimCS" => AnimGraphCategory.AimMatrix,

        "Selector" or "ParameterizedSelector" or "FloatSelector" or "IDSelector"
            or "TargetSelector" or "BoneMaskSelectorNode" => AnimGraphCategory.Selector,

        "StateMachine" or "State" or "Transition" or "EntryOverride"
            or "EntryStateOverride" => AnimGraphCategory.StateMachine,

        "RootMotionOverride" or "TargetWarp" or "OrientationWarp" or "TargetOffset"
            or "TargetPoint" or "TargetInfo" => AnimGraphCategory.Motion,

        "ExternalPose" or "ZeroPose" or "ReferencePose" or "Passthrough" => AnimGraphCategory.Input,

        "SpeedScale" or "VelocityBasedSpeedScale" or "DurationScale" or "TimeCondition"
            or "CurrentSyncEvent" or "CurrentSyncEventID" => AnimGraphCategory.Timing,

        "ReferencedGraph" => AnimGraphCategory.ExternalReference,

        _ => AnimGraphCategory.Other,
    };
}
