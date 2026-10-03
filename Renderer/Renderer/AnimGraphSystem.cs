using System.Diagnostics;
using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.AnimLib
{
    /// <summary>Whether nodes are on the active branch of their state machine.</summary>
    public enum BranchState
    {
        /// <summary>On the active branch.</summary>
        Active,
        /// <summary>On an inactive branch that is blending out.</summary>
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

    public abstract partial class GraphNode
    {
        /// <summary>
        /// Wires child node references and definition-derived state. Runs once for every node in
        /// array order at graph construction (Esoterica InstantiateNode).
        /// </summary>
        public abstract void Instantiate(GraphContext ctx);

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

        /// <summary>Whether the node is active.</summary>
        public bool IsInitialized => initializationCount > 0;

        /// <summary>Activates the node, or adds a reference if it is already active.</summary>
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

        /// <summary>Releases a reference, deactivating the node when none remain.</summary>
        public void Shutdown(GraphContext ctx)
        {
            Debug.Assert(IsInitialized);
            if (initializationCount > 0 && --initializationCount == 0)
            {
                ShutdownInternal(ctx);
            }
        }

        /// <summary>Resets the node state when it becomes active.</summary>
        protected virtual void InitializeInternal(GraphContext ctx)
        {
            Debug.Assert(!IsInitialized);
            initializationCount++;
        }

        /// <summary>Clears the node state when it becomes inactive.</summary>
        protected virtual void ShutdownInternal(GraphContext ctx)
        {
            lastUpdateID = uint.MaxValue;
        }
    }

    public abstract partial class ValueNode
    {
        /// <inheritdoc/>
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

    /// <summary>The state shared by the nodes of a graph during an update.</summary>
    public sealed class GraphContext
    {
        /// <summary>The graph this context evaluates.</summary>
        public AnimationGraph Graph { get; }
        internal GraphNode[] Nodes { get; set; }
        internal PoseNode RootNode { get; set; }

        /// <summary>Whether the nodes being updated are on the active branch of their state machine.</summary>
        public BranchState BranchState { get; internal set; } = BranchState.Active;

        /// <summary>Seconds advanced by the current update.</summary>
        public float DeltaTime { get; internal set; }

        /// <summary>Incremented once per graph update; used by nodes to evaluate at most once per frame.</summary>
        public uint UpdateID { get; private set; }

        /// <summary>The AnimLib view of the graph's skeleton (reference pose, bone masks).</summary>
        public Skeleton Skeleton => Graph.AnimLibSkeleton;

        /// <summary>The pose produced by the previous graph update, used to resolve bone targets.</summary>
        internal Pose Pose { get; }

        /// <summary>The events sampled during the current graph update.</summary>
        public SampledEventsBuffer SampledEvents { get; } = new();

        /// <summary>The character's world transform for the current update.</summary>
        public Transform WorldTransform { get; internal set; } = Transform.Identity;

        /// <summary>The inverse of <see cref="WorldTransform"/>.</summary>
        public Transform WorldTransformInverse { get; internal set; } = Transform.Identity;

        private GraphDefinition graphDefinition;

        // Control and virtual parameter node indices by parameter name
        private readonly Dictionary<string, short> parameterLookup = [];

        internal GraphContext(KVObject graph, AnimationGraph owner)
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
                Nodes[i] = GraphNodeFactory.Create(nodeArray[i]);
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

            // Everything an update can need is sized here, so updates never allocate
            var boneCount = owner.ParentSpaceReferencePose.Length;
            BoneMaskPool = new BoneMaskPool(boneCount);
            zeroPose = new Transform[boneCount];
            Array.Fill(zeroPose, TransformMath.Zero);
            AllocateCachedPoses(boneCount);
            SampledEvents.EnsureCapacity(CountPossibleEvents());

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
        internal void ResetGraphState(SyncTrackTime initTime = default)
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

        /// <summary>Lists the state each active state machine is in, by node path.</summary>
        internal void DescribeActiveStates(System.Text.StringBuilder output, string indent)
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
        internal ValueNode? GetParameterNode(string name)
            => parameterLookup.TryGetValue(name, out var nodeIdx) ? (ValueNode)Nodes[nodeIdx] : null;

        // Layer context. Transitions temporarily swap in a scratch context for their target state
        // (Esoterica swaps the m_pLayerContext pointer), so the reference is settable.
        internal bool IsInLayer { get; set; }
        internal LayerContext LayerContext { get; set; }

        private readonly LayerContext ownLayerContext = new();

        private readonly Transform[] zeroPose;

        /// <summary>
        /// The pose standing in for a source that produced none: the zero pose inside an additive
        /// layer, the reference pose otherwise.
        /// </summary>
        internal Transform[] GetDefaultPose()
        {
            if (IsInLayer && LayerContext.IsAdditive)
            {
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

        internal void ResetReferencedGraphState(GraphContext parent, SyncTrackTime initTime)
        {
            TransferContextDataFromParent(parent);
            ResetGraphState(initTime);
            ReleaseParentContextData();
        }

        internal GraphPoseNodeResult EvaluateReferencedGraph(GraphContext parent, SyncTrackTimeRange? updateRange)
        {
            TransferContextDataFromParent(parent);
            var result = Update(parent.DeltaTime, updateRange);
            ReleaseParentContextData();
            return result;
        }

        /// <summary>Scratch buffers for bone mask task list evaluation.</summary>
        internal BoneMaskPool BoneMaskPool { get; }

        // Cached pose buffers (stand-in for Esoterica's task-system cached pose buffers): forced
        // transitions snapshot their in-flight blend here so the same state is never updated twice
        // in one frame. Buffers are recycled, so steady state allocates nothing.
        private readonly List<Transform[]> cachedPoseBuffers = [];
        private readonly List<bool> cachedPoseBufferInUse = [];

        // Each transition holds at most one cached pose, and only state machines with forceable
        // transitions cache any
        private void AllocateCachedPoses(int boneCount)
        {
            var count = 0;
            foreach (var node in Nodes)
            {
                if (node is StateMachineNode stateMachine
                    && stateMachine.StateDefinitions.Any(static state => state.TransitionDefinitions.Any(static transition => transition.CanBeForced)))
                {
                    count += stateMachine.StateDefinitions.Sum(static state => state.TransitionDefinitions.Length);
                }
            }

            cachedPoseBuffers.Capacity = count;
            cachedPoseBufferInUse.Capacity = count;

            for (var i = 0; i < count; i++)
            {
                cachedPoseBuffers.Add(new Transform[boneCount]);
                cachedPoseBufferInUse.Add(false);
            }
        }

        // Every clip event sampled twice (a looping range) plus state and graph events
        private int CountPossibleEvents()
        {
            var count = Nodes.Length;
            foreach (var slot in Graph.DataSlots)
            {
                count += 2 * (slot?.Animation.Events.Length ?? 0);
            }

            return count;
        }

        internal int CreateCachedPose()
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

        internal bool IsValidCachedPose(int id) => id >= 0 && id < cachedPoseBuffers.Count && cachedPoseBufferInUse[id];

        internal Transform[] GetCachedPoseBuffer(int id)
        {
            Debug.Assert(IsValidCachedPose(id));
            return cachedPoseBuffers[id];
        }

        internal void DestroyCachedPose(int id)
        {
            Debug.Assert(IsValidCachedPose(id));
            cachedPoseBufferInUse[id] = false;
        }

        /// <summary>Resolves a referenced graph slot index to the instantiated child graph, if any.</summary>
        internal AnimationGraph? GetReferencedGraph(short referencedGraphIdx)
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

        /// <summary>Resolves a node index of the definition to the node instance.</summary>
        public void SetNodeFromIndex<T>(short childNodeIdx, ref T childNode) where T : GraphNode
        {
            Debug.Assert(childNodeIdx < Nodes.Length);
            childNode = (T)Nodes[childNodeIdx];
        }

        /// <summary>Resolves a node index that may be unset (-1) to the node instance.</summary>
        public void SetOptionalNodeFromIndex<T>(short childNodeIdx, ref T? childNode) where T : GraphNode
        {
            if (childNodeIdx >= 0)
            {
                SetNodeFromIndex(childNodeIdx, ref childNode!);
            }
        }

        /// <summary>Resolves an array of node indices to the node instances.</summary>
        public void SetNodesFromIndexArray<T>(short[] childNodeIndices, ref T[] childNodes) where T : GraphNode
        {
            childNodes = new T[childNodeIndices.Length];

            for (var i = 0; i < childNodeIndices.Length; i++)
            {
                SetNodeFromIndex(childNodeIndices[i], ref childNodes[i]);
            }
        }

        private readonly HashSet<(short, string)> loggedWarnings = new(16);

        /// <summary>Logs a warning with a detail value, formatting it only the first time it is logged.</summary>
        public void LogWarning<T>(short nodeIdx, string message, T detail)
        {
            if (loggedWarnings.Add((nodeIdx, message)))
            {
                Console.WriteLine($"[AnimGraph][Node {nodeIdx}] Warning: {message} ('{detail}')");
            }
        }

        /// <summary>Logs a warning for a node, once per distinct message.</summary>
        public void LogWarning(short nodeIdx, string message)
        {
            // Lifecycle warnings (invalid selections, missing clips) can repeat on every state
            // entry; log each distinct warning once per node.
            if (loggedWarnings.Add((nodeIdx, message)))
            {
                Console.WriteLine($"[AnimGraph][Node {nodeIdx}] Warning: {message}");
            }
        }

        private readonly HashSet<string> warnedNotImplemented = new(16);

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

        internal GraphPoseNodeResult Update(float timeStep, SyncTrackTimeRange? updateRange = null)
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
