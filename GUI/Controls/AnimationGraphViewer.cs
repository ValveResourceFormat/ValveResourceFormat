using System.Drawing;
using System.Windows.Forms;
using GUI.Utils;
using NodeGraphControl.Elements;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Viewers;

internal class AnimationGraphViewer : NodeGraphControl.NodeGraphControl
{
    private VrfGuiContext vrfGuiContext;
    private KVObject graphDefinition;

    public AnimationGraphViewer(VrfGuiContext guiContext, KVObject data)
    {
        Dock = DockStyle.Fill;
        GridStyle = EGridStyle.Grid;
        // BackColor =

        vrfGuiContext = guiContext;
        graphDefinition = data;

        CreateGraph();
    }

    private void CreateGraph()
    {
        var rootNodeIdx = graphDefinition.GetInt32Property("m_nRootNodeIdx");
        var nodePaths = graphDefinition.GetArray<string>("m_nodePaths");
        var nodes = graphDefinition.GetArray("m_nodes");

        string GetName(int nodeIdx)
        {
            return nodePaths[nodeIdx].Split('/')[^1];
        }

        string GetType(int nodeIdx)
        {
            var className = nodes[nodeIdx].GetStringProperty("_class");
            var @type = className["CNm".Length..^"Node::CDefinition".Length];
            return @type;
        }

        AddNode(new Node
        {
            Name = GetName(rootNodeIdx),
            Location = new Point(0, 0),
            NodeType = GetType(rootNodeIdx),
            Description = "Root node"
        });
    }

    #region Nodes

    class Node : AbstractNode
    {
        public Node()
        {
            BaseColor = Color.FromArgb(255, 31, 36, 42);
        }

        public override bool IsReady() => true;
        public override void Execute() { }
    }

    #endregion Nodes
}
