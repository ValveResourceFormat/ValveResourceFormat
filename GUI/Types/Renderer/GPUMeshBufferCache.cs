using System.Diagnostics;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    partial class GPUMeshBufferCache
    {
        private readonly Dictionary<ulong, GPUMeshBuffers> gpuBuffers = [];
        private readonly Dictionary<VAOKey, int> vertexArrayObjects = [];

        private struct VAOKey
        {
            public GPUMeshBuffers VBIB;
            public int Shader;
            public int VertexIndex;
            public int IndexIndex;
        }

        public GPUMeshBuffers CreateVertexIndexBuffers(ulong key, VBIB vbib)
        {
            if (!gpuBuffers.TryGetValue(key, out var gpuVbib))
            {
                gpuVbib = new GPUMeshBuffers(vbib);
                gpuBuffers.Add(key, gpuVbib);
            }

            return gpuVbib;
        }

        public int GetVertexArrayObject(ulong key, VertexDrawBuffer[] vertexBuffers, RenderMaterial material, int idxIndex)
        {
            Debug.Assert(vertexBuffers != null && vertexBuffers.Length > 0);

            var gpuVbib = gpuBuffers[key];
            var vaoKey = new VAOKey
            {
                VBIB = gpuVbib,
                Shader = material.Shader.Program,
                VertexIndex = vertexBuffers[0].Handle, // Probably good enough since every draw call will be creating new buffers
                IndexIndex = idxIndex,
            };

            if (vertexArrayObjects.TryGetValue(vaoKey, out var vaoHandle))
            {
                return vaoHandle;
            }

            GL.CreateVertexArrays(1, out int newVaoHandle);
            GL.VertexArrayElementBuffer(newVaoHandle, idxIndex);

            // Workaround a bug in Intel drivers when mixing float and integer attributes
            // See https://gist.github.com/stefalie/e17a20a88a0fdbd97110611569a6605f for reference
            // We are using DSA apis, so we don't actually need to bind the VAO
            GL.BindVertexArray(newVaoHandle);

            var bindingIndex = 0;
            vertexBuffers = AddMissingAttributes(vertexBuffers, material.Shader);

            foreach (var curVertexBuffer in vertexBuffers)
            {
                GL.VertexArrayVertexBuffer(newVaoHandle, bindingIndex, curVertexBuffer.Handle, 0, (int)curVertexBuffer.ElementSizeInBytes);

                foreach (var attribute in curVertexBuffer.InputLayoutFields)
                {
                    var attributeLocation = -1;
                    var insgElemName = string.Empty;

                    if (material.Material is { InputSignature.Elements.Length: > 0 })
                    {
                        var matchingName = Material.FindD3DInputSignatureElement(material.Material.InputSignature, attribute.SemanticName, attribute.SemanticIndex).Name;
                        if (!string.IsNullOrEmpty(matchingName))
                        {
                            insgElemName = matchingName;
                            attributeLocation = material.Shader.Attributes.GetValueOrDefault(insgElemName switch
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

                        attributeLocation = material.Shader.Attributes.GetValueOrDefault(attributeName, -1);
                    }

                    // Ignore this attribute if it is not found in the shader
                    if (attributeLocation == -1)
                    {
#if DEBUG
                        Utils.Log.Debug(nameof(GPUMeshBufferCache), $"Attribute {attribute.SemanticName} ({attribute.SemanticIndex}) could not be bound in shader {material.Shader.Name} (insg: {insgElemName})");
#endif
                        continue;
                    }

                    BindVertexAttrib(newVaoHandle, attribute, attributeLocation, (int)attribute.Offset, bindingIndex);
                }

                bindingIndex++;
            }

            GL.BindVertexArray(0);

            vertexArrayObjects.Add(vaoKey, newVaoHandle);
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
