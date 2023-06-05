using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    public class GPUMeshBufferCache
    {
        private readonly Dictionary<VBIB, GPUMeshBuffers> gpuBuffers = new();
        private readonly Dictionary<VAOKey, uint> vertexArrayObjects = new();

        private struct VAOKey
        {
            public GPUMeshBuffers VBIB;
            public Shader Shader;
            public uint VertexIndex;
            public uint IndexIndex;
            public uint BaseVertex;
        }

        public GPUMeshBufferCache()
        {
        }

        public GPUMeshBuffers GetVertexIndexBuffers(VBIB vbib)
        {
            if (gpuBuffers.TryGetValue(vbib, out var gpuVbib))
            {
                return gpuVbib;
            }
            else
            {
                var newGpuVbib = new GPUMeshBuffers(vbib);
                gpuBuffers.Add(vbib, newGpuVbib);
                return newGpuVbib;
            }
        }

        public uint GetVertexArrayObject(VBIB vbib, Shader shader, RenderMaterial material,
            uint vtxIndex, uint idxIndex, uint baseVertex)
        {
            var gpuVbib = GetVertexIndexBuffers(vbib);
            var vaoKey = new VAOKey
            {
                VBIB = gpuVbib,
                Shader = shader,
                VertexIndex = vtxIndex,
                IndexIndex = idxIndex,
                BaseVertex = baseVertex,
            };

            if (vertexArrayObjects.TryGetValue(vaoKey, out var vaoHandle))
            {
                return vaoHandle;
            }
            else
            {
                GL.GenVertexArrays(1, out uint newVaoHandle);

                GL.BindVertexArray(newVaoHandle);
                GL.BindBuffer(BufferTarget.ArrayBuffer, gpuVbib.VertexBuffers[vtxIndex].Handle);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, gpuVbib.IndexBuffers[idxIndex].Handle);

                var curVertexBuffer = vbib.VertexBuffers[(int)vtxIndex];
                var texCoordNum = 0;
                var colorNum = 0;
                foreach (var attribute in curVertexBuffer.InputLayoutFields)
                {
                    var attributeName = "v" + attribute.SemanticName;

                    if (attribute.SemanticName == "TEXCOORD" && texCoordNum++ > 0)
                    {
                        attributeName += texCoordNum;
                    }
                    else if (attribute.SemanticName == "COLOR" && colorNum++ > 0)
                    {
                        attributeName += colorNum;
                    }

                    var attributeLocation = GL.GetAttribLocation(shader.Program, attributeName);
                    if (attributeLocation == -1)
                    {
                        if (material.VsInputSignature == null)
                        {
                            continue;
                        }

                        foreach (var elem in material.VsInputSignature.GetArray<IKeyValueCollection>("m_elems"))
                        {
                            var d3dSemanticName = elem.GetProperty<string>("m_pD3DSemanticName");
                            var d3dSemanticIndex = elem.GetIntegerProperty("m_nD3DSemanticIndex");

                            if (d3dSemanticName == attribute.SemanticName && d3dSemanticIndex == attribute.SemanticIndex)
                            {
                                attributeLocation = GL.GetAttribLocation(shader.Program,
                                    elem.GetProperty<string>("m_pName"));
                                break;
                            }
                        }

                        // Ignore this attribute if it is not found in the shader
                        if (attributeLocation == -1)
                        {
                            continue;
                        }
                    }

                    BindVertexAttrib(attribute, attributeName, attributeLocation, (int)curVertexBuffer.ElementSizeInBytes, baseVertex);
                }

                GL.BindVertexArray(0);

                vertexArrayObjects.Add(vaoKey, newVaoHandle);
                return newVaoHandle;
            }
        }

        private static void BindVertexAttrib(VBIB.RenderInputLayoutField attribute, string attributeName,
            int attributeLocation, int stride, uint baseVertex)
        {
            GL.EnableVertexAttribArray(attributeLocation);

            switch (attribute.Format)
            {
                case DXGI_FORMAT.R32G32B32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 3, VertexAttribPointerType.Float, false, stride, (IntPtr)(baseVertex + attribute.Offset));
                    break;

                case DXGI_FORMAT.R8G8B8A8_UNORM:
                    GL.VertexAttribPointer(attributeLocation, 4, VertexAttribPointerType.UnsignedByte, false, stride, (IntPtr)(baseVertex + attribute.Offset));
                    break;

                case DXGI_FORMAT.R32G32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.Float, false, stride, (IntPtr)(baseVertex + attribute.Offset));
                    break;

                case DXGI_FORMAT.R16G16_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.HalfFloat, false, stride, (IntPtr)(baseVertex + attribute.Offset));
                    break;

                case DXGI_FORMAT.R32G32B32A32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 4, VertexAttribPointerType.Float, false, stride, (IntPtr)(baseVertex + attribute.Offset));
                    break;

                case DXGI_FORMAT.R8G8B8A8_UINT:
                    GL.VertexAttribPointer(attributeLocation, 4, VertexAttribPointerType.UnsignedByte, false, stride, (IntPtr)(baseVertex + attribute.Offset));
                    break;

                case DXGI_FORMAT.R16G16_SINT:
                    GL.VertexAttribIPointer(attributeLocation, 2, VertexAttribIntegerType.Short, stride, (IntPtr)(baseVertex + attribute.Offset));
                    break;

                case DXGI_FORMAT.R16G16B16A16_SINT:
                    GL.VertexAttribIPointer(attributeLocation, 4, VertexAttribIntegerType.Short, stride, (IntPtr)(baseVertex + attribute.Offset));
                    break;

                case DXGI_FORMAT.R16G16_SNORM:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.Short, true, stride, (IntPtr)(baseVertex + attribute.Offset));
                    break;

                case DXGI_FORMAT.R16G16_UNORM:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.UnsignedShort, true, stride, (IntPtr)(baseVertex + attribute.Offset));
                    break;

                default:
                    throw new NotImplementedException($"Unknown vertex attribute format {attribute.Format}");
            }
        }
    }
}
