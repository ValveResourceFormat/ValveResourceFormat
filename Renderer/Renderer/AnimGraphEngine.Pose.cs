using System.Diagnostics;
using System.Linq;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class PoseNode
    {
        public int LoopCount;
        public TimeSpan Duration;
        public float CurrentTime; /* Percent */ 
        public float PreviousTime;  /* Percent */ 
    }


    partial class ParameterizedClipSelectorNode
    {
        public ClipReferenceNode[] OptionNodes;
        public FloatValueNode ParameterNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodesFromIndexArray(OptionNodeIndices, ref OptionNodes);
            ctx.SetNodeFromIndex(ParameterNodeIdx, ref ParameterNode);
        }

        public ClipReferenceNode SelectOption(GraphContext ctx)
        {
            var selectedIndex = (int)ParameterNode.GetValue(ctx);

            if (HasWeightsSet)
            {
                // ?
            }

            return OptionNodes[selectedIndex];
        }
    }
}
