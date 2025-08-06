using System.Drawing;
using System.Linq;
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

        Dictionary<int, Node> createdNodes = new(nodes.Length);

        Node CreateNode(string[] nodePaths, KVObject[] nodes, int nodeIdx)
        {
            if (createdNodes.TryGetValue(nodeIdx, out var existingNode))
            {
                return existingNode;
            }

            var node = new Node
            {
                Name = GetName(nodeIdx),
                Data = nodes[nodeIdx],
                NodeType = GetType(nodeIdx),
            };

            AddNode(node);
            createdNodes[nodeIdx] = node;
            return node;
        }

        // Used for calculating some node position.
        var depth = 0;
        var previousChildHeight = 0;

        // TODO: childOutputName should be determined by the child.
        void CreateChild(Node parent, int totalChildren, int nodeIdx, int height, int offset = 300, string? parentInputName = null, string? childOutputName = null)
        {
            var childNode = CreateNode(nodePaths, nodes, nodeIdx);

            // child node already exists, all we do is connect to its existing output.
            if (childNode.Sockets.Count > 0)
            {
                var output = childNode.Sockets.FirstOrDefault(s => s is SocketOut) as SocketOut;
                var input = new SocketIn(typeof(int), parentInputName ?? childNode.Name, parent, false);
                parent.Sockets.Add(input);

                Connect(output, input);
                return;
            }

            var additionalWidthDepth = depth * 70;
            var additionalHeightDepth = depth * 10;

            height = previousChildHeight == 0 ? height : previousChildHeight;

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
                CreateChild(node, options.Length + 1, parameterNodeIdx, 120, 300);

                foreach (var optionNodeIdx in options)
                {
                    CreateChild(node, options.Length + 1, optionNodeIdx, 80, 300);
                }
            }
            else if (node.NodeType == "Selector")
            {
                // Select the first option for which the condition passes?
                var options = data.GetArray<int>("m_optionNodeIndices");
                var conditions = data.GetArray<int>("m_conditionNodeIndices");

                var i = 0;
                foreach (var (optionNodeIdx, conditionNodeIdx) in options.Zip(conditions))
                {
                    CreateChild(node, options.Length + conditions.Length, optionNodeIdx, 80, 300, $"Option{i}");
                    CreateChild(node, options.Length + conditions.Length, conditionNodeIdx, 80, 300, $"Condition{i}");
                    i++;
                }
            }
            else if (node.NodeType is "LayerBlend")
            {
                var baseNodeIdx = data.GetInt32Property("m_nBaseNodeIdx");
                CreateChild(node, 1, baseNodeIdx, 100, 300, "Base", "Result");

                // m_layerDefinition
                node.Sockets.Add(new SocketIn(typeof(int), "Layer", node, false));
            }
            else if (node.NodeType is "Blend1D")
            {
                foreach (var sourceNodeIdx in data.GetArray<int>("m_sourceNodeIndices"))
                {
                    CreateChild(node, 2, sourceNodeIdx, 100, 300, "Source");
                }
            }
            else if (node.Data.ContainsKey("m_nInputValueNodeIdx")) // ComparisonNode
            {
                var childNodeIdx = data.GetInt32Property("m_nInputValueNodeIdx");
                CreateChild(node, 1, childNodeIdx, 100, 300, "Value", "Result");
            }
            else if (node.Data.ContainsKey("m_conditionNodeIndices")) // Conditional node
            {
                var conditions = data.GetArray<int>("m_conditionNodeIndices");
                foreach (var condition in conditions)
                {
                    CreateChild(node, conditions.Length + 1, condition, 80);
                }
            }
            else if (node.Data.ContainsKey("m_nChildNodeIdx"))
            {
                var childCount = 1;
                if (node.NodeType == "Scale")
                {
                    childCount = 3;
                    CreateChild(node, childCount, data.GetInt32Property("m_nMaskNodeIdx"), 130, 300, "Mask");
                    CreateChild(node, childCount, data.GetInt32Property("m_nEnableNodeIdx"), 130, 300, "Enable");
                }

                var childNodeIdx = data.GetInt32Property("m_nChildNodeIdx");
                CreateChild(node, childCount, childNodeIdx, 100, 300, "Input", "Result");
            }
            else if (node.NodeType is "Clip" or "ReferencedGraph")
            {
                var resources = graphDefinition.GetArray<string>("m_resources");
                node.ExternalResourceName = node.NodeType switch
                {
                    "ReferencedGraph" => resources[graphDefinition.GetArray("m_referencedGraphSlots")[data.GetInt32Property("m_nReferencedGraphIdx")].GetInt32Property("m_dataSlotIdx")],
                    "Clip" => data.GetInt32Property("m_nDataSlotIdx") == -1 ? null : resources[data.GetInt32Property("m_nDataSlotIdx")], // can be -1 on variations, for example bizon ironsight clip
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
