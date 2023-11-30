using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Utils;

namespace GUI.Types.Renderer
{
    class RenderableMesh
    {
        public AABB BoundingBox { get; }
        public Vector4 Tint { get; set; } = Vector4.One;

        private readonly VrfGuiContext guiContext;
        public List<DrawCall> DrawCallsOpaque { get; } = [];
        public List<DrawCall> DrawCallsOverlay { get; } = new List<DrawCall>(1);
        public List<DrawCall> DrawCallsBlended { get; } = [];
        private IEnumerable<DrawCall> DrawCalls => DrawCallsOpaque.Concat(DrawCallsOverlay).Concat(DrawCallsBlended);

        public int? AnimationTexture { get; private set; }
        public int AnimationTextureSize { get; private set; }

        public int MeshIndex { get; }

        public FlexStateManager FlexStateManager { get; }

        private readonly int VBIBHashCode;

        public RenderableMesh(Mesh mesh, int meshIndex, Scene scene, Model model = null,
            Dictionary<string, string> initialMaterialTable = null, Morph morph = null)
        {
            guiContext = scene.GuiContext;

            var vbib = mesh.VBIB;
            if (model != null)
            {
                vbib = model.RemapBoneIndices(vbib, meshIndex);
            }
            VBIBHashCode = vbib.GetHashCode();

            mesh.GetBounds();
            BoundingBox = new AABB(mesh.MinBounds, mesh.MaxBounds);
            MeshIndex = meshIndex;

            var meshSceneObjects = mesh.Data.GetArray("m_sceneObjects");
            ConfigureDrawCalls(scene, vbib, meshSceneObjects, initialMaterialTable);

            if (morph != null)
            {
                FlexStateManager = new FlexStateManager(guiContext, morph);
            }
        }

        public IEnumerable<string> GetSupportedRenderModes()
            => DrawCalls
                .SelectMany(drawCall => drawCall.Material.Shader.RenderModes)
                .Distinct();

        public void SetRenderMode(string renderMode)
        {
            foreach (var call in DrawCalls)
            {
                // Recycle old shader parameters that are not render modes since we are scrapping those anyway
                var parameters = call.Material.Shader.Parameters
                    .Where(kvp => !kvp.Key.StartsWith("renderMode", StringComparison.InvariantCulture))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                if (renderMode != null && call.Material.Shader.RenderModes.Contains(renderMode))
                {
                    parameters.Add($"renderMode_{renderMode}", 1);
                }

                call.Material.Shader = guiContext.ShaderLoader.LoadShader(call.Material.Shader.Name, parameters);
                UpdateVertexArrayObject(call);
            }
        }

        public void SetAnimationTexture(int? texture, int animationTextureSize)
        {
            AnimationTexture = texture;
            AnimationTextureSize = animationTextureSize;

            FlexStateManager?.ResetControllers();
        }

        public void ReplaceMaterials(Dictionary<string, string> materialTable)
        {
            foreach (var drawCall in DrawCalls)
            {
                var material = drawCall.Material;
                var materialData = material.Material;
                var materialName = materialData.Name;

                if (materialTable.TryGetValue(materialName, out var replacementName))
                {
                    // Recycle non-material-derived shader arguments
                    var staticParams = materialData.GetShaderArguments();
                    var dynamicParams = new Dictionary<string, byte>(material.Shader.Parameters.Except(staticParams));

                    drawCall.Material = guiContext.MaterialLoader.GetMaterial(replacementName, dynamicParams);
                    UpdateVertexArrayObject(drawCall);
                }
            }
        }

        public void SetMaterialForMaterialViewer(Resource resourceMaterial)
        {
            foreach (var drawCall in DrawCalls)
            {
                var material = drawCall.Material;
                var materialData = material.Material;

                // Recycle non-material-derived shader arguments
                var staticParams = materialData.GetShaderArguments();
                var dynamicParams = new Dictionary<string, byte>(material.Shader.Parameters.Except(staticParams));

                drawCall.Material = guiContext.MaterialLoader.LoadMaterial(resourceMaterial, dynamicParams);
                UpdateVertexArrayObject(drawCall);
            }
        }

        private void UpdateVertexArrayObject(DrawCall drawCall)
        {
            drawCall.VertexArrayObject = guiContext.MeshBufferCache.GetVertexArrayObject(
                   VBIBHashCode,
                   drawCall.VertexBuffer,
                   drawCall.Material,
                   drawCall.IndexBuffer.Id);
        }

        private void ConfigureDrawCalls(Scene scene, VBIB vbib, IKeyValueCollection[] sceneObjects, Dictionary<string, string> materialReplacementTable)
        {
            guiContext.MeshBufferCache.GetVertexIndexBuffers(VBIBHashCode, vbib);

            var vertexOffset = 0;
            foreach (var sceneObject in sceneObjects)
            {
                var i = 0;
                var objectDrawCalls = sceneObject.GetArray("m_drawCalls");
                var objectDrawBounds = sceneObject.ContainsKey("m_drawBounds")
                    ? sceneObject.GetArray("m_drawBounds")
                    : [];

                foreach (var objectDrawCall in objectDrawCalls)
                {
                    var materialName = objectDrawCall.GetProperty<string>("m_material") ?? objectDrawCall.GetProperty<string>("m_pMaterial");
                    if (materialReplacementTable?.TryGetValue(materialName, out var replacementName) is true)
                    {
                        materialName = replacementName;
                    }

                    var shaderArguments = new Dictionary<string, byte>(scene.RenderAttributes);

                    if (Mesh.IsCompressedNormalTangent(objectDrawCall))
                    {
                        var vertexBuffer = objectDrawCall.GetArray("m_vertexBuffers")[0]; // TODO: Not just 0
                        var vertexBufferId = vertexBuffer.GetInt32Property("m_hBuffer");
                        var inputLayout = vbib.VertexBuffers[vertexBufferId].InputLayoutFields.FirstOrDefault(static i => i.SemanticName == "NORMAL");

                        var version = inputLayout.Format switch
                        {
                            DXGI_FORMAT.R32_UINT => (byte)2, // Added in CS2 on 2023-08-03
                            _ => (byte)1,
                        };

                        shaderArguments.Add("D_COMPRESSED_NORMALS_AND_TANGENTS", version);
                    }

                    if (Mesh.HasBakedLightingFromLightMap(objectDrawCall) && scene.LightingInfo.HasValidLightmaps)
                    {
                        shaderArguments.Add("D_BAKED_LIGHTING_FROM_LIGHTMAP", 1);
                    }
                    else if (Mesh.HasBakedLightingFromVertexStream(objectDrawCall))
                    {
                        shaderArguments.Add("D_BAKED_LIGHTING_FROM_VERTEX_STREAM", 1);
                    }

                    var material = guiContext.MaterialLoader.GetMaterial(materialName, shaderArguments);

                    // TODO: Don't pass around so much shit
                    var drawCall = CreateDrawCall(objectDrawCall, material, vbib);
                    if (i < objectDrawBounds.Length)
                    {
                        drawCall.DrawBounds = new AABB(
                            objectDrawBounds[i].GetSubCollection("m_vMinBounds").ToVector3(),
                            objectDrawBounds[i].GetSubCollection("m_vMaxBounds").ToVector3()
                        );
                    }

                    if (drawCall.Material.IsOverlay)
                    {
                        DrawCallsOverlay.Add(drawCall);
                    }
                    else if (drawCall.Material.IsTranslucent)
                    {
                        DrawCallsBlended.Add(drawCall);
                    }
                    else
                    {
                        DrawCallsOpaque.Add(drawCall);
                    }

                    drawCall.VertexIdOffset = vertexOffset;
                    vertexOffset += objectDrawCall.GetInt32Property("m_nVertexCount");

                    i++;
                }
            }

            DrawCallsOpaque.TrimExcess();
            DrawCallsOverlay.TrimExcess();
            DrawCallsBlended.TrimExcess();
        }

        private DrawCall CreateDrawCall(IKeyValueCollection objectDrawCall, RenderMaterial material, VBIB vbib)
        {
            var drawCall = new DrawCall()
            {
                Material = material,
            };

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

            // Index buffer
            {
                var indexBufferObject = objectDrawCall.GetSubCollection("m_indexBuffer");
                var indexBuffer = default(IndexDrawBuffer);
                indexBuffer.Id = indexBufferObject.GetUInt32Property("m_hBuffer");
                indexBuffer.Offset = indexBufferObject.GetUInt32Property("m_nBindOffsetBytes");
                drawCall.IndexBuffer = indexBuffer;

                var indexElementSize = vbib.IndexBuffers[(int)drawCall.IndexBuffer.Id].ElementSizeInBytes;
                drawCall.StartIndex = (nint)(objectDrawCall.GetUInt32Property("m_nStartIndex") * indexElementSize);
                drawCall.IndexCount = objectDrawCall.GetInt32Property("m_nIndexCount");

                drawCall.IndexType = indexElementSize switch
                {
                    2 => DrawElementsType.UnsignedShort,
                    4 => DrawElementsType.UnsignedInt,
                    _ => throw new UnexpectedMagicException("Unsupported index type", indexElementSize, nameof(indexElementSize)),
                };
            }

            // Vertex buffer
            {
                var vertexBufferObject = objectDrawCall.GetArray("m_vertexBuffers")[0]; // TODO: Not just 0
                var vertexBuffer = default(VertexDrawBuffer);
                vertexBuffer.Id = vertexBufferObject.GetUInt32Property("m_hBuffer");
                vertexBuffer.Offset = vertexBufferObject.GetUInt32Property("m_nBindOffsetBytes");

                var vertexBufferVbib = vbib.VertexBuffers[(int)vertexBuffer.Id];
                vertexBuffer.ElementSizeInBytes = vertexBufferVbib.ElementSizeInBytes;
                vertexBuffer.InputLayoutFields = vertexBufferVbib.InputLayoutFields;

                drawCall.VertexBuffer = vertexBuffer;

                drawCall.BaseVertex = objectDrawCall.GetInt32Property("m_nBaseVertex");
                //drawCall.VertexCount = objectDrawCall.GetUInt32Property("m_nVertexCount");
            }

            var tintAlpha = Vector4.One;

            if (objectDrawCall.ContainsKey("m_vTintColor"))
            {
                var tintColor = objectDrawCall.GetSubCollection("m_vTintColor").ToVector3();
                tintAlpha = new Vector4(tintColor, 1.0f);
            }

            if (objectDrawCall.ContainsKey("m_flAlpha"))
            {
                tintAlpha.W = objectDrawCall.GetFloatProperty("m_flAlpha");
            }

            drawCall.TintColor = tintAlpha;

            if (objectDrawCall.ContainsKey("m_nMeshID"))
            {
                drawCall.MeshId = objectDrawCall.GetInt32Property("m_nMeshID");
            }

            if (objectDrawCall.ContainsKey("m_nFirstMeshlet"))
            {
                drawCall.FirstMeshlet = objectDrawCall.GetInt32Property("m_nFirstMeshlet");
                drawCall.NumMeshlets = objectDrawCall.GetInt32Property("m_nNumMeshlets");
            }

            drawCall.VertexArrayObject = guiContext.MeshBufferCache.GetVertexArrayObject(
                VBIBHashCode,
                drawCall.VertexBuffer,
                drawCall.Material,
                drawCall.IndexBuffer.Id);

            return drawCall;
        }
    }

    internal interface IRenderableMeshCollection
    {
        List<RenderableMesh> RenderableMeshes { get; }
    }
}
