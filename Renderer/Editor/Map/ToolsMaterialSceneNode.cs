using ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer.Editor.Map;

/// <summary>
/// The faces of a map mesh drawn with a tools material, such as a clip or a trigger, shown as translucent shapes
/// with the texture of the material, the way compiled collision built from tools materials is, see
/// <see cref="ShapeSceneNode.ToolsMaterial"/>.
/// </summary>
internal sealed class ToolsMaterialSceneNode : ShapeSceneNode
{
    private static readonly Color32 FillColor = new(1f, 1f, 1f, OrderIndependentFillAlpha);

    private ToolsMaterialSceneNode(Scene scene, List<SimpleVertexNormal> verts, List<int> inds) : base(scene, verts, inds)
    {
    }

    /// <inheritdoc/>
    protected override bool IsDoubleSided => false;

    /// <inheritdoc/>
    internal override bool HasOrderIndependentFill => IsTranslucentRenderMode;

    /// <summary>Creates the node of the faces of one material of a mesh.</summary>
    /// <param name="scene">The scene the node is for, which it is not added to.</param>
    /// <param name="name">What the node is named.</param>
    /// <param name="group">The faces, in the space of the mesh.</param>
    /// <param name="material">The path of the tools material they are drawn with.</param>
    public static ToolsMaterialSceneNode Create(Scene scene, string name, PolygonMesh.BakedSubmesh group, string material)
    {
        var verts = new List<SimpleVertexNormal>(group.Positions.Length);
        var bounds = default(AABB);

        for (var i = 0; i < group.Positions.Length; i++)
        {
            verts.Add(new(group.Positions[i], FillColor, group.Normals[i]));
            bounds = i == 0 ? new AABB(group.Positions[i], group.Positions[i]) : bounds.Encapsulate(group.Positions[i]);
        }

        var node = new ToolsMaterialSceneNode(scene, verts, [.. group.Indices])
        {
            Name = name,
            LocalBoundingBox = bounds,
        };

        node.SetToolsMaterial(material);

        return node;
    }
}
