using System.Linq;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Positions, normals and tangents of the meshes of one model LoD level, transformed per vertex on the CPU, to draw
    /// over the meshes' own streams with <see cref="SceneNodes.ModelSceneNode.SetBakedVertexStreams"/>. One instance can
    /// serve any number of nodes of the same model.
    /// </summary>
    public sealed class BakedVertexStreams
    {
        private sealed record Binding(int[] BufferIndices, int Stream);

        private sealed record BakedMesh(int MeshIndex, AABB Bounds, Binding[] Bindings, int[]? ReversedIndexBuffers);

        private readonly List<BakedMesh> meshes = [];
        private readonly VBIB buffers;
        private readonly string label;
        private GPUMeshBuffers? gpuBuffers;
        private RendererContext? gpuContext;

        /// <summary>Gets the LoD level whose meshes were baked.</summary>
        public int LodLevel { get; }

        private BakedVertexStreams(Model model, int lodLevel)
        {
            buffers = new VBIB { Resource = model.Resource };
            label = model.Name;
            LodLevel = lodLevel;
        }

        /// <summary>
        /// Transforms the meshes of one LoD level in the default mesh groups. This touches no GPU state, so it can run on
        /// any thread.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <param name="fileLoader">Loads the model's external meshes.</param>
        /// <param name="lodLevel">The LoD level, clamped to the levels the model has; null for its lowest level.</param>
        /// <param name="map">Maps the model-space vertex attributes. Draw calls of a mirroring map draw with the triangle winding reversed.</param>
        /// <returns>The baked streams.</returns>
        public static BakedVertexStreams Create(Model model, IFileLoader fileLoader, int? lodLevel, VertexAttributeMap map)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(fileLoader);
            ArgumentNullException.ThrowIfNull(map);

            var lodInfo = model.LodInfo;
            var highestLevel = lodInfo.AvailableLevels.Count > 0 ? lodInfo.AvailableLevels[^1] : lodInfo.LowestLevel;
            var level = Math.Clamp(lodLevel ?? lodInfo.LowestLevel, lodInfo.LowestLevel, highestLevel);
            var meshGroups = model.MeshGroups;
            var defaultGroups = meshGroups.Defaults.ToHashSet();

            var result = new BakedVertexStreams(model, level);

            foreach (var embeddedMesh in model.GetEmbeddedMeshesForLod(level))
            {
                if (meshGroups.IsMeshInAnyGroup(embeddedMesh.MeshIndex, defaultGroups))
                {
                    result.AddMesh(embeddedMesh.Mesh, embeddedMesh.MeshIndex, map);
                }
            }

            foreach (var refMesh in model.GetReferenceMeshNamesForLod(level))
            {
                if (meshGroups.IsMeshInAnyGroup(refMesh.MeshIndex, defaultGroups)
                    && fileLoader.LoadFileCompiled(refMesh.MeshName)?.DataBlock is Mesh mesh)
                {
                    result.AddMesh(mesh, refMesh.MeshIndex, map);
                }
            }

            return result;
        }

        private void AddMesh(Mesh mesh, int meshIndex, VertexAttributeMap map)
        {
            var vbib = mesh.VBIB;
            var bindings = new List<Binding>();
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var sceneObject in mesh.Data.GetArray("m_sceneObjects"))
            {
                foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
                {
                    var bufferIndices = drawCall.GetArray("m_vertexBuffers").Select(static buffer => buffer.GetInt32Property("m_hBuffer")).ToArray();

                    if (bindings.Exists(binding => binding.BufferIndices.AsSpan().SequenceEqual(bufferIndices))
                        || VBIB.TransformVertexAttributes([.. bufferIndices.Select(index => vbib.VertexBuffers[index])], map) is not { } stream)
                    {
                        continue;
                    }

                    foreach (var position in VBIB.GetVector3AttributeArray(stream, stream.InputLayoutFields[0]))
                    {
                        min = Vector3.Min(min, position);
                        max = Vector3.Max(max, position);
                    }

                    bindings.Add(new Binding(bufferIndices, buffers.VertexBuffers.Count));
                    buffers.VertexBuffers.Add(stream);
                }
            }

            if (bindings.Count == 0)
            {
                return;
            }

            int[]? reversedIndexBuffers = null;

            if (map.Mirrored)
            {
                reversedIndexBuffers = new int[vbib.IndexBuffers.Count];

                for (var i = 0; i < reversedIndexBuffers.Length; i++)
                {
                    reversedIndexBuffers[i] = buffers.IndexBuffers.Count;
                    buffers.IndexBuffers.Add(ReverseWinding(vbib.IndexBuffers[i]));
                }
            }

            if (min.X > max.X)
            {
                mesh.GetBounds();
                (min, max) = (mesh.MinBounds, mesh.MaxBounds);
            }

            meshes.Add(new BakedMesh(meshIndex, new AABB(min, max), [.. bindings], reversedIndexBuffers));
        }

        private static VBIB.OnDiskBufferData ReverseWinding(VBIB.OnDiskBufferData indexBuffer)
        {
            var data = (byte[])indexBuffer.Data.Clone();
            var size = (int)indexBuffer.ElementSizeInBytes;
            var triangleSize = size * 3;
            Span<byte> swap = stackalloc byte[size];

            for (var offset = 0; offset + triangleSize <= data.Length; offset += triangleSize)
            {
                var second = data.AsSpan(offset + size, size);
                var third = data.AsSpan(offset + size * 2, size);

                second.CopyTo(swap);
                third.CopyTo(second);
                swap.CopyTo(third);
            }

            var reversed = indexBuffer;
            reversed.Data = data;
            return reversed;
        }

        /// <summary>
        /// Binds the streams over the draw calls of the meshes that were baked, uploading them on first use, and sets those
        /// meshes' bounds. When the map mirrors, the draw calls draw with the triangle winding reversed.
        /// </summary>
        internal void Bind(IEnumerable<RenderableMesh> renderableMeshes, RendererContext rendererContext)
        {
            if (gpuBuffers == null)
            {
                gpuBuffers = new GPUMeshBuffers(buffers, $"{label} baked");
                gpuContext = rendererContext;
            }

            foreach (var renderableMesh in renderableMeshes)
            {
                var mesh = meshes.Find(baked => baked.MeshIndex == renderableMesh.MeshIndex);

                if (mesh == null)
                {
                    continue;
                }

                renderableMesh.BoundingBox = mesh.Bounds;

                foreach (var drawCall in renderableMesh.DrawCalls)
                {
                    var bufferIndices = Array.ConvertAll(drawCall.VertexBuffers, static buffer => buffer.BufferIndex);
                    var binding = Array.Find(mesh.Bindings, candidate => candidate.BufferIndices.AsSpan().SequenceEqual(bufferIndices));

                    if (binding == null)
                    {
                        continue;
                    }

                    if (mesh.ReversedIndexBuffers != null)
                    {
                        drawCall.IndexBuffer = drawCall.IndexBuffer with
                        {
                            Handle = gpuBuffers.IndexBuffers[mesh.ReversedIndexBuffers[drawCall.IndexBuffer.BufferIndex]],
                        };
                    }

                    var stream = buffers.VertexBuffers[binding.Stream];

                    drawCall.AddVertexBuffer(new VertexDrawBuffer
                    {
                        Handle = gpuBuffers.VertexBuffers[binding.Stream],
                        BufferIndex = binding.Stream,
                        ElementSizeInBytes = stream.ElementSizeInBytes,
                        InputLayoutFields = stream.InputLayoutFields,
                    });
                }
            }
        }

        /// <summary>
        /// Frees the GPU buffers. Nodes bound to these streams must be deleted first.
        /// </summary>
        public void Delete()
        {
            if (gpuBuffers == null || gpuContext == null)
            {
                return;
            }

            gpuContext.MeshBufferCache.InvalidateVertexArrayObjectsForFreedBuffers([.. gpuBuffers.VertexBuffers, .. gpuBuffers.IndexBuffers]);
            gpuBuffers.Delete();
            gpuBuffers = null;
            gpuContext = null;
        }
    }
}
