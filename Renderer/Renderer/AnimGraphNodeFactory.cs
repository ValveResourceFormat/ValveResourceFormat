using System.Collections.Concurrent;
using ValveResourceFormat.Renderer.AnimGraphNodes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.AnimLib
{
    /// <summary>
    /// Creates an animation graph node from its compiled definition.
    /// </summary>
    /// <param name="definition">The <c>m_nodes</c> entry of the graph definition.</param>
    public delegate GraphNode GraphNodeCreator(KVObject definition);

    /// <summary>
    /// Maps compiled graph node classes to the runtime nodes that evaluate them.
    /// </summary>
    /// <remarks>
    /// Filled in statically rather than by scanning types, to stay trim-safe and AOT-compatible. Game code
    /// adds its own nodes, or replaces built-in ones, through <see cref="Register"/>.
    /// </remarks>
    public static class GraphNodeFactory
    {
        // Graphs load on several threads, and registrations may come in later
        private static readonly ConcurrentDictionary<string, GraphNodeCreator> Creators = new(StringComparer.Ordinal);

        static GraphNodeFactory()
        {
            Register("CNmAndNode::CDefinition", static data => new AndNode(data));
            Register("CNmAnimationPoseNode::CDefinition", static data => new AnimationPoseNode(data));
            Register("CNmBlend1DNode::CDefinition", static data => new Blend1DNode(data));
            Register("CNmBlend2DNode::CDefinition", static data => new Blend2DNode(data));
            Register("CNmBodyGroupNode::CDefinition", static data => new BodyGroupNode(data));
            Register("CNmBoneMaskBlendNode::CDefinition", static data => new BoneMaskBlendNode(data));
            Register("CNmBoneMaskNode::CDefinition", static data => new BoneMaskNode(data));
            Register("CNmBoneMaskSelectorNode::CDefinition", static data => new BoneMaskSelectorNode(data));
            Register("CNmBoneMaskSwitchNode::CDefinition", static data => new BoneMaskSwitchNode(data));
            Register("CNmBoneMaskValueNode::CDefinition", static data => new BoneMaskValueNode(data));
            Register("CNmBoolValueNode::CDefinition", static data => new BoolValueNode(data));
            Register("CNmCachedBoolNode::CDefinition", static data => new CachedBoolNode(data));
            Register("CNmCachedFloatNode::CDefinition", static data => new CachedFloatNode(data));
            Register("CNmCachedIDNode::CDefinition", static data => new CachedIDNode(data));
            Register("CNmCachedTargetNode::CDefinition", static data => new CachedTargetNode(data));
            Register("CNmCachedVectorNode::CDefinition", static data => new CachedVectorNode(data));
            Register("CNmChainLookatNode::CDefinition", static data => new ChainLookatNode(data));
            Register("CNmClipNode::CDefinition", static data => new ClipNode(data));
            Register("CNmClipSelectorNode::CDefinition", static data => new ClipSelectorNode(data));
            Register("CNmConstBoolNode::CDefinition", static data => new ConstBoolNode(data));
            Register("CNmConstFloatNode::CDefinition", static data => new ConstFloatNode(data));
            Register("CNmConstIDNode::CDefinition", static data => new ConstIDNode(data));
            Register("CNmConstTargetNode::CDefinition", static data => new ConstTargetNode(data));
            Register("CNmConstVectorNode::CDefinition", static data => new ConstVectorNode(data));
            Register("CNmControlParameterBoolNode::CDefinition", static data => new ControlParameterBoolNode(data));
            Register("CNmControlParameterFloatNode::CDefinition", static data => new ControlParameterFloatNode(data));
            Register("CNmControlParameterIDNode::CDefinition", static data => new ControlParameterIDNode(data));
            Register("CNmControlParameterTargetNode::CDefinition", static data => new ControlParameterTargetNode(data));
            Register("CNmControlParameterVectorNode::CDefinition", static data => new ControlParameterVectorNode(data));
            Register("CNmCurrentSyncEventIDNode::CDefinition", static data => new CurrentSyncEventIDNode(data));
            Register("CNmCurrentSyncEventNode::CDefinition", static data => new CurrentSyncEventNode(data));
            Register("CNmDurationScaleNode::CDefinition", static data => new DurationScaleNode(data));
            Register("CNmExternalPoseNode::CDefinition", static data => new ExternalPoseNode(data));
            Register("CNmFixedWeightBoneMaskNode::CDefinition", static data => new FixedWeightBoneMaskNode(data));
            Register("CNmFloatAngleMathNode::CDefinition", static data => new FloatAngleMathNode(data));
            Register("CNmFloatClampNode::CDefinition", static data => new FloatClampNode(data));
            Register("CNmFloatComparisonNode::CDefinition", static data => new FloatComparisonNode(data));
            Register("CNmFloatCurveEventNode::CDefinition", static data => new FloatCurveEventNode(data));
            Register("CNmFloatCurveNode::CDefinition", static data => new FloatCurveNode(data));
            Register("CNmFloatEaseNode::CDefinition", static data => new FloatEaseNode(data));
            Register("CNmFloatMathNode::CDefinition", static data => new FloatMathNode(data));
            Register("CNmFloatRangeComparisonNode::CDefinition", static data => new FloatRangeComparisonNode(data));
            Register("CNmFloatRemapNode::CDefinition", static data => new FloatRemapNode(data));
            Register("CNmFloatSelectorNode::CDefinition", static data => new FloatSelectorNode(data));
            Register("CNmFloatSpringNode::CDefinition", static data => new FloatSpringNode(data));
            Register("CNmFloatSwitchNode::CDefinition", static data => new FloatSwitchNode(data));
            Register("CNmFloatValueNode::CDefinition", static data => new FloatValueNode(data));
            Register("CNmFollowBoneNode::CDefinition", static data => new FollowBoneNode(data));
            Register("CNmFootEventConditionNode::CDefinition", static data => new FootEventConditionNode(data));
            Register("CNmFootIKNode::CDefinition", static data => new FootIKNode(data));
            Register("CNmFootstepEventIDNode::CDefinition", static data => new FootstepEventIDNode(data));
            Register("CNmFootstepEventPercentageThroughNode::CDefinition", static data => new FootstepEventPercentageThroughNode(data));
            Register("CNmGraphEventConditionNode::CDefinition", static data => new GraphEventConditionNode(data));
            Register("CNmIDBasedClipSelectorNode::CDefinition", static data => new IDBasedClipSelectorNode(data));
            Register("CNmIDBasedSelectorNode::CDefinition", static data => new IDBasedSelectorNode(data));
            Register("CNmIDComparisonNode::CDefinition", static data => new IDComparisonNode(data));
            Register("CNmIDEventConditionNode::CDefinition", static data => new IDEventConditionNode(data));
            Register("CNmIDEventNode::CDefinition", static data => new IDEventNode(data));
            Register("CNmIDEventPercentageThroughNode::CDefinition", static data => new IDEventPercentageThroughNode(data));
            Register("CNmIDSelectorNode::CDefinition", static data => new IDSelectorNode(data));
            Register("CNmIDSwitchNode::CDefinition", static data => new IDSwitchNode(data));
            Register("CNmIDToFloatNode::CDefinition", static data => new IDToFloatNode(data));
            Register("CNmIDValueNode::CDefinition", static data => new IDValueNode(data));
            Register("CNmIsExternalGraphSlotFilledNode::CDefinition", static data => new IsExternalGraphSlotFilledNode(data));
            Register("CNmIsExternalPoseSetNode::CDefinition", static data => new IsExternalPoseSetNode(data));
            Register("CNmIsInactiveBranchConditionNode::CDefinition", static data => new IsInactiveBranchConditionNode(data));
            Register("CNmIsTargetSetNode::CDefinition", static data => new IsTargetSetNode(data));
            Register("CNmLayerBlendNode::CDefinition", static data => new LayerBlendNode(data));
            Register("CNmNotNode::CDefinition", static data => new NotNode(data));
            Register("CNmOrNode::CDefinition", static data => new OrNode(data));
            Register("CNmOrientationWarpNode::CDefinition", static data => new OrientationWarpNode(data));
            Register("CNmParameterizedBlendNode::CDefinition", static data => new ParameterizedBlendNode(data));
            Register("CNmParameterizedClipSelectorNode::CDefinition", static data => new ParameterizedClipSelectorNode(data));
            Register("CNmParameterizedSelectorNode::CDefinition", static data => new ParameterizedSelectorNode(data));
            Register("CNmPassthroughNode::CDefinition", static data => new PassthroughNode(data));
            Register("CNmPoseNode::CDefinition", static data => new PoseNode(data));
            Register("CNmReferencePoseNode::CDefinition", static data => new ReferencePoseNode(data));
            Register("CNmReferencedGraphNode::CDefinition", static data => new ReferencedGraphNode(data));
            Register("CNmRootMotionOverrideNode::CDefinition", static data => new RootMotionOverrideNode(data));
            Register("CNmScaleNode::CDefinition", static data => new ScaleNode(data));
            Register("CNmSelectorNode::CDefinition", static data => new SelectorNode(data));
            Register("CNmSpeedScaleBaseNode::CDefinition", static data => new SpeedScaleBaseNode(data));
            Register("CNmSpeedScaleNode::CDefinition", static data => new SpeedScaleNode(data));
            Register("CNmStateCompletedConditionNode::CDefinition", static data => new StateCompletedConditionNode(data));
            Register("CNmStateMachineNode::CDefinition", static data => new StateMachineNode(data));
            Register("CNmStateNode::CDefinition", static data => new StateNode(data));
            Register("CNmSyncEventIndexConditionNode::CDefinition", static data => new SyncEventIndexConditionNode(data));
            Register("CNmTargetInfoNode::CDefinition", static data => new TargetInfoNode(data));
            Register("CNmTargetOffsetNode::CDefinition", static data => new TargetOffsetNode(data));
            Register("CNmTargetPointNode::CDefinition", static data => new TargetPointNode(data));
            Register("CNmTargetSelectorNode::CDefinition", static data => new TargetSelectorNode(data));
            Register("CNmTargetValueNode::CDefinition", static data => new TargetValueNode(data));
            Register("CNmTargetWarpNode::CDefinition", static data => new TargetWarpNode(data));
            Register("CNmTimeConditionNode::CDefinition", static data => new TimeConditionNode(data));
            Register("CNmTimeControlledClipNode::CDefinition", static data => new TimeControlledClipNode(data));
            Register("CNmTransitionEventConditionNode::CDefinition", static data => new TransitionEventConditionNode(data));
            Register("CNmTransitionNode::CDefinition", static data => new TransitionNode(data));
            Register("CNmTwoBoneIKNode::CDefinition", static data => new TwoBoneIKNode(data));
            Register("CNmVectorCreateNode::CDefinition", static data => new VectorCreateNode(data));
            Register("CNmVectorInfoNode::CDefinition", static data => new VectorInfoNode(data));
            Register("CNmVectorNegateNode::CDefinition", static data => new VectorNegateNode(data));
            Register("CNmVectorValueNode::CDefinition", static data => new VectorValueNode(data));
            Register("CNmVelocityBasedSpeedScaleNode::CDefinition", static data => new VelocityBasedSpeedScaleNode(data));
            Register("CNmVelocityBlendNode::CDefinition", static data => new VelocityBlendNode(data));
            Register("CNmVirtualParameterBoneMaskNode::CDefinition", static data => new VirtualParameterBoneMaskNode(data));
            Register("CNmVirtualParameterBoolNode::CDefinition", static data => new VirtualParameterBoolNode(data));
            Register("CNmVirtualParameterFloatNode::CDefinition", static data => new VirtualParameterFloatNode(data));
            Register("CNmVirtualParameterIDNode::CDefinition", static data => new VirtualParameterIDNode(data));
            Register("CNmVirtualParameterTargetNode::CDefinition", static data => new VirtualParameterTargetNode(data));
            Register("CNmVirtualParameterVectorNode::CDefinition", static data => new VirtualParameterVectorNode(data));
            Register("CNmZeroPoseNode::CDefinition", static data => new ZeroPoseNode(data));

            // Client nodes
            Register("CNmAimCSNode::CDefinition", static data => new AimCSNode(data));
            Register("CNmSnapWeaponNode::CDefinition", static data => new SnapWeaponNode(data));
        }

        /// <summary>
        /// Registers the node created for a compiled class, replacing any earlier registration.
        /// </summary>
        /// <param name="className">The <c>_class</c> of the compiled definition, such as <c>CNmClipNode::CDefinition</c>.</param>
        /// <param name="creator">Creates the node from its definition.</param>
        public static void Register(string className, GraphNodeCreator creator)
        {
            Creators[className] = creator;
        }

        /// <summary>Gets whether a node is registered for a compiled class.</summary>
        public static bool IsSupported(string className) => Creators.ContainsKey(className);

        /// <summary>Creates the node for one <c>m_nodes</c> entry of a graph definition.</summary>
        /// <exception cref="InvalidOperationException">No node is registered for the definition's class.</exception>
        public static GraphNode Create(KVObject definition)
        {
            var className = definition.GetProperty<string>("_class")
                ?? throw new InvalidOperationException("Graph node has no _class property.");

            if (!Creators.TryGetValue(className, out var creator))
            {
                throw new InvalidOperationException($"Unknown graph node type {className}.");
            }

            return creator(definition);
        }
    }
}
