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

        /// <summary>Identifies a VAO by what it actually is: a shader's attribute locations bound to a
        /// specific set of GPU buffer objects. Not tied to any higher-level resource name, so buffers that
        /// happen to share a mesh name (or none at all) still dedupe correctly, and are never confused with
        /// buffers that happen to reuse a freed handle under an unrelated name.</summary>
        private readonly struct VAOKey : IEquatable<VAOKey>
        {
            public required int Shader { get; init; }
            public required int IndexBuffer { get; init; }
            public required int[] VertexBuffers { get; init; }

            public bool Equals(VAOKey other)
                => Shader == other.Shader
                && IndexBuffer == other.IndexBuffer
                && VertexBuffers.AsSpan().SequenceEqual(other.VertexBuffers);

            public override bool Equals(object? obj) => obj is VAOKey other && Equals(other);

            public override int GetHashCode()
            {
                var hash = new HashCode();
                hash.Add(Shader);
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

        /// <summary>Returns a cached VAO for the given shader/buffer combination, creating it if necessary.
        /// The cache key is the shader's attribute locations plus the actual GPU buffer handles bound to
        /// it - what a VAO fundamentally is - so callers never need to invent a unique name to keep unrelated
        /// geometry from colliding, and identical buffer/shader combinations dedupe automatically regardless
        /// of which mesh (if any) they came from.</summary>
        /// <param name="vertexBuffers">Vertex buffer bindings for the draw call.</param>
        /// <param name="shader">Shader whose attribute locations the VAO is built against.</param>
        /// <param name="inputSignature">Material input signature mapping buffer semantics to shader attribute names.</param>
        /// <param name="idxIndex">OpenGL handle of the index buffer, or 0 for non-indexed geometry.</param>
        /// <param name="debugLabel">Optional label applied to the VAO in debug builds when newly created.</param>
        /// <returns>The OpenGL VAO handle.</returns>
        public int GetVertexArrayObject(VertexDrawBuffer[] vertexBuffers, Shader shader, Material.VsInputSignature inputSignature, int idxIndex, string? debugLabel = null)
        {
            var vaoKey = new VAOKey
            {
                Shader = shader.Program,
                IndexBuffer = idxIndex,
                VertexBuffers = Array.ConvertAll(vertexBuffers, vb => vb.Handle),
            };

            if (vertexArrayObjects.TryGetValue(vaoKey, out var vaoHandle))
            {
                return vaoHandle;
            }

            var newVaoHandle = CreateVertexArrayObject(vertexBuffers, shader, inputSignature, idxIndex, debugLabel);
            vertexArrayObjects.Add(vaoKey, newVaoHandle);
            return newVaoHandle;
        }

        /// <summary>Builds a new VAO for the given shader/buffer combination without caching it.</summary>
        /// <param name="vertexBuffers">Vertex buffer bindings for the draw call.</param>
        /// <param name="shader">Shader whose attribute locations the VAO is built against.</param>
        /// <param name="inputSignature">Material input signature mapping buffer semantics to shader attribute names.</param>
        /// <param name="idxIndex">OpenGL handle of the index buffer.</param>
        /// <param name="debugLabel">Optional label applied to the VAO in debug builds.</param>
        /// <returns>The OpenGL VAO handle.</returns>
        private int CreateVertexArrayObject(VertexDrawBuffer[] vertexBuffers, Shader shader, Material.VsInputSignature inputSignature, int idxIndex, string? debugLabel = null)
        {
            Debug.Assert(vertexBuffers != null && vertexBuffers.Length > 0);

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

            var bindingIndex = 0;
            vertexBuffers = AddMissingAttributes(vertexBuffers, shader);

            foreach (var curVertexBuffer in vertexBuffers)
            {
                GL.VertexArrayVertexBuffer(newVaoHandle, bindingIndex, curVertexBuffer.Handle, 0, (int)curVertexBuffer.ElementSizeInBytes);

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
                            attributeLocation = shader.Attributes.GetValueOrDefault(insgElemName switch
                            {
                                "vLightmapUVW" => "vLightmapUV",
                                _ => insgElemName,
                            }, -1);
                        }
                    }

                    // Fallback to guessing basic attribute name if INSG does not exist or attribute was not found
                    if (attributeLocation == -1)
                    {
                        var attributeName = "v" + attribute.SemanticName;
                        if (attribute.SemanticIndex > 0 && attribute.SemanticName
                            is "TEXCOORD"
                            or "COLOR"
                            or "BLENDINDICES"
                            or "BLENDWEIGHT")
                        {
                            attributeName += attribute.SemanticIndex;
                        }

                        attributeLocation = shader.Attributes.GetValueOrDefault(attributeName, -1);
                    }

                    // Ignore this attribute if it is not found in the shader
                    if (attributeLocation == -1)
                    {
#if DEBUG
                        RendererContext.Logger.LogDebug("Attribute {SemanticName} ({SemanticIndex}) could not be bound in shader {ShaderName} (insg: {InsgElemName})", attribute.SemanticName, attribute.SemanticIndex, shader.Name, insgElemName);
#endif
                        continue;
                    }

                    BindVertexAttrib(newVaoHandle, attribute, attributeLocation, (int)attribute.Offset, bindingIndex);
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

        private VertexDrawBuffer[] AddMissingAttributes(VertexDrawBuffer[] vertexBuffers, Shader shader)
        {
            if (shader.Attributes.TryGetValue("vCOLOR", out var colorAttributeLocation)
                        && !vertexBuffers.Any(vb => vb.InputLayoutFields.Any(f => f.SemanticName == "COLOR")))
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

        private static void BindVertexAttrib(int vao, VBIB.RenderInputLayoutField attribute, int attributeLocation, int offset, int bindingIndex)
        {
            GL.EnableVertexArrayAttrib(vao, attributeLocation);
            GL.VertexArrayAttribBinding(vao, attributeLocation, bindingIndex);

            switch (attribute.Format)
            {
                case DXGI_FORMAT.R32G32B32_FLOAT:
                    GL.VertexArrayAttribFormat(vao, attributeLocation, 3, VertexAttribType.Float, false, offset);
                    break;

                case DXGI_FORMAT.R8G8B8A8_UNORM:
                    GL.VertexArrayAttribFormat(vao, attributeLocation, 4, VertexAttribType.UnsignedByte, true, offset);
                    break;

                case DXGI_FORMAT.R32_FLOAT:
                    GL.VertexArrayAttribFormat(vao, attributeLocation, 1, VertexAttribType.Float, false, offset);
                    break;

                case DXGI_FORMAT.R32G32_FLOAT:
                    GL.VertexArrayAttribFormat(vao, attributeLocation, 2, VertexAttribType.Float, false, offset);
                    break;

                case DXGI_FORMAT.R16G16_FLOAT:
                    GL.VertexArrayAttribFormat(vao, attributeLocation, 2, VertexAttribType.HalfFloat, false, offset);
                    break;

                case DXGI_FORMAT.R32G32B32A32_FLOAT:
                    GL.VertexArrayAttribFormat(vao, attributeLocation, 4, VertexAttribType.Float, false, offset);
                    break;

                case DXGI_FORMAT.R32G32B32A32_SINT:
                    GL.VertexArrayAttribIFormat(vao, attributeLocation, 4, VertexAttribType.Int, offset);
                    break;

                case DXGI_FORMAT.R8G8B8A8_UINT:
                    GL.VertexArrayAttribIFormat(vao, attributeLocation, 4, VertexAttribType.UnsignedByte, offset);
                    break;

                case DXGI_FORMAT.R16G16_SINT:
                    GL.VertexArrayAttribIFormat(vao, attributeLocation, 2, VertexAttribType.Short, offset);
                    break;

                case DXGI_FORMAT.R16G16B16A16_SINT:
                    GL.VertexArrayAttribIFormat(vao, attributeLocation, 4, VertexAttribType.Short, offset);
                    break;

                case DXGI_FORMAT.R16G16B16A16_UINT:
                    GL.VertexArrayAttribIFormat(vao, attributeLocation, 4, VertexAttribType.UnsignedShort, offset);
                    break;

                case DXGI_FORMAT.R16G16B16A16_UNORM:
                    GL.VertexArrayAttribFormat(vao, attributeLocation, 4, VertexAttribType.UnsignedShort, true, offset);
                    break;

                case DXGI_FORMAT.R16G16B16A16_FLOAT:
                    GL.VertexArrayAttribFormat(vao, attributeLocation, 4, VertexAttribType.HalfFloat, false, offset);
                    break;

                case DXGI_FORMAT.R16G16_SNORM:
                    GL.VertexArrayAttribFormat(vao, attributeLocation, 2, VertexAttribType.Short, true, offset);
                    break;

                case DXGI_FORMAT.R16G16_UNORM:
                    GL.VertexArrayAttribFormat(vao, attributeLocation, 2, VertexAttribType.UnsignedShort, true, offset);
                    break;

                case DXGI_FORMAT.R32_UINT:
                    GL.VertexArrayAttribIFormat(vao, attributeLocation, 1, VertexAttribType.UnsignedInt, offset);
                    break;

                // :VertexAttributeFormat - When adding new attribute here, also implement it in the VBIB code
                default:
                    throw new NotImplementedException($"Unknown vertex attribute format {attribute.Format} ({attribute.SemanticName})");
            }
        }
    }
}
