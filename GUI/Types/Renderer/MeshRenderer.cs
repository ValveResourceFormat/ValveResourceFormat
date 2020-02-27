using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    internal class MeshRenderer : IMeshRenderer
    {
        private Mesh Mesh { get; }

        public Matrix4x4 Transform
        {
            get => meshTransform;
            set
            {
                meshTransform = value;
                BoundingBox = localBoundingBox.Transform(Transform);
            }
        }

        public Vector4 Tint { get; set; } = Vector4.One;
        public AABB BoundingBox { get; private set; }
        public long LayerIndex { get; set; } = -1;

        private readonly VrfGuiContext guiContext;
        private AABB localBoundingBox;
        private Matrix4x4 meshTransform = Matrix4x4.Identity;

        private List<DrawCall> drawCallsOpaque = new List<DrawCall>();
        private List<DrawCall> drawCallsBlended = new List<DrawCall>();

        private int? animationTexture;
        private int boneCount;

        public MeshRenderer(Mesh mesh, VrfGuiContext vrfGuiContext, Dictionary<string, string> skinMaterials = null)
        {
            Mesh = mesh;
            guiContext = vrfGuiContext;

            localBoundingBox = new AABB(mesh.MinBounds, mesh.MaxBounds);
            BoundingBox = localBoundingBox.Transform(Transform);

            SetupDrawCalls(skinMaterials);
        }

        public IEnumerable<string> GetSupportedRenderModes()
            => drawCallsOpaque
                .SelectMany(drawCall => drawCall.Shader.RenderModes)
                .Union(drawCallsBlended.SelectMany(drawCall => drawCall.Shader.RenderModes))
                .Distinct();

        public void SetRenderMode(string renderMode)
        {
            var drawCalls = drawCallsOpaque.Union(drawCallsBlended);

            foreach (var call in drawCalls)
            {
                // Recycle old shader parameters that are not render modes since we are scrapping those anyway
                var parameters = call.Shader.Parameters
                    .Where(kvp => !kvp.Key.StartsWith("renderMode"))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                if (renderMode != null && call.Shader.RenderModes.Contains(renderMode))
                {
                    parameters.Add($"renderMode_{renderMode}", true);
                }

                call.Shader = guiContext.ShaderLoader.LoadShader(call.Shader.Name, parameters);
            }
        }

        public void SetAnimationTexture(int? texture, int numBones)
        {
            animationTexture = texture;
            boneCount = numBones;
        }

        public void Update(float frameTime)
        {
            // Nothing to do here
        }

        public void Render(Camera camera, RenderPass renderPass)
        {
            switch (renderPass)
            {
                case RenderPass.Both:
                    Render(camera, true);
                    Render(camera, false);
                    break;
                case RenderPass.Opaque: Render(camera, true); break;
                case RenderPass.Translucent: Render(camera, false); break;
            }
        }

        private void Render(Camera camera, bool opaque)
        {
            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);

            var drawCalls = opaque ? drawCallsOpaque : drawCallsBlended;
            var viewProjectionMatrix = camera.ViewProjectionMatrix.ToOpenTK();

            foreach (var call in drawCalls)
            {
                int uniformLocation;

                GL.UseProgram(call.Shader.Program);

                uniformLocation = call.Shader.GetUniformLocation("vLightPosition");
                GL.Uniform3(uniformLocation, camera.Location.ToOpenTK());

                uniformLocation = call.Shader.GetUniformLocation("vEyePosition");
                GL.Uniform3(uniformLocation, camera.Location.ToOpenTK());

                uniformLocation = call.Shader.GetUniformLocation("uProjectionViewMatrix");
                GL.UniformMatrix4(uniformLocation, false, ref viewProjectionMatrix);

                uniformLocation = call.Shader.GetUniformLocation("bAnimated");
                if (uniformLocation != -1)
                {
                    GL.Uniform1(uniformLocation, animationTexture.HasValue ? 1.0f : 0.0f);
                }

                //Push animation texture to the shader (if it supports it)
                if (animationTexture.HasValue)
                {
                    uniformLocation = call.Shader.GetUniformLocation("animationTexture");
                    if (uniformLocation != -1)
                    {
                        GL.ActiveTexture(TextureUnit.Texture0);
                        GL.BindTexture(TextureTarget.Texture2D, animationTexture.Value);
                        GL.Uniform1(uniformLocation, 0);
                    }

                    uniformLocation = call.Shader.GetUniformLocation("fNumBones");
                    if (uniformLocation != -1)
                    {
                        var v = (float)Math.Max(1, boneCount - 1);
                        GL.Uniform1(uniformLocation, v);
                    }
                }

                var transform = Transform.ToOpenTK();
                uniformLocation = call.Shader.GetUniformLocation("transform");
                GL.UniformMatrix4(uniformLocation, false, ref transform);

                uniformLocation = call.Shader.GetUniformLocation("m_vTintColorSceneObject");
                if (uniformLocation > -1)
                {
                    var tint = Tint.ToOpenTK();
                    GL.Uniform4(uniformLocation, tint);
                }

                GL.BindVertexArray(call.VertexArrayObject);

                uniformLocation = call.Shader.GetUniformLocation("m_vTintColorDrawCall");
                if (uniformLocation > -1)
                {
                    GL.Uniform3(uniformLocation, call.TintColor);
                }

                call.Material.Render(call.Shader);

                GL.DrawElements(call.PrimitiveType, call.IndexCount, call.IndiceType, (IntPtr)call.StartIndex);

                call.Material.PostRender();
            }

            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);
        }

        private void SetupDrawCalls(Dictionary<string, string> skinMaterials)
        {
            var vbib = Mesh.VBIB;
            var data = Mesh.GetData();

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
            var sceneObjects = data.GetArray("m_sceneObjects");

            foreach (var sceneObject in sceneObjects)
            {
                var objectDrawCalls = sceneObject.GetArray("m_drawCalls");

                foreach (var objectDrawCall in objectDrawCalls)
                {
                    var materialName = objectDrawCall.GetProperty<string>("m_material");

                    if (skinMaterials != null && skinMaterials.ContainsKey(materialName))
                    {
                        materialName = skinMaterials[materialName];
                    }

                    var material = guiContext.MaterialLoader.GetMaterial(materialName);
                    var isOverlay = material.Material.IntParams.ContainsKey("F_OVERLAY");

                    // Ignore overlays for now
                    if (isOverlay)
                    {
                        continue;
                    }

                    var shaderArguments = new Dictionary<string, bool>();
                    if (objectDrawCall.ContainsKey("m_bUseCompressedNormalTangent"))
                    {
                        shaderArguments.Add("fulltangent", !objectDrawCall.GetProperty<bool>("m_bUseCompressedNormalTangent"));
                    }

                    if (objectDrawCall.ContainsKey("m_nFlags"))
                    {
                        var flags = objectDrawCall.GetProperty<object>("m_nFlags");

                        switch (flags)
                        {
                            case string flagsString:
                                if (flagsString.Contains("MESH_DRAW_FLAGS_USE_COMPRESSED_NORMAL_TANGENT"))
                                {
                                    shaderArguments.Add("fulltangent", false);
                                }

                                break;
                            case long flagsLong:
                                // TODO: enum
                                if ((flagsLong & 2) == 2)
                                {
                                    shaderArguments.Add("fulltangent", false);
                                }

                                break;
                        }
                    }

                    // TODO: Don't pass around so much shit
                    var drawCall = CreateDrawCall(objectDrawCall, vertexBuffers, indexBuffers, shaderArguments, vbib, material);

                    if (drawCall.Material.IsBlended)
                    {
                        drawCallsBlended.Add(drawCall);
                    }
                    else
                    {
                        drawCallsOpaque.Add(drawCall);
                    }
                }
            }

            //drawCalls = drawCalls.OrderBy(x => x.Material.Parameters.Name).ToList();
        }

        private DrawCall CreateDrawCall(IKeyValueCollection objectDrawCall, uint[] vertexBuffers, uint[] indexBuffers, IDictionary<string, bool> shaderArguments, VBIB block, RenderMaterial material)
        {
            var drawCall = new DrawCall();

            switch (objectDrawCall.GetProperty<string>("m_nPrimitiveType"))
            {
                case "RENDER_PRIM_TRIANGLES":
                    drawCall.PrimitiveType = PrimitiveType.Triangles;
                    break;
                default:
                    throw new Exception("Unknown PrimitiveType in drawCall! (" + objectDrawCall.GetProperty<string>("m_nPrimitiveType") + ")");
            }

            drawCall.Material = material;
            // Add shader parameters from material to the shader parameters from the draw call
            var combinedShaderParameters = shaderArguments
                .Concat(material.Material.GetShaderArguments())
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            // Load shader
            drawCall.Shader = guiContext.ShaderLoader.LoadShader(drawCall.Material.Material.ShaderName, combinedShaderParameters);

            //Bind and validate shader
            GL.UseProgram(drawCall.Shader.Program);

            var indexBufferObject = objectDrawCall.GetSubCollection("m_indexBuffer");

            var indexBuffer = default(DrawBuffer);
            indexBuffer.Id = Convert.ToUInt32(indexBufferObject.GetProperty<object>("m_hBuffer"));
            indexBuffer.Offset = Convert.ToUInt32(indexBufferObject.GetProperty<object>("m_nBindOffsetBytes"));
            drawCall.IndexBuffer = indexBuffer;

            var bufferSize = block.IndexBuffers[(int)drawCall.IndexBuffer.Id].Size;
            //drawCall.BaseVertex = Convert.ToUInt32(objectDrawCall.GetProperty<object>("m_nBaseVertex"));
            //drawCall.VertexCount = Convert.ToUInt32(objectDrawCall.GetProperty<object>("m_nVertexCount"));
            drawCall.StartIndex = Convert.ToUInt32(objectDrawCall.GetProperty<object>("m_nStartIndex")) * bufferSize;
            drawCall.IndexCount = Convert.ToInt32(objectDrawCall.GetProperty<object>("m_nIndexCount"));

            if (objectDrawCall.ContainsKey("m_vTintColor"))
            {
                var tintColor = objectDrawCall.GetSubCollection("m_vTintColor").ToVector3();
                drawCall.TintColor = new OpenTK.Vector3(tintColor.X, tintColor.Y, tintColor.Z);
            }

            if (!drawCall.Material.Textures.ContainsKey("g_tTintMask"))
            {
                drawCall.Material.Textures.Add("g_tTintMask", MaterialLoader.CreateSolidTexture(1f, 1f, 1f));
            }

            if (!drawCall.Material.Textures.ContainsKey("g_tNormal"))
            {
                drawCall.Material.Textures.Add("g_tNormal", MaterialLoader.CreateSolidTexture(0.5f, 1f, 0.5f));
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

            var m_vertexBuffers = objectDrawCall.GetSubCollection("m_vertexBuffers");
            var m_vertexBuffer = m_vertexBuffers.GetSubCollection("0"); // TODO: Not just 0

            var vertexBuffer = default(DrawBuffer);
            vertexBuffer.Id = Convert.ToUInt32(m_vertexBuffer.GetProperty<object>("m_hBuffer"));
            vertexBuffer.Offset = Convert.ToUInt32(m_vertexBuffer.GetProperty<object>("m_nBindOffsetBytes"));
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
