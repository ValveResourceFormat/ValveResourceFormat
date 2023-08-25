using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    class GPUMeshBufferCache
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
                foreach (var attribute in curVertexBuffer.InputLayoutFields)
                {
                    var attributeName = "v" + attribute.SemanticName;
                    if (attribute.SemanticName is "TEXCOORD" or "COLOR" && attribute.SemanticIndex > 0)
                    {
                        attributeName += attribute.SemanticIndex;
                    }

                    var attributeLocation = GL.GetAttribLocation(shader.Program, attributeName);
                    if (attributeLocation == -1)
                    {
                        Material.InputSignatureElement elem = default;
                        if (material.VsInputSignature is not null)
                        {
                            elem = Material.FindD3DInputSignatureElement(material.VsInputSignature, attribute.SemanticName, attribute.SemanticIndex);
                        }

                        if (elem.Name is not null)
                        {
                            attributeLocation = GL.GetAttribLocation(shader.Program, elem.Name);
                        }

                        // Ignore this attribute if it is not found in the shader
                        if (attributeLocation == -1)
                        {
#if DEBUG
                            Console.WriteLine($"Attribute {attributeName} could not be bound in shader {shader.Name} (insg: {elem.Name})");
#endif
                            continue;
                        }
                    }

                    BindVertexAttrib(attribute, attributeLocation, (int)curVertexBuffer.ElementSizeInBytes, baseVertex);
                }

                GL.BindVertexArray(0);

                vertexArrayObjects.Add(vaoKey, newVaoHandle);
                return newVaoHandle;
            }
        }

        private static void BindVertexAttrib(VBIB.RenderInputLayoutField attribute, int attributeLocation, int stride, uint baseVertex)
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

                case DXGI_FORMAT.R32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 1, VertexAttribPointerType.Float, false, stride, (IntPtr)(baseVertex + attribute.Offset));
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

                case DXGI_FORMAT.R32_UINT:
                    GL.VertexAttribIPointer(attributeLocation, 1, VertexAttribIntegerType.UnsignedInt, stride, (IntPtr)(baseVertex + attribute.Offset));
                    break;

                default:
                    throw new NotImplementedException($"Unknown vertex attribute format {attribute.Format} ({attribute.SemanticName})");
            }
        }
    }
}
