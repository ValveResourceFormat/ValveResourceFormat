using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.KeyValues;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    internal class MeshObject
    {
        public Resource Resource { get; set; }
        public Matrix4 Transform { get; set; } = Matrix4.Identity;
        public Vector4 TintColor { get; set; } = Vector4.One;
        public List<DrawCall> DrawCalls { get; set; } = new List<DrawCall>();
        public List<string> SkinMaterials { get; set; } = new List<string>();

        /* Construct a mesh object from it's resource */
        public void LoadFromResource(MaterialLoader materialLoader)
        {
            if (Resource != null)
            {
                var block = Resource.VBIB;
                var data = (BinaryKV3)Resource.Blocks[BlockType.DATA];
                var modelArguments = (ArgumentDependencies)((ResourceEditInfo)Resource.Blocks[BlockType.REDI]).Structs[ResourceEditInfo.REDIStruct.ArgumentDependencies];

                var vertexBuffers = new uint[block.VertexBuffers.Count];
                var indexBuffers = new uint[block.IndexBuffers.Count];

                GL.GenBuffers(block.VertexBuffers.Count, vertexBuffers);
                GL.GenBuffers(block.IndexBuffers.Count, indexBuffers);

                for (var i = 0; i < block.VertexBuffers.Count; i++)
                {
                    GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffers[i]);
                    GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(block.VertexBuffers[i].Count * block.VertexBuffers[i].Size), block.VertexBuffers[i].Buffer, BufferUsageHint.StaticDraw);

                    var verticeBufferSize = 0;
                    GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferSize, out verticeBufferSize);
                }

                for (var i = 0; i < block.IndexBuffers.Count; i++)
                {
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, indexBuffers[i]);
                    GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(block.IndexBuffers[i].Count * block.IndexBuffers[i].Size), block.IndexBuffers[i].Buffer, BufferUsageHint.StaticDraw);

                    var indiceBufferSize = 0;
                    GL.GetBufferParameter(BufferTarget.ElementArrayBuffer, BufferParameterName.BufferSize, out indiceBufferSize);
                }

                //Prepare drawcalls
                var a = (KVObject)data.Data.Properties["m_sceneObjects"].Value;

                for (var b = 0; b < a.Properties.Count; b++)
                {
                    var c = (KVObject)((KVObject)a.Properties[b.ToString()].Value).Properties["m_drawCalls"].Value;

                    for (var i = 0; i < c.Properties.Count; i++)
                    {
                        var d = (KVObject)c.Properties[i.ToString()].Value;

                        var materialName = d.Properties["m_material"].Value.ToString();

                        if (SkinMaterials.Any())
                        {
                            materialName = SkinMaterials[i];
                        }

                        var material = materialLoader.GetMaterial(materialName);

                        // TODO: Don't pass around so much shit
                        var drawCall = CreateDrawCall(d.Properties, vertexBuffers, indexBuffers, modelArguments, Resource.VBIB, material);
                        DrawCalls.Add(drawCall);
                    }
                }

                DrawCalls = DrawCalls.OrderBy(x => x.Material.Name).ToList();

                // No longer need the resource, we extracted all data
                Resource = null;
            }
        }

        //Set up a draw call
        private DrawCall CreateDrawCall(Dictionary<string, KVValue> drawProperties, uint[] vertexBuffers, uint[] indexBuffers, ArgumentDependencies modelArguments, VBIB block, Material material)
        {
            var drawCall = new DrawCall();

            switch (drawProperties["m_nPrimitiveType"].Value.ToString())
            {
                case "RENDER_PRIM_TRIANGLES":
                    drawCall.PrimitiveType = PrimitiveType.Triangles;
                    break;
                default:
                    throw new Exception("Unknown PrimitiveType in drawCall! (" + drawProperties["m_nPrimitiveType"].Value + ")");
            }

            drawCall.Material = material;

            // Load shader
            drawCall.Shader = ShaderLoader.LoadShader(drawCall.Material.ShaderName, modelArguments);

            //Bind and validate shader
            GL.UseProgram(drawCall.Shader.Program);

            var f = (KVObject)drawProperties["m_indexBuffer"].Value;

            var indexBuffer = default(DrawBuffer);
            indexBuffer.Id = Convert.ToUInt32(f.Properties["m_hBuffer"].Value);
            indexBuffer.Offset = Convert.ToUInt32(f.Properties["m_nBindOffsetBytes"].Value);
            drawCall.IndexBuffer = indexBuffer;

            var bufferSize = block.IndexBuffers[(int)drawCall.IndexBuffer.Id].Size;
            drawCall.BaseVertex = Convert.ToUInt32(drawProperties["m_nBaseVertex"].Value);
            drawCall.VertexCount = Convert.ToUInt32(drawProperties["m_nVertexCount"].Value);
            drawCall.StartIndex = Convert.ToUInt32(drawProperties["m_nStartIndex"].Value) * bufferSize;
            drawCall.IndexCount = Convert.ToInt32(drawProperties["m_nIndexCount"].Value);

            if (drawProperties.ContainsKey("m_vTintColor"))
            {
                var tint = (KVObject)drawProperties["m_vTintColor"].Value;
                drawCall.TintColor = new Vector3(
                    Convert.ToSingle(tint.Properties["0"].Value),
                    Convert.ToSingle(tint.Properties["1"].Value),
                    Convert.ToSingle(tint.Properties["2"].Value));
            }

            if (bufferSize == 2)
            {
                //shopkeeper_vr
                drawCall.IndiceType = DrawElementsType.UnsignedShort;
            }
            else if (bufferSize == 4)
            {
                //glados
                drawCall.IndiceType = DrawElementsType.UnsignedInt;
            }
            else
            {
                throw new Exception("Unsupported indice type");
            }

            var g = (KVObject)drawProperties["m_vertexBuffers"].Value;
            var h = (KVObject)g.Properties["0"].Value; // TODO: Not just 0

            var vertexBuffer = default(DrawBuffer);
            vertexBuffer.Id = Convert.ToUInt32(h.Properties["m_hBuffer"].Value);
            vertexBuffer.Offset = Convert.ToUInt32(h.Properties["m_nBindOffsetBytes"].Value);
            drawCall.VertexBuffer = vertexBuffer;

            GL.GenVertexArrays(1, out drawCall.VertexArrayObject);

            GL.BindVertexArray(drawCall.VertexArrayObject);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffers[drawCall.VertexBuffer.Id]);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, indexBuffers[drawCall.IndexBuffer.Id]);

            var curVertexBuffer = block.VertexBuffers[(int)drawCall.VertexBuffer.Id];
            var texCoordNum = 0;
            foreach (var attribute in curVertexBuffer.Attributes)
            {
                var attributeName = "v" + attribute.Name;

                // TODO: other params too?
                if (attribute.Name == "TEXCOORD" && texCoordNum++ > 0)
                {
                    attributeName += texCoordNum;
                }

                BindVertexAttrib(attribute, attributeName, drawCall.Shader.Program, (int)curVertexBuffer.Size);
            }

            GL.BindVertexArray(0);

            return drawCall;
        }

        private void BindVertexAttrib(VBIB.VertexAttribute attribute, string attributeName, int shaderProgram, int stride)
        {
            var attributeLocation = GL.GetAttribLocation(shaderProgram, attributeName);

            //Ignore this attribute if it is not found in the shader
            if (attributeLocation == -1)
            {
                return;
            }

            GL.EnableVertexAttribArray(attributeLocation);

            switch (attribute.Type)
            {
                case DXGI_FORMAT.R32G32B32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 3, VertexAttribPointerType.Float, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R8G8B8A8_UNORM:
                    GL.VertexAttribPointer(attributeLocation, 4, VertexAttribPointerType.UnsignedByte, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R32G32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.Float, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R16G16_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.HalfFloat, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R32G32B32A32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 4, VertexAttribPointerType.Float, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R8G8B8A8_UINT:
                    GL.VertexAttribPointer(attributeLocation, 4, VertexAttribPointerType.UnsignedByte, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R16G16_SINT:
                    GL.VertexAttribIPointer(attributeLocation, 2, VertexAttribIntegerType.Short, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R16G16B16A16_SINT:
                    GL.VertexAttribIPointer(attributeLocation, 4, VertexAttribIntegerType.Short, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R16G16_UNORM:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.UnsignedShort, true, stride, (IntPtr)attribute.Offset);
                    break;

                default:
                    throw new Exception("Unknown attribute format " + attribute.Type);
            }
        }
    }
}
