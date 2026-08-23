using System.Linq;
using System.Runtime.InteropServices;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO.ContentFormats.ValveMap;

namespace ValveResourceFormat.Renderer.SceneNodes;

/// <summary>Scene node that renders a Hammer-authored source-map mesh.</summary>
public sealed class ValveMapMeshSceneNode : MeshCollectionNode
{
    private readonly List<RenderableMesh> meshes;

    /// <inheritdoc/>
    public override Vector4 Tint
    {
        get => meshes.Count > 0 ? meshes[0].Tint : Vector4.One;
        set
        {
            foreach (var mesh in meshes)
            {
                mesh.Tint = value;
            }
        }
    }

    /// <summary>Initializes a node from one source-map mesh.</summary>
    /// <param name="scene">The scene that owns this node.</param>
    /// <param name="mapMesh">Decoded source-map mesh data.</param>
    public ValveMapMeshSceneNode(Scene scene, ValveMapMesh mapMesh)
        : base(scene)
    {
        meshes = [];
        var hasBounds = false;
        var bounds = default(AABB);

        foreach (var part in mapMesh.Parts)
        {
            if (part.Vertices.Count == 0 || part.Indices.Count == 0)
            {
                continue;
            }

            var vertices = part.Vertices.Select(static vertex => new Vertex(vertex)).ToArray();
            var indices = part.Indices.ToArray();
            var partBounds = new AABB();
            foreach (var vertex in vertices)
            {
                partBounds = partBounds.Encapsulate(vertex.Position);
            }

            var vbib = new VBIB { Resource = null! };
            vbib.VertexBuffers.Add(new VBIB.OnDiskBufferData
            {
                ElementCount = (uint)vertices.Length,
                ElementSizeInBytes = (uint)Vertex.InputLayout.Stride,
                InputLayoutFields = Vertex.InputLayout.Fields(),
                Data = MemoryMarshal.Cast<Vertex, byte>(vertices).ToArray(),
            });
            vbib.IndexBuffers.Add(new VBIB.OnDiskBufferData
            {
                ElementCount = (uint)indices.Length,
                ElementSizeInBytes = sizeof(uint),
                InputLayoutFields = [],
                Data = MemoryMarshal.Cast<uint, byte>(indices).ToArray(),
            });

            var name = $"Map mesh {mapMesh.NodeId}";
            var material = Scene.RendererContext.MaterialLoader.GetMaterial(part.MaterialName, null);
            meshes.Add(RenderableMesh.CreateMesh(name, material, vbib, partBounds, Scene.RendererContext));
            bounds = hasBounds ? bounds.Union(partBounds) : partBounds;
            hasBounds = true;
        }

        RenderableMeshes = meshes;
        LocalBoundingBox = hasBounds ? bounds : default;
    }

    /// <inheritdoc/>
    public override IEnumerable<string> GetSupportedRenderModes()
        => meshes.SelectMany(static mesh => mesh.GetSupportedRenderModes()).Distinct();

#if DEBUG
    /// <inheritdoc/>
    public override void UpdateVertexArrayObjects()
    {
        foreach (var mesh in meshes)
        {
            mesh.UpdateVertexArrayObjects();
        }
    }
#endif

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Vertex
    {
        [VertexAttribute(VertexSlot.Position)] public readonly Vector3 Position;
        [VertexAttribute(VertexSlot.Normal)] public readonly Vector3 Normal;
        [VertexAttribute(VertexSlot.Tangent)] public readonly Vector4 Tangent;
        [VertexAttribute(VertexSlot.TexCoord)] public readonly Vector2 TexCoord;

        public static readonly VertexInputLayout InputLayout = VertexInputLayout.FromStruct<Vertex>();

        public Vertex(ValveMapMeshVertex vertex)
        {
            Position = vertex.Position;
            Normal = vertex.Normal;
            Tangent = vertex.Tangent;
            TexCoord = vertex.TexCoord;
        }
    }
}
