using System.Drawing;
using System.Windows.Forms;
using GUI.Utils;
using NodeGraphControl.Elements;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Viewers;

internal class AnimationGraphViewer : NodeGraphControl.NodeGraphControl
{
    private readonly VrfGuiContext vrfGuiContext;
    private readonly KVObject graphDefinition;

    public AnimationGraphViewer(VrfGuiContext guiContext, KVObject data)
    {
        Dock = DockStyle.Fill;
        GridStyle = EGridStyle.Grid;
        // BackColor =

        vrfGuiContext = guiContext;
        graphDefinition = data;

        CreateGraph();
    }

    private bool firstPaint = true;
    protected override void OnPaint(PaintEventArgs e)
    {
        if (firstPaint)
        {
            firstPaint = false;
            FocusView(PointF.Empty);
        }

        base.OnPaint(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);

        var element = FindElementAtMousePoint(e.Location);

        if (element is Node { ExternalResourceName: not null } node)
        {
            var foundFile = vrfGuiContext.FileLoader.FindFileWithContext(node.ExternalResourceName + ValveResourceFormat.IO.GameFileLoader.CompiledFileSuffix);
            if (foundFile.Context != null)
            {
                Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
            }
        }
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

        static void CalculateChildNodeLocation(Node node, int totalChildren, Node childNode, int childNodeHeight, int horizontalOffset = 300)
        {
            var childIndex = node.Sockets.Count;
            childNode.Location = node.Location - new Size(horizontalOffset, totalChildren * childNodeHeight / 2 - childIndex * childNodeHeight);
        }

        Node CreateNode(string[] nodePaths, KVObject[] nodes, int nodeIdx)
        {
            var node = new Node
            {
                Name = GetName(nodeIdx),
                Data = nodes[nodeIdx],
                NodeType = GetType(nodeIdx),
            };

            AddNode(node);
            return node;
        }

        // Used for calculating some node position.
        var depth = 0;
        var previousChildHeight = 0;

        void CreateChild(Node parent, int totalChildren, int nodeIdx, int height, int offset = 300, string? parentInputName = null, string? childOutputName = null)
        {
            var additionalWidthDepth = depth * 70;
            var additionalHeightDepth = depth * 10;

            height = previousChildHeight == 0 ? height : previousChildHeight;

            var childNode = CreateNode(nodePaths, nodes, nodeIdx);
            var outputSocket = new SocketOut(typeof(int), childOutputName ?? "Result", childNode);
            childNode.Sockets.Add(outputSocket);
            CalculateChildNodeLocation(parent, totalChildren, childNode, height + Random.Shared.Next(0, 20) + additionalHeightDepth, offset + Random.Shared.Next(0, 5) + additionalWidthDepth);

            var inputSocket = new SocketIn(typeof(int), parentInputName ?? childNode.Name, parent, false);
            parent.Sockets.Add(inputSocket);

            Connect(outputSocket, inputSocket);

            try
            {
                depth += 1;
                CreateChildren(childNode, nodeIdx);
                depth -= 1;
            }
            catch (IndexOutOfRangeException)
            {
                Console.WriteLine($"Node handling error for {childNode.Name}.");
            }

            childNode.Calculate(); // node and wire position
            previousChildHeight = (int)childNode.BoundsFull.Height;
        }

        void CreateChildren(Node node, int nodeIdx)
        {
            var data = nodes[nodeIdx];
            previousChildHeight = 0;

            if (node.NodeType == "StateMachine")
            {
                var children = data.GetArray("m_stateDefinitions");
                foreach (var stateDefinition in children)
                {
                    var stateNodeIdx = stateDefinition.GetInt32Property("m_nStateNodeIdx");
                    var entryConditionNodeIdx = stateDefinition.GetInt32Property("m_nEntryConditionNodeIdx"); // can be -1
                    // m_transitionDefinitions

                    var stateName = GetName(stateNodeIdx);
                    var stateNode = nodes[stateNodeIdx];
                    var stateInputIdx = stateNode.GetInt32Property("m_nChildNodeIdx");

                    Console.WriteLine($"State: {GetName(stateNodeIdx)}, Entry Condition: {(entryConditionNodeIdx == -1 ? "None" : GetName(entryConditionNodeIdx))}");

                    CreateChild(node, children.Length, stateInputIdx, 150, 450, stateName, "Result");
                }
            }
            else if (node.NodeType == "ParameterizedClipSelector")
            {
                var options = data.GetArray<int>("m_optionNodeIndices");

                var parameterNodeIdx = data.GetInt32Property("m_parameterNodeIdx");
                CreateChild(node, options.Length + 1, parameterNodeIdx, 120, 300, null, "Parameter");

                foreach (var optionNodeIdx in options)
                {
                    CreateChild(node, options.Length + 1, optionNodeIdx, 80, 300);
                }
            }
            else if (node.Data.ContainsKey("m_nChildNodeIdx"))
            {
                var childNodeIdx = data.GetInt32Property("m_nChildNodeIdx");
                CreateChild(node, 1, childNodeIdx, 100, 300, "Input", "Result");
            }
            else if (node.NodeType is "Clip" or "ReferencedGraph")
            {
                var resources = graphDefinition.GetArray<string>("m_resources");
                node.ExternalResourceName = node.NodeType switch
                {
                    "ReferencedGraph" => resources[graphDefinition.GetArray("m_referencedGraphSlots")[data.GetInt32Property("m_nReferencedGraphIdx")].GetInt32Property("m_dataSlotIdx")],
                    "Clip" => resources[data.GetInt32Property("m_nDataSlotIdx")],
                    _ => null
                };
            }
            else
            {
                Console.WriteLine($"Unhandled node type: {node.NodeType} ({node.Name})");
            }

            node.Calculate();
        }

        var root = CreateNode(nodePaths, nodes, rootNodeIdx);
        root.StartNode = true;

        CreateChildren(root, rootNodeIdx);
    }

    #region Nodes

    class Node : AbstractNode
    {
        public KVObject Data { get; set; }

        public string? ExternalResourceName { get; set; }

        public Node()
        {
            BaseColor = Color.FromArgb(255, 31, 36, 42);
        }

        public override bool IsReady() => true;
        public override void Execute() { }
    }

    #endregion Nodes
}
