global using Pose = System.Numerics.Matrix4x4[];


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
        public float Weight;
        public float RootMotionWeight;
    }

    abstract partial class GraphNode
    {
        public abstract void Initialize(GraphContext context);
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
    }

    class GraphContext
    {
        public AnimationGraphController Controller { get; set; }
        public GraphNode[] Nodes { get; set; }
        public PoseNode RootNode { get; set; }

        public BranchState BranchState { get; set; } = BranchState.Active;
        public float DeltaTime { get; set; }

        public Skeleton Skeleton { get; } // => Controller.Skeleton;
        public Matrix4x4 WorldTransformInverse;

        private GraphDefinition graphDefinition;
        private ResourceTypes.ModelAnimation.Skeleton nmSkel;

        public GraphContext(KVObject graph, ResourceTypes.ModelAnimation.Skeleton skeleton, AnimationGraphController controller)
        {
            graphDefinition = new GraphDefinition(graph);
            nmSkel = skeleton;
            Controller = controller;

            // Create nodes
            var nodeArray = graph.GetArray<KVObject>("m_nodes");
            Nodes = new GraphNode[nodeArray.Length];

            // todo: code gen
            var allNodeTypes = typeof(GraphNode).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(GraphNode)))
                .ToDictionary(t => t.Name, t => t);

            // Transfer node data from KVObject
            for (short i = 0; i < nodeArray.Length; i++)
            {
                var nodeData = nodeArray[i];
                var @class = nodeData.GetProperty<string>("_class");

                // find the correct node type on the AnimLib namespace
                var nodeTypeName = @class["CNm".Length..^"::CDefinition".Length];
                var nodeType = allNodeTypes[nodeTypeName];

                var node = (GraphNode?)Activator.CreateInstance(nodeType, [nodeData])
                    ?? throw new InvalidOperationException($"Could not create instance of node type {nodeType.Name}.");

                Nodes[i] = node;
            }

            // Initialize nodes, populate strong references
            foreach (var node in Nodes)
            {
                node.Initialize(this);
            }

            RootNode = (PoseNode)Nodes[graphDefinition.RootNodeIdx];
        }

        public Pose Pose { get; }// => Controller.Pose;

        // Layer context
        public bool IsInLayer { get; set; }
        public LayerContext LayerContext { get; }

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

        public GraphPoseNodeResult Update(float timeStep)
        {
            DeltaTime = timeStep;
            var poseResult = RootNode.Update(this);
            return poseResult;
        }
    }
}
