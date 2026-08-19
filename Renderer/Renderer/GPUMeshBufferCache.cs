using System.Diagnostics;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

#if DEBUG
using Microsoft.Extensions.Logging;
#endif

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Caches GPU mesh buffers and vertex array objects for efficient mesh rendering.
    /// </summary>
    public partial class GPUMeshBufferCache
    {
        private readonly RendererContext RendererContext;
        private readonly Dictionary<string, GPUMeshBuffers> gpuBuffers = [];
        private readonly Dictionary<VAOKey, int> vertexArrayObjects = [];

        /// <summary>Gets the number of distinct vertex array objects currently cached.</summary>
        public int VertexArrayObjectCount => vertexArrayObjects.Count;

        /// <summary>Identifies a vertex array object by the attribute layout of an input signature and the
        /// GPU buffers.</summary>
        private readonly struct VAOKey : IEquatable<VAOKey>
        {
            public required int InputSignature { get; init; }
            public required int IndexBuffer { get; init; }
            public required int[] VertexBuffers { get; init; }

            public bool Equals(VAOKey other)
                => InputSignature == other.InputSignature
                && IndexBuffer == other.IndexBuffer
                && VertexBuffers.AsSpan().SequenceEqual(other.VertexBuffers);

            public override bool Equals(object? obj) => obj is VAOKey other && Equals(other);

            public override int GetHashCode()
            {
                var hash = new HashCode();
                hash.Add(InputSignature);
                hash.Add(IndexBuffer);

                foreach (var handle in VertexBuffers)
                {
                    hash.Add(handle);
                }

                return hash.ToHashCode();
            }
        }

        /// <summary>Initializes a new GPU mesh buffer cache.</summary>
        /// <param name="rendererContext">The renderer context owning this cache.</param>
        public GPUMeshBufferCache(RendererContext rendererContext)
        {
            RendererContext = rendererContext;
        }

        /// <summary>Returns cached GPU buffers for the named mesh, uploading them if not yet present.</summary>
        /// <param name="meshName">Unique name identifying the mesh.</param>
        /// <param name="vbib">Vertex and index buffer data to upload on first use.</param>
        /// <returns>The GPU buffers for the mesh.</returns>
        public GPUMeshBuffers CreateVertexIndexBuffers(string meshName, VBIB vbib)
        {
            if (!gpuBuffers.TryGetValue(meshName, out var gpuVbib))
            {
                gpuVbib = new GPUMeshBuffers(vbib, meshName);
                gpuBuffers.Add(meshName, gpuVbib);
            }

            return gpuVbib;
        }

        /// <summary>Uploads the mesh buffers (if not yet present) and returns a VAO for the first
        /// vertex/index buffer pair, without exposing the GPU buffer handles to the caller.</summary>
        /// <param name="meshName">Unique name identifying the mesh.</param>
        /// <param name="vbib">Vertex and index buffer data; the first vertex buffer's layout describes the attributes.</param>
        /// <param name="inputSignature">Optional material input signature mapping buffer semantics to shader attribute names.</param>
        /// <returns>The OpenGL VAO handle.</returns>
        public int UploadBuffersAndCreateVertexArray(string meshName, VBIB vbib, Material.VsInputSignature inputSignature = default)
        {
            var gpuVbib = CreateVertexIndexBuffers(meshName, vbib);
            var vertexBuffer = vbib.VertexBuffers[0];

            return GetVertexArrayObject(
            [
                new VertexDrawBuffer
                {
                    Handle = gpuVbib.VertexBuffers[0],
                    ElementSizeInBytes = vertexBuffer.ElementSizeInBytes,
                    InputLayoutFields = vertexBuffer.InputLayoutFields,
                },
            ], inputSignature, vbib.IndexBuffers.Count > 0 ? gpuVbib.IndexBuffers[0] : 0, meshName);
        }

        /// <summary>
        /// Disposes any cached gpu buffers and frees gpu vertex arrays.
        /// </summary>
        public void Clear()
        {
            foreach (var item in gpuBuffers)
            {
                item.Value.Delete();
            }

            gpuBuffers.Clear();

            foreach (var item in vertexArrayObjects)
            {
                VertexArray.Delete(item.Value);
            }

            vertexArrayObjects.Clear();
        }

        /// <summary>Deletes and removes the cached GPU buffers and vertex arrays for the specified mesh.</summary>
        /// <param name="meshName">Unique name identifying the mesh to delete.</param>
        public void DeleteVertexIndexBuffers(string meshName)
        {
            if (gpuBuffers.TryGetValue(meshName, out var gpuVbib))
            {
                gpuVbib.Delete();
                gpuBuffers.Remove(meshName);
                InvalidateVertexArrayObjectsForFreedBuffers([.. gpuVbib.VertexBuffers, .. gpuVbib.IndexBuffers]);
            }
        }

        /// <summary>Deletes and removes the cached VAOs built from the given GPU buffer handles, which the
        /// caller is about to delete. Because OpenGL never assigns a handle to two live objects at once, a
        /// handle passed here can only match VAOs built from that exact buffer, so this is a precise
        /// invalidation. Skipping it would leave a stale cache entry that silently matches whatever unrelated
        /// buffer GL later reuses that handle for.</summary>
        /// <param name="bufferHandles">Vertex and/or index buffer handles about to be freed.</param>
        public void InvalidateVertexArrayObjectsForFreedBuffers(params int[] bufferHandles)
            => DeleteVertexArrayObjects(key
                => Array.IndexOf(bufferHandles, key.IndexBuffer) >= 0
                || key.VertexBuffers.Any(handle => Array.IndexOf(bufferHandles, handle) >= 0));

        private void DeleteVertexArrayObjects(Func<VAOKey, bool> predicate)
        {
            List<VAOKey>? keysToRemove = null;

            foreach (var (key, vao) in vertexArrayObjects)
            {
                if (predicate(key))
                {
                    VertexArray.Delete(vao);
                    (keysToRemove ??= []).Add(key);
                }
            }

            keysToRemove?.ForEach(key => vertexArrayObjects.Remove(key));
        }

        /// <summary>Returns a cached VAO for the given buffers, creating it if necessary. Locations are
        /// canonical, so it is valid for every shader that draws these buffers. Keyed by what a VAO
        /// fundamentally is, so callers never need to invent a unique name to keep unrelated geometry from
        /// colliding, and identical layouts dedupe automatically.</summary>
        /// <param name="vertexBuffers">Vertex buffer bindings for the draw call.</param>
        /// <param name="inputSignature">Material input signature mapping buffer semantics to shader attribute names.</param>
        /// <param name="idxIndex">OpenGL handle of the index buffer, or 0 for non-indexed geometry.</param>
        /// <param name="debugLabel">Optional label applied to the VAO in debug builds when newly created.</param>
        /// <returns>The OpenGL VAO handle.</returns>
        public int GetVertexArrayObject(VertexDrawBuffer[] vertexBuffers, Material.VsInputSignature inputSignature, int idxIndex, string? debugLabel = null)
        {
            var vaoKey = new VAOKey
            {
                InputSignature = inputSignature.Hash,
                IndexBuffer = idxIndex,
                VertexBuffers = Array.ConvertAll(vertexBuffers, vb => vb.Handle),
            };

            if (vertexArrayObjects.TryGetValue(vaoKey, out var vaoHandle))
            {
                return vaoHandle;
            }

            var newVaoHandle = CreateVertexArrayObject(vertexBuffers, inputSignature, idxIndex, debugLabel);
            vertexArrayObjects.Add(vaoKey, newVaoHandle);
            return newVaoHandle;
        }

        /// <summary>Builds a new VAO for the given buffers without caching it. Each attribute takes the
        /// canonical location of its material input signature name, or of its own buffer semantic when the
        /// signature is absent or names something unknown.</summary>
        /// <param name="vertexBuffers">Vertex buffer bindings for the draw call.</param>
        /// <param name="inputSignature">Material input signature mapping buffer semantics to shader attribute names.</param>
        /// <param name="idxIndex">OpenGL handle of the index buffer.</param>
        /// <param name="debugLabel">Optional label applied to the VAO in debug builds.</param>
        /// <returns>The OpenGL VAO handle.</returns>
        private int CreateVertexArrayObject(VertexDrawBuffer[] vertexBuffers, Material.VsInputSignature inputSignature, int idxIndex, string? debugLabel = null)
        {
            Debug.Assert(vertexBuffers != null && vertexBuffers.Length > 0);

            GL.CreateVertexArrays(1, out int newVaoHandle);
            VertexArray.StartRecording(newVaoHandle);

            // Check for non-indexed geometry
            if (idxIndex != 0)
            {
                GL.VertexArrayElementBuffer(newVaoHandle, idxIndex);
            }

            // Workaround a bug in Intel drivers when mixing float and integer attributes
            // See https://gist.github.com/stefalie/e17a20a88a0fdbd97110611569a6605f for reference
            // We are using DSA apis, so we don't actually need to bind the VAO
            GL.BindVertexArray(newVaoHandle);

            var bindingIndex = 0;
            var boundLocations = 0;
            vertexBuffers = AddMissingAttributes(vertexBuffers);

            foreach (var curVertexBuffer in vertexBuffers)
            {
                GL.VertexArrayVertexBuffer(newVaoHandle, bindingIndex, curVertexBuffer.Handle, 0, (int)curVertexBuffer.ElementSizeInBytes);

                foreach (var attribute in curVertexBuffer.InputLayoutFields)
                {
                    var attributeLocation = VertexAttributeLocations.Resolve(inputSignature, attribute, out var insgElemName);

                    // Unknown, or a location an earlier buffer already took, which the table's aliases allow
                    if (attributeLocation == -1 || (boundLocations & (1 << attributeLocation)) != 0)
                    {
#if DEBUG
                        if (attributeLocation == -1 && !string.IsNullOrEmpty(insgElemName))
                        {
                            RendererContext.Logger.LogDebug("Attribute {SemanticName} ({SemanticIndex}) has no canonical location (insg: {InsgElemName})", attribute.SemanticName, attribute.SemanticIndex, insgElemName);
                        }
#endif
                        continue;
                    }

                    boundLocations |= 1 << attributeLocation;

                    GL.EnableVertexArrayAttrib(newVaoHandle, attributeLocation);
                    GL.VertexArrayAttribBinding(newVaoHandle, attributeLocation, bindingIndex);
                    VertexArray.SetAttribFormat(newVaoHandle, attributeLocation, attribute.Format, (int)attribute.Offset);
                }

                bindingIndex++;
            }

#if DEBUG
            if (debugLabel != null)
            {
                GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, newVaoHandle, Math.Min(GLEnvironment.MaxLabelLength, debugLabel.Length), debugLabel);
            }
#endif

            return newVaoHandle;
        }

        private VertexDrawBuffer[] AddMissingAttributes(VertexDrawBuffer[] vertexBuffers)
        {
            // Shaders read white where a mesh has no COLOR stream, matching the engine default
            if (!vertexBuffers.Any(vb => vb.InputLayoutFields.Any(f => f.SemanticName == "COLOR")))
            {
                var defaultColor = new VertexDrawBuffer
                {
                    Handle = VectorOneVertexBuffer,
                    ElementSizeInBytes = 0, // required for the singular attribute to apply to all vertices
                    InputLayoutFields =
                    [
                        new VBIB.RenderInputLayoutField
                        {
                            SemanticName = "COLOR",
                            Format = DXGI_FORMAT.R32G32B32A32_FLOAT,
                        },
                    ],
                };

                vertexBuffers = [.. vertexBuffers, defaultColor];
            }

            return vertexBuffers;
        }
    }
}
