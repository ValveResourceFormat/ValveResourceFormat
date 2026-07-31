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

        public void Reset()
        {
            Weight = 1f;
            RootMotionWeight = 1f;
            MaskTaskList = default;
        }
    }

    abstract partial class GraphNode
    {
        public abstract void Initialize(GraphContext context);

        private uint lastUpdateID = uint.MaxValue;

        /// <summary>True if this node has already been marked active during the current graph update.</summary>
        public bool WasUpdated(GraphContext ctx) => lastUpdateID == ctx.UpdateID;

        /// <summary>Marks this node as active/evaluated for the current graph update.</summary>
        public void MarkNodeActive(GraphContext ctx) => lastUpdateID = ctx.UpdateID;
    }

    abstract partial class ValueNode
    {
        public override void Initialize(GraphContext ctx) { }
    }

    static class KVObjectExtensions2
    {
        public static GlobalSymbol[] GetSymbolArray(this KVObject collection, string name)
        {
            return [.. collection.GetArray<string>(name).Select(s => new GlobalSymbol(s))];
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

        public Transform WorldTransformInverse = Transform.Identity;

        private GraphDefinition graphDefinition;

        public GraphContext(KVObject graph, AnimationGraph owner)
        {
            graphDefinition = new GraphDefinition(graph);
            Graph = owner;
            Pose = new Pose(owner.AnimLibSkeleton);

            // Create nodes
            var nodeArray = graph.GetArray<KVObject>("m_nodes");
            Nodes = new GraphNode[nodeArray.Length];

            // Transfer node data from KVObject
            for (short i = 0; i < nodeArray.Length; i++)
            {
                Nodes[i] = CreateNode(nodeArray[i]);
            }

            // Initialize nodes, populate strong references
            foreach (var node in Nodes)
            {
                node.Initialize(this);
            }

            RootNode = (PoseNode)Nodes[graphDefinition.RootNodeIdx];
        }

        // Maps schema class names (minus the CNm prefix and ::CDefinition suffix) to AnimLib node types.
        // todo: code gen
        private static readonly Dictionary<string, Type> NodeTypes = typeof(GraphNode).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(GraphNode)))
            .ToDictionary(t => t.Name, t => t);

        /// <summary>Creates the AnimLib node instance for one <c>m_nodes</c> entry of a graph definition.</summary>
        public static GraphNode CreateNode(KVObject nodeData)
        {
            var @class = nodeData.GetProperty<string>("_class")
                ?? throw new InvalidOperationException("Graph node has no _class property.");

            var nodeTypeName = @class["CNm".Length..^"::CDefinition".Length];
            var nodeType = NodeTypes[nodeTypeName];

            return (GraphNode?)Activator.CreateInstance(nodeType, [nodeData])
                ?? throw new InvalidOperationException($"Could not create instance of node type {nodeType.Name}.");
        }

        // Layer context
        public bool IsInLayer { get; set; }
        public LayerContext LayerContext { get; } = new();

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

        public void LogWarning(short nodeIdx, string message)
        {
            Console.WriteLine($"[AnimGraph][Node {nodeIdx}] Warning: {message}");
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

        public GraphPoseNodeResult Update(float timeStep)
        {
            UpdateID++;
            DeltaTime = timeStep;
            SampledEvents.Clear();
            var poseResult = RootNode.Update(this);
            Pose.SetParentSpaceTransforms(poseResult.Pose);
            return poseResult;
        }
    }
}
