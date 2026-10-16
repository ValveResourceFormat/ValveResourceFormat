using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Animation;

[KV3Transfer]
public partial class AndNode : BoolValueNode
{
    public short[] ConditionNodeIndices { get; } = [];
}

[KV3Transfer]
public partial class AnimationPoseNode : PoseNode
{
    public short PoseTimeValueNodeIdx { get; } = -1;
    public short DataSlotIdx { get; } = -1;
    public Range InputTimeRemapRange { get; }
    public float UserSpecifiedTime { get; }
    public bool UseFramesAsInput { get; }
}

[KV3Transfer]
public partial class Blend1DNode : ParameterizedBlendNode
{
    public ParameterizedBlendNode.ParameterizationType Parameterization { get; }
}

[KV3Transfer]
public partial class Blend2DNode : PoseNode
{
    public short[] SourceNodeIndices { get; } = [];
    public short InputParameterNodeIdx0 { get; } = -1;
    public short InputParameterNodeIdx1 { get; } = -1;
    public Vector2[] Values { get; } = [];
    public uint[] Indices { get; } = [];
    public uint[] HullIndices { get; } = [];
    public bool AllowLooping { get; } = true;
}

[KV3Transfer]
public partial class BodyGroupNode : PassthroughNode
{
    public short EnabledNodeIdx { get; } = -1;
    public BodyGroupEvent Event { get; }
}

[KV3Transfer]
public partial class BoneMaskBlendNode : BoneMaskValueNode
{
    public short SourceMaskNodeIdx { get; } = -1;
    public short TargetMaskNodeIdx { get; } = -1;
    public short BlendWeightValueNodeIdx { get; } = -1;
}

[KV3Transfer]
public partial class BoneMaskNode : BoneMaskValueNode
{
    public StringToken BoneMaskID { get; }
}

[KV3Transfer]
public partial class BoneMaskSelectorNode : BoneMaskValueNode
{
    [KVProperty("m_defaultMaskNodeIdx")]
    public short DefaultMaskNodeIdx { get; } = -1;
    [KVProperty("m_parameterValueNodeIdx")]
    public short ParameterValueNodeIdx { get; } = -1;
    public bool SwitchDynamically { get; }
    public short[] MaskNodeIndices { get; } = [];
    public StringToken[] ParameterValues { get; } = [];
    public float BlendTimeSeconds { get; } = 0.1f;
}

[KV3Transfer]
public partial class BoneMaskSwitchNode : BoneMaskValueNode
{
    public short SwitchValueNodeIdx { get; } = -1;
    public short TrueValueNodeIdx { get; } = -1;
    public short FalseValueNodeIdx { get; } = -1;
    public float BlendTimeSeconds { get; } = 0.1f;
    public bool SwitchDynamically { get; }
}

[KV3Transfer] public partial class BoneMaskValueNode : ValueNode { }

/// <summary>A graph node that produces a boolean value.</summary>
[KV3Transfer] public partial class BoolValueNode : ValueNode { }

[KV3Transfer]
public partial class CachedBoolNode : BoolValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public CachedValueMode Mode { get; }
}

[KV3Transfer]
public partial class CachedFloatNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public CachedValueMode Mode { get; }
}

[KV3Transfer]
public partial class CachedIDNode : IDValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public CachedValueMode Mode { get; }
}

[KV3Transfer]
public partial class CachedTargetNode : TargetValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public CachedValueMode Mode { get; }
}

[KV3Transfer]
public partial class CachedVectorNode : VectorValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public CachedValueMode Mode { get; }
}

[KV3Transfer]
public partial class ChainLookatNode : PassthroughNode
{
    public StringToken EndEffectorBoneID { get; }
    public Vector3 EndEffectorForwardAxis { get; } = new(1f, 0f, 0f);
    public Vector3 EndEffectorOffset { get; } = new(1f, 0f, 0f);
    public short LookatTargetNodeIdx { get; } = -1;
    public short EnabledNodeIdx { get; } = -1;
    public float BlendTimeSeconds { get; }
    public float[] ChainWeights { get; } = [];
    public byte ChainLength { get; } = 2;
    public bool IsTargetInWorldSpace { get; }
}

[KV3Transfer]
public partial class ClipNode : ClipReferenceNode
{
    public short PlayInReverseValueNodeIdx { get; } = -1;
    public bool SampleRootMotion { get; } = true;
    public bool AllowLooping { get; }
    public short DataSlotIdx { get; } = -1;
    public short ResetTimeValueNodeIdx { get; } = -1;
    public StringToken[] GraphEvents { get; } = [];
    public float SpeedMultiplier { get; } = 1f;
    public int StartSyncEventOffset { get; }
}

[KV3Transfer] public partial class ClipReferenceNode : PoseNode { }

[KV3Transfer]
public partial class ClipSelectorNode : ClipReferenceNode
{
    public short[] OptionNodeIndices { get; } = [];
    public short[] ConditionNodeIndices { get; } = [];
}

[KV3Transfer]
public partial class ConstBoolNode : BoolValueNode
{
    public bool Value { get; }
}

[KV3Transfer]
public partial class ConstFloatNode : FloatValueNode
{
    public float Value { get; }
}

[KV3Transfer]
public partial class ConstIDNode : IDValueNode
{
    public StringToken Value { get; }
}

[KV3Transfer]
public partial class ConstTargetNode : TargetValueNode
{
    public Target Value { get; }
}

[KV3Transfer]
public partial class ConstVectorNode : VectorValueNode
{
    public Vector3 Value { get; }
}

[KV3Transfer] public partial class ControlParameterBoolNode : BoolValueNode { }

[KV3Transfer] public partial class ControlParameterFloatNode : FloatValueNode { }

[KV3Transfer] public partial class ControlParameterIDNode : IDValueNode { }

[KV3Transfer] public partial class ControlParameterTargetNode : TargetValueNode { }

[KV3Transfer] public partial class ControlParameterVectorNode : VectorValueNode { }

[KV3Transfer]
public partial class CurrentSyncEventIDNode : IDValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
}

[KV3Transfer]
public partial class CurrentSyncEventNode : FloatValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public CurrentSyncEventNode.CurrentSyncEventNodeInfoType InfoType { get; }
}

public partial class CurrentSyncEventNode
{
    public enum CurrentSyncEventNodeInfoType : byte
    {
        IndexAndPercentage = 0,
        IndexOnly = 1,
        PercentageOnly = 2,
    }
}

[KV3Transfer] public partial class DurationScaleNode : SpeedScaleBaseNode { }

[KV3Transfer]
public partial class ExternalPoseNode : PoseNode
{
    public bool ShouldSampleRootMotion { get; }
}

[KV3Transfer]
public partial class FixedWeightBoneMaskNode : BoneMaskValueNode
{
    public float BoneWeight { get; }
}

[KV3Transfer]
public partial class FloatAngleMathNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public FloatAngleMathNode.OperationType Operation { get; }
}

public partial class FloatAngleMathNode
{
    public enum OperationType : byte
    {
        ClampTo180 = 0,
        ClampTo360 = 1,
        FlipHemisphere = 2,
        FlipHemisphereNegate = 3,
    }
}

[KV3Transfer]
public partial class FloatClampNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public Range ClampRange { get; }
}

[KV3Transfer]
public partial class FloatComparisonNode : BoolValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public short ComparandValueNodeIdx { get; } = -1;
    public FloatComparisonNode.ComparisonType Comparison { get; }
    public float Epsilon { get; }
    public float ComparisonValue { get; }
}

public partial class FloatComparisonNode
{
    public enum ComparisonType : byte
    {
        GreaterThanEqual = 0,
        LessThanEqual = 1,
        NearEqual = 2,
        GreaterThan = 3,
        LessThan = 4,
    }
}

[KV3Transfer]
public partial class FloatCurveEventNode : FloatValueNode
{
    public StringToken EventID { get; }
    public short DefaultNodeIdx { get; } = -1;
    public float DefaultValue { get; }
    public BitFlags EventConditionRules { get; }
}

[KV3Transfer]
public partial class FloatCurveNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve Curve { get; }
}

[KV3Transfer]
public partial class FloatEaseNode : FloatValueNode
{
    public float EaseTime { get; } = 1f;
    public float StartValue { get; }
    public short InputValueNodeIdx { get; } = -1;
    public EasingOperation EasingOp { get; }
    public bool UseStartValue { get; }
}

[KV3Transfer]
public partial class FloatMathNode : FloatValueNode
{
    public short InputValueNodeIdxA { get; } = -1;
    public short InputValueNodeIdxB { get; } = -1;
    public bool ReturnAbsoluteResult { get; }
    public bool ReturnNegatedResult { get; }
    public FloatMathNode.OperatorType Operator { get; }
    public float ValueB { get; }
}

public partial class FloatMathNode
{
    public enum OperatorType : byte
    {
        Add = 0,
        Sub = 1,
        Mul = 2,
        Div = 3,
        Mod = 4,
        Abs = 5,
        Negate = 6,
        Floor = 7,
        Ceiling = 8,
        IntegerPart = 9,
        FractionalPart = 10,
        InverseFractionalPart = 11,
    }
}

[KV3Transfer]
public partial class FloatRangeComparisonNode : BoolValueNode
{
    public Range Range { get; }
    public short InputValueNodeIdx { get; } = -1;
    public bool IsInclusiveCheck { get; } = true;
}

[KV3Transfer]
public partial class FloatRemapNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public FloatRemapNode.RemapRange InputRange { get; }
    public FloatRemapNode.RemapRange OutputRange { get; }
}

public partial class FloatRemapNode
{
    [KV3Transfer]
    public partial class RemapRange
    {
        public float Begin { get; }
        public float End { get; }
    }
}

[KV3Transfer]
public partial class FloatSelectorNode : FloatValueNode
{
    public short[] ConditionNodeIndices { get; } = [];
    public float[] Values { get; } = [];
    public float DefaultValue { get; }
    public float EaseTime { get; } = 0.2f;
    public EasingOperation EasingOp { get; }
}

[KV3Transfer]
public partial class FloatSpringNode : FloatValueNode
{
    public float StartValue { get; }
    public float Hertz { get; } = 4f;
    public float DampingRatio { get; } = 0.7f;
    public short InputValueNodeIdx { get; } = -1;
    public bool UseStartValue { get; }
}

[KV3Transfer]
public partial class FloatSwitchNode : FloatValueNode
{
    public short SwitchValueNodeIdx { get; } = -1;
    public short TrueValueNodeIdx { get; } = -1;
    public short FalseValueNodeIdx { get; } = -1;
    public float FalseValue { get; }
    public float TrueValue { get; } = 1f;
}

/// <summary>A graph node that produces a float value.</summary>
[KV3Transfer] public partial class FloatValueNode : ValueNode { }

[KV3Transfer]
public partial class FollowBoneNode : PassthroughNode
{
    public StringToken Bone { get; }
    public StringToken FollowTargetBone { get; }
    public short EnabledNodeIdx { get; } = -1;
    public FollowBoneMode Mode { get; }
}

[KV3Transfer]
public partial class FootEventConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public FootPhaseCondition PhaseCondition { get; }
    public BitFlags EventConditionRules { get; }
}

[KV3Transfer]
public partial class FootIKNode : PassthroughNode
{
    public StringToken LeftEffectorBoneID { get; }
    public StringToken RightEffectorBoneID { get; }
    public short LeftTargetNodeIdx { get; } = -1;
    public short RightTargetNodeIdx { get; } = -1;
    public short EnabledNodeIdx { get; } = -1;
    public float BlendTimeSeconds { get; }
    public IKBlendMode BlendMode { get; }
    public bool IsTargetInWorldSpace { get; }
}

[KV3Transfer]
public partial class FootstepEventIDNode : IDValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public BitFlags EventConditionRules { get; }
}

[KV3Transfer]
public partial class FootstepEventPercentageThroughNode : FloatValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public FootPhaseCondition PhaseCondition { get; }
    public BitFlags EventConditionRules { get; }
}

[KV3Transfer]
public partial class GraphEventConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public BitFlags EventConditionRules { get; }
    public GraphEventConditionNode.Condition[] Conditions { get; } = [];
}

public partial class GraphEventConditionNode
{
    [KV3Transfer]
    public partial class Condition
    {
        public StringToken EventID { get; }
        public GraphEventTypeCondition EventTypeCondition { get; }
    }
}

/// <summary>A node of an animation graph.</summary>
[KV3Transfer]
public partial class GraphNode
{
    /// <summary>The index of the node in the graph.</summary>
    public short NodeIdx { get; }
}

// Valve extension: selects a clip option by matching an ID parameter against per-option IDs.
[KV3Transfer]
public partial class IDBasedClipSelectorNode : ClipReferenceNode
{
    public short[] OptionNodeIndices { get; } = [];
    public StringToken[] OptionIDs { get; } = [];
    public short ParameterNodeIdx { get; } = -1;
    public short FallbackNodeIdx { get; } = -1;
    public bool IgnoreInvalidOptions { get; }
}

// Valve extension: selects a pose option by matching an ID parameter against per-option IDs.
[KV3Transfer]
public partial class IDBasedSelectorNode : PoseNode
{
    public short[] OptionNodeIndices { get; } = [];
    public StringToken[] OptionIDs { get; } = [];
    public short ParameterNodeIdx { get; } = -1;
    public short FallbackNodeIdx { get; } = -1;
    public bool IgnoreInvalidOptions { get; }
}

[KV3Transfer]
public partial class IDComparisonNode : BoolValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public IDComparisonNode.ComparisonType Comparison { get; }
    public StringToken[] ComparisionIDs { get; } = [];
}

public partial class IDComparisonNode
{
    public enum ComparisonType : byte
    {
        Matches = 0,
        DoesntMatch = 1,
    }
}

[KV3Transfer]
public partial class IDEventConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public BitFlags EventConditionRules { get; }
    public StringToken[] EventIDs { get; } = [];
}

[KV3Transfer]
public partial class IDEventNode : IDValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public BitFlags EventConditionRules { get; }
    public StringToken DefaultValue { get; }
}

// Note: outputs a float (the percentage through the matched event), FloatValueNode in Esoterica
[KV3Transfer]
public partial class IDEventPercentageThroughNode : FloatValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public BitFlags EventConditionRules { get; }
    public StringToken EventID { get; }
}

[KV3Transfer]
public partial class IDSelectorNode : IDValueNode
{
    public short[] ConditionNodeIndices { get; } = [];
    public StringToken[] Values { get; } = [];
    public StringToken DefaultValue { get; }
}

[KV3Transfer]
public partial class IDSwitchNode : IDValueNode
{
    public short SwitchValueNodeIdx { get; } = -1;
    public short TrueValueNodeIdx { get; } = -1;
    public short FalseValueNodeIdx { get; } = -1;
    public StringToken FalseValue { get; }
    public StringToken TrueValue { get; }
}

[KV3Transfer]
public partial class IDToFloatNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    [KVProperty("m_defaultValue")]
    public float DefaultValue { get; }
    public StringToken[] IDs { get; } = [];
    public float[] Values { get; } = [];
}

/// <summary>A graph node that produces an ID value.</summary>
[KV3Transfer] public partial class IDValueNode : ValueNode { }

[KV3Transfer]
public partial class IsExternalGraphSlotFilledNode : BoolValueNode
{
    public short ExternalGraphNodeIdx { get; } = -1;
}

[KV3Transfer]
public partial class IsExternalPoseSetNode : BoolValueNode
{
    public short ExternalPoseNodeIdx { get; } = -1;
}

[KV3Transfer] public partial class IsInactiveBranchConditionNode : BoolValueNode { }

[KV3Transfer]
public partial class IsTargetSetNode : BoolValueNode
{
    public short InputValueNodeIdx { get; } = -1;
}

[KV3Transfer]
public partial class LayerBlendNode : PoseNode
{
    public short BaseNodeIdx { get; } = -1;
    public bool OnlySampleBaseRootMotion { get; } = true;
    public LayerBlendNode.LayerDefinitionType[] LayerDefinition { get; } = [];
}

public partial class LayerBlendNode
{
    [KV3Transfer]
    public partial class LayerDefinitionType
    {
        public short InputNodeIdx { get; } = -1;
        public short WeightValueNodeIdx { get; } = -1;
        public short BoneMaskValueNodeIdx { get; } = -1;
        public short RootMotionWeightValueNodeIdx { get; } = -1;
        public bool IsSynchronized { get; }
        public bool IgnoreEvents { get; }
        public bool IsStateMachineLayer { get; }
        public PoseBlendMode BlendMode { get; }
    }
}

[KV3Transfer]
public partial class NotNode : BoolValueNode
{
    public short InputValueNodeIdx { get; } = -1;
}

[KV3Transfer]
public partial class OrNode : BoolValueNode
{
    public short[] ConditionNodeIndices { get; } = [];
}

[KV3Transfer]
public partial class OrientationWarpNode : PoseNode
{
    public short ClipReferenceNodeIdx { get; } = -1;
    public short TargetValueNodeIdx { get; } = -1;
    public bool IsOffsetNode { get; }
    public bool IsOffsetRelativeToCharacter { get; } = true;
    public bool WarpTranslation { get; }
    public OrientationWarpNode.AlignmentModeType AlignmentMode { get; }
    public RootMotionData.SamplingMode SamplingMode { get; } = RootMotionData.SamplingMode.WorldSpace;
}

public partial class OrientationWarpNode
{
    public enum AlignmentModeType : byte
    {
        MovementDirection = 0,
        AnimationEndFacing = 1,
    }
}

[KV3Transfer]
public partial class ParameterizedBlendNode : PoseNode
{
    public short[] SourceNodeIndices { get; } = [];
    public short InputParameterValueNodeIdx { get; } = -1;
    public bool AllowLooping { get; } = true;
}

public partial class ParameterizedBlendNode
{
    [KV3Transfer]
    public partial class BlendRange
    {
        public short InputIdx0 { get; } = -1;
        public short InputIdx1 { get; } = -1;
        public Range ParameterValueRange { get; }
    }
}

public partial class ParameterizedBlendNode
{
    [KV3Transfer]
    public partial class ParameterizationType
    {
        public ParameterizedBlendNode.BlendRange[] BlendRanges { get; } = [];
        public Range ParameterRange { get; }
    }
}

[KV3Transfer]
public partial class ParameterizedClipSelectorNode : ClipReferenceNode
{
    public short[] OptionNodeIndices { get; } = [];
    public byte[] OptionWeights { get; } = [];
    [KVProperty("m_parameterNodeIdx")]
    public short ParameterNodeIdx { get; } = -1;
    public bool IgnoreInvalidOptions { get; }
    public bool HasWeightsSet { get; }
}

[KV3Transfer]
public partial class ParameterizedSelectorNode : PoseNode
{
    public short[] OptionNodeIndices { get; } = [];
    public byte[] OptionWeights { get; } = [];
    [KVProperty("m_parameterNodeIdx")]
    public short ParameterNodeIdx { get; } = -1;
    public bool IgnoreInvalidOptions { get; }
    public bool HasWeightsSet { get; }
}

/// <summary>A pose node that passes its child pose through.</summary>
[KV3Transfer]
public partial class PassthroughNode : PoseNode
{
    /// <summary>The index of the child pose node.</summary>
    public short ChildNodeIdx { get; } = -1;
}

/// <summary>A graph node that produces a pose.</summary>
[KV3Transfer] public partial class PoseNode : GraphNode { }

[KV3Transfer] public partial class ReferencePoseNode : PoseNode { }

[KV3Transfer]
public partial class ReferencedGraphNode : PoseNode
{
    public short ReferencedGraphIdx { get; } = -1;
    public short FallbackNodeIdx { get; } = -1;
}

[KV3Transfer]
public partial class RootMotionOverrideNode : PassthroughNode
{
    [KVProperty("m_desiredMovingVelocityNodeIdx")]
    public short DesiredMovingVelocityNodeIdx { get; } = -1;
    [KVProperty("m_desiredFacingDirectionNodeIdx")]
    public short DesiredFacingDirectionNodeIdx { get; } = -1;
    [KVProperty("m_linearVelocityLimitNodeIdx")]
    public short LinearVelocityLimitNodeIdx { get; } = -1;
    [KVProperty("m_angularVelocityLimitNodeIdx")]
    public short AngularVelocityLimitNodeIdx { get; } = -1;
    [KVProperty("m_enabledNodeIdx")]
    public short EnabledNodeIdx { get; } = -1;
    [KVProperty("m_maxLinearVelocity")]
    public float MaxLinearVelocity { get; } = -1f;
    [KVProperty("m_maxAngularVelocityRadians")]
    public float MaxAngularVelocityRadians { get; } = -1f;
    public BitFlags OverrideFlags { get; }
}

public partial class RootMotionOverrideNode
{
    public enum OverrideFlagsType : byte
    {
        AllowMoveX = 0,
        AllowMoveY = 1,
        AllowMoveZ = 2,
        AllowFacingPitch = 3,
        ListenForEvents = 4,
    }
}

[KV3Transfer]
public partial class ScaleNode : PassthroughNode
{
    public short MaskNodeIdx { get; } = -1;
    public short EnableNodeIdx { get; } = -1;
}

[KV3Transfer]
public partial class SelectorNode : PoseNode
{
    public short[] OptionNodeIndices { get; } = [];
    public short[] ConditionNodeIndices { get; } = [];
}

[KV3Transfer]
public partial class SpeedScaleBaseNode : PassthroughNode
{
    public short InputValueNodeIdx { get; } = -1;
    public float DefaultInputValue { get; }
}

[KV3Transfer] public partial class SpeedScaleNode : SpeedScaleBaseNode { }

[KV3Transfer]
public partial class StateCompletedConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public short TransitionDurationOverrideNodeIdx { get; } = -1;
    public float TransitionDurationSeconds { get; }
}

[KV3Transfer]
public partial class StateMachineNode : PoseNode
{
    public StateMachineNode.StateDefinition[] StateDefinitions { get; } = [];
    public short DefaultStateIndex { get; } = -1;
}

public partial class StateMachineNode
{
    [KV3Transfer]
    public partial class StateDefinition
    {
        public short StateNodeIdx { get; } = -1;
        public short EntryConditionNodeIdx { get; } = -1;
        public StateMachineNode.TransitionDefinition[] TransitionDefinitions { get; } = [];
    }
}

public partial class StateMachineNode
{
    [KV3Transfer]
    public partial class TransitionDefinition
    {
        public short TargetStateIdx { get; } = -1;
        public short ConditionNodeIdx { get; } = -1;
        public short TransitionNodeIdx { get; } = -1;
        public bool CanBeForced { get; }
    }
}

[KV3Transfer]
public partial class StateNode : PoseNode
{
    public short ChildNodeIdx { get; } = -1;
    public StringToken[] EntryEvents { get; } = [];
    public StringToken[] ExecuteEvents { get; } = [];
    public StringToken[] ExitEvents { get; } = [];
    public StateNode.TimedEvent[] TimedRemainingEvents { get; } = [];
    public StateNode.TimedEvent[] TimedElapsedEvents { get; } = [];
    public short LayerWeightNodeIdx { get; } = -1;
    public short LayerRootMotionWeightNodeIdx { get; } = -1;
    public short LayerBoneMaskNodeIdx { get; } = -1;
    public bool IsOffState { get; }
    public bool UseActualElapsedTimeInStateForTimedEvents { get; }
}

public partial class StateNode
{
    [KV3Transfer]
    public partial class TimedEvent
    {
        public StringToken ID { get; }
        public float TimeValueSeconds { get; }
        public StateNode.TimedEvent.Comparison ComparisionOperator { get; }
    }
}

public partial class StateNode
{
    public partial class TimedEvent
    {
        public enum Comparison : byte
        {
            LessThanEqual = 0,
            GreaterThanEqual = 1,
        }
    }
}

[KV3Transfer]
public partial class SyncEventIndexConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public SyncEventIndexConditionNode.TriggerModeType TriggerMode { get; }
    [KVProperty("m_syncEventIdx")]
    public int SyncEventIdx { get; } = -1;
}

public partial class SyncEventIndexConditionNode
{
    public enum TriggerModeType : byte
    {
        ExactlyAtEventIndex = 0,
        GreaterThanEqualToEventIndex = 1,
    }
}

[KV3Transfer]
public partial class TargetInfoNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public TargetInfoNode.Info InfoType { get; } = TargetInfoNode.Info.Distance;
    public bool IsWorldSpaceTarget { get; } = true;
}

public partial class TargetInfoNode
{
    public enum Info : uint
    {
        AngleHorizontal = 0,
        AngleVertical = 1,
        Distance = 2,
        DistanceHorizontalOnly = 3,
        DistanceVerticalOnly = 4,
        DeltaOrientationX = 5,
        DeltaOrientationY = 6,
        DeltaOrientationZ = 7,
    }
}

[KV3Transfer]
public partial class TargetOffsetNode : TargetValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public bool IsBoneSpaceOffset { get; } = true;
    public Quaternion RotationOffset { get; }
    public Vector3 TranslationOffset { get; }
}

[KV3Transfer]
public partial class TargetPointNode : VectorValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public bool IsWorldSpaceTarget { get; } = true;
}

[KV3Transfer]
public partial class TargetSelectorNode : ClipReferenceNode
{
    public short[] OptionNodeIndices { get; } = [];
    public float OrientationScoreWeight { get; } = 1f;
    public float PositionScoreWeight { get; } = 1f;
    [KVProperty("m_parameterNodeIdx")]
    public short ParameterNodeIdx { get; } = -1;
    public bool IgnoreInvalidOptions { get; }
    public bool IsWorldSpaceTarget { get; } = true;
    public StringToken AlignmentBoneID { get; }
}

[KV3Transfer] public partial class TargetValueNode : ValueNode { }

[KV3Transfer]
public partial class TargetWarpNode : PoseNode
{
    public short ClipReferenceNodeIdx { get; } = -1;
    public short TargetValueNodeIdx { get; } = -1;
    public RootMotionData.SamplingMode SamplingMode { get; }
    public TargetWarpNode.TargetUpdateRuleType TargetUpdateRule { get; }
    public bool AlignWithTargetAtLastWarpEvent { get; }
    public float SamplingPositionErrorThresholdSq { get; }
    public float MaxTangentLength { get; } = 1.25f;
    public float LerpFallbackDistanceThreshold { get; } = 0.1f;
    public float TargetUpdateDistanceThreshold { get; } = 0.1f;
    public float TargetUpdateAngleThresholdRadians { get; } = 0.087266f;
    public StringToken AlignmentBoneID { get; }
}

public partial class TargetWarpNode
{
    public enum TargetUpdateRuleType : byte
    {
        None = 0,
        Recalculate = 1,
        Offset = 2,
        RecalculateOrOffset = 3,
    }
}

[KV3Transfer]
public partial class TimeConditionNode : BoolValueNode
{
    [KVProperty("m_sourceStateNodeIdx")]
    public short SourceStateNodeIdx { get; } = -1;
    public short InputValueNodeIdx { get; } = -1;
    public float Comparand { get; }
    public TimeConditionNode.ComparisonType Type { get; } = TimeConditionNode.ComparisonType.ElapsedTime;
    public TimeConditionNode.OperatorType Operator { get; }
}

public partial class TimeConditionNode
{
    public enum ComparisonType : byte
    {
        PercentageThroughState = 0,
        PercentageThroughSyncEvent = 1,
        ElapsedTime = 2,
    }
}

public partial class TimeConditionNode
{
    public enum OperatorType : byte
    {
        LessThan = 0,
        LessThanEqual = 1,
        GreaterThan = 2,
        GreaterThanEqual = 3,
    }
}

[KV3Transfer]
public partial class TimeControlledClipNode : PoseNode
{
    public short PlayInReverseValueNodeIdx { get; } = -1;
    public bool SampleRootMotion { get; } = true;
    public short DataSlotIdx { get; } = -1;
    public short TimeValueNodeIdx { get; } = -1;
    public StringToken[] GraphEvents { get; } = [];
}

[KV3Transfer]
public partial class TransitionEventConditionNode : BoolValueNode
{
    public StringToken RequireRuleID { get; }
    public BitFlags EventConditionRules { get; }
    public short SourceStateNodeIdx { get; } = -1;
    public TransitionRuleCondition RuleCondition { get; }
}

[KV3Transfer]
public partial class TransitionNode : PoseNode
{
    public short TargetStateNodeIdx { get; } = -1;
    public short DurationOverrideNodeIdx { get; } = -1;
    [KVProperty("m_timeOffsetOverrideNodeIdx")]
    public short TimeOffsetOverrideNodeIdx { get; } = -1;
    [KVProperty("m_startBoneMaskNodeIdx")]
    public short StartBoneMaskNodeIdx { get; } = -1;
    [KVProperty("m_flDuration")]
    public float DurationSeconds { get; } // Definition duration from file
    public Percent BoneMaskBlendInTimePercentage { get; }
    public float TimeOffset { get; }
    public BitFlags TransitionOptions { get; }
    [KVProperty("m_targetSyncIDNodeIdx")]
    public short TargetSyncIDNodeIdx { get; } = -1;
    public EasingOperation BlendWeightEasing { get; }
    public RootMotionBlendMode RootMotionBlend { get; }
}

public partial class TransitionNode
{
    public enum TransitionOptionsType : byte
    {
        None = 0,
        ClampDuration = 1,
        Synchronized = 2,
        MatchSourceTime = 3,
        MatchSyncEventIndex = 4,
        MatchSyncEventID = 5,
        MatchSyncEventPercentage = 6,
        PreferClosestSyncEventID = 7,
        MatchTimeInSeconds = 8,
        OffsetTimeInSeconds = 9,
    }
}

[KV3Transfer]
public partial class TwoBoneIKNode : PassthroughNode
{
    public StringToken EffectorBoneID { get; }
    public short EffectorTargetNodeIdx { get; } = -1;
    public short EnabledNodeIdx { get; } = -1;
    public float BlendTimeSeconds { get; }
    public IKBlendMode BlendMode { get; }
    public bool IsTargetInWorldSpace { get; }
    public float ChainRotationWeight { get; }
}

/// <summary>A graph node that produces a value.</summary>
[KV3Transfer] public partial class ValueNode : GraphNode { }

[KV3Transfer]
public partial class VectorCreateNode : VectorValueNode
{
    [KVProperty("m_inputVectorValueNodeIdx")]
    public short InputVectorValueNodeIdx { get; } = -1;
    [KVProperty("m_inputValueXNodeIdx")]
    public short InputValueXNodeIdx { get; } = -1;
    [KVProperty("m_inputValueYNodeIdx")]
    public short InputValueYNodeIdx { get; } = -1;
    [KVProperty("m_inputValueZNodeIdx")]
    public short InputValueZNodeIdx { get; } = -1;
}

[KV3Transfer]
public partial class VectorInfoNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public VectorInfoNode.Info DesiredInfo { get; }
}

public partial class VectorInfoNode
{
    public enum Info : byte
    {
        X = 0,
        Y = 1,
        Z = 2,
        Length = 3,
        AngleHorizontal = 4,
        AngleVertical = 5,
    }
}

[KV3Transfer]
public partial class VectorNegateNode : VectorValueNode
{
    public short InputValueNodeIdx { get; } = -1;
}

[KV3Transfer] public partial class VectorValueNode : ValueNode { }

[KV3Transfer] public partial class VelocityBasedSpeedScaleNode : SpeedScaleBaseNode { }

[KV3Transfer] public partial class VelocityBlendNode : ParameterizedBlendNode { }

[KV3Transfer]
public partial class VirtualParameterBoneMaskNode : BoneMaskValueNode
{
    public short ChildNodeIdx { get; } = -1;
}

[KV3Transfer]
public partial class VirtualParameterBoolNode : BoolValueNode
{
    public short ChildNodeIdx { get; } = -1;
}

[KV3Transfer]
public partial class VirtualParameterFloatNode : FloatValueNode
{
    public short ChildNodeIdx { get; } = -1;
}

[KV3Transfer]
public partial class VirtualParameterIDNode : IDValueNode
{
    public short ChildNodeIdx { get; } = -1;
}

[KV3Transfer]
public partial class VirtualParameterTargetNode : TargetValueNode
{
    public short ChildNodeIdx { get; } = -1;
}

[KV3Transfer]
public partial class VirtualParameterVectorNode : VectorValueNode
{
    public short ChildNodeIdx { get; } = -1;
}

[KV3Transfer] public partial class ZeroPoseNode : PoseNode { }
