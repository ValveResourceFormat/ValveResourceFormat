using System.Diagnostics;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using ValveKeyValue;
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
        /// <summary>Gets the axis-aligned bounding box of the mesh in local space.</summary>
        public AABB BoundingBox { get; }

        /// <summary>Gets or sets the tint color multiplier applied to the entire mesh.</summary>
        public Vector4 Tint { get; set; } = Vector4.One;

        /// <summary>Gets or sets the alpha component of <see cref="Tint"/>.</summary>
        public float Alpha { get => Tint.W; set => Tint = Tint with { W = value }; }

        private readonly RendererContext renderContext;

        /// <summary>Gets the list of meshlets for GPU-driven indirect culling.</summary>
        public List<Meshlet> Meshlets { get; } = [];

        /// <summary>Gets the opaque draw calls for this mesh.</summary>
        public List<DrawCall> DrawCallsOpaque { get; } = [];

        /// <summary>Gets the static overlay draw calls for this mesh.</summary>
        public List<DrawCall> DrawCallsOverlay { get; } = [];

        /// <summary>Gets the translucent (blended) draw calls for this mesh.</summary>
        public List<DrawCall> DrawCallsBlended { get; } = [];

        /// <summary>Gets all draw calls across all render buckets.</summary>
        public IEnumerable<DrawCall> DrawCalls => DrawCallsOpaque.Concat(DrawCallsOverlay).Concat(DrawCallsBlended);

        /// <summary>Gets the GPU storage buffer holding the bone matrices for skeletal animation, or <see langword="null"/> if not animated.</summary>
        public StorageBuffer? BoneMatricesGpu { get; private set; }

        /// <summary>Gets the starting bone index in the model-space bone array for this mesh.</summary>
        public int MeshBoneOffset { get; private set; }

        /// <summary>Gets the number of bones used by this mesh.</summary>
        public int MeshBoneCount { get; private set; }

        /// <summary>Gets the number of bone weights per vertex (4 or 8).</summary>
        public int BoneWeightCount { get; private set; }

        /// <summary>Gets the name of the source mesh resource.</summary>
        public string Name { get; }

        /// <summary>Gets the index of this mesh within its parent model.</summary>
        public int MeshIndex { get; }

        /// <summary>Gets the flex state manager for morph target animation, or <see langword="null"/> if unsupported.</summary>
        public FlexStateManager? FlexStateManager { get; }

        /// <summary>Constructs a renderable mesh from a resource mesh, uploading geometry and configuring draw calls.</summary>
        /// <param name="mesh">Source mesh resource.</param>
        /// <param name="meshIndex">Index of this mesh within the parent model.</param>
        /// <param name="scene">Scene providing render context and lighting info.</param>
        /// <param name="model">Optional owning model, used for bone remapping.</param>
        /// <param name="initialMaterialTable">Optional material name overrides.</param>
        /// <param name="morph">Optional morph data for facial animation.</param>
        /// <param name="isAggregate">When <see langword="true"/>, all draw calls go into the opaque bucket for aggregate rendering.</param>
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

        /// <summary>Returns the union of all render mode names supported by the materials in this mesh.</summary>
        public IEnumerable<string> GetSupportedRenderModes()
            => DrawCalls
                .SelectMany(static drawCall => drawCall.Material.Shader.RenderModes);

#if DEBUG
        /// <summary>Recreates all vertex array objects. Debug-only, used for hot-reloading shaders.</summary>
        public void UpdateVertexArrayObjects()
        {
            foreach (var call in DrawCalls)
            {
                call.Material.Shader.EnsureLoaded();
                call.UpdateVertexArrayObject();
            }
        }
#endif

        /// <summary>Assigns the GPU bone matrices buffer and resets flex controllers.</summary>
        /// <param name="buffer">The storage buffer holding bone matrices, or <see langword="null"/> to disable skinning.</param>
        public void SetBoneMatricesBuffer(StorageBuffer? buffer)
        {
            BoneMatricesGpu = buffer;

            FlexStateManager?.ResetControllers();
        }

        /// <summary>Recompiles all draw call materials with a modified shader static combo value.</summary>
        /// <param name="combo">The combo name and new value to apply.</param>
        public void SetMaterialCombo((string ComboName, byte ComboValue) combo)
        {
            foreach (var drawCall in DrawCalls)
            {
                var material = drawCall.Material;
                var materialData = material.Material;
                var materialName = materialData.Name;

                var currentCombos = material.Shader.Parameters;
                if (currentCombos.GetValueOrDefault(combo.ComboName) == combo.ComboValue)
                {
                    continue;
                }

                var newCombos = currentCombos.ToDictionary();

                newCombos[combo.ComboName] = combo.ComboValue;
                drawCall.SetNewMaterial(renderContext.MaterialLoader.GetMaterial(materialName, newCombos));
            }
        }

        /// <summary>Replaces materials on draw calls according to the provided name-to-name mapping.</summary>
        /// <param name="materialTable">Dictionary mapping original material names to replacement material names.</param>
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

        /// <summary>Replaces all draw call materials with a single material resource for use in the material viewer.</summary>
        /// <param name="resourceMaterial">The material resource to apply to all draw calls.</param>
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

        private void ConfigureDrawCalls(Scene scene, VBIB vbib, IReadOnlyList<KVObject> sceneObjects, Dictionary<string, string>? materialReplacementTable, bool isAggregate)
        {
            if (vbib.VertexBuffers.Count == 0)
            {
                return;
            }

            var gpuVbib = renderContext.MeshBufferCache.CreateVertexIndexBuffers(Name, vbib);

            // note: we are flattening the scene objects into one mesh
            // we are not sure when there can be more than one scene object here.

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
                    var materialName = objectDrawCall.GetStringProperty("m_material") ?? objectDrawCall.GetStringProperty("m_pMaterial");
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
                    if (i < objectDrawBounds.Count)
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

                var meshlets = sceneObject.GetArray("m_meshlets");
                if (meshlets != null)
                {
                    Meshlets.EnsureCapacity(Meshlets.Count + meshlets.Count);

                    foreach (var meshletData in meshlets)
                    {
                        Meshlets.Add(new Meshlet(meshletData));
                    }
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
                VertexBuffers = new VertexDrawBuffer[vertexBuffers.Count]
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

        /// <summary>Creates a renderable mesh from raw vertex and index buffers with a single material.</summary>
        /// <param name="name">Name for the mesh and GPU buffer labels.</param>
        /// <param name="material">Material to assign to the single draw call.</param>
        /// <param name="vertexIndexBuffers">Vertex and index buffer data to upload.</param>
        /// <param name="bounds">Bounding box of the mesh.</param>
        /// <param name="renderContext">Renderer context for uploading buffers.</param>
        /// <returns>A new <see cref="RenderableMesh"/> with one opaque draw call.</returns>
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
        /// <summary>Gets or sets the tint color applied to all meshes in this node.</summary>
        public abstract Vector4 Tint { get; set; }

        /// <inheritdoc/>
        protected MeshCollectionNode(Scene scene) : base(scene)
        {
        }

        /// <summary>Gets the list of renderable meshes owned by this node.</summary>
        public List<RenderableMesh> RenderableMeshes { get; protected init; } = [];

        /// <inheritdoc/>
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
