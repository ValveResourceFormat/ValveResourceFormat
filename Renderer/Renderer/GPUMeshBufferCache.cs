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

        /// <summary>A vertex attribute resolved to its canonical location, ready to be bound into a VAO.</summary>
        private readonly record struct AttributeBinding(int Location, DXGI_FORMAT Format, int Offset, int BindingIndex);

        /// <summary>Identifies a VAO by what it actually is: a set of GPU buffer objects with resolved
        /// attribute bindings. Attribute locations are canonical (<see cref="VertexAttributeLocations"/>),
        /// so the key carries no shader - one VAO serves every shader that draws the mesh. The resolved
        /// bindings participate because a material's input signature can alias the same buffers onto
        /// different attribute names. Not tied to any higher-level resource name, so buffers that happen
        /// to share a mesh name (or none at all) still dedupe correctly, and are never confused with
        /// buffers that happen to reuse a freed handle under an unrelated name.</summary>
        private readonly struct VAOKey : IEquatable<VAOKey>
        {
            public required int IndexBuffer { get; init; }
            public required int[] VertexBuffers { get; init; }
            public required AttributeBinding[] Attributes { get; init; }

            public bool Equals(VAOKey other)
                => IndexBuffer == other.IndexBuffer
                && VertexBuffers.AsSpan().SequenceEqual(other.VertexBuffers)
                && Attributes.AsSpan().SequenceEqual(other.Attributes);

            public override bool Equals(object? obj) => obj is VAOKey other && Equals(other);

            public override int GetHashCode()
            {
                var hash = new HashCode();
                hash.Add(IndexBuffer);

                foreach (var handle in VertexBuffers)
                {
                    hash.Add(handle);
                }

                foreach (var attribute in Attributes)
                {
                    hash.Add(attribute);
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
                gpuVbib = new GPUMeshBuffers(vbib);
                gpuBuffers.Add(meshName, gpuVbib);

#if DEBUG
                for (var i = 0; i < gpuVbib.VertexBuffers.Length; i++)
                {
                    var bufferLabel = $"{meshName} VB {i}";
                    GL.ObjectLabel(ObjectLabelIdentifier.Buffer, gpuVbib.VertexBuffers[i], Math.Min(GLEnvironment.MaxLabelLength, bufferLabel.Length), bufferLabel);
                }

                for (var i = 0; i < gpuVbib.IndexBuffers.Length; i++)
                {
                    var bufferLabel = $"{meshName} IB {i}";
                    GL.ObjectLabel(ObjectLabelIdentifier.Buffer, gpuVbib.IndexBuffers[i], Math.Min(GLEnvironment.MaxLabelLength, bufferLabel.Length), bufferLabel);
                }
#endif
            }

            return gpuVbib;
        }

        /// <summary>Uploads the mesh buffers (if not yet present) and returns vertex array state for the
        /// first vertex/index buffer pair, without exposing the GPU buffer handles to the caller.</summary>
        /// <param name="meshName">Unique name identifying the mesh.</param>
        /// <param name="vbib">Vertex and index buffer data; the first vertex buffer's layout describes the attributes.</param>
        /// <param name="inputSignature">Optional material input signature mapping buffer semantics to shader attribute names.</param>
        /// <returns>Vertex array state for the mesh.</returns>
        public RenderVao UploadBuffersAndCreateVertexArray(string meshName, VBIB vbib, Material.VsInputSignature inputSignature = default)
        {
            var gpuVbib = CreateVertexIndexBuffers(meshName, vbib);
            var vertexBuffer = vbib.VertexBuffers[0];

            return new RenderVao(this,
            [
                new VertexDrawBuffer
                {
                    Handle = gpuVbib.VertexBuffers[0],
                    ElementSizeInBytes = vertexBuffer.ElementSizeInBytes,
                    InputLayoutFields = vertexBuffer.InputLayoutFields,
                },
            ], vbib.IndexBuffers.Count > 0 ? gpuVbib.IndexBuffers[0] : 0, inputSignature, meshName);
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
                GL.DeleteVertexArray(item.Value);
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
        /// handle passed here can only ever match VAOs built from that exact buffer - never an unrelated one -
        /// so this is a precise invalidation, not a general sweep. Skipping this call before deleting a buffer
        /// would leave a stale cache entry that silently matches whatever unrelated buffer GL later reuses that
        /// handle for.</summary>
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
                    GL.DeleteVertexArray(vao);
                    (keysToRemove ??= []).Add(key);
                }
            }

            keysToRemove?.ForEach(key => vertexArrayObjects.Remove(key));
        }

        /// <summary>Returns a cached VAO for the given buffers, creating it if necessary. Attribute
        /// locations are canonical, so the returned VAO is valid for every shader that draws these
        /// buffers. The cache key is the resolved attribute bindings plus the actual GPU buffer handles -
        /// what a VAO fundamentally is - so callers never need to invent a unique name to keep unrelated
        /// geometry from colliding, and identical layouts dedupe automatically regardless of which mesh
        /// (if any) they came from.</summary>
        /// <param name="vertexBuffers">Vertex buffer bindings for the draw call.</param>
        /// <param name="inputSignature">Material input signature mapping buffer semantics to shader attribute names.</param>
        /// <param name="idxIndex">OpenGL handle of the index buffer, or 0 for non-indexed geometry.</param>
        /// <param name="debugLabel">Optional label applied to the VAO in debug builds when newly created.</param>
        /// <returns>The OpenGL VAO handle.</returns>
        public int GetVertexArrayObject(VertexDrawBuffer[] vertexBuffers, Material.VsInputSignature inputSignature, int idxIndex, string? debugLabel = null)
        {
            Debug.Assert(vertexBuffers != null && vertexBuffers.Length > 0);

            vertexBuffers = AddMissingAttributes(vertexBuffers);
            var attributes = ResolveAttributeBindings(vertexBuffers, inputSignature);

            var vaoKey = new VAOKey
            {
                IndexBuffer = idxIndex,
                VertexBuffers = Array.ConvertAll(vertexBuffers, vb => vb.Handle),
                Attributes = attributes,
            };

            if (vertexArrayObjects.TryGetValue(vaoKey, out var vaoHandle))
            {
                return vaoHandle;
            }

            var newVaoHandle = CreateVertexArrayObject(vertexBuffers, attributes, idxIndex, debugLabel);
            vertexArrayObjects.Add(vaoKey, newVaoHandle);
            return newVaoHandle;
        }

        /// <summary>Resolves each buffer attribute to its canonical location: the material input signature
        /// name wins when it resolves, otherwise the attribute's own buffer semantic. Attributes unknown
        /// to the canonical table are skipped, as is any duplicate resolution to an already-taken
        /// location (shared slots in the table make that possible in principle).</summary>
        private static AttributeBinding[] ResolveAttributeBindings(VertexDrawBuffer[] vertexBuffers, Material.VsInputSignature inputSignature)
        {
            var bindings = new List<AttributeBinding>();
            var usedLocations = 0;
            var bindingIndex = 0;

            foreach (var curVertexBuffer in vertexBuffers)
            {
                foreach (var attribute in curVertexBuffer.InputLayoutFields)
                {
                    var attributeLocation = -1;
                    var insgElemName = string.Empty;

                    if (inputSignature.Elements is { Length: > 0 })
                    {
                        var matchingName = Material.FindD3DInputSignatureElement(inputSignature, attribute.SemanticName, attribute.SemanticIndex).Name;
                        if (!string.IsNullOrEmpty(matchingName))
                        {
                            insgElemName = matchingName;
                            attributeLocation = VertexAttributeLocations.Get(insgElemName);
                        }
                    }

                    // Fall back to the buffer's own semantic if INSG does not exist or the name was unknown
                    if (attributeLocation == -1)
                    {
                        attributeLocation = VertexAttributeLocations.Get(attribute.SemanticName, attribute.SemanticIndex);
                    }

                    if (attributeLocation == -1 || (usedLocations & (1 << attributeLocation)) != 0)
                    {
#if DEBUG
                        if (attributeLocation == -1 && !string.IsNullOrEmpty(insgElemName))
                        {
                            RendererContext.Logger.LogDebug("Attribute {SemanticName} ({SemanticIndex}) has no canonical location (insg: {InsgElemName})", attribute.SemanticName, attribute.SemanticIndex, insgElemName);
                        }
#endif
                        continue;
                    }

                    usedLocations |= 1 << attributeLocation;
                    bindings.Add(new AttributeBinding(attributeLocation, attribute.Format, (int)attribute.Offset, bindingIndex));
                }

                bindingIndex++;
            }

            return [.. bindings];
        }

        /// <summary>Builds a new VAO for the given buffers and resolved attribute bindings without caching it.</summary>
        /// <param name="vertexBuffers">Vertex buffer bindings for the draw call.</param>
        /// <param name="attributes">Attribute bindings resolved by <see cref="ResolveAttributeBindings"/>.</param>
        /// <param name="idxIndex">OpenGL handle of the index buffer.</param>
        /// <param name="debugLabel">Optional label applied to the VAO in debug builds.</param>
        /// <returns>The OpenGL VAO handle.</returns>
        private static int CreateVertexArrayObject(VertexDrawBuffer[] vertexBuffers, AttributeBinding[] attributes, int idxIndex, string? debugLabel = null)
        {
            GL.CreateVertexArrays(1, out int newVaoHandle);

            // Check for non-indexed geometry
            if (idxIndex != 0)
            {
                GL.VertexArrayElementBuffer(newVaoHandle, idxIndex);
            }

            // Workaround a bug in Intel drivers when mixing float and integer attributes
            // See https://gist.github.com/stefalie/e17a20a88a0fdbd97110611569a6605f for reference
            // We are using DSA apis, so we don't actually need to bind the VAO
            GL.BindVertexArray(newVaoHandle);

            for (var bindingIndex = 0; bindingIndex < vertexBuffers.Length; bindingIndex++)
            {
                var curVertexBuffer = vertexBuffers[bindingIndex];
                GL.VertexArrayVertexBuffer(newVaoHandle, bindingIndex, curVertexBuffer.Handle, 0, (int)curVertexBuffer.ElementSizeInBytes);
            }

            foreach (var attribute in attributes)
            {
                BindVertexAttrib(newVaoHandle, attribute);
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
            // Shaders read white where a mesh has no COLOR stream, matching the engine default.
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

        private static void BindVertexAttrib(int vao, AttributeBinding binding)
        {
            var (attributeLocation, format, offset, bindingIndex) = binding;

            GL.EnableVertexArrayAttrib(vao, attributeLocation);
            GL.VertexArrayAttribBinding(vao, attributeLocation, bindingIndex);
            VertexFormat.SetVertexArrayAttribFormat(vao, attributeLocation, format, offset);
        }
    }
}
