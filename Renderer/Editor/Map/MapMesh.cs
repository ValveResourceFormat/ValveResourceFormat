using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.Renderer.Editor.Gizmos;

namespace ValveResourceFormat.Renderer.Editor.Map;

/// <summary>
/// A mesh of a source map where it was placed, with the nodes it is drawn with. A gizmo moves and turns it by its
/// origin and angles, and scales it into its vertices, as Hammer does, so it never stores a scale of its own.
/// </summary>
public sealed class MapMesh : IGizmoTransformable
{
    private readonly Scene scene;
    private readonly List<SceneNode> nodes = [];

    private MapPlacement transformStart;
    private Matrix4x4[] nodeTransformsAtStart = [];
    private Matrix4x4? vertexTransform;

    internal MapMesh(Scene scene, PlacedMapNode placedNode, CMapMesh mesh, MapEntity? owner)
    {
        this.scene = scene;

        PlacedNode = placedNode;
        Mesh = mesh;
        Owner = owner;
        Placement = new MapPlacement(mesh.Origin, new Vector3(mesh.Angles.Pitch, mesh.Angles.Yaw, mesh.Angles.Roll), mesh.Scales);
    }

    /// <summary>Gets the node of the map the mesh is, with where it was placed.</summary>
    public PlacedMapNode PlacedNode { get; }

    /// <summary>Gets the mesh.</summary>
    public CMapMesh Mesh { get; }

    /// <summary>Gets the entity the mesh belongs to, or <see langword="null"/> for a mesh of the world.</summary>
    public MapEntity? Owner { get; }

    /// <summary>Gets where the mesh is, as it is being dragged.</summary>
    public MapPlacement Placement { get; private set; }

    /// <summary>Gets the nodes the mesh is drawn with.</summary>
    public IReadOnlyList<SceneNode> Nodes => nodes;

    /// <inheritdoc/>
    public Vector3 Origin => Placement.Origin;

    /// <inheritdoc/>
    public Matrix4x4 RigidTransform => Placement.RigidTransform;

    /// <summary>Gets the bounds of the mesh in the world.</summary>
    public AABB Bounds => GetBounds(Matrix4x4.Identity);

    /// <inheritdoc/>
    public AABB GetBounds(in Matrix4x4 worldToSpace)
    {
        AABB? bounds = null;

        foreach (var node in nodes)
        {
            var nodeBounds = node.LocalBoundingBox.Transform(node.Transform * worldToSpace);
            bounds = bounds?.Union(nodeBounds) ?? nodeBounds;
        }

        var origin = Vector3.Transform(Origin, worldToSpace);
        return bounds ?? new AABB(origin, origin);
    }

    /// <inheritdoc/>
    /// <remarks>What an instance or prefab places is moved with it, not on its own.</remarks>
    public GizmoTransformModes SupportedTransformModes => PlacedNode.IsInstanced ? GizmoTransformModes.None : GizmoTransformModes.All;

    /// <inheritdoc/>
    public IGizmoTransformable? Parent => Owner;

    internal void AddNode(SceneNode node) => nodes.Add(node);

    /// <inheritdoc/>
    public void BeginTransform()
    {
        transformStart = Placement;
        vertexTransform = null;
        nodeTransformsAtStart = [.. nodes.Select(static node => node.Transform)];

        // A mesh is static until it is first moved, and dynamic from then on
        foreach (var node in nodes)
        {
            if (scene.Remove(node, dynamic: false))
            {
                scene.Add(node, dynamic: true);
            }
        }
    }

    /// <inheritdoc/>
    public void ApplyGizmoTransform(GizmoResult result)
    {
        (Placement, vertexTransform) = GizmoTransforms.TransformMesh(transformStart, result);
        PlaceNodes();
    }

    /// <inheritdoc/>
    public void EndTransform(bool commit)
    {
        if (!commit)
        {
            Placement = transformStart;
            vertexTransform = null;
            PlaceNodes();
            return;
        }

        Mesh.Origin = Placement.Origin;
        Mesh.Angles = new Datamodel.QAngle(Placement.Angles.X, Placement.Angles.Y, Placement.Angles.Z);

        if (vertexTransform is { } transform)
        {
            var polygonMesh = Mesh.Mesh;

            foreach (var vertex in polygonMesh.VertexHandles)
            {
                polygonMesh.SetVertexPosition(vertex, Vector3.Transform(polygonMesh.GetVertexPosition(vertex), transform));
            }

            vertexTransform = null;
        }
    }

    // Each node keeps where it is from the mesh, its vertices as they were built followed by what they went through
    private void PlaceNodes()
    {
        if (!Matrix4x4.Invert(transformStart.Transform, out var fromStart))
        {
            return;
        }

        var toCurrent = fromStart * (vertexTransform ?? Matrix4x4.Identity) * Placement.Transform;

        for (var i = 0; i < nodes.Count && i < nodeTransformsAtStart.Length; i++)
        {
            nodes[i].Transform = nodeTransformsAtStart[i] * toCurrent;
        }
    }
}
