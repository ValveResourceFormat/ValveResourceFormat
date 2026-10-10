using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class AndNode : BoolValueNode
{
    public short[] ConditionNodeIndices { get; } = [];
}

[KV3Transfer]
partial class AnimationPoseNode : PoseNode
{
    public short PoseTimeValueNodeIdx { get; } = -1;
    public short DataSlotIdx { get; } = -1;
    public Range InputTimeRemapRange { get; }
    public float UserSpecifiedTime { get; }
    public bool UseFramesAsInput { get; }
}

[KV3Transfer]
partial class Blend1DNode : ParameterizedBlendNode
{
    public ParameterizedBlendNode.ParameterizationType Parameterization { get; }
}

[KV3Transfer]
partial class Blend2DNode : PoseNode
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
partial class BodyGroupNode : PassthroughNode
{
    public short EnabledNodeIdx { get; } = -1;
    public BodyGroupEvent Event { get; }
}

[KV3Transfer]
partial class BoneMaskBlendNode : BoneMaskValueNode
{
    public short SourceMaskNodeIdx { get; } = -1;
    public short TargetMaskNodeIdx { get; } = -1;
    public short BlendWeightValueNodeIdx { get; } = -1;
}

[KV3Transfer]
partial class BoneMaskNode : BoneMaskValueNode
{
    public GlobalSymbol BoneMaskID { get; }
}

[KV3Transfer]
partial class BoneMaskSelectorNode : BoneMaskValueNode
{
    [KVProperty("m_defaultMaskNodeIdx")]
    public short DefaultMaskNodeIdx { get; } = -1;
    [KVProperty("m_parameterValueNodeIdx")]
    public short ParameterValueNodeIdx { get; } = -1;
    public bool SwitchDynamically { get; }
    public short[] MaskNodeIndices { get; } = [];
    public GlobalSymbol[] ParameterValues { get; } = [];
    public float BlendTimeSeconds { get; } = 0.1f;
}

[KV3Transfer]
partial class BoneMaskSwitchNode : BoneMaskValueNode
{
    public short SwitchValueNodeIdx { get; } = -1;
    public short TrueValueNodeIdx { get; } = -1;
    public short FalseValueNodeIdx { get; } = -1;
    public float BlendTimeSeconds { get; } = 0.1f;
    public bool SwitchDynamically { get; }
}

[KV3Transfer] partial class BoneMaskValueNode : ValueNode { }

/// <summary>A graph node that produces a boolean value.</summary>
[KV3Transfer] partial class BoolValueNode : ValueNode { }

[KV3Transfer]
partial class CachedBoolNode : BoolValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public CachedValueMode Mode { get; }
}

[KV3Transfer]
partial class CachedFloatNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public CachedValueMode Mode { get; }
}

[KV3Transfer]
partial class CachedIDNode : IDValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public CachedValueMode Mode { get; }
}

[KV3Transfer]
partial class CachedTargetNode : TargetValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public CachedValueMode Mode { get; }
}

[KV3Transfer]
partial class CachedVectorNode : VectorValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public CachedValueMode Mode { get; }
}

[KV3Transfer]
partial class ChainLookatNode : PassthroughNode
{
    public GlobalSymbol EndEffectorBoneID { get; }
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
partial class ClipNode : ClipReferenceNode
{
    public short PlayInReverseValueNodeIdx { get; } = -1;
    public bool SampleRootMotion { get; } = true;
    public bool AllowLooping { get; }
    public short DataSlotIdx { get; } = -1;
    public short ResetTimeValueNodeIdx { get; } = -1;
    public GlobalSymbol[] GraphEvents { get; } = [];
    public float SpeedMultiplier { get; } = 1f;
    public int StartSyncEventOffset { get; }
}

[KV3Transfer] partial class ClipReferenceNode : PoseNode { }

[KV3Transfer]
partial class ClipSelectorNode : ClipReferenceNode
{
    public short[] OptionNodeIndices { get; } = [];
    public short[] ConditionNodeIndices { get; } = [];
}

[KV3Transfer]
partial class ConstBoolNode : BoolValueNode
{
    public bool Value { get; }
}

[KV3Transfer]
partial class ConstFloatNode : FloatValueNode
{
    public float Value { get; }
}

[KV3Transfer]
partial class ConstIDNode : IDValueNode
{
    public GlobalSymbol Value { get; }
}

[KV3Transfer]
partial class ConstTargetNode : TargetValueNode
{
    public Target Value { get; }
}

[KV3Transfer]
partial class ConstVectorNode : VectorValueNode
{
    public Vector3 Value { get; }
}

[KV3Transfer] partial class ControlParameterBoolNode : BoolValueNode { }

[KV3Transfer] partial class ControlParameterFloatNode : FloatValueNode { }

[KV3Transfer] partial class ControlParameterIDNode : IDValueNode { }

[KV3Transfer] partial class ControlParameterTargetNode : TargetValueNode { }

[KV3Transfer] partial class ControlParameterVectorNode : VectorValueNode { }

[KV3Transfer]
partial class CurrentSyncEventIDNode : IDValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
}

[KV3Transfer]
partial class CurrentSyncEventNode : FloatValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public CurrentSyncEventNode.CurrentSyncEventNodeInfoType InfoType { get; }
}

partial class CurrentSyncEventNode
{
    internal enum CurrentSyncEventNodeInfoType : byte
    {
        IndexAndPercentage = 0,
        IndexOnly = 1,
        PercentageOnly = 2,
    }
}

[KV3Transfer] partial class DurationScaleNode : SpeedScaleBaseNode { }

[KV3Transfer]
partial class ExternalPoseNode : PoseNode
{
    public bool ShouldSampleRootMotion { get; }
}

[KV3Transfer]
partial class FixedWeightBoneMaskNode : BoneMaskValueNode
{
    public float BoneWeight { get; }
}

[KV3Transfer]
partial class FloatAngleMathNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public FloatAngleMathNode.OperationType Operation { get; }
}

partial class FloatAngleMathNode
{
    internal enum OperationType : byte
    {
        ClampTo180 = 0,
        ClampTo360 = 1,
        FlipHemisphere = 2,
        FlipHemisphereNegate = 3,
    }
}

[KV3Transfer]
partial class FloatClampNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public Range ClampRange { get; }
}

[KV3Transfer]
partial class FloatComparisonNode : BoolValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public short ComparandValueNodeIdx { get; } = -1;
    public FloatComparisonNode.ComparisonType Comparison { get; }
    public float Epsilon { get; }
    public float ComparisonValue { get; }
}

partial class FloatComparisonNode
{
    internal enum ComparisonType : byte
    {
        GreaterThanEqual = 0,
        LessThanEqual = 1,
        NearEqual = 2,
        GreaterThan = 3,
        LessThan = 4,
    }
}

[KV3Transfer]
partial class FloatCurveEventNode : FloatValueNode
{
    public GlobalSymbol EventID { get; }
    public short DefaultNodeIdx { get; } = -1;
    public float DefaultValue { get; }
    public BitFlags EventConditionRules { get; }
}

[KV3Transfer]
partial class FloatCurveNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve Curve { get; }
}

[KV3Transfer]
partial class FloatEaseNode : FloatValueNode
{
    public float EaseTime { get; } = 1f;
    public float StartValue { get; }
    public short InputValueNodeIdx { get; } = -1;
    public EasingOperation EasingOp { get; }
    public bool UseStartValue { get; }
}

[KV3Transfer]
partial class FloatMathNode : FloatValueNode
{
    public short InputValueNodeIdxA { get; } = -1;
    public short InputValueNodeIdxB { get; } = -1;
    public bool ReturnAbsoluteResult { get; }
    public bool ReturnNegatedResult { get; }
    public FloatMathNode.OperatorType Operator { get; }
    public float ValueB { get; }
}

partial class FloatMathNode
{
    internal enum OperatorType : byte
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
partial class FloatRangeComparisonNode : BoolValueNode
{
    public Range Range { get; }
    public short InputValueNodeIdx { get; } = -1;
    public bool IsInclusiveCheck { get; } = true;
}

[KV3Transfer]
partial class FloatRemapNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public FloatRemapNode.RemapRange InputRange { get; }
    public FloatRemapNode.RemapRange OutputRange { get; }
}

partial class FloatRemapNode
{
    [KV3Transfer]
    internal partial class RemapRange
    {
        public float Begin { get; }
        public float End { get; }
    }
}

[KV3Transfer]
partial class FloatSelectorNode : FloatValueNode
{
    public short[] ConditionNodeIndices { get; } = [];
    public float[] Values { get; } = [];
    public float DefaultValue { get; }
    public float EaseTime { get; } = 0.2f;
    public EasingOperation EasingOp { get; }
}

[KV3Transfer]
partial class FloatSpringNode : FloatValueNode
{
    public float StartValue { get; }
    public float Hertz { get; } = 4f;
    public float DampingRatio { get; } = 0.7f;
    public short InputValueNodeIdx { get; } = -1;
    public bool UseStartValue { get; }
}

[KV3Transfer]
partial class FloatSwitchNode : FloatValueNode
{
    public short SwitchValueNodeIdx { get; } = -1;
    public short TrueValueNodeIdx { get; } = -1;
    public short FalseValueNodeIdx { get; } = -1;
    public float FalseValue { get; }
    public float TrueValue { get; } = 1f;
}

/// <summary>A graph node that produces a float value.</summary>
[KV3Transfer] partial class FloatValueNode : ValueNode { }

[KV3Transfer]
partial class FollowBoneNode : PassthroughNode
{
    public GlobalSymbol Bone { get; }
    public GlobalSymbol FollowTargetBone { get; }
    public short EnabledNodeIdx { get; } = -1;
    public FollowBoneMode Mode { get; }
}

[KV3Transfer]
partial class FootEventConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public FootPhaseCondition PhaseCondition { get; }
    public BitFlags EventConditionRules { get; }
}

[KV3Transfer]
partial class FootIKNode : PassthroughNode
{
    public GlobalSymbol LeftEffectorBoneID { get; }
    public GlobalSymbol RightEffectorBoneID { get; }
    public short LeftTargetNodeIdx { get; } = -1;
    public short RightTargetNodeIdx { get; } = -1;
    public short EnabledNodeIdx { get; } = -1;
    public float BlendTimeSeconds { get; }
    public IKBlendMode BlendMode { get; }
    public bool IsTargetInWorldSpace { get; }
}

[KV3Transfer]
partial class FootstepEventIDNode : IDValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public BitFlags EventConditionRules { get; }
}

[KV3Transfer]
partial class FootstepEventPercentageThroughNode : FloatValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public FootPhaseCondition PhaseCondition { get; }
    public BitFlags EventConditionRules { get; }
}

[KV3Transfer]
partial class GraphEventConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public BitFlags EventConditionRules { get; }
    public GraphEventConditionNode.Condition[] Conditions { get; } = [];
}

partial class GraphEventConditionNode
{
    [KV3Transfer]
    internal partial class Condition
    {
        public GlobalSymbol EventID { get; }
        public GraphEventTypeCondition EventTypeCondition { get; }
    }
}

/// <summary>A node of an animation graph.</summary>
[KV3Transfer]
partial class GraphNode
{
    /// <summary>The index of the node in the graph.</summary>
    public short NodeIdx { get; }
}

// Valve extension: selects a clip option by matching an ID parameter against per-option IDs.
[KV3Transfer]
partial class IDBasedClipSelectorNode : ClipReferenceNode
{
    public short[] OptionNodeIndices { get; } = [];
    public GlobalSymbol[] OptionIDs { get; } = [];
    public short ParameterNodeIdx { get; } = -1;
    public short FallbackNodeIdx { get; } = -1;
    public bool IgnoreInvalidOptions { get; }
}

// Valve extension: selects a pose option by matching an ID parameter against per-option IDs.
[KV3Transfer]
partial class IDBasedSelectorNode : PoseNode
{
    public short[] OptionNodeIndices { get; } = [];
    public GlobalSymbol[] OptionIDs { get; } = [];
    public short ParameterNodeIdx { get; } = -1;
    public short FallbackNodeIdx { get; } = -1;
    public bool IgnoreInvalidOptions { get; }
}

[KV3Transfer]
partial class IDComparisonNode : BoolValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public IDComparisonNode.ComparisonType Comparison { get; }
    public GlobalSymbol[] ComparisionIDs { get; } = [];
}

partial class IDComparisonNode
{
    internal enum ComparisonType : byte
    {
        Matches = 0,
        DoesntMatch = 1,
    }
}

[KV3Transfer]
partial class IDEventConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public BitFlags EventConditionRules { get; }
    public GlobalSymbol[] EventIDs { get; } = [];
}

[KV3Transfer]
partial class IDEventNode : IDValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public BitFlags EventConditionRules { get; }
    public GlobalSymbol DefaultValue { get; }
}

// Note: outputs a float (the percentage through the matched event), FloatValueNode in Esoterica
[KV3Transfer]
partial class IDEventPercentageThroughNode : FloatValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public BitFlags EventConditionRules { get; }
    public GlobalSymbol EventID { get; }
}

[KV3Transfer]
partial class IDSelectorNode : IDValueNode
{
    public short[] ConditionNodeIndices { get; } = [];
    public GlobalSymbol[] Values { get; } = [];
    public GlobalSymbol DefaultValue { get; }
}

[KV3Transfer]
partial class IDSwitchNode : IDValueNode
{
    public short SwitchValueNodeIdx { get; } = -1;
    public short TrueValueNodeIdx { get; } = -1;
    public short FalseValueNodeIdx { get; } = -1;
    public GlobalSymbol FalseValue { get; }
    public GlobalSymbol TrueValue { get; }
}

[KV3Transfer]
partial class IDToFloatNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    [KVProperty("m_defaultValue")]
    public float DefaultValue { get; }
    public GlobalSymbol[] IDs { get; } = [];
    public float[] Values { get; } = [];
}

/// <summary>A graph node that produces an ID value.</summary>
[KV3Transfer] partial class IDValueNode : ValueNode { }

[KV3Transfer]
partial class IsExternalGraphSlotFilledNode : BoolValueNode
{
    public short ExternalGraphNodeIdx { get; } = -1;
}

[KV3Transfer]
partial class IsExternalPoseSetNode : BoolValueNode
{
    public short ExternalPoseNodeIdx { get; } = -1;
}

[KV3Transfer] partial class IsInactiveBranchConditionNode : BoolValueNode { }

[KV3Transfer]
partial class IsTargetSetNode : BoolValueNode
{
    public short InputValueNodeIdx { get; } = -1;
}

[KV3Transfer]
partial class LayerBlendNode : PoseNode
{
    public short BaseNodeIdx { get; } = -1;
    public bool OnlySampleBaseRootMotion { get; } = true;
    public LayerBlendNode.LayerDefinitionType[] LayerDefinition { get; } = [];
}

partial class LayerBlendNode
{
    [KV3Transfer]
    internal partial class LayerDefinitionType
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
partial class NotNode : BoolValueNode
{
    public short InputValueNodeIdx { get; } = -1;
}

[KV3Transfer]
partial class OrNode : BoolValueNode
{
    public short[] ConditionNodeIndices { get; } = [];
}

[KV3Transfer]
partial class OrientationWarpNode : PoseNode
{
    public short ClipReferenceNodeIdx { get; } = -1;
    public short TargetValueNodeIdx { get; } = -1;
    public bool IsOffsetNode { get; }
    public bool IsOffsetRelativeToCharacter { get; } = true;
    public bool WarpTranslation { get; }
    public OrientationWarpNode.AlignmentModeType AlignmentMode { get; }
    public RootMotionData.SamplingMode SamplingMode { get; } = RootMotionData.SamplingMode.WorldSpace;
}

partial class OrientationWarpNode
{
    internal enum AlignmentModeType : byte
    {
        MovementDirection = 0,
        AnimationEndFacing = 1,
    }
}

[KV3Transfer]
partial class ParameterizedBlendNode : PoseNode
{
    public short[] SourceNodeIndices { get; } = [];
    public short InputParameterValueNodeIdx { get; } = -1;
    public bool AllowLooping { get; } = true;
}

partial class ParameterizedBlendNode
{
    [KV3Transfer]
    internal partial class BlendRange
    {
        public short InputIdx0 { get; } = -1;
        public short InputIdx1 { get; } = -1;
        public Range ParameterValueRange { get; }
    }
}

partial class ParameterizedBlendNode
{
    [KV3Transfer]
    internal partial class ParameterizationType
    {
        public ParameterizedBlendNode.BlendRange[] BlendRanges { get; } = [];
        public Range ParameterRange { get; }
    }
}

[KV3Transfer]
partial class ParameterizedClipSelectorNode : ClipReferenceNode
{
    public short[] OptionNodeIndices { get; } = [];
    public byte[] OptionWeights { get; } = [];
    [KVProperty("m_parameterNodeIdx")]
    public short ParameterNodeIdx { get; } = -1;
    public bool IgnoreInvalidOptions { get; }
    public bool HasWeightsSet { get; }
}

[KV3Transfer]
partial class ParameterizedSelectorNode : PoseNode
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
partial class PassthroughNode : PoseNode
{
    /// <summary>The index of the child pose node.</summary>
    public short ChildNodeIdx { get; } = -1;
}

/// <summary>A graph node that produces a pose.</summary>
[KV3Transfer] partial class PoseNode : GraphNode { }

[KV3Transfer] partial class ReferencePoseNode : PoseNode { }

[KV3Transfer]
partial class ReferencedGraphNode : PoseNode
{
    public short ReferencedGraphIdx { get; } = -1;
    public short FallbackNodeIdx { get; } = -1;
}

[KV3Transfer]
partial class RootMotionOverrideNode : PassthroughNode
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

partial class RootMotionOverrideNode
{
    internal enum OverrideFlagsType : byte
    {
        AllowMoveX = 0,
        AllowMoveY = 1,
        AllowMoveZ = 2,
        AllowFacingPitch = 3,
        ListenForEvents = 4,
    }
}

[KV3Transfer]
partial class ScaleNode : PassthroughNode
{
    public short MaskNodeIdx { get; } = -1;
    public short EnableNodeIdx { get; } = -1;
}

[KV3Transfer]
partial class SelectorNode : PoseNode
{
    public short[] OptionNodeIndices { get; } = [];
    public short[] ConditionNodeIndices { get; } = [];
}

[KV3Transfer]
partial class SpeedScaleBaseNode : PassthroughNode
{
    public short InputValueNodeIdx { get; } = -1;
    public float DefaultInputValue { get; }
}

[KV3Transfer] partial class SpeedScaleNode : SpeedScaleBaseNode { }

[KV3Transfer]
partial class StateCompletedConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public short TransitionDurationOverrideNodeIdx { get; } = -1;
    public float TransitionDurationSeconds { get; }
}

[KV3Transfer]
partial class StateMachineNode : PoseNode
{
    public StateMachineNode.StateDefinition[] StateDefinitions { get; } = [];
    public short DefaultStateIndex { get; } = -1;
}

partial class StateMachineNode
{
    [KV3Transfer]
    internal partial class StateDefinition
    {
        public short StateNodeIdx { get; } = -1;
        public short EntryConditionNodeIdx { get; } = -1;
        public StateMachineNode.TransitionDefinition[] TransitionDefinitions { get; } = [];
    }
}

partial class StateMachineNode
{
    [KV3Transfer]
    internal partial class TransitionDefinition
    {
        public short TargetStateIdx { get; } = -1;
        public short ConditionNodeIdx { get; } = -1;
        public short TransitionNodeIdx { get; } = -1;
        public bool CanBeForced { get; }
    }
}

[KV3Transfer]
partial class StateNode : PoseNode
{
    public short ChildNodeIdx { get; } = -1;
    public GlobalSymbol[] EntryEvents { get; } = [];
    public GlobalSymbol[] ExecuteEvents { get; } = [];
    public GlobalSymbol[] ExitEvents { get; } = [];
    public StateNode.TimedEvent[] TimedRemainingEvents { get; } = [];
    public StateNode.TimedEvent[] TimedElapsedEvents { get; } = [];
    public short LayerWeightNodeIdx { get; } = -1;
    public short LayerRootMotionWeightNodeIdx { get; } = -1;
    public short LayerBoneMaskNodeIdx { get; } = -1;
    public bool IsOffState { get; }
    public bool UseActualElapsedTimeInStateForTimedEvents { get; }
}

partial class StateNode
{
    [KV3Transfer]
    internal partial class TimedEvent
    {
        public GlobalSymbol ID { get; }
        public float TimeValueSeconds { get; }
        public StateNode.TimedEvent.Comparison ComparisionOperator { get; }
    }
}

partial class StateNode
{
    partial class TimedEvent
    {
        internal enum Comparison : byte
        {
            LessThanEqual = 0,
            GreaterThanEqual = 1,
        }
    }
}

[KV3Transfer]
partial class SyncEventIndexConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public SyncEventIndexConditionNode.TriggerModeType TriggerMode { get; }
    [KVProperty("m_syncEventIdx")]
    public int SyncEventIdx { get; } = -1;
}

partial class SyncEventIndexConditionNode
{
    internal enum TriggerModeType : byte
    {
        ExactlyAtEventIndex = 0,
        GreaterThanEqualToEventIndex = 1,
    }
}

[KV3Transfer]
partial class TargetInfoNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public TargetInfoNode.Info InfoType { get; } = TargetInfoNode.Info.Distance;
    public bool IsWorldSpaceTarget { get; } = true;
}

partial class TargetInfoNode
{
    internal enum Info : uint
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
partial class TargetOffsetNode : TargetValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public bool IsBoneSpaceOffset { get; } = true;
    public Quaternion RotationOffset { get; }
    public Vector3 TranslationOffset { get; }
}

[KV3Transfer]
partial class TargetPointNode : VectorValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public bool IsWorldSpaceTarget { get; } = true;
}

[KV3Transfer]
partial class TargetSelectorNode : ClipReferenceNode
{
    public short[] OptionNodeIndices { get; } = [];
    public float OrientationScoreWeight { get; } = 1f;
    public float PositionScoreWeight { get; } = 1f;
    [KVProperty("m_parameterNodeIdx")]
    public short ParameterNodeIdx { get; } = -1;
    public bool IgnoreInvalidOptions { get; }
    public bool IsWorldSpaceTarget { get; } = true;
    public GlobalSymbol AlignmentBoneID { get; }
}

[KV3Transfer] partial class TargetValueNode : ValueNode { }

[KV3Transfer]
partial class TargetWarpNode : PoseNode
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
    public GlobalSymbol AlignmentBoneID { get; }
}

partial class TargetWarpNode
{
    internal enum TargetUpdateRuleType : byte
    {
        None = 0,
        Recalculate = 1,
        Offset = 2,
        RecalculateOrOffset = 3,
    }
}

[KV3Transfer]
partial class TimeConditionNode : BoolValueNode
{
    [KVProperty("m_sourceStateNodeIdx")]
    public short SourceStateNodeIdx { get; } = -1;
    public short InputValueNodeIdx { get; } = -1;
    public float Comparand { get; }
    public TimeConditionNode.ComparisonType Type { get; } = TimeConditionNode.ComparisonType.ElapsedTime;
    public TimeConditionNode.OperatorType Operator { get; }
}

partial class TimeConditionNode
{
    internal enum ComparisonType : byte
    {
        PercentageThroughState = 0,
        PercentageThroughSyncEvent = 1,
        ElapsedTime = 2,
    }
}

partial class TimeConditionNode
{
    internal enum OperatorType : byte
    {
        LessThan = 0,
        LessThanEqual = 1,
        GreaterThan = 2,
        GreaterThanEqual = 3,
    }
}

[KV3Transfer]
partial class TimeControlledClipNode : PoseNode
{
    public short PlayInReverseValueNodeIdx { get; } = -1;
    public bool SampleRootMotion { get; } = true;
    public short DataSlotIdx { get; } = -1;
    public short TimeValueNodeIdx { get; } = -1;
    public GlobalSymbol[] GraphEvents { get; } = [];
}

[KV3Transfer]
partial class TransitionEventConditionNode : BoolValueNode
{
    public GlobalSymbol RequireRuleID { get; }
    public BitFlags EventConditionRules { get; }
    public short SourceStateNodeIdx { get; } = -1;
    public TransitionRuleCondition RuleCondition { get; }
}

[KV3Transfer]
partial class TransitionNode : PoseNode
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

partial class TransitionNode
{
    internal enum TransitionOptionsType : byte
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
partial class TwoBoneIKNode : PassthroughNode
{
    public GlobalSymbol EffectorBoneID { get; }
    public short EffectorTargetNodeIdx { get; } = -1;
    public short EnabledNodeIdx { get; } = -1;
    public float BlendTimeSeconds { get; }
    public IKBlendMode BlendMode { get; }
    public bool IsTargetInWorldSpace { get; }
    public float ChainRotationWeight { get; }
}

/// <summary>A graph node that produces a value.</summary>
[KV3Transfer] partial class ValueNode : GraphNode { }

[KV3Transfer]
partial class VectorCreateNode : VectorValueNode
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
partial class VectorInfoNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public VectorInfoNode.Info DesiredInfo { get; }
}

partial class VectorInfoNode
{
    internal enum Info : byte
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
partial class VectorNegateNode : VectorValueNode
{
    public short InputValueNodeIdx { get; } = -1;
}

[KV3Transfer] partial class VectorValueNode : ValueNode { }

[KV3Transfer] partial class VelocityBasedSpeedScaleNode : SpeedScaleBaseNode { }

[KV3Transfer] partial class VelocityBlendNode : ParameterizedBlendNode { }

[KV3Transfer]
partial class VirtualParameterBoneMaskNode : BoneMaskValueNode
{
    public short ChildNodeIdx { get; } = -1;
}

[KV3Transfer]
partial class VirtualParameterBoolNode : BoolValueNode
{
    public short ChildNodeIdx { get; } = -1;
}

[KV3Transfer]
partial class VirtualParameterFloatNode : FloatValueNode
{
    public short ChildNodeIdx { get; } = -1;
}

[KV3Transfer]
partial class VirtualParameterIDNode : IDValueNode
{
    public short ChildNodeIdx { get; } = -1;
}

[KV3Transfer]
partial class VirtualParameterTargetNode : TargetValueNode
{
    public short ChildNodeIdx { get; } = -1;
}

[KV3Transfer]
partial class VirtualParameterVectorNode : VectorValueNode
{
    public short ChildNodeIdx { get; } = -1;
}

[KV3Transfer] partial class ZeroPoseNode : PoseNode { }
