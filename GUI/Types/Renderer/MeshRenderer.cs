using System;
using System.Collections.Generic;
using System.Linq;
using GUI.Utils;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Renderer
{
    internal class MeshRenderer : IMeshRenderer
    {
        public Mesh Mesh { get; }

        private readonly VrfGuiContext guiContext;

        private List<DrawCall> drawCalls = new List<DrawCall>();
        private string prevMaterial = string.Empty;

        public MeshRenderer(Mesh mesh, VrfGuiContext vrfGuiContext)
        {
            Mesh = mesh;
            guiContext = vrfGuiContext;

            SetupDrawCalls();
        }

        public IEnumerable<string> GetSupportedRenderModes()
            => drawCalls.SelectMany(drawCall => drawCall.Shader.RenderModes).Distinct();

        public void SetRenderMode(string renderMode)
        {
            foreach (var call in drawCalls)
            {
                if (renderMode == null || call.Shader.RenderModes.Contains(renderMode))
                {
                    // Recycle old shader parameters that are not render modes since we are scrapping those anyway
                    call.Shader.Parameters = call.Shader.Parameters
                        .Where(kvp => !kvp.Key.StartsWith("renderMode"))
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                    if (renderMode != null)
                    {
                        call.Shader.Parameters.Add($"renderMode_{renderMode}", true);
                    }

                    call.Shader = ShaderLoader.LoadShader(call.Shader.Name, call.Shader.Parameters);
                    prevMaterial = string.Empty; // Reset previous material to force reloading textures
                }
            }
        }

        public void Update(float frameTime)
        {
            // Nothing to do here
        }

        public void Render(Camera camera)
        {
            foreach (var call in drawCalls)
            {
                int uniformLocation;

                GL.UseProgram(call.Shader.Program);

                uniformLocation = call.Shader.GetUniformLocation("vLightPosition");
                GL.Uniform3(uniformLocation, camera.Location);

                uniformLocation = call.Shader.GetUniformLocation("vEyePosition");
                GL.Uniform3(uniformLocation, camera.Location);

                uniformLocation = call.Shader.GetUniformLocation("projection");
                var matrix = camera.ProjectionMatrix;
                GL.UniformMatrix4(uniformLocation, false, ref matrix);

                uniformLocation = call.Shader.GetUniformLocation("modelview");
                matrix = camera.CameraViewMatrix;
                GL.UniformMatrix4(uniformLocation, false, ref matrix);

                uniformLocation = call.Shader.GetUniformLocation("bAnimated");
                if (uniformLocation != -1)
                {
                    //GL.Uniform1(uniformLocation, Animations.Count == 0 ? 0.0f : 1.0f);
                    GL.Uniform1(uniformLocation, 0.0f);
                }

                //Push animation texture to the shader (if it supports it)
                /*if (Animations.Count > 0)
                {
                    uniformLocation = call.Shader.GetUniformLocation("animationTexture");
                    if (uniformLocation != -1)
                    {
                        GL.ActiveTexture(TextureUnit.Texture0);
                        GL.BindTexture(TextureTarget.Texture2D, AnimationTexture);
                        GL.Uniform1(uniformLocation, 0);
                    }

                    uniformLocation = call.Shader.GetUniformLocation("fNumBones");
                    if (uniformLocation != -1)
                    {
                        var v = (float)Math.Max(1, Skeleton.Bones.Length - 1);
                        GL.Uniform1(uniformLocation, v);
                    }
                }*/

                //var transform = obj.Transform;
                var transform = Matrix4.Identity;
                uniformLocation = call.Shader.GetUniformLocation("transform");
                GL.UniformMatrix4(uniformLocation, false, ref transform);

                uniformLocation = call.Shader.GetUniformLocation("m_vTintColorSceneObject");
                if (uniformLocation > -1)
                {
                    GL.Uniform4(uniformLocation, Vector4.One);
                }

                GL.BindVertexArray(call.VertexArrayObject);

                uniformLocation = call.Shader.GetUniformLocation("m_vTintColorDrawCall");
                if (uniformLocation > -1)
                {
                    GL.Uniform3(uniformLocation, call.TintColor);
                }

                if (call.Material.Parameters.Name != prevMaterial)
                {
                    prevMaterial = call.Material.Parameters.Name;

                    //Start at 1, texture unit 0 is reserved for the animation texture
                    var textureUnit = 1;
                    foreach (var texture in call.Material.Textures)
                    {
                        uniformLocation = call.Shader.GetUniformLocation(texture.Key);

                        if (uniformLocation > -1)
                        {
                            GL.ActiveTexture(TextureUnit.Texture0 + textureUnit);
                            GL.BindTexture(TextureTarget.Texture2D, texture.Value);
                            GL.Uniform1(uniformLocation, textureUnit);

                            textureUnit++;
                        }
                    }

                    foreach (var param in call.Material.Parameters.FloatParams)
                    {
                        uniformLocation = call.Shader.GetUniformLocation(param.Key);

                        if (uniformLocation > -1)
                        {
                            GL.Uniform1(uniformLocation, param.Value);
                        }
                    }

                    foreach (var param in call.Material.Parameters.VectorParams)
                    {
                        uniformLocation = call.Shader.GetUniformLocation(param.Key);

                        if (uniformLocation > -1)
                        {
                            GL.Uniform4(uniformLocation, new Vector4(param.Value.X, param.Value.Y, param.Value.Z, param.Value.W));
                        }
                    }

                    var alpha = 0f;
                    if (call.Material.Parameters.IntParams.ContainsKey("F_ALPHA_TEST") &&
                        call.Material.Parameters.IntParams["F_ALPHA_TEST"] == 1 &&
                        call.Material.Parameters.FloatParams.ContainsKey("g_flAlphaTestReference"))
                    {
                        alpha = call.Material.Parameters.FloatParams["g_flAlphaTestReference"];
                    }

                    var alphaReference = call.Shader.GetUniformLocation("g_flAlphaTestReference");
                    GL.Uniform1(alphaReference, alpha);

                    /*
                    if (call.Material.IntParams.ContainsKey("F_TRANSLUCENT") && call.Material.IntParams["F_TRANSLUCENT"] == 1)
                    {
                        GL.Enable(EnableCap.Blend);
                        GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
                    }
                    else
                    {
                        GL.Disable(EnableCap.Blend);
                    }
                    */
                }

                GL.DrawElements(call.PrimitiveType, call.IndexCount, call.IndiceType, (IntPtr)call.StartIndex);
            }
        }

        private void SetupDrawCalls()
        {
            var vbib = Mesh.VBIB;
            var data = (BinaryKV3)Mesh.Data;

            var vertexBuffers = new uint[vbib.VertexBuffers.Count];
            var indexBuffers = new uint[vbib.IndexBuffers.Count];

            GL.GenBuffers(vbib.VertexBuffers.Count, vertexBuffers);
            GL.GenBuffers(vbib.IndexBuffers.Count, indexBuffers);

            for (var i = 0; i < vbib.VertexBuffers.Count; i++)
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffers[i]);
                GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(vbib.VertexBuffers[i].Count * vbib.VertexBuffers[i].Size), vbib.VertexBuffers[i].Buffer, BufferUsageHint.StaticDraw);

                GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferSize, out int _);
            }

            for (var i = 0; i < vbib.IndexBuffers.Count; i++)
            {
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, indexBuffers[i]);
                GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(vbib.IndexBuffers[i].Count * vbib.IndexBuffers[i].Size), vbib.IndexBuffers[i].Buffer, BufferUsageHint.StaticDraw);

                GL.GetBufferParameter(BufferTarget.ElementArrayBuffer, BufferParameterName.BufferSize, out int _);
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

                    /*if (i < SkinMaterials.Count)
                    {
                        materialName = SkinMaterials[i];
                    }*/

                    var material = guiContext.MaterialLoader.GetMaterial(materialName);

                    var shaderArguments = new Dictionary<string, bool>();
                    if (d.Properties.TryGetValue("m_bUseCompressedNormalTangent", out var compressedNormalTangent))
                    {
                        shaderArguments.Add("fulltangent", !(bool)compressedNormalTangent.Value);
                    }

                    // TODO: Don't pass around so much shit
                    var drawCall = CreateDrawCall(d.Properties, vertexBuffers, indexBuffers, shaderArguments, vbib, material);
                    drawCalls.Add(drawCall);
                }
            }

            drawCalls = drawCalls.OrderBy(x => x.Material.Parameters.Name).ToList();
        }

        private DrawCall CreateDrawCall(Dictionary<string, KVValue> drawProperties, uint[] vertexBuffers, uint[] indexBuffers, IDictionary<string, bool> shaderArguments, VBIB block, Material material)
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
            drawCall.Shader = ShaderLoader.LoadShader(drawCall.Material.Parameters.ShaderName, shaderArguments);

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

                if (!drawCall.Material.Textures.ContainsKey("g_tTintMask"))
                {
                    drawCall.Material.Textures.Add("g_tTintMask", MaterialLoader.CreateSolidTexture(1f, 1f, 1f));
                }
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

            GL.GenVertexArrays(1, out uint vertexArrayObject);
            drawCall.VertexArrayObject = vertexArrayObject;

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
