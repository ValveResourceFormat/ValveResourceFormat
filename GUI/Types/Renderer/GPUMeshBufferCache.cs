using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    class GPUMeshBufferCache
    {
        private readonly Dictionary<int, GPUMeshBuffers> gpuBuffers = [];
        private readonly Dictionary<VAOKey, uint> vertexArrayObjects = [];
        private QuadIndexBuffer quadIndices;


        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public QuadIndexBuffer QuadIndices
        {
            get
            {
                quadIndices ??= new QuadIndexBuffer(65532);

                return quadIndices;
            }
        }


        private struct VAOKey
        {
            public GPUMeshBuffers VBIB;
            public Shader Shader;
            public uint VertexIndex;
            public uint IndexIndex;
        }

        public GPUMeshBufferCache()
        {
        }

        public GPUMeshBuffers GetVertexIndexBuffers(int key, VBIB vbib)
        {
            if (gpuBuffers.TryGetValue(key, out var gpuVbib))
            {
                return gpuVbib;
            }
            else
            {
                ArgumentNullException.ThrowIfNull(vbib);

                var newGpuVbib = new GPUMeshBuffers(vbib);
                gpuBuffers.Add(key, newGpuVbib);
                return newGpuVbib;
            }
        }

        public uint GetVertexArrayObject(int key, VertexDrawBuffer curVertexBuffer, RenderMaterial material, uint idxIndex)
        {
            var gpuVbib = GetVertexIndexBuffers(key, null);
            var vaoKey = new VAOKey
            {
                VBIB = gpuVbib,
                Shader = material.Shader,
                VertexIndex = curVertexBuffer.Id,
                IndexIndex = idxIndex,
            };

            if (vertexArrayObjects.TryGetValue(vaoKey, out var vaoHandle))
            {
                return vaoHandle;
            }

            GL.GenVertexArrays(1, out uint newVaoHandle);

            GL.BindVertexArray(newVaoHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, gpuVbib.VertexBuffers[curVertexBuffer.Id].Handle);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, gpuVbib.IndexBuffers[idxIndex].Handle);

            foreach (var attribute in curVertexBuffer.InputLayoutFields)
            {
                var attributeLocation = -1;
                var insgElemName = string.Empty;

                if (material.VsInputSignature is not null)
                {
                    var elem = Material.FindD3DInputSignatureElement(material.VsInputSignature, attribute.SemanticName, attribute.SemanticIndex);

                    if (elem.Name is not null)
                    {
                        insgElemName = elem.Name;
                        attributeLocation = GL.GetAttribLocation(material.Shader.Program, insgElemName);
                    }
                }

                // Fallback to guessing basic attribute name if INSG does not exist or attribute was not found
                if (attributeLocation == -1)
                {
                    var attributeName = "v" + attribute.SemanticName;
                    if (attribute.SemanticName is "TEXCOORD" or "COLOR" && attribute.SemanticIndex > 0)
                    {
                        attributeName += attribute.SemanticIndex;
                    }

                    attributeLocation = GL.GetAttribLocation(material.Shader.Program, attributeName);
                }

                // Ignore this attribute if it is not found in the shader
                if (attributeLocation == -1)
                {
#if DEBUG
                    Utils.Log.Debug(nameof(GPUMeshBufferCache), $"Attribute {attribute.SemanticName} ({attribute.SemanticIndex}) could not be bound in shader {material.Shader.Name} (insg: {insgElemName})");
#endif
                    continue;
                }

                BindVertexAttrib(attribute, attributeLocation, (int)curVertexBuffer.ElementSizeInBytes, (IntPtr)attribute.Offset);
            }

            GL.BindVertexArray(0);

            vertexArrayObjects.Add(vaoKey, newVaoHandle);
            return newVaoHandle;
        }

        private static void BindVertexAttrib(VBIB.RenderInputLayoutField attribute, int attributeLocation, int stride, IntPtr offset)
        {
            GL.EnableVertexAttribArray(attributeLocation);

            switch (attribute.Format)
            {
                case DXGI_FORMAT.R32G32B32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 3, VertexAttribPointerType.Float, false, stride, offset);
                    break;

                case DXGI_FORMAT.R8G8B8A8_UNORM:
                    GL.VertexAttribPointer(attributeLocation, 4, VertexAttribPointerType.UnsignedByte, false, stride, offset);
                    break;

                case DXGI_FORMAT.R32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 1, VertexAttribPointerType.Float, false, stride, offset);
                    break;

                case DXGI_FORMAT.R32G32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.Float, false, stride, offset);
                    break;

                case DXGI_FORMAT.R16G16_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.HalfFloat, false, stride, offset);
                    break;

                case DXGI_FORMAT.R32G32B32A32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 4, VertexAttribPointerType.Float, false, stride, offset);
                    break;

                case DXGI_FORMAT.R8G8B8A8_UINT:
                    GL.VertexAttribPointer(attributeLocation, 4, VertexAttribPointerType.UnsignedByte, false, stride, offset);
                    break;

                case DXGI_FORMAT.R16G16_SINT:
                    GL.VertexAttribIPointer(attributeLocation, 2, VertexAttribIntegerType.Short, stride, offset);
                    break;

                case DXGI_FORMAT.R16G16B16A16_SINT:
                    GL.VertexAttribIPointer(attributeLocation, 4, VertexAttribIntegerType.Short, stride, offset);
                    break;

                case DXGI_FORMAT.R16G16_SNORM:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.Short, true, stride, offset);
                    break;

                case DXGI_FORMAT.R16G16_UNORM:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.UnsignedShort, true, stride, offset);
                    break;

                case DXGI_FORMAT.R32_UINT:
                    GL.VertexAttribIPointer(attributeLocation, 1, VertexAttribIntegerType.UnsignedInt, stride, offset);
                    break;

                default:
                    throw new NotImplementedException($"Unknown vertex attribute format {attribute.Format} ({attribute.SemanticName})");
            }
        }
    }
}
