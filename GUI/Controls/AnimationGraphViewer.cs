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
            const string Preffix = "CNm";
            const string Suffix = "Node::CDefinition";
            var @type = className[Preffix.Length..^Suffix.Length];
            return @type;
        }

        void HandleNode(Node node, int nodeIdx)
        {
            var data = nodes[nodeIdx];

            if (node.NodeType == "StateMachine")
            {
                foreach (var stateDefinition in data.GetArray("m_stateDefinitions"))
                {
                    var stateNodeIdx = stateDefinition.GetInt32Property("m_nStateNodeIdx");
                    var entryConditionNodeIdx = stateDefinition.GetInt32Property("m_nEntryConditionNodeIdx");
                    // m_transitionDefinitions

                    var stateName = GetName(stateNodeIdx);
                    var stateNode = nodes[stateNodeIdx];
                    var stateInputIdx = stateNode.GetInt32Property("m_nChildNodeIdx");

                    Console.WriteLine($"State: {GetName(stateNodeIdx)}, Entry Condition: {GetName(entryConditionNodeIdx)}");

                    var inputSocket = new SocketIn(typeof(int), stateName, node, false);
                    node.Sockets.Add(inputSocket);

                    var stateInputNode = AddNode(new Node
                    {
                        Name = GetName(stateInputIdx),
                        Location = new Point(0, 0),
                        NodeType = GetType(stateInputIdx),
                    });


                    var outputSocket = new SocketOut(typeof(int), "Output", stateInputNode);
                    stateInputNode.Sockets.Add(outputSocket);

                    Connect(outputSocket, inputSocket);
                }
            }
        }

        var root = AddNode(new Node
        {
            Name = GetName(rootNodeIdx),
            Location = new Point(0, 0),
            NodeType = GetType(rootNodeIdx),
            Description = "Root node"
        });

        root.StartNode = true;

        HandleNode(root, rootNodeIdx);
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
