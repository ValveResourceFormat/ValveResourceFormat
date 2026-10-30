using System.Globalization;
using System.Runtime.InteropServices;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer.Source1
{
    /// <summary>
    /// Shared parts of the batches of small pieces, such as sprites or ropes, that are collected into one mesh per map area.
    /// </summary>
    internal static class Source1MeshBatch
    {
        /// <summary>The side of the square map areas that get a mesh of their own.</summary>
        public const float ChunkSize = 2048f;

        /// <summary>Gets the map area a point at world coordinates is in.</summary>
        public static (int X, int Y) GetChunk(Vector3 position)
            => ((int)MathF.Floor(position.X / ChunkSize), (int)MathF.Floor(position.Y / ChunkSize));

        /// <summary>
        /// Creates the material of pieces that a shader turns into their final shape, as the depth prepass would draw
        /// the points they are placed at instead.
        /// </summary>
        /// <param name="materialLoader">The loader of the source material.</param>
        /// <param name="source">The material to take the textures from.</param>
        /// <param name="shaderName">The shader that draws the pieces.</param>
        /// <param name="setFlags">Sets the shader flags that vary between the kinds of pieces, or null.</param>
        public static RenderMaterial CreateMaterial(MaterialLoader materialLoader, RenderMaterial source, string shaderName, Action<RenderMaterial>? setFlags = null)
        {
            var material = materialLoader.CreateSource1MaterialWithShader(source, shaderName);
            material.IntParams["F_RENDER_BACKFACES"] = 1;
            material.IntParams["F_NO_Z_PREPASS"] = 1;

            setFlags?.Invoke(material);
            material.LoadRenderState();

            return material;
        }
    }

    /// <summary>
    /// Collects the triangles of many small pieces that share one material, such as sprites or ropes, into one mesh.
    /// </summary>
    internal sealed class Source1MeshBatch<TVertex>
        where TVertex : unmanaged
    {
        private readonly List<TVertex> vertices = [];
        private readonly List<int> indices = [];
        private AABB bounds;

        /// <summary>Adds a vertex and returns its index.</summary>
        /// <param name="vertex">The vertex.</param>
        /// <param name="position">Where the vertex draws, or the point it moves away from.</param>
        /// <param name="reach">How far from <paramref name="position"/> the vertex can draw.</param>
        public int AddVertex(TVertex vertex, Vector3 position, float reach)
        {
            var extent = new Vector3(reach);
            var min = position - extent;
            var max = position + extent;

            bounds = vertices.Count == 0 ? new AABB(min, max) : bounds.Encapsulate(min).Encapsulate(max);
            vertices.Add(vertex);

            return vertices.Count - 1;
        }

        /// <summary>Adds the two triangles of a quad from the indices of its four corners in order.</summary>
        public void AddQuad(int first, int second, int third, int fourth)
        {
            indices.Add(first);
            indices.Add(second);
            indices.Add(third);
            indices.Add(first);
            indices.Add(third);
            indices.Add(fourth);
        }

        /// <summary>Uploads the collected triangles as one mesh node of a scene.</summary>
        /// <param name="scene">The scene to add the node to.</param>
        /// <param name="name">The name of the node.</param>
        /// <param name="material">The material of the mesh.</param>
        /// <param name="layout">The vertex format of <typeparamref name="TVertex"/>.</param>
        public void AddTo(Scene scene, string name, RenderMaterial material, VertexInputLayout layout)
        {
            var buffers = layout.CreateBuffers<TVertex, int>(CollectionsMarshal.AsSpan(vertices), CollectionsMarshal.AsSpan(indices));
            var mesh = RenderableMesh.CreateMesh(name, material, buffers, bounds, scene.RendererContext);
            mesh.PlaceDrawCall(mesh.DrawCallsOpaque[0]);

            scene.Add(new MeshSceneNode(scene, mesh) { Name = name }, false);
        }
    }

    /// <summary>
    /// The batches of one kind of piece, one per scene, material and map area.
    /// </summary>
    internal sealed class Source1MeshBatchSet<TVertex>
        where TVertex : unmanaged
    {
        private readonly record struct BatchKey(Scene Scene, RenderMaterial Material, int X, int Y);

        private readonly Dictionary<BatchKey, Source1MeshBatch<TVertex>> batches = [];

        /// <summary>Gets the batch for pieces of a material placed at a point, creating it on first use.</summary>
        public Source1MeshBatch<TVertex> Get(Scene scene, RenderMaterial material, Vector3 origin)
        {
            var (x, y) = Source1MeshBatch.GetChunk(origin);
            var key = new BatchKey(scene, material, x, y);

            if (!batches.TryGetValue(key, out var batch))
            {
                batch = new Source1MeshBatch<TVertex>();
                batches.Add(key, batch);
            }

            return batch;
        }

        /// <summary>Adds the mesh of every batch to its scene.</summary>
        /// <param name="namePrefix">The start of the node names, which continue with the material and the map area.</param>
        /// <param name="layout">The vertex format of <typeparamref name="TVertex"/>.</param>
        public void AddTo(string namePrefix, VertexInputLayout layout)
        {
            foreach (var (key, batch) in batches)
            {
                var name = string.Create(CultureInfo.InvariantCulture, $"{namePrefix}/{key.Material.Material.Name}/{key.X}_{key.Y}");
                batch.AddTo(key.Scene, name, key.Material, layout);
            }
        }
    }
}
