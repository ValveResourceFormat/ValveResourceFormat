using System.IO;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Graphs;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

/// <summary>
/// Decompiles a compiled Pulse graph back into an editable <c>.vpulse</c> document that resourcecompiler can build again.
/// </summary>
/// <remarks>
/// The editor nodes are rebuilt from the debug info every instruction keeps.
/// </remarks>
public sealed class PulseGraphExtract
{
    /// <summary>Receives diagnostics about parts of the graph that could not be reconstructed exactly.</summary>
    public IProgress<string>? ProgressReporter { get; init; }

    /// <summary>
    /// Gets the map of exported content paths to the paths they are written at, which references to them follow.
    /// Null writes them as they are.
    /// </summary>
    public ContentPathMap? ReferenceMap { get; init; }

    private readonly KVObject graph;

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseGraphExtract"/> class.
    /// </summary>
    /// <param name="resource">The compiled pulse graph resource.</param>
    public PulseGraphExtract(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        graph = (resource.DataBlock as BinaryKV3)?.Data
            ?? throw new InvalidDataException("Resource DataBlock is not a BinaryKV3.");
    }

    /// <summary>
    /// Converts the graph to a content file holding the <c>.vpulse</c> document.
    /// </summary>
    public ContentFile ToContentFile()
    {
        return new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToDocumentString()),
        };
    }

    /// <summary>
    /// Builds the <c>.vpulse</c> document as KV3 text.
    /// </summary>
    /// <exception cref="InvalidDataException">A chunk has no debug info for some of its instructions.</exception>
    public string ToDocumentString() => ReferenceMap.MapReferences(BuildDocument()).ToKV3String(KV3IDLookup.Get("vpulse17"));

    private KVObject BuildDocument()
    {
        var definition = new PulseGraphDefinition(graph, ProgressReporter);

        for (var i = 0; i < definition.Chunks.Count; i++)
        {
            if (!definition.Chunks[i].HasDebugInfo)
            {
                throw new InvalidDataException($"Chunk {i} has no debug info for some of its instructions, which the editor nodes are rebuilt from.");
            }
        }

        var walker = new PulseGraphWalker(definition);
        walker.Walk();

        LayOut(walker);

        var root = KVObject.Collection();
        root.Add("_class", "Root");
        root.Add("m_NodeID", walker.RootNodeId);

        var children = KVObject.Array();

        for (var i = 0; i < definition.Vars.Count; i++)
        {
            children.Add(BuildVariableDefinition(definition.Vars[i], walker.VariableNodeIds[i]));
        }

        for (var i = 0; i < definition.PublicOutputs.Count; i++)
        {
            children.Add(BuildPublicOutputDefinition(definition.PublicOutputs[i], walker.PublicOutputNodeIds[i]));
        }

        foreach (var node in walker.Nodes)
        {
            children.Add(BuildNode(node, walker));
        }

        root.Add("m_Children", children);
        root.Add("m_DomainIdentifier", graph.GetStringProperty("m_DomainIdentifier", string.Empty));
        root.Add("m_DomainSubType", graph.GetStringProperty("m_DomainSubType", "PVAL_VOID"));
        root.Add("m_ParentMapName", graph.GetStringProperty("m_ParentMapName", string.Empty));
        root.Add("m_ParentXmlName", graph.GetStringProperty("m_ParentXmlName", string.Empty));

        return root;
    }

    private static void LayOut(PulseGraphWalker walker)
    {
        var nodes = walker.Nodes;
        var index = new Dictionary<int, int>(nodes.Count);

        for (var i = 0; i < nodes.Count; i++)
        {
            index[nodes[i].Id] = i;
        }

        var edges = new List<GraphLayoutEdge>();

        for (var i = 0; i < nodes.Count; i++)
        {
            foreach (var outflow in nodes[i].Outflows)
            {
                if (index.TryGetValue(outflow.Target, out var target) && target != i)
                {
                    edges.Add(GraphPlacement.MakeEdge(i, target));
                }
            }

            foreach (var inparam in nodes[i].Inparams)
            {
                if (index.TryGetValue(inparam.SourceNode, out var source) && source != i)
                {
                    edges.Add(GraphPlacement.MakeEdge(source, i));
                }
            }
        }

        var positions = GraphPlacement.Layout(nodes.Count, [.. edges]);

        for (var i = 0; i < nodes.Count; i++)
        {
            nodes[i].Position = new Vector2(MathF.Round(positions[i].X), MathF.Round(positions[i].Y));
        }
    }

    private static KVObject BuildNode(PulseEditorNode editorNode, PulseGraphWalker walker)
    {
        var node = KVObject.Collection();
        node.Add("_class", editorNode.ClassName);
        node.Add("m_NodeID", editorNode.Id);

        if (editorNode.Name != null)
        {
            node.Add("m_Name", editorNode.Name);
        }

        node.Add("m_vPosition", KVObject.Array([editorNode.Position.X, editorNode.Position.Y]));

        foreach (var (key, value) in editorNode.Fields)
        {
            node.Add(key, value);
        }

        bool IsWritten(int nodeId) => walker.TryGetNode(nodeId, out _);

        var children = KVObject.Array();

        foreach (var outflow in editorNode.Outflows)
        {
            if (!IsWritten(outflow.Target))
            {
                continue;
            }

            var connection = KVObject.Collection();
            connection.Add("_class", "Connection_Outflow");
            connection.Add("m_NodeID", outflow.Id);
            connection.Add("m_SourceOutflowName", outflow.Outflow);
            connection.Add("m_DestInflowName", outflow.Inflow);
            connection.Add("m_DestFlowNodeID", outflow.Target);
            children.Add(connection);
        }

        foreach (var inparam in editorNode.Inparams)
        {
            if (!IsWritten(inparam.SourceNode))
            {
                continue;
            }

            var connection = KVObject.Collection();
            connection.Add("_class", "Connection_Inparam");
            connection.Add("m_NodeID", inparam.Id);
            connection.Add("m_SourceNode", inparam.SourceNode);
            connection.Add("m_SourceOutparamName", inparam.SourcePort);
            connection.Add("m_ThisInparamName", inparam.ThisPort);
            children.Add(connection);
        }

        if (children.Count > 0)
        {
            node.Add("m_Children", children);
        }

        return node;
    }

    private static KVObject BuildVariableDefinition(KVObject variable, int nodeId)
    {
        var node = KVObject.Collection();
        node.Add("_class", "VariableDefinition");
        node.Add("m_NodeID", nodeId);
        node.Add("m_Name", variable.GetStringProperty("m_Name", string.Empty));

        var value = KVObject.Collection();
        value.Add("m_Type", variable.GetStringProperty("m_Type", "PVAL_INVALID"));
        value.Add("m_Value", variable.TryGetValue("m_DefaultValue", out var defaultValue) ? defaultValue : KVObject.Null());
        node.Add("m_Value", value);

        node.Add("m_Description", variable.GetStringProperty("m_Description", string.Empty));
        node.Add("m_nKeysSource", variable.GetStringProperty("m_nKeysSource", "PRIVATE"));
        node.Add("m_bIsPublicBlackboardVariable", variable.GetBooleanProperty("m_bIsPublicBlackboardVariable"));
        return node;
    }

    private static KVObject BuildPublicOutputDefinition(KVObject output, int nodeId)
    {
        var node = KVObject.Collection();
        node.Add("_class", "PublicOutputDefinition");
        node.Add("m_NodeID", nodeId);
        node.Add("m_Name", output.GetStringProperty("m_Name", string.Empty));
        node.Add("m_Description", output.GetStringProperty("m_Description", string.Empty));
        node.Add("m_Args", PulseGraphWalker.BuildMethodArgs(output.GetArray("m_Args")));
        return node;
    }
}
