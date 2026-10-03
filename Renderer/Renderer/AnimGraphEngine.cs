using System.Diagnostics;
using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.AnimLib
{
    enum BranchState
    {
        Active,
        Inactive,
    };

    class LayerContext
    {
        public float Weight = 1f;
        public float RootMotionWeight = 1f;
        public BoneMaskTaskList MaskTaskList;
        public bool IsAdditive;

        public void Reset()
        {
            Weight = 1f;
            RootMotionWeight = 1f;
            MaskTaskList = default;
            IsAdditive = false;
        }

        public void CopyFrom(LayerContext other)
        {
            Weight = other.Weight;
            RootMotionWeight = other.RootMotionWeight;
            MaskTaskList = other.MaskTaskList;
            IsAdditive = other.IsAdditive;
        }
    }

    abstract partial class GraphNode
    {
        /// <summary>
        /// Wires child node references and definition-derived state. Runs once for every node in
        /// array order at graph construction (Esoterica InstantiateNode).
        /// </summary>
        public abstract void Instantiate(GraphContext context);

        private uint lastUpdateID = uint.MaxValue;

        /// <summary>True if this node has already been marked active during the current graph update.</summary>
        public bool WasUpdated(GraphContext ctx) => lastUpdateID == ctx.UpdateID;

        /// <summary>Marks this node as active/evaluated for the current graph update.</summary>
        public void MarkNodeActive(GraphContext ctx) => lastUpdateID = ctx.UpdateID;

        // Activation lifecycle (Esoterica GraphNode::Initialize/Shutdown): nodes are initialized
        // when their subtree becomes active and shut down when it deactivates, reference-counted
        // because value nodes can be shared by multiple active parents. Instances persist; the
        // lifecycle only resets internal state, so activation allocates nothing.
        private protected int initializationCount;

        public bool IsInitialized => initializationCount > 0;

        public virtual void Initialize(GraphContext ctx)
        {
            if (IsInitialized)
            {
                initializationCount++;
            }
            else
            {
                InitializeInternal(ctx);
            }
        }

        public void Shutdown(GraphContext ctx)
        {
            Debug.Assert(IsInitialized);
            if (initializationCount > 0 && --initializationCount == 0)
            {
                ShutdownInternal(ctx);
            }
        }

        protected virtual void InitializeInternal(GraphContext ctx)
        {
            Debug.Assert(!IsInitialized);
            initializationCount++;
        }

        protected virtual void ShutdownInternal(GraphContext ctx)
        {
            lastUpdateID = uint.MaxValue;
        }
    }

    abstract partial class ValueNode
    {
        public override void Instantiate(GraphContext ctx) { }
    }

    static class KVObjectExtensions2
    {
        public static GlobalSymbol[] GetSymbolArray(this KVObject collection, string name)
        {
            // Missing on resources compiled before the field was added
            return [.. (collection.GetArray<string>(name) ?? []).Select(s => new GlobalSymbol(s))];
        }

        /// <summary>Parses an 8-float KV3 array property (position, scale, rotation) into a transform.</summary>
        public static Transform GetTransformProperty(this KVObject collection, string name)
        {
            var data = collection.GetProperty<KVObject>(name);
            if (data == null)
            {
                return Transform.Identity;
            }

            var (position, scale, rotation) = data.ToTransform();
            return new Transform(position, scale, rotation);
        }

        /// <summary>Parses an array of 8-float KV3 arrays (position, scale, rotation) into transforms.</summary>
        public static Transform[] GetTransformArray(this KVObject collection, string name)
        {
            var outer = collection.GetArray(name);
            if (outer == null)
            {
                return [];
            }

            var result = new Transform[outer.Count];
            for (var i = 0; i < result.Length; i++)
            {
                var (position, scale, rotation) = outer[i].ToTransform();
                result[i] = new Transform(position, scale, rotation);
            }

            return result;
        }
    }

    class GraphContext
    {
        public AnimationGraph Graph { get; }
        public GraphNode[] Nodes { get; set; }
        public PoseNode RootNode { get; set; }

        public BranchState BranchState { get; set; } = BranchState.Active;
        public float DeltaTime { get; set; }

        /// <summary>Incremented once per graph update; used by nodes to evaluate at most once per frame.</summary>
        public uint UpdateID { get; private set; }

        /// <summary>The AnimLib view of the graph's skeleton (reference pose, bone masks).</summary>
        public Skeleton Skeleton => Graph.AnimLibSkeleton;

        /// <summary>The pose produced by the previous graph update, used to resolve bone targets.</summary>
        public Pose Pose { get; }

        /// <summary>The events sampled during the current graph update.</summary>
        public SampledEventsBuffer SampledEvents { get; } = new();

        public Transform WorldTransform = Transform.Identity;
        public Transform WorldTransformInverse = Transform.Identity;

        private GraphDefinition graphDefinition;

        // Control and virtual parameter node indices by parameter name
        private readonly Dictionary<string, short> parameterLookup = [];

        public GraphContext(KVObject graph, AnimationGraph owner)
        {
            graphDefinition = new GraphDefinition(graph);
            Graph = owner;
            LayerContext = ownLayerContext;
            Pose = new Pose(owner.AnimLibSkeleton);

            // Create nodes
            var nodeArray = graph.GetArray<KVObject>("m_nodes");
            Nodes = new GraphNode[nodeArray.Length];

            // Transfer node data from KVObject
            for (short i = 0; i < nodeArray.Length; i++)
            {
                Nodes[i] = CreateNode(nodeArray[i]);
            }

            for (short i = 0; i < graphDefinition.ControlParameterIDs.Length; i++)
            {
                parameterLookup.TryAdd(graphDefinition.ControlParameterIDs[i].Name, i);
            }

            for (var i = 0; i < graphDefinition.VirtualParameterIDs.Length; i++)
            {
                parameterLookup.TryAdd(graphDefinition.VirtualParameterIDs[i].Name, graphDefinition.VirtualParameterNodeIndices[i]);
            }

            // Instantiate nodes: wire strong references and definition-derived state. Referenced graphs
            // map their parameters by name here, so the lookup has to be filled first.
            foreach (var node in Nodes)
            {
                node.Instantiate(this);
            }

            RootNode = (PoseNode)Nodes[graphDefinition.RootNodeIdx];

            // Initialize persistent graph nodes (control and virtual parameters); they stay
            // initialized for the instance's whole life (Esoterica GraphInstance::Initialize).
            // The root node initializes lazily on the first update.
            foreach (var nodeIdx in graphDefinition.PersistentNodeIndices)
            {
                Nodes[nodeIdx].Initialize(this);
            }
        }

        /// <summary>
        /// (Re)initializes the root node tree at the given time (Esoterica
        /// GraphInstance::ResetGraphState).
        /// </summary>
        public void ResetGraphState(SyncTrackTime initTime = default)
        {
            if (RootNode.IsInitialized)
            {
                RootNode.Shutdown(this);
            }

            // Bump the update ID to ensure that any initialization code that relies on it is
            // dirtied (value nodes evaluated during initialization must recompute).
            UpdateID++;
            RootNode.Initialize(this, initTime);
        }

        /// <summary>Creates the AnimLib node instance for one <c>m_nodes</c> entry of a graph definition.</summary>
        public static GraphNode CreateNode(KVObject nodeData)
        {
            var @class = nodeData.GetProperty<string>("_class")
                ?? throw new InvalidOperationException("Graph node has no _class property.");

            // Schema class names minus the CNm prefix and ::CDefinition suffix
            var nodeTypeName = @class["CNm".Length..^"::CDefinition".Length];

            return nodeTypeName switch
            {
                "AimCSNode" => new AimCSNode(nodeData),
                "AndNode" => new AndNode(nodeData),
                "AnimationPoseNode" => new AnimationPoseNode(nodeData),
                "Blend1DNode" => new Blend1DNode(nodeData),
                "Blend2DNode" => new Blend2DNode(nodeData),
                "BodyGroupNode" => new BodyGroupNode(nodeData),
                "BoneMaskBlendNode" => new BoneMaskBlendNode(nodeData),
                "BoneMaskNode" => new BoneMaskNode(nodeData),
                "BoneMaskSelectorNode" => new BoneMaskSelectorNode(nodeData),
                "BoneMaskSwitchNode" => new BoneMaskSwitchNode(nodeData),
                "BoneMaskValueNode" => new BoneMaskValueNode(nodeData),
                "BoolValueNode" => new BoolValueNode(nodeData),
                "CachedBoolNode" => new CachedBoolNode(nodeData),
                "CachedFloatNode" => new CachedFloatNode(nodeData),
                "CachedIDNode" => new CachedIDNode(nodeData),
                "CachedTargetNode" => new CachedTargetNode(nodeData),
                "CachedVectorNode" => new CachedVectorNode(nodeData),
                "ChainLookatNode" => new ChainLookatNode(nodeData),
                "ClipNode" => new ClipNode(nodeData),
                "ClipSelectorNode" => new ClipSelectorNode(nodeData),
                "ConstBoolNode" => new ConstBoolNode(nodeData),
                "ConstFloatNode" => new ConstFloatNode(nodeData),
                "ConstIDNode" => new ConstIDNode(nodeData),
                "ConstTargetNode" => new ConstTargetNode(nodeData),
                "ConstVectorNode" => new ConstVectorNode(nodeData),
                "ControlParameterBoolNode" => new ControlParameterBoolNode(nodeData),
                "ControlParameterFloatNode" => new ControlParameterFloatNode(nodeData),
                "ControlParameterIDNode" => new ControlParameterIDNode(nodeData),
                "ControlParameterTargetNode" => new ControlParameterTargetNode(nodeData),
                "ControlParameterVectorNode" => new ControlParameterVectorNode(nodeData),
                "CurrentSyncEventIDNode" => new CurrentSyncEventIDNode(nodeData),
                "CurrentSyncEventNode" => new CurrentSyncEventNode(nodeData),
                "DurationScaleNode" => new DurationScaleNode(nodeData),
                "ExternalPoseNode" => new ExternalPoseNode(nodeData),
                "FixedWeightBoneMaskNode" => new FixedWeightBoneMaskNode(nodeData),
                "FloatAngleMathNode" => new FloatAngleMathNode(nodeData),
                "FloatClampNode" => new FloatClampNode(nodeData),
                "FloatComparisonNode" => new FloatComparisonNode(nodeData),
                "FloatCurveEventNode" => new FloatCurveEventNode(nodeData),
                "FloatCurveNode" => new FloatCurveNode(nodeData),
                "FloatEaseNode" => new FloatEaseNode(nodeData),
                "FloatMathNode" => new FloatMathNode(nodeData),
                "FloatRangeComparisonNode" => new FloatRangeComparisonNode(nodeData),
                "FloatRemapNode" => new FloatRemapNode(nodeData),
                "FloatSelectorNode" => new FloatSelectorNode(nodeData),
                "FloatSpringNode" => new FloatSpringNode(nodeData),
                "FloatSwitchNode" => new FloatSwitchNode(nodeData),
                "FloatValueNode" => new FloatValueNode(nodeData),
                "FollowBoneNode" => new FollowBoneNode(nodeData),
                "FootEventConditionNode" => new FootEventConditionNode(nodeData),
                "FootIKNode" => new FootIKNode(nodeData),
                "FootstepEventIDNode" => new FootstepEventIDNode(nodeData),
                "FootstepEventPercentageThroughNode" => new FootstepEventPercentageThroughNode(nodeData),
                "GraphEventConditionNode" => new GraphEventConditionNode(nodeData),
                "IDBasedClipSelectorNode" => new IDBasedClipSelectorNode(nodeData),
                "IDBasedSelectorNode" => new IDBasedSelectorNode(nodeData),
                "IDComparisonNode" => new IDComparisonNode(nodeData),
                "IDEventConditionNode" => new IDEventConditionNode(nodeData),
                "IDEventNode" => new IDEventNode(nodeData),
                "IDEventPercentageThroughNode" => new IDEventPercentageThroughNode(nodeData),
                "IDSelectorNode" => new IDSelectorNode(nodeData),
                "IDSwitchNode" => new IDSwitchNode(nodeData),
                "IDToFloatNode" => new IDToFloatNode(nodeData),
                "IDValueNode" => new IDValueNode(nodeData),
                "IsExternalGraphSlotFilledNode" => new IsExternalGraphSlotFilledNode(nodeData),
                "IsExternalPoseSetNode" => new IsExternalPoseSetNode(nodeData),
                "IsInactiveBranchConditionNode" => new IsInactiveBranchConditionNode(nodeData),
                "IsTargetSetNode" => new IsTargetSetNode(nodeData),
                "LayerBlendNode" => new LayerBlendNode(nodeData),
                "NotNode" => new NotNode(nodeData),
                "OrNode" => new OrNode(nodeData),
                "OrientationWarpNode" => new OrientationWarpNode(nodeData),
                "ParameterizedBlendNode" => new ParameterizedBlendNode(nodeData),
                "ParameterizedClipSelectorNode" => new ParameterizedClipSelectorNode(nodeData),
                "ParameterizedSelectorNode" => new ParameterizedSelectorNode(nodeData),
                "PassthroughNode" => new PassthroughNode(nodeData),
                "PoseNode" => new PoseNode(nodeData),
                "ReferencePoseNode" => new ReferencePoseNode(nodeData),
                "ReferencedGraphNode" => new ReferencedGraphNode(nodeData),
                "RootMotionOverrideNode" => new RootMotionOverrideNode(nodeData),
                "ScaleNode" => new ScaleNode(nodeData),
                "SelectorNode" => new SelectorNode(nodeData),
                "SnapWeaponNode" => new SnapWeaponNode(nodeData),
                "SpeedScaleBaseNode" => new SpeedScaleBaseNode(nodeData),
                "SpeedScaleNode" => new SpeedScaleNode(nodeData),
                "StateCompletedConditionNode" => new StateCompletedConditionNode(nodeData),
                "StateMachineNode" => new StateMachineNode(nodeData),
                "StateNode" => new StateNode(nodeData),
                "SyncEventIndexConditionNode" => new SyncEventIndexConditionNode(nodeData),
                "TargetInfoNode" => new TargetInfoNode(nodeData),
                "TargetOffsetNode" => new TargetOffsetNode(nodeData),
                "TargetPointNode" => new TargetPointNode(nodeData),
                "TargetSelectorNode" => new TargetSelectorNode(nodeData),
                "TargetValueNode" => new TargetValueNode(nodeData),
                "TargetWarpNode" => new TargetWarpNode(nodeData),
                "TimeConditionNode" => new TimeConditionNode(nodeData),
                "TimeControlledClipNode" => new TimeControlledClipNode(nodeData),
                "TransitionEventConditionNode" => new TransitionEventConditionNode(nodeData),
                "TransitionNode" => new TransitionNode(nodeData),
                "TwoBoneIKNode" => new TwoBoneIKNode(nodeData),
                "VectorCreateNode" => new VectorCreateNode(nodeData),
                "VectorInfoNode" => new VectorInfoNode(nodeData),
                "VectorNegateNode" => new VectorNegateNode(nodeData),
                "VectorValueNode" => new VectorValueNode(nodeData),
                "VelocityBasedSpeedScaleNode" => new VelocityBasedSpeedScaleNode(nodeData),
                "VelocityBlendNode" => new VelocityBlendNode(nodeData),
                "VirtualParameterBoneMaskNode" => new VirtualParameterBoneMaskNode(nodeData),
                "VirtualParameterBoolNode" => new VirtualParameterBoolNode(nodeData),
                "VirtualParameterFloatNode" => new VirtualParameterFloatNode(nodeData),
                "VirtualParameterIDNode" => new VirtualParameterIDNode(nodeData),
                "VirtualParameterTargetNode" => new VirtualParameterTargetNode(nodeData),
                "VirtualParameterVectorNode" => new VirtualParameterVectorNode(nodeData),
                "ZeroPoseNode" => new ZeroPoseNode(nodeData),
                _ => throw new InvalidOperationException($"Unknown graph node type {nodeTypeName}."),
            };
        }

        /// <summary>Lists the state each active state machine is in, by node path.</summary>
        public void DescribeActiveStates(System.Text.StringBuilder output, string indent)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;

            foreach (var node in Nodes)
            {
                if (node is not StateMachineNode { IsInitialized: true, IsValid: true } stateMachine)
                {
                    continue;
                }

                var transition = stateMachine.ActiveTransition is { } activeTransition
                    ? $" (transitioning, {activeTransition.ProgressPercentage:P0})"
                    : string.Empty;

                output.Append(culture, $"{indent}{GetNodePath(stateMachine.NodeIdx)} -> {GetNodePath(stateMachine.ActiveStateNode.NodeIdx)}{transition}{Environment.NewLine}");
            }
        }

        private string GetNodePath(short nodeIdx)
            => nodeIdx >= 0 && nodeIdx < graphDefinition.NodePaths.Length ? graphDefinition.NodePaths[nodeIdx] : $"#{nodeIdx}";

        /// <summary>The control or virtual parameter node with the given name, if the graph has one.</summary>
        public ValueNode? GetParameterNode(string name)
            => parameterLookup.TryGetValue(name, out var nodeIdx) ? (ValueNode)Nodes[nodeIdx] : null;

        // Layer context. Transitions temporarily swap in a scratch context for their target state
        // (Esoterica swaps the m_pLayerContext pointer), so the reference is settable.
        public bool IsInLayer { get; set; }
        public LayerContext LayerContext { get; set; }

        private readonly LayerContext ownLayerContext = new();

        private Transform[]? zeroPose;

        /// <summary>
        /// The pose standing in for a source that produced none: the zero pose inside an additive
        /// layer, the reference pose otherwise.
        /// </summary>
        public Transform[] GetDefaultPose()
        {
            if (IsInLayer && LayerContext.IsAdditive)
            {
                if (zeroPose == null)
                {
                    zeroPose = new Transform[Graph.ParentSpaceReferencePose.Length];
                    Array.Fill(zeroPose, TransformMath.Zero);
                }

                return zeroPose;
            }

            return Graph.ParentSpaceReferencePose;
        }

        // A referenced graph evaluates within its parent's layer, branch and world
        private void TransferContextDataFromParent(GraphContext parent)
        {
            LayerContext = parent.LayerContext;
            IsInLayer = parent.IsInLayer;
            DeltaTime = parent.DeltaTime;
            WorldTransform = parent.WorldTransform;
            WorldTransformInverse = parent.WorldTransformInverse;
            BranchState = parent.BranchState;
        }

        private void ReleaseParentContextData()
        {
            LayerContext = ownLayerContext;
            IsInLayer = false;
        }

        public void ResetReferencedGraphState(GraphContext parent, SyncTrackTime initTime)
        {
            TransferContextDataFromParent(parent);
            ResetGraphState(initTime);
            ReleaseParentContextData();
        }

        public GraphPoseNodeResult EvaluateReferencedGraph(GraphContext parent, SyncTrackTimeRange? updateRange)
        {
            TransferContextDataFromParent(parent);
            var result = Update(parent.DeltaTime, updateRange);
            ReleaseParentContextData();
            return result;
        }

        /// <summary>Scratch buffers for bone mask task list evaluation.</summary>
        public BoneMaskPool BoneMaskPool { get; } = new();

        // Cached pose buffers (stand-in for Esoterica's task-system cached pose buffers): forced
        // transitions snapshot their in-flight blend here so the same state is never updated twice
        // in one frame. Buffers are recycled, so steady state allocates nothing.
        private readonly List<Transform[]> cachedPoseBuffers = [];
        private readonly List<bool> cachedPoseBufferInUse = [];

        public int CreateCachedPose()
        {
            for (var i = 0; i < cachedPoseBuffers.Count; i++)
            {
                if (!cachedPoseBufferInUse[i])
                {
                    cachedPoseBufferInUse[i] = true;
                    Graph.ParentSpaceReferencePose.CopyTo(cachedPoseBuffers[i], 0);
                    return i;
                }
            }

            var buffer = new Transform[Graph.ParentSpaceReferencePose.Length];
            Graph.ParentSpaceReferencePose.CopyTo(buffer, 0);
            cachedPoseBuffers.Add(buffer);
            cachedPoseBufferInUse.Add(true);
            return cachedPoseBuffers.Count - 1;
        }

        public bool IsValidCachedPose(int id) => id >= 0 && id < cachedPoseBuffers.Count && cachedPoseBufferInUse[id];

        public Transform[] GetCachedPoseBuffer(int id)
        {
            Debug.Assert(IsValidCachedPose(id));
            return cachedPoseBuffers[id];
        }

        public void DestroyCachedPose(int id)
        {
            Debug.Assert(IsValidCachedPose(id));
            cachedPoseBufferInUse[id] = false;
        }

        /// <summary>Resolves a referenced graph slot index to the instantiated child graph, if any.</summary>
        public AnimationGraph? GetReferencedGraph(short referencedGraphIdx)
        {
            var slots = graphDefinition.ReferencedGraphSlots;
            if (referencedGraphIdx < 0 || referencedGraphIdx >= slots.Length)
            {
                return null;
            }

            var dataSlotIdx = slots[referencedGraphIdx].DataSlotIdx;
            if (dataSlotIdx < 0 || dataSlotIdx >= Graph.ChildGraphs.Length)
            {
                return null;
            }

            return Graph.ChildGraphs[dataSlotIdx];
        }

        public void SetNodeFromIndex<T>(short childNodeIdx, ref T childNode) where T : GraphNode
        {
            Debug.Assert(childNodeIdx < Nodes.Length);
            childNode = (T)Nodes[childNodeIdx];
        }

        public void SetOptionalNodeFromIndex<T>(short childNodeIdx, ref T? childNode) where T : GraphNode
        {
            if (childNodeIdx >= 0)
            {
                SetNodeFromIndex(childNodeIdx, ref childNode!);
            }
        }

        public void SetNodesFromIndexArray<T>(short[] childNodeIndices, ref T[] childNodes) where T : GraphNode
        {
            childNodes = new T[childNodeIndices.Length];

            for (var i = 0; i < childNodeIndices.Length; i++)
            {
                SetNodeFromIndex(childNodeIndices[i], ref childNodes[i]);
            }
        }

        private readonly HashSet<(short, string)> loggedWarnings = [];

        public void LogWarning(short nodeIdx, string message)
        {
            // Lifecycle warnings (invalid selections, missing clips) can repeat on every state
            // entry; log each distinct warning once per node.
            if (loggedWarnings.Add((nodeIdx, message)))
            {
                Console.WriteLine($"[AnimGraph][Node {nodeIdx}] Warning: {message}");
            }
        }

        private readonly HashSet<string> warnedNotImplemented = [];

        /// <summary>
        /// Logs, once per node type, that a value node has no implementation yet and evaluates to a
        /// default value, so a single unported node degrades the graph instead of killing it.
        /// </summary>
        public void LogNodeNotImplemented(short nodeIdx, string typeName)
        {
            if (warnedNotImplemented.Add(typeName))
            {
                Console.WriteLine($"[AnimGraph][Node {nodeIdx}] {typeName} is not implemented yet; returning a default value.");
            }
        }

        public GraphPoseNodeResult Update(float timeStep, SyncTrackTimeRange? updateRange = null)
        {
            UpdateID++;
            DeltaTime = timeStep;
            SampledEvents.Clear();

            if (!RootNode.IsInitialized)
            {
                ResetGraphState();
            }

            var poseResult = RootNode.Update(this, updateRange);
            Pose.SetParentSpaceTransforms(poseResult.Pose);
            return poseResult;
        }
    }
}
