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
    internal class RenderableMesh
    {
        public AABB BoundingBox { get; }
        public Vector4 Tint { get; set; } = Vector4.One;

        private readonly VrfGuiContext guiContext;
        public List<DrawCall> DrawCallsOpaque { get; } = new List<DrawCall>();
        public List<DrawCall> DrawCallsBlended { get; } = new List<DrawCall>();
        public int? AnimationTexture { get; private set; }
        public int BoneCount { get; private set; }

        public float Time { get; private set; } = 0f;

        public RenderableMesh(Mesh mesh, VrfGuiContext guiContext, Dictionary<string, string> skinMaterials = null)
        {
            this.guiContext = guiContext;
            BoundingBox = new AABB(mesh.MinBounds, mesh.MaxBounds);

            SetupDrawCalls(mesh, skinMaterials);
        }

        public IEnumerable<string> GetSupportedRenderModes()
            => DrawCallsOpaque
                .SelectMany(drawCall => drawCall.Shader.RenderModes)
                .Union(DrawCallsBlended.SelectMany(drawCall => drawCall.Shader.RenderModes))
                .Distinct();

        public void SetRenderMode(string renderMode)
        {
            var drawCalls = DrawCallsOpaque.Union(DrawCallsBlended);

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
            AnimationTexture = texture;
            BoneCount = numBones;
        }

        public void Update(float timeStep)
        {
            Time += timeStep;
        }

        private void SetupDrawCalls(Mesh mesh, Dictionary<string, string> skinMaterials)
        {
            var vbib = mesh.VBIB;
            var data = mesh.GetData();
            var gpuMeshBuffers = guiContext.MeshBufferCache.GetOrCreateVBIB(vbib);

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
                    var drawCall = CreateDrawCall(objectDrawCall, vbib, gpuMeshBuffers, shaderArguments, material);

                    if (drawCall.Material.IsBlended)
                    {
                        DrawCallsBlended.Add(drawCall);
                    }
                    else
                    {
                        DrawCallsOpaque.Add(drawCall);
                    }
                }
            }

            //drawCalls = drawCalls.OrderBy(x => x.Material.Parameters.Name).ToList();
        }

        private DrawCall CreateDrawCall(IKeyValueCollection objectDrawCall, VBIB vbib, GPUMeshBuffers gpuMeshBuffers, IDictionary<string, bool> shaderArguments, RenderMaterial material)
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

            var indexElementSize = vbib.IndexBuffers[(int)drawCall.IndexBuffer.Id].Size;
            //drawCall.BaseVertex = Convert.ToUInt32(objectDrawCall.GetProperty<object>("m_nBaseVertex"));
            //drawCall.VertexCount = Convert.ToUInt32(objectDrawCall.GetProperty<object>("m_nVertexCount"));
            drawCall.StartIndex = Convert.ToUInt32(objectDrawCall.GetProperty<object>("m_nStartIndex")) * indexElementSize;
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

            if (indexElementSize == 2)
            {
                //shopkeeper_vr
                drawCall.IndexType = DrawElementsType.UnsignedShort;
            }
            else if (indexElementSize == 4)
            {
                //glados
                drawCall.IndexType = DrawElementsType.UnsignedInt;
            }
            else
            {
                throw new Exception("Unsupported index type");
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
            GL.BindBuffer(BufferTarget.ArrayBuffer, gpuMeshBuffers.VertexBuffers[drawCall.VertexBuffer.Id].Handle);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, gpuMeshBuffers.IndexBuffers[drawCall.IndexBuffer.Id].Handle);

            var curVertexBuffer = vbib.VertexBuffers[(int)drawCall.VertexBuffer.Id];
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

    internal interface IRenderableMeshCollection
    {
        IEnumerable<RenderableMesh> RenderableMeshes { get; }
    }
}
