using System.Diagnostics;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// GPU-ready mesh with draw calls, materials, and optional skeletal animation support.
    /// </summary>
    [DebuggerDisplay("{Name}")]
    public class RenderableMesh
    {
        public AABB BoundingBox { get; }
        public Vector4 Tint { get; set; } = Vector4.One;
        public float Alpha { get => Tint.W; set => Tint = Tint with { W = value }; }

        private readonly RendererContext renderContext;
        public List<DrawCall> DrawCallsOpaque { get; } = [];
        public List<DrawCall> DrawCallsOverlay { get; } = [];
        public List<DrawCall> DrawCallsBlended { get; } = [];
        public IEnumerable<DrawCall> DrawCalls => DrawCallsOpaque.Concat(DrawCallsOverlay).Concat(DrawCallsBlended);

        public StorageBuffer? BoneMatricesGpu { get; private set; }
        public int MeshBoneOffset { get; private set; }
        public int MeshBoneCount { get; private set; }
        public int BoneWeightCount { get; private set; }

        public string Name { get; }
        public int MeshIndex { get; }

        public FlexStateManager? FlexStateManager { get; }

        public RenderableMesh(Mesh mesh, int meshIndex, Scene scene, Model? model = null,
            Dictionary<string, string>? initialMaterialTable = null, Morph? morph = null, bool isAggregate = false)
        {
            renderContext = scene.RendererContext;

            Name = mesh.Name;

            var vbib = mesh.VBIB;

            if (model != null)
            {
                var remapTableStarts = model.Data.GetIntegerArray("m_remappingTableStarts");
                if (remapTableStarts.Length > meshIndex)
                {
                    MeshBoneOffset = (int)remapTableStarts[meshIndex];
                }

                var modelSpaceBoneIndices = model.GetRemapTable(meshIndex);
                if (modelSpaceBoneIndices != null)
                {
                    MeshBoneCount = modelSpaceBoneIndices.Length;
                }
            }

            BoneWeightCount = mesh.Data.GetSubCollection("m_skeleton")?.GetInt32Property("m_nBoneWeightCount") ?? 0;

            mesh.GetBounds();
            BoundingBox = new AABB(mesh.MinBounds, mesh.MaxBounds);
            MeshIndex = meshIndex;

            var meshSceneObjects = mesh.Data.GetArray("m_sceneObjects");
            ConfigureDrawCalls(scene, vbib, meshSceneObjects, initialMaterialTable, isAggregate);

            if (morph != null)
            {
                FlexStateManager = new FlexStateManager(renderContext, morph);
            }
        }

        public IEnumerable<string> GetSupportedRenderModes()
            => DrawCalls
                .SelectMany(static drawCall => drawCall.Material.Shader.RenderModes);

#if DEBUG
        public void UpdateVertexArrayObjects()
        {
            foreach (var call in DrawCalls)
            {
                call.Material.Shader.EnsureLoaded();
                call.UpdateVertexArrayObject();
            }
        }
#endif

        public void SetBoneMatricesBuffer(StorageBuffer? buffer)
        {
            BoneMatricesGpu = buffer;

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

                    drawCall.SetNewMaterial(renderContext.MaterialLoader.GetMaterial(replacementName, dynamicParams));
                }
            }
        }

        public void SetMaterialForMaterialViewer(Resource resourceMaterial)
        {
            var oldDrawCalls = DrawCalls.ToList();

            DrawCallsOpaque.Clear();
            DrawCallsOverlay.Clear();
            DrawCallsBlended.Clear();

            foreach (var drawCall in oldDrawCalls)
            {
                var material = drawCall.Material;
                var materialData = material.Material;

                // Recycle non-material-derived shader arguments
                var staticParams = materialData.GetShaderArguments();
                var dynamicParams = new Dictionary<string, byte>(material.Shader.Parameters.Except(staticParams));

                drawCall.SetNewMaterial(renderContext.MaterialLoader.LoadMaterial(resourceMaterial, dynamicParams));

                // Ignore overlays in material viewer, since there is nothing to overlay.
                if (drawCall.Material.IsTranslucent)
                {
                    DrawCallsBlended.Add(drawCall);
                }
                else
                {
                    DrawCallsOpaque.Add(drawCall);
                }
            }
        }

        private void ConfigureDrawCalls(Scene scene, VBIB vbib, KVObject[] sceneObjects, Dictionary<string, string>? materialReplacementTable, bool isAggregate)
        {
            if (vbib.VertexBuffers.Count == 0)
            {
                return;
            }

            var gpuVbib = renderContext.MeshBufferCache.CreateVertexIndexBuffers(Name, vbib);

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

                    if (materialName == null && Mesh.IsOccluder(objectDrawCall))
                    {
                        continue;
                    }

                    var shaderArguments = new Dictionary<string, byte>(scene.RenderAttributes);

                    if (BoneWeightCount > 4)
                    {
                        shaderArguments.Add("D_EIGHT_BONE_BLENDING", 1);
                    }

                    if (Mesh.IsCompressedNormalTangent(objectDrawCall))
                    {
                        var compressedVersion = (byte)1;
                        var vertexBuffers = objectDrawCall.GetArray("m_vertexBuffers");

                        foreach (var vertexBufferObject in vertexBuffers)
                        {
                            var vertexBufferId = vertexBufferObject.GetInt32Property("m_hBuffer");
                            var vertexBuffer = vbib.VertexBuffers[vertexBufferId];

                            var vertexNormal = vertexBuffer.InputLayoutFields.FirstOrDefault(static i => i.SemanticName == "NORMAL");

                            if (vertexNormal.Format != DXGI_FORMAT.UNKNOWN)
                            {
                                compressedVersion = vertexNormal.Format switch
                                {
                                    DXGI_FORMAT.R32_UINT => 2, // Added in CS2 on 2023-08-03
                                    _ => 1,
                                };

                                break;
                            }
                        }

                        shaderArguments.Add("D_COMPRESSED_NORMALS_AND_TANGENTS", compressedVersion);
                    }

                    if (Mesh.HasBakedLightingFromLightMap(objectDrawCall) && scene.LightingInfo.HasValidLightmaps)
                    {
                        shaderArguments.Add("D_BAKED_LIGHTING_FROM_LIGHTMAP", 1);
                    }
                    else if (Mesh.HasBakedLightingFromVertexStream(objectDrawCall))
                    {
                        shaderArguments.Add("D_BAKED_LIGHTING_FROM_VERTEX_STREAM", 1);
                    }
                    else if (scene.LightingInfo.HasValidLightProbes)
                    {
                        shaderArguments.Add("D_BAKED_LIGHTING_FROM_PROBE", 1);
                    }

                    var material = renderContext.MaterialLoader.GetMaterial(materialName, shaderArguments);

                    var drawCall = CreateDrawCall(objectDrawCall, material, vbib, gpuVbib);
                    if (i < objectDrawBounds.Length)
                    {
                        drawCall.DrawBounds = new AABB(
                            objectDrawBounds[i].GetSubCollection("m_vMinBounds").ToVector3(),
                            objectDrawBounds[i].GetSubCollection("m_vMaxBounds").ToVector3()
                        );
                    }

                    AddDrawCall(drawCall, isAggregate);

                    drawCall.VertexIdOffset = vertexOffset;
                    vertexOffset += objectDrawCall.GetInt32Property("m_nVertexCount");

                    i++;
                }
            }
        }

        private void AddDrawCall(DrawCall drawCall, bool isAggregate)
        {
            if (isAggregate)
            {
                DrawCallsOpaque.Add(drawCall);
                return;
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
        }

        private DrawCall CreateDrawCall(KVObject objectDrawCall, RenderMaterial material, VBIB vbib, GPUMeshBuffers gpuVbib)
        {
            var vertexBuffers = objectDrawCall.GetArray("m_vertexBuffers");

            var drawCall = new DrawCall
            {
                Material = material,
                MeshBuffers = renderContext.MeshBufferCache,
                MeshName = Name,
                VertexBuffers = new VertexDrawBuffer[vertexBuffers.Length]
            };

            var primitiveType = objectDrawCall.GetEnumValue<RenderPrimitiveType>("m_nPrimitiveType");

            drawCall.PrimitiveType = primitiveType switch
            {
                RenderPrimitiveType.RENDER_PRIM_TRIANGLES => PrimitiveType.Triangles,
                _ => throw new NotImplementedException($"Unknown PrimitiveType in drawCall! {primitiveType}"),
            };

            // Index buffer
            {
                var indexBufferObject = objectDrawCall.GetSubCollection("m_indexBuffer");
                var bufferIndex = indexBufferObject.GetUInt32Property("m_hBuffer");
                var indexBuffer = new IndexDrawBuffer
                {
                    Handle = gpuVbib.IndexBuffers[(int)bufferIndex],
                    Offset = indexBufferObject.GetUInt32Property("m_nBindOffsetBytes")
                };
                drawCall.IndexBuffer = indexBuffer;

                var indexElementSize = vbib.IndexBuffers[(int)bufferIndex].ElementSizeInBytes;
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
                var bindingIndex = 0;

                foreach (var vertexBufferObject in vertexBuffers)
                {
                    var bufferIndex = vertexBufferObject.GetUInt32Property("m_hBuffer");
                    var vertexBufferVbib = vbib.VertexBuffers[(int)bufferIndex];
                    var inputLayoutFields = vertexBufferVbib.InputLayoutFields;

                    if (BoneWeightCount > 4)
                    {
                        var newInputLayout = new List<VBIB.RenderInputLayoutField>(inputLayoutFields.Length + 2);
                        foreach (var inputField in inputLayoutFields)
                        {
                            if (inputField.SemanticName is "BLENDINDICES" or "BLENDWEIGHT")
                            {
                                var (newFormat, formatSize) = inputField.Format switch
                                {
                                    // Blendindices
                                    DXGI_FORMAT.R32G32B32A32_SINT => (DXGI_FORMAT.R16G16B16A16_UINT, 8u),
                                    DXGI_FORMAT.R16G16B16A16_UINT => (DXGI_FORMAT.R8G8B8A8_UINT, 4u),

                                    // Blendweight
                                    DXGI_FORMAT.R16G16B16A16_UNORM => (DXGI_FORMAT.R8G8B8A8_UNORM, 4u),

                                    _ => (DXGI_FORMAT.UNKNOWN, 0u),
                                };

                                if (newFormat != DXGI_FORMAT.UNKNOWN)
                                {
                                    newInputLayout.Add(inputField with
                                    {
                                        Format = newFormat,
                                    });

                                    newInputLayout.Add(inputField with
                                    {
                                        SemanticIndex = 2,
                                        Format = newFormat,
                                        Offset = inputField.Offset + formatSize,
                                    });

                                    continue;
                                }
                            }

                            newInputLayout.Add(inputField);
                        }

                        inputLayoutFields = [.. newInputLayout];
                    }

                    var vertexBuffer = new VertexDrawBuffer
                    {
                        Handle = gpuVbib.VertexBuffers[(int)bufferIndex],
                        Offset = vertexBufferObject.GetUInt32Property("m_nBindOffsetBytes"),
                        ElementSizeInBytes = vertexBufferVbib.ElementSizeInBytes,
                        InputLayoutFields = inputLayoutFields,
                    };

                    drawCall.VertexBuffers[bindingIndex++] = vertexBuffer;
                }

                drawCall.BaseVertex = objectDrawCall.GetInt32Property("m_nBaseVertex");
                drawCall.VertexCount = objectDrawCall.GetUInt32Property("m_nVertexCount");
            }

            var tintAlpha = Vector4.One;

            if (objectDrawCall.ContainsKey("m_vTintColor"))
            {
                var tintColor = objectDrawCall.GetSubCollection("m_vTintColor").ToVector3();
                tintColor = ColorSpace.SrgbLinearToGamma(tintColor);
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

            if (drawCall.Material.Shader.IsLoaded)
            {
                drawCall.UpdateVertexArrayObject();
            }

            return drawCall;
        }

        private RenderableMesh(string name, AABB bounds, RendererContext renderContext)
        {
            Name = name;
            BoundingBox = bounds;
            this.renderContext = renderContext;
        }

        public static RenderableMesh CreateMesh(string name, RenderMaterial material, VBIB vertexIndexBuffers, AABB bounds, RendererContext renderContext)
        {
            var mesh = new RenderableMesh(name, bounds, renderContext);
            var gpuVbib = renderContext.MeshBufferCache.CreateVertexIndexBuffers(name, vertexIndexBuffers);

            var vb = vertexIndexBuffers.VertexBuffers[0];
            var ib = vertexIndexBuffers.IndexBuffers[0];

            var drawCall = new DrawCall
            {
                Material = material,
                MeshBuffers = renderContext.MeshBufferCache,
                MeshName = name,
                PrimitiveType = PrimitiveType.Triangles,
                VertexCount = vb.ElementCount,
                StartIndex = 0,
                IndexCount = (int)ib.ElementCount,
                IndexType = DrawElementsType.UnsignedInt,

                VertexBuffers =
                [
                    new VertexDrawBuffer()
                    {
                        Handle = gpuVbib.VertexBuffers[0],
                        ElementSizeInBytes = vb.ElementSizeInBytes,
                        InputLayoutFields = vb.InputLayoutFields,
                    }
                ],

                IndexBuffer = new IndexDrawBuffer()
                {
                    Handle = gpuVbib.IndexBuffers[0],
                }
            };

            mesh.DrawCallsOpaque.Add(drawCall);
            return mesh;
        }
    }

    /// <summary>
    /// Base class for scene nodes that contain a collection of renderable meshes.
    /// </summary>
    public abstract class MeshCollectionNode : SceneNode
    {
        public abstract Vector4 Tint { get; set; }

        protected MeshCollectionNode(Scene scene) : base(scene)
        {
        }

        public List<RenderableMesh> RenderableMeshes { get; protected init; } = [];

        public override void Delete()
        {
            foreach (var mesh in RenderableMeshes)
            {
                foreach (var drawCall in mesh.DrawCalls)
                {
                    drawCall.DeleteVertexArrayObject();
                }
            }
        }
    }
}
