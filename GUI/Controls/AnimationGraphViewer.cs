using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using NodeGraphControl.Elements;
using ValveResourceFormat.Serialization.KeyValues;
using ZstdSharp.Unsafe;

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
            horizontalOffset += totalChildren * 15; // offset for each child node
            childNode.Location = node.Location - new Size(horizontalOffset, totalChildren * childNodeHeight / 2 - childIndex * childNodeHeight);
        }

        Dictionary<int, Node> createdNodes = new(nodes.Length);

        Node CreateNode(string[] nodePaths, KVObject[] nodes, int nodeIdx)
        {
            if (createdNodes.TryGetValue(nodeIdx, out var existingNode))
            {
                return existingNode;
            }

            var node = new Node(nodes[nodeIdx])
            {
                Name = $"({nodeIdx}) {GetName(nodeIdx)}",
                NodeType = GetType(nodeIdx),
            };

            AddNode(node);
            createdNodes[nodeIdx] = node;
            return node;
        }

        // Used for calculating some node position.
        var depth = 0;
        var previousChildLocation = new Point(0, 0);
        var previousChildHeight = 0;

        (Node, SocketIn) CreateInputAndChild(Node parent, int totalChildren, int nodeIdx, int height, int offset = 300, string? parentInputName = null, string? childOutputName = null, bool hub = false)
        {
            var (childNode, childNodeOutput) = CreateChild(parent, totalChildren, nodeIdx, height, offset, childOutputName);

            var input = new SocketIn(typeof(int), parentInputName ?? childNode.Name, parent, hub: hub);
            parent.Sockets.Add(input);
            Connect(childNodeOutput, input);

            return (childNode, input);
        }

        (Node, SocketOut) CreateChild(Node parent, int totalChildren, int nodeIdx, int height, int offset = 300, string? childOutputName = null)
        {
            var childNode = CreateNode(nodePaths, nodes, nodeIdx);

            // child node already exists, all we do is connect to its existing output.
            if (childNode.Sockets.Count > 0)
            {
                var output = childNode.Sockets.FirstOrDefault(s => s is SocketOut) as SocketOut;
                return (childNode, output);
            }

            var moreWidthFurtherDeep = depth * 70;
            var extraHeightCloseDepth = 200 / (int)Math.Pow(depth + 1, 1.5f);

            height = Math.Max(height, previousChildHeight + 20);

            if (childNode.NodeType is "Clip" or "ReferencedGraph")
            {
                childOutputName = string.Empty;
            }

            var childNodeOutput = new SocketOut(typeof(int), childOutputName ?? string.Empty, childNode);
            childNode.Sockets.Add(childNodeOutput);
            CalculateChildNodeLocation(parent, totalChildren, childNode, height + Random.Shared.Next(0, 20) + extraHeightCloseDepth, offset + Random.Shared.Next(0, 20) + moreWidthFurtherDeep);

            if (previousChildHeight > 0)
            {
                // No vertical overlap
                childNode.Location = new Point(childNode.Location.X, Math.Max(childNode.Location.Y, previousChildLocation.Y + height));
            }

            try
            {
                depth += 1;
                CreateChildren(childNode, nodeIdx);
                depth -= 1;
            }
            catch (IndexOutOfRangeException)
            {
                Console.WriteLine($"Error creating children for {childNode.Name} (idx = {nodeIdx}).");
            }

            childNode.Calculate(); // node and wire position
            previousChildLocation = childNode.Location;
            previousChildHeight = (int)childNode.BoundsFull.Height;
            return (childNode, childNodeOutput);
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

                    var input = new SocketIn(typeof(int), stateName, node, hub: true);
                    node.Sockets.Add(input);

                    if (stateInputIdx != -1)
                    {
                        var (_, stateNodeOut) = CreateChild(node, children.Length, stateInputIdx, 150, 500);
                        Connect(stateNodeOut, input);
                    }

                    if (entryConditionNodeIdx != -1)
                    {
                        var (_, childOutput) = CreateChild(node, children.Length, entryConditionNodeIdx, 80, 300, stateName);
                        Connect(childOutput, input);
                    }
                }
            }
            else if (node.NodeType is "ParameterizedSelector" or "ParameterizedClipSelector")
            {
                var options = data.GetArray<int>("m_optionNodeIndices");

                var parameterNodeIdx = data.GetInt32Property("m_parameterNodeIdx");
                CreateInputAndChild(node, options.Length + 1, parameterNodeIdx, 120, 300);

                foreach (var optionNodeIdx in options)
                {
                    CreateInputAndChild(node, options.Length + 1, optionNodeIdx, 80, 300);
                }
            }
            else if (node.NodeType is "Selector" or "ClipSelector")
            {
                // Select the first option for which the condition passes?
                var options = data.GetArray<int>("m_optionNodeIndices");
                var conditions = data.GetArray<int>("m_conditionNodeIndices");

                var i = 0;
                foreach (var (optionNodeIdx, conditionNodeIdx) in options.Zip(conditions))
                {
                    var (_, optionInput) = CreateInputAndChild(node, options.Length, optionNodeIdx, 80, 300, hub: true);
                    var (_, conditionOutput) = CreateChild(node, options.Length, conditionNodeIdx, 80, 300);
                    Connect(conditionOutput, optionInput);
                    i++;
                }
            }
            else if (node.NodeType is "LayerBlend")
            {
                var baseNodeIdx = data.GetInt32Property("m_nBaseNodeIdx");
                CreateInputAndChild(node, 1, baseNodeIdx, 100, 300, "Base", "Result");

                var layerInput = new SocketIn(typeof(int), "Layer", node, false);
                node.Sockets.Add(layerInput);

                var layerDefinition = data.GetArray("m_layerDefinition");
                foreach (var layer in layerDefinition)
                {
                    // might be better to create a layer node
                    var inputNodeIdx = layer.GetInt32Property("m_nInputNodeIdx");
                    var blendMode = layer.GetStringProperty("m_blendMode");

                    // m_nWeightValueNodeIdx m_nBoneMaskValueNodeIdx m_nRootMotionWeightValueNodeIdx optional (-1)
                    var (_, childOutput) = CreateChild(node, layerDefinition.Length, inputNodeIdx, 100, 300, blendMode);
                    Connect(childOutput, layerInput);
                }
            }
            else if (node.NodeType is "Blend1D" or "Blend2D")
            {
                foreach (var sourceNodeIdx in data.GetArray<int>("m_sourceNodeIndices"))
                {
                    CreateInputAndChild(node, 2, sourceNodeIdx, 100, 300, "Source");
                }
            }
            else if (node.NodeType is "BoneMask")
            {
                node.Sockets.Add(new SocketIn(typeof(string), data.GetProperty<string>("m_boneMaskID"), node, false));
            }
            else if (node.NodeType is "IDEventCondition")
            {
                var eventIds = data.GetArray<string>("m_eventIDs");
                var sourceStateNodeIdx = data.GetInt32Property("m_nSourceStateNodeIdx");
                if (sourceStateNodeIdx != -1)
                {
                    node.Sockets.Add(new SocketIn(typeof(string), $"State: {GetName(sourceStateNodeIdx)}", node, false));
                }

                foreach (var eventId in eventIds)
                {
                    node.Sockets.Add(new SocketIn(typeof(string), $"Event: '{eventId}'", node, false));
                }
            }
            else if (node.NodeType.EndsWith("Math"))
            {
                var inputNodeIdxA = data.GetProperty("m_nInputValueNodeIdxA", -1);
                var inputNodeIdxB = data.GetProperty("m_nInputValueNodeIdxB", -1);

                CreateInputAndChild(node, 2, inputNodeIdxA, 100, 300, "A");

                var @operator = data.GetProperty<string>("m_operator");
                node.Sockets.Add(new SocketIn(typeof(string), @operator, node, false));

                if (inputNodeIdxB != -1)
                {
                    CreateInputAndChild(node, 2, inputNodeIdxB, 100, 300, "B");
                }
                else
                {
                    if (node.NodeType == "FloatMath")
                    {
                        var constantValue = data.GetFloatProperty("m_flValueB");
                        var constantValueSocket = new SocketIn(typeof(float), $"{constantValue:f}", node, false);
                        node.Sockets.Add(constantValueSocket);
                    }
                }
            }
            else if (node.Data.ContainsKey("m_nInputValueNodeIdx")) // ComparisonNode
            {
                var childNodeIdx = data.GetInt32Property("m_nInputValueNodeIdx");
                CreateInputAndChild(node, 1, childNodeIdx, 100, 300, GetName(childNodeIdx));

                if (data.ContainsKey("m_comparison"))
                {
                    var comparison = data.GetProperty<string>("m_comparison");
                    node.Sockets.Add(new SocketIn(typeof(string), comparison, node, false));
                }

                if (node.NodeType is "IDComparison")
                {
                    var comparisonIds = data.GetArray<string>("m_comparisionIDs");
                    foreach (var comparisonId in comparisonIds)
                    {
                        node.Sockets.Add(new SocketIn(typeof(string), $"'{comparisonId}'", node, false));
                    }
                }
                else if (node.NodeType is "FloatComparison" or "IntComparison")
                {
                    var comparandNodeIdx = data.GetInt32Property("m_nComparandValueNodeIdx");
                    if (comparandNodeIdx != -1)
                    {
                        CreateInputAndChild(node, 1, comparandNodeIdx, 100, 300, "Comparand");
                    }
                    else
                    {
                        var constantValue = data.GetFloatProperty("m_flComparisonValue");
                        var constantValueSocket = new SocketIn(typeof(float), $"{constantValue:f}", node, false);
                        node.Sockets.Add(constantValueSocket);
                    }
                }
            }
            else if (node.Data.ContainsKey("m_conditionNodeIndices")) // Conditional node
            {
                var conditions = data.GetArray<int>("m_conditionNodeIndices");
                foreach (var condition in conditions)
                {
                    CreateInputAndChild(node, conditions.Length + 1, condition, 80);
                }
            }
            else if (node.Data.ContainsKey("m_nChildNodeIdx"))
            {
                var childCount = 1;
                if (node.NodeType == "Scale")
                {
                    childCount = 3;
                    CreateInputAndChild(node, childCount, data.GetInt32Property("m_nMaskNodeIdx"), 130, 300, "Mask");
                    CreateInputAndChild(node, childCount, data.GetInt32Property("m_nEnableNodeIdx"), 130, 300, "Enable");
                }

                var childNodeIdx = data.GetInt32Property("m_nChildNodeIdx");
                CreateInputAndChild(node, childCount, childNodeIdx, 100, 300, "Input", "Result");
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
        Log.Debug(nameof(AnimationGraphViewer), $"Created {createdNodes.Count} nodes (out of {nodes.Length}) or {createdNodes.Count / (float)nodes.Length:P}.");
    }

    #region Nodes

    class Node : AbstractNode
    {
        public KVObject Data { get; set; }

        // todo: polymorphism
        public string? ExternalResourceName { get; set; }

        public Node(KVObject data)
        {
            Data = data;
            BaseColor = Color.FromArgb(255, 31, 36, 42);
        }

        public override bool IsReady() => true;
        public override void Execute() { }

        public override void Draw(Graphics g)
        {
            base.Draw(g);

            if (string.IsNullOrEmpty(ExternalResourceName))
            {
                return;
            }

            using var font = new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Regular);

            var position = new PointF
            {
                X = Location.X + 3,
                Y = Location.Y + 45
            };

            var fileExtensionStart = ExternalResourceName.LastIndexOf('.');
            var trimStr = ExternalResourceName[..fileExtensionStart];
            trimStr = trimStr.Replace(".vnmgraph", string.Empty);
            trimStr = trimStr.Split('/').LastOrDefault() ?? trimStr;
            if (trimStr.Length > 23)
            {
                trimStr = '…' + trimStr[^22..];
            }

            using var brush = new SolidBrush(Color.MediumOrchid);
            g.DrawString(trimStr, font, brush, position);
        }
    }

    #endregion Nodes
}
