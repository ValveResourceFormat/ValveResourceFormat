using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ValveResourceFormat.Renderer.AnimLib
{
    enum BranchState
    {
        Active,
        Inactive,
    };

    class GraphContext
    {
        public required AnimationGraphController Controller { get; set; }
        public required GraphNode[] Nodes { get; set; }
        public BranchState BranchState { get; set; } = BranchState.Active;
        public float DeltaTime { get; set; }

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
    }
}
