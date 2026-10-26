using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.ValveMap;

namespace ValveResourceFormat.Renderer.Editor.Map;

/// <summary>
/// A mesh of a source map where it was placed, with the nodes it is drawn with.
/// </summary>
public sealed class MapMesh
{
    private readonly List<SceneNode> nodes = [];

    internal MapMesh(PlacedMapNode placedNode, CMapMesh mesh, MapEntity? owner)
    {
        PlacedNode = placedNode;
        Mesh = mesh;
        Owner = owner;
    }

    /// <summary>Gets the node of the map the mesh is, with where it was placed.</summary>
    public PlacedMapNode PlacedNode { get; }

    /// <summary>Gets the mesh.</summary>
    public CMapMesh Mesh { get; }

    /// <summary>Gets the entity the mesh belongs to, or <see langword="null"/> for a mesh of the world.</summary>
    public MapEntity? Owner { get; }

    /// <summary>Gets the nodes the mesh is drawn with.</summary>
    public IReadOnlyList<SceneNode> Nodes => nodes;

    /// <summary>Gets the bounds of the mesh in the world, or where it is placed when it draws nothing.</summary>
    public AABB Bounds
    {
        get
        {
            AABB? bounds = null;

            foreach (var node in nodes)
            {
                bounds = bounds?.Union(node.BoundingBox) ?? node.BoundingBox;
            }

            var origin = PlacedNode.Transform.Translation;
            return bounds ?? new AABB(origin, origin);
        }
    }

    internal void AddNode(SceneNode node) => nodes.Add(node);
}
