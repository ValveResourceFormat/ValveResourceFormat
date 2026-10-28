using System.IO;
using System.Linq;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

/// <summary>
/// Decompiles VMix mix graphs, and sounds compiled from them, to the vmixtool source format.
/// </summary>
/// <remarks>
/// The editor nodes are rebuilt from the compiled command list of each graph: every node compiles to a
/// recognisable run of commands, and the debug names and meters the compiler keeps name most of them.
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/sounddoc_lib/CVMixToolDoc">CVMixToolDoc</seealso>
public sealed partial class VMixGraphExtract
{
    private const float ColumnWidth = 320f;
    private const float RowSpacing = 60f;

    private const string ManifestName = "mixgraphs.vrman";

    private readonly Resource resource;
    private readonly IFileLoader? fileLoader;
    private readonly KVObject root;
    private readonly IReadOnlyList<KVObject> compiledGraphs;
    private readonly Dictionary<string, string> siblingFiles = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the warnings about compiled data that has no exact editor equivalent, from the last decompile.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="VMixGraphExtract"/> class.
    /// </summary>
    /// <param name="resource">A VMix resource, or a sound whose container is a mix graph.</param>
    /// <param name="fileLoader">
    /// Loads the other graphs of the folder, which graphs in their own files inline as subgraphs. Without it,
    /// subgraphs are only found among the graphs of the same file.
    /// </param>
    public VMixGraphExtract(Resource resource, IFileLoader? fileLoader = null)
    {
        this.resource = resource;
        this.fileLoader = fileLoader;

        if (resource.ResourceType == ResourceType.Sound)
        {
            var container = GetGraphSound(resource)
                ?? throw new InvalidDataException("The sound does not contain a mix graph.");
            root = container;
            compiledGraphs = [container.GetSubCollection("m_graphDesc")];
            return;
        }

        root = (resource.DataBlock as BinaryKV3)?.Data.Root
            ?? throw new InvalidDataException("Resource DataBlock is not a BinaryKV3 or is null.");
        compiledGraphs = GetList(root, "Graphs");
    }

    /// <summary>
    /// Gets the sound container of a sound that is compiled from a mix graph, or null when it is not one.
    /// </summary>
    public static KVObject? GetGraphSound(Resource resource)
    {
        if (resource.GetBlockByType(BlockType.CTRL) is not BinaryKV3 ctrl)
        {
            return null;
        }

        var container = ctrl.Data.Root;
        return container.GetStringProperty("_class") == "CVoiceContainerVMixSnd" && container.ContainsKey("m_graphDesc") ? container : null;
    }

    /// <summary>
    /// Converts the mix graphs to a vmix content file.
    /// </summary>
    public ContentFile ToContentFile() => new()
    {
        Data = Encoding.UTF8.GetBytes(ToEditorDocument()),
        FileName = Path.ChangeExtension(resource.FileName, "vmix"),
    };

    /// <summary>
    /// Builds the vmix source document.
    /// </summary>
    public string ToEditorDocument()
    {
        var format = resource.ResourceType == ResourceType.Sound ? MixGraphFormat.Heap : DetectFormat(root);
        var warnings = new List<string>();
        var graphs = KVObject.Array();
        var mixGraphs = compiledGraphs.Select(compiled => ReadGraph(compiled, format)).ToList();
        var ownNames = mixGraphs.Select(static graph => graph.Name).ToHashSet(StringComparer.Ordinal);
        var siblings = format == MixGraphFormat.Heap ? LoadSiblingGraphs(ownNames) : [];

        // Subgraph switches name their plugs after nodes of their subgraphs, so those are decompiled first
        var subgraphNodes = new Dictionary<(string Graph, string Type), string>();
        var graphNodeNames = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var graph in mixGraphs.Concat(siblings))
        {
            var names = graphNodeNames[graph.Name] = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in new MixGraphDecompiler(graph).Run().Nodes)
            {
                subgraphNodes.TryAdd((graph.Name, node.Type), node.Name);
                names.Add(node.Name);

                if (node.FriendlyName != null && node.FriendlyName != node.Name)
                {
                    names.Add(string.Concat("=", node.FriendlyName));
                }
            }
        }

        // The graph that has most of the inlined nodes, the smallest of those on a tie; never the graph inlining it
        Func<IReadOnlyCollection<string>, string?> SubgraphFinder(string inliningGraph) => innerNames =>
        {
            static double Score(HashSet<string> names, IReadOnlyCollection<string> inner)
            {
                var shared = inner.Count(names.Contains);
                return shared == 0 ? 0 : shared - names.Count / 10000.0;
            }

            var best = graphNodeNames.Where(graph => graph.Key != inliningGraph).MaxBy(graph => Score(graph.Value, innerNames));
            return best.Value != null && Score(best.Value, innerNames) > 0 ? best.Key : null;
        };

        // Graphs that inline a subgraph name its outputs, which the subgraph itself may not
        var outputNames = new Dictionary<(string Graph, PlugSource Source), string>();
        var allGraphs = mixGraphs.Concat(siblings).ToList();

        if (allGraphs.Any(static graph => graph.AudioMeters.Any(static meter => meter.Count(static c => c == '/') >= 1 && meter.Contains("audio.Output", StringComparison.Ordinal))))
        {
            foreach (var graph in allGraphs)
            {
                var decompiler = new MixGraphDecompiler(graph) { FindSubgraph = SubgraphFinder(graph.Name) }.Run();

                foreach (var (subgraph, source, output) in decompiler.SubgraphOutputs)
                {
                    outputNames.TryAdd((subgraph, source), output);
                }
            }
        }

        foreach (var graph in mixGraphs)
        {
            var decompiler = new MixGraphDecompiler(graph)
            {
                SubgraphFile = SubgraphFile,
                SubgraphNodes = (subgraph, type) => subgraphNodes.GetValueOrDefault((subgraph, type)),
                FindSubgraph = SubgraphFinder(graph.Name),
                OutputName = source => outputNames.GetValueOrDefault((graph.Name, source)),
            }.Run();

            if (format != MixGraphFormat.EditorCompiled)
            {
                decompiler.AddControlStoreFallbacks();
            }

            warnings.AddRange(decompiler.Warnings.Select(warning => $"{graph.Name}: {warning}"));
            graphs.Add(ToEditorGraph(graph, decompiler));
        }

        Warnings = warnings;

        var document = KVObject.Collection();

        if (format != MixGraphFormat.Heap)
        {
            document.Add("dsp_presets", root.TryGetValue("dsp_presets", out var presets) ? presets : KVObject.Array());

            if (root.TryGetValue("dsp_preview", out var preview))
            {
                document.Add("dsp_preview", preview);
            }
        }

        document.Add("Graphs", graphs);

        var editor = KVObject.Collection();
        editor.Add("SelectedGraph", 0);
        document.Add("Editor", editor);

        return document.ToKV3String();
    }

    /// <summary>
    /// Subgraphs of graphs in separate files are named after the file, which sits beside the graph that uses it.
    /// </summary>
    /// <summary>
    /// Graphs in their own files are listed by the resource manifest of their folder.
    /// </summary>
    private List<MixGraph> LoadSiblingGraphs(HashSet<string> ownNames)
    {
        var graphs = new List<MixGraph>();
        var file = SourceFileName();
        var folder = file.LastIndexOf('/') is var slash and >= 0 ? file[..(slash + 1)] : string.Empty;

        using var manifest = fileLoader?.LoadFileCompiled(string.Concat(folder, ManifestName));

        if (manifest?.ExternalReferences == null)
        {
            return graphs;
        }

        foreach (var reference in manifest.ExternalReferences.ResourceRefInfoList)
        {
            var source = Path.ChangeExtension(reference.Name, "vmix").Replace('\\', '/');

            if (string.Equals(source, file, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var sibling = fileLoader!.LoadFileCompiled(reference.Name);

            if (sibling == null)
            {
                continue;
            }

            var compiled = sibling.ResourceType == ResourceType.Sound
                ? GetGraphSound(sibling)?.GetSubCollection("m_graphDesc")
                : (sibling.DataBlock as BinaryKV3)?.Data.Root.GetArray("Graphs")?.FirstOrDefault();

            if (compiled == null)
            {
                continue;
            }

            var graph = ReadGraph(compiled, MixGraphFormat.Heap);

            if (ownNames.Contains(graph.Name))
            {
                continue;
            }

            siblingFiles.TryAdd(graph.Name, source);
            graphs.Add(graph);
        }

        return graphs;
    }

    private string SubgraphFile(string name)
    {
        if (siblingFiles.TryGetValue(name, out var siblingFile))
        {
            return siblingFile;
        }

        var file = SourceFileName();
        var folder = file.LastIndexOf('/') is var slash and >= 0 ? file[..(slash + 1)] : string.Empty;

        if (compiledGraphs.Any(graph => GetString(graph, "Name") == name))
        {
            return file;
        }

        return string.Concat(folder, name.ToLowerInvariant(), ".vmix");
    }

    /// <summary>
    /// The content path of the vmix this resource was compiled from, as the editor references it.
    /// </summary>
    private string SourceFileName()
    {
        foreach (var dependency in resource.EditInfo?.InputDependencies ?? [])
        {
            var file = dependency.ContentRelativeFilename.Replace('\\', '/');

            if (file.EndsWith(".vmix", StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        var name = Path.GetFileName(resource.FileName ?? "graph.vmix_c");
        return Path.ChangeExtension(name, "vmix");
    }

    private static KVObject ToEditorGraph(MixGraph graph, MixGraphDecompiler decompiler)
    {
        var nodes = decompiler.Nodes.ToList();

        // The compiler walks back from the outputs starting at the last one, so they are listed in reverse of the order they compiled in
        static bool IsRoot(MixNode node) => node.Type is "audio.Output" or "control.Output";
        var outputs = nodes.Where(IsRoot).Reverse().ToList();
        var next = 0;

        for (var k = 0; k < nodes.Count; k++)
        {
            if (IsRoot(nodes[k]))
            {
                nodes[k] = outputs[next++];
            }
        }

        var positions = Layout(nodes);
        var result = KVObject.Collection();

        result.Add("m_nGraphOutputChannels", graph.OutputChannels);
        result.Add("Name", graph.Name);

        if (graph.IsMainGraph || graph.Format == MixGraphFormat.Heap)
        {
            result.Add("m_bIsMainGraph", graph.IsMainGraph);
        }

        if (graph.Format is MixGraphFormat.ProcessorTypes or MixGraphFormat.ProcessorClasses)
        {
            result.Add("m_nNodesCreated", NodeCounters(nodes, graph.Format == MixGraphFormat.ProcessorClasses
                ? static type => type.CounterIndex2025
                : static type => type.CounterIndex2024));
        }

        var editorNodes = KVObject.Array();

        foreach (var node in nodes)
        {
            editorNodes.Add(ToEditorNode(node, positions[node.Name], graph.Format));
        }

        var edges = KVObject.Array();

        foreach (var node in nodes)
        {
            foreach (var (plug, source) in node.Inputs)
            {
                var edge = KVObject.Collection();
                edge.Add("plug0", $"{source.Node}/{source.Plug}");
                edge.Add("plug1", $"{node.Name}/{plug}");
                edges.Add(edge);
            }
        }

        result.Add("m_EditorNodes", editorNodes);
        result.Add("m_EditorEdges", edges);
        // Graphs compiled by vmixtool kept its view of them
        result.Add("m_EditorState", graph.Source.TryGetValue("m_EditorState", out var editorState) ? editorState : EditorState(graph.Format));

        if (graph.Format == MixGraphFormat.EditorCompiled)
        {
            // vmixtool compiled these graphs itself and saved the result beside the nodes, which is what the resource compiler reads
            foreach (var (key, value) in graph.Source)
            {
                if (!result.ContainsKey(key))
                {
                    result.Add(key, value);
                }
            }
        }

        return result;
    }

    private static KVObject EditorState(MixGraphFormat format)
    {
        static KVObject Axis()
        {
            var axis = KVObject.Collection();
            axis.Add("pos", 0.0f);
            axis.Add("min", -50000.0f);
            axis.Add("max", 50000.0f);
            axis.Add("scale", 0.5f);
            return axis;
        }

        var view = KVObject.Collection();
        view.Add("XAxis", Axis());
        view.Add("YAxis", Axis());

        if (format != MixGraphFormat.Heap)
        {
            return view;
        }

        var state = KVObject.Collection();
        state.Add("m_viewConfig", view);
        return state;
    }

    private static KVObject NodeCounters(List<MixNode> nodes, Func<NodeType, int> counterIndex)
    {
        var count = NodeTypes.Max(counterIndex) + 1;
        var counters = new int[count];

        foreach (var node in nodes)
        {
            if (!NodeTypesByName.TryGetValue(node.Type, out var type) || counterIndex(type) is not (var index and >= 0))
            {
                continue;
            }

            var created = node.Name.StartsWith(node.Type, StringComparison.Ordinal)
                && int.TryParse(node.Name.AsSpan(node.Type.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
                ? number
                : 1;
            counters[index] = Math.Max(counters[index], created);
        }

        return KVObject.Array(counters.Select(static counter => (KVObject)counter));
    }

    private static KVObject ToEditorNode(MixNode node, (float X, float Y) position, MixGraphFormat format)
    {
        var (width, height) = NodeSize(node);
        var properties = KVObject.Collection();
        properties.Add("_class", NodeTypesByName.TryGetValue(node.Type, out var type) && type.ClassName.Length > 0 ? type.ClassName : "CMixPropertyBase");
        properties.Add("m_Comment", string.Empty);
        properties.Add("m_name", node.FriendlyName ?? node.Name);

        if (format == MixGraphFormat.Heap)
        {
            properties.Add("m_bActive", true);
            properties.Add("m_bSolo", false);
            properties.Add("m_bEditProperties", false);
        }

        foreach (var (key, value) in node.Properties)
        {
            properties[key] = value;
        }

        var result = KVObject.Collection();
        result.Add("name", node.Name);
        result.Add("friendlyname", node.FriendlyName ?? node.Name);
        result.Add("type", node.Type);
        result.Add("editor_pos", KVObject.Array([position.X, position.Y]));
        result.Add("editor_size", KVObject.Array([width, height]));
        result.Add("properties", properties);
        return result;
    }

    private static (float Width, float Height) NodeSize(MixNode node)
    {
        if (node.Type.StartsWith("control.", StringComparison.Ordinal))
        {
            return (160f, 70f + 20f * Math.Max(0, node.Inputs.Count - 1));
        }

        return node.Type switch
        {
            "audio.Mix" or "audio.Splitter" or "audio.Blend" => (140f, 300f),
            "audio.Track" or "effect.Input" or "vsnd.Input" => (140f, 75f),
            _ => (184f, 135f),
        };
    }

    /// <summary>
    /// Places nodes in columns by their longest path from a source, and in rows by the order they compiled in.
    /// </summary>
    private static Dictionary<string, (float X, float Y)> Layout(List<MixNode> nodes)
    {
        var byName = nodes.ToDictionary(static node => node.Name, StringComparer.Ordinal);
        var depth = new Dictionary<string, int>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        int Depth(MixNode node)
        {
            if (depth.TryGetValue(node.Name, out var known))
            {
                return known;
            }

            if (!visiting.Add(node.Name))
            {
                return 0;
            }

            var result = 0;

            foreach (var (_, source) in node.Inputs)
            {
                if (byName.TryGetValue(source.Node, out var sourceNode))
                {
                    result = Math.Max(result, Depth(sourceNode) + 1);
                }
            }

            visiting.Remove(node.Name);
            depth[node.Name] = result;
            return result;
        }

        var columns = new Dictionary<int, float>();
        var positions = new Dictionary<string, (float X, float Y)>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            var column = Depth(node);
            var y = columns.GetValueOrDefault(column);
            positions[node.Name] = (column * ColumnWidth, y);
            columns[column] = y + NodeSize(node).Height + RowSpacing;
        }

        return positions;
    }
}
