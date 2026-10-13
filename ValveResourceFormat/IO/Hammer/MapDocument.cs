using System.IO;
using System.Linq;
using Datamodel.Codecs;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.ResourceTypes;
using DMElement = Datamodel.Element;

namespace ValveResourceFormat.IO;

/// <summary>
/// A source map file (<c>.vmap</c>) as Hammer edits it: the tree of map nodes under the world, and which
/// of them are hidden.
/// </summary>
public sealed class MapDocument : IDisposable
{
    private readonly Datamodel.Datamodel datamodel;
    // The flags that hide a node
    private const MapNodeHiddenFlags HiddenFlagsMask = MapNodeHiddenFlags.Hidden | MapNodeHiddenFlags.AutoHidden | MapNodeHiddenFlags.HiddenByTool;

    private readonly HashSet<DMElement> hiddenNodes = [];

    /// <summary>Gets the root element of the map.</summary>
    public CMapRootElement Root { get; }

    /// <summary>Gets the world, the node every other node of the map hangs under.</summary>
    public CMapWorld World => Root.World;

    /// <summary>Gets the path the map was loaded from, when it came from a file.</summary>
    public string? FileName { get; }

    /// <summary>Gets the version of the map format the file was saved in.</summary>
    public int FormatVersion => datamodel.FormatVersion;

    private MapDocument(Datamodel.Datamodel datamodel, string? fileName)
    {
        this.datamodel = datamodel;
        FileName = fileName;

        Root = datamodel.Root as CMapRootElement
            ?? throw new InvalidDataException($"Expected a map root element, got '{datamodel.Root?.ClassName}'.");

        // The nodes convert their saved data into its runtime form. The saved elements they no longer refer to are
        // then dropped from the Datamodel, which would otherwise keep them in memory for as long as the map is open.
        Root.PostLoadFromFile();
        datamodel.AllElements.Trim();

        CollectHiddenNodes(World, parentHidden: false);
    }

    /// <summary>Loads a map from a file.</summary>
    /// <param name="fileName">Path to the <c>.vmap</c> file.</param>
    public static MapDocument Load(string fileName)
    {
        using var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Load(stream, fileName);
    }

    /// <summary>Loads a map from a stream.</summary>
    /// <param name="stream">Stream holding the map in any Datamodel encoding.</param>
    /// <param name="fileName">Path the stream came from, which prefabs resolve against.</param>
    public static MapDocument Load(Stream stream, string? fileName = null)
    {
        var datamodel = Datamodel.Datamodel.Load<CMapRootElement>(stream, DeferredMode.Disabled, new LoadOptions { Factory = MapElementFactory.Instance });

        try
        {
            if (datamodel.Format != "vmap")
            {
                throw new InvalidDataException($"Expected a map, got a Datamodel of format '{datamodel.Format}'.");
            }

            return new MapDocument(datamodel, fileName);
        }
        catch
        {
            datamodel.Dispose();
            throw;
        }
    }

    // A hidden node hides everything under it
    private void CollectHiddenNodes(DMElement node, bool parentHidden)
    {
        var hidden = parentHidden || !Root.Visibility.IsNodeVisible(node, HiddenFlagsMask) || node is MapNode { ForceHidden: true };

        if (hidden)
        {
            hiddenNodes.Add(node);
        }

        if (node is not MapNode mapNode)
        {
            return;
        }

        foreach (var child in mapNode.Children)
        {
            if (child != null)
            {
                CollectHiddenNodes(child, hidden);
            }
        }
    }

    /// <summary>
    /// Gets whether a node of this map is hidden, by itself or by a node it is under. The target of an
    /// instance stays hidden inside the instance too.
    /// </summary>
    /// <param name="node">The node.</param>
    public bool IsHidden(DMElement node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return hiddenNodes.Contains(node);
    }

    /// <summary>
    /// Gets the transform of a node from its own space to the world. Every node stores this in full: a
    /// child does not store it relative to its parent.
    /// </summary>
    /// <param name="node">The node.</param>
    public static Matrix4x4 GetNodeTransform(DMElement node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node is not MapNode mapNode)
        {
            return Matrix4x4.Identity;
        }

        return Matrix4x4.CreateScale(mapNode.Scales) * GetNodeRigidTransform(mapNode);
    }

    /// <summary>
    /// Gets the transform of a node from its own space to the world without its scale, which is what
    /// instances are placed by.
    /// </summary>
    /// <param name="node">The node.</param>
    public static Matrix4x4 GetNodeRigidTransform(DMElement node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node is not MapNode mapNode)
        {
            return Matrix4x4.Identity;
        }

        var angles = mapNode.Angles;

        return EntityTransformHelper.ToRigidTransformationMatrix(new Vector3(angles.Pitch, angles.Yaw, angles.Roll), mapNode.Origin);
    }

    /// <summary>
    /// Finds the folder the map's content paths are relative to, which is the folder a <c>maps/</c> path
    /// leading to the map starts in.
    /// </summary>
    /// <returns>The folder, or <see langword="null"/> when the map did not come from a file.</returns>
    public string? FindContentRoot()
    {
        var directory = Path.GetDirectoryName(FileName);

        while (!string.IsNullOrEmpty(directory))
        {
            if (Path.GetFileName(directory).Equals("maps", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(directory);
            }

            directory = Path.GetDirectoryName(directory);
        }

        return Path.GetDirectoryName(FileName);
    }

    /// <summary>
    /// Writes the map out in a Datamodel encoding. The saved data of the nodes is rebuilt for the write and
    /// discarded again afterwards.
    /// </summary>
    /// <param name="stream">Stream to write to.</param>
    /// <param name="encoding">Name of the encoding, <c>keyvalues2</c> for text or <c>binary</c>.</param>
    /// <param name="encodingVersion">Version of the encoding.</param>
    public void Save(Stream stream, string encoding, int encodingVersion)
    {
        Root.PreSaveToFile();

        try
        {
            datamodel.Save(stream, encoding, encodingVersion);
        }
        finally
        {
            Root.PostSaveToFile();
            datamodel.AllElements.Trim();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => datamodel.Dispose();

    // Needed because a map also holds elements of the model format, such as the vertex data painted onto a prop, which are stored in a namespace
    // other than ContentFormats.ValveMap
    internal sealed class MapElementFactory : IElementFactory
    {
        public static readonly MapElementFactory Instance = new();

        private static readonly string ModelNamespace = typeof(DmeVertexData).Namespace!;

        public IReadOnlyList<Datamodel.ElementSchema> Schemas => [.. Datamodel.Datamodel.ElementFactories.SelectMany(static factory => factory.Schemas)];

        public DMElement? Create(string nameSpace, string className)
        {
            foreach (var factory in Datamodel.Datamodel.ElementFactories)
            {
                if ((factory.Create(nameSpace, className) ?? factory.Create(ModelNamespace, className)) is { } element)
                {
                    return element;
                }
            }

            return null;
        }
    }
}
