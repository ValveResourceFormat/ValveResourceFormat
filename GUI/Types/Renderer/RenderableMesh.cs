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
using ValveResourceFormat.Utils;

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
        public int AnimationTextureSize { get; private set; }

        public int MeshIndex { get; }

        private readonly Mesh mesh;
        private readonly VBIB VBIB;
        private readonly List<DrawCall> DrawCallsAll = new();

        public RenderableMesh(Mesh mesh, int meshIndex, VrfGuiContext guiContext,
            Dictionary<string, string> skinMaterials = null, Model model = null)
        {
            this.guiContext = guiContext;
            this.mesh = mesh;
            VBIB = mesh.VBIB;
            if (model != null)
            {
                VBIB = model.RemapBoneIndices(VBIB, meshIndex);
            }
            mesh.GetBounds();
            BoundingBox = new AABB(mesh.MinBounds, mesh.MaxBounds);
            MeshIndex = meshIndex;

            ConfigureDrawCalls(skinMaterials, true);
        }

        public IEnumerable<string> GetSupportedRenderModes()
            => DrawCallsAll
                .SelectMany(drawCall => drawCall.Shader.RenderModes)
                .Distinct();

        public void SetRenderMode(string renderMode)
        {
            var drawCalls = DrawCallsAll;

            foreach (var call in drawCalls)
            {
                // Recycle old shader parameters that are not render modes since we are scrapping those anyway
                var parameters = call.Shader.Parameters
                    .Where(kvp => !kvp.Key.StartsWith("renderMode", StringComparison.InvariantCulture))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                if (renderMode != null && call.Shader.RenderModes.Contains(renderMode))
                {
                    parameters.Add($"renderMode_{renderMode}", 1);
                }

                call.Shader = guiContext.ShaderLoader.LoadShader(call.Shader.Name, parameters);
                call.VertexArrayObject = guiContext.MeshBufferCache.GetVertexArrayObject(
                    VBIB,
                    call.Shader,
                    call.Material,
                    call.VertexBuffer.Id,
                    call.IndexBuffer.Id,
                    call.BaseVertex);
            }
        }

        public void SetAnimationTexture(int? texture, int animationTextureSize)
        {
            AnimationTexture = texture;
            AnimationTextureSize = animationTextureSize;
        }

        public void SetSkin(Dictionary<string, string> skinMaterials)
        {
            ConfigureDrawCalls(skinMaterials, false);
        }

        private void ConfigureDrawCalls(Dictionary<string, string> skinMaterials, bool firstSetup)
        {
            var data = mesh.Data;
            var sceneObjects = data.GetArray("m_sceneObjects");

            if (firstSetup)
            {
                // This call has side effects because it uploads to gpu
                guiContext.MeshBufferCache.GetVertexIndexBuffers(VBIB);
            }

            foreach (var sceneObject in sceneObjects)
            {
                var i = 0;
                var objectDrawCalls = sceneObject.GetArray("m_drawCalls");
                var objectDrawBounds = sceneObject.ContainsKey("m_drawBounds")
                    ? sceneObject.GetArray("m_drawBounds")
                    : Array.Empty<IKeyValueCollection>();

                foreach (var objectDrawCall in objectDrawCalls)
                {
                    var materialName = objectDrawCall.GetProperty<string>("m_material") ?? objectDrawCall.GetProperty<string>("m_pMaterial");

                    if (skinMaterials != null && skinMaterials.ContainsKey(materialName))
                    {
                        materialName = skinMaterials[materialName];
                    }

                    var material = guiContext.MaterialLoader.GetMaterial(materialName);
                    var shaderArguments = new Dictionary<string, byte>(guiContext.RenderArgs);

                    if (Mesh.IsCompressedNormalTangent(objectDrawCall))
                    {
                        shaderArguments.Add("D_COMPRESSED_NORMALS_AND_TANGENTS", 1);
                    }

                    if (Mesh.HasBakedLightingFromLightMap(objectDrawCall)
                        && shaderArguments.TryGetValue("LightmapGameVersionNumber", out var version)
                        && version > 0)
                    {
                        shaderArguments.Add("D_BAKED_LIGHTING_FROM_LIGHTMAP", 1);
                    }
                    else if (Mesh.HasBakedLightingFromVertexStream(objectDrawCall))
                    {
                        shaderArguments.Add("D_BAKED_LIGHTING_FROM_VERTEX_STREAM", 1);
                    }

                    if (firstSetup)
                    {
                        // TODO: Don't pass around so much shit
                        var drawCall = CreateDrawCall(objectDrawCall, shaderArguments, material);
                        if (objectDrawBounds.Length > i)
                        {
                            drawCall.DrawBounds = new AABB(
                                objectDrawBounds[i].GetSubCollection("m_vMinBounds").ToVector3(),
                                objectDrawBounds[i].GetSubCollection("m_vMaxBounds").ToVector3()
                            );
                        }

                        DrawCallsAll.Add(drawCall);

                        if (drawCall.Material.IsBlended)
                        {
                            DrawCallsBlended.Add(drawCall);
                        }
                        else
                        {
                            DrawCallsOpaque.Add(drawCall);
                        }

                        i++;
                        continue;
                    }

                    SetupDrawCallMaterial(DrawCallsAll[i++], shaderArguments, material);
                }
            }
        }

        private DrawCall CreateDrawCall(IKeyValueCollection objectDrawCall, IDictionary<string, byte> shaderArguments, RenderMaterial material)
        {
            var drawCall = new DrawCall();
            var primitiveType = objectDrawCall.GetProperty<object>("m_nPrimitiveType");

            if (primitiveType is byte primitiveTypeByte)
            {
                if ((RenderPrimitiveType)primitiveTypeByte == RenderPrimitiveType.RENDER_PRIM_TRIANGLES)
                {
                    drawCall.PrimitiveType = PrimitiveType.Triangles;
                }
            }
            else if (primitiveType is string primitiveTypeString)
            {
                if (primitiveTypeString == "RENDER_PRIM_TRIANGLES")
                {
                    drawCall.PrimitiveType = PrimitiveType.Triangles;
                }
            }

            if (drawCall.PrimitiveType != PrimitiveType.Triangles)
            {
                throw new NotImplementedException("Unknown PrimitiveType in drawCall! (" + primitiveType + ")");
            }

            SetupDrawCallMaterial(drawCall, shaderArguments, material);

            var indexBufferObject = objectDrawCall.GetSubCollection("m_indexBuffer");

            var indexBuffer = default(DrawBuffer);
            indexBuffer.Id = indexBufferObject.GetUInt32Property("m_hBuffer");
            indexBuffer.Offset = indexBufferObject.GetUInt32Property("m_nBindOffsetBytes");
            drawCall.IndexBuffer = indexBuffer;

            var vertexElementSize = VBIB.VertexBuffers[(int)drawCall.VertexBuffer.Id].ElementSizeInBytes;
            drawCall.BaseVertex = objectDrawCall.GetUInt32Property("m_nBaseVertex") * vertexElementSize;
            //drawCall.VertexCount = objectDrawCall.GetUInt32Property("m_nVertexCount");

            var indexElementSize = VBIB.IndexBuffers[(int)drawCall.IndexBuffer.Id].ElementSizeInBytes;
            drawCall.StartIndex = objectDrawCall.GetUInt32Property("m_nStartIndex") * indexElementSize;
            drawCall.IndexCount = objectDrawCall.GetInt32Property("m_nIndexCount");

            if (objectDrawCall.ContainsKey("m_vTintColor"))
            {
                var tintColor = objectDrawCall.GetSubCollection("m_vTintColor").ToVector3();
                drawCall.TintColor = new OpenTK.Vector3(tintColor.X, tintColor.Y, tintColor.Z);
            }

            if (objectDrawCall.ContainsKey("m_nMeshID"))
            {
                drawCall.MeshId = objectDrawCall.GetInt32Property("m_nMeshID");
            }

            if (objectDrawCall.ContainsKey("m_nFirstMeshlet"))
            {
                drawCall.FirstMeshlet = objectDrawCall.GetInt32Property("m_nFirstMeshlet");
                drawCall.NumMeshlets = objectDrawCall.GetInt32Property("m_nNumMeshlets");
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
                throw new UnexpectedMagicException("Unsupported index type", indexElementSize, nameof(indexElementSize));
            }

            var m_vertexBuffer = objectDrawCall.GetArray("m_vertexBuffers")[0]; // TODO: Not just 0

            var vertexBuffer = default(DrawBuffer);
            vertexBuffer.Id = m_vertexBuffer.GetUInt32Property("m_hBuffer");
            vertexBuffer.Offset = m_vertexBuffer.GetUInt32Property("m_nBindOffsetBytes");
            drawCall.VertexBuffer = vertexBuffer;

            drawCall.VertexArrayObject = guiContext.MeshBufferCache.GetVertexArrayObject(
                VBIB,
                drawCall.Shader,
                drawCall.Material,
                drawCall.VertexBuffer.Id,
                drawCall.IndexBuffer.Id,
                drawCall.BaseVertex);

            return drawCall;
        }

        private void SetupDrawCallMaterial(DrawCall drawCall, IDictionary<string, byte> shaderArguments, RenderMaterial material)
        {
            drawCall.Material = material;
            // Add shader parameters from material to the shader parameters from the draw call
            var combinedShaderParameters = shaderArguments
                .Concat(material.Material.GetShaderArguments())
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            // Load shader
            drawCall.Shader = guiContext.ShaderLoader.LoadShader(drawCall.Material.Material.ShaderName, combinedShaderParameters);

            //Bind and validate shader
            GL.UseProgram(drawCall.Shader.Program);
        }
    }

    internal interface IRenderableMeshCollection
    {
        IEnumerable<RenderableMesh> RenderableMeshes { get; }
    }
}
