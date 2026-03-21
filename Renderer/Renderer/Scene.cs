global using DepthOnlyDrawBuckets = System.Collections.Generic.Dictionary<ValveResourceFormat.Renderer.DepthOnlyProgram, System.Collections.Generic.List<ValveResourceFormat.Renderer.MeshBatchRenderer.Request>>;

using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Container for scene nodes with spatial partitioning, lighting, and render state management.
    /// </summary>
    public class Scene : IDisposable
    {
        /// <summary>
        /// Context data passed to scene nodes during per-frame update.
        /// </summary>
        public readonly struct UpdateContext
        {
            /// <summary>Gets the camera used for view-dependent node updates.</summary>
            public required Camera Camera { get; init; }

            /// <summary>Gets the text renderer available for nodes that need to draw labels.</summary>
            public required TextRenderer TextRenderer { get; init; }

            /// <summary>Gets the elapsed time in seconds since the last update.</summary>
            public required float Timestep { get; init; }
        }

        /// <summary>
        /// Context data passed to scene nodes and renderers during draw calls.
        /// </summary>
        public struct RenderContext
        {
            /// <summary>Gets or sets the scene being rendered.</summary>
            public required Scene Scene { get; set; }

            /// <summary>Gets the camera providing view and projection matrices.</summary>
            public required Camera Camera { get; init; }

            /// <summary>Gets or sets the framebuffer that is the render target.</summary>
            public required Framebuffer Framebuffer { get; set; }

            /// <summary>Gets or sets the current render pass being executed.</summary>
            public RenderPass RenderPass { get; set; }

            /// <summary>Gets or sets an optional shader that overrides per-material shaders for this pass.</summary>
            public Shader? ReplacementShader { get; set; }

            /// <summary>Gets the list of scene-level textures bound to reserved texture slots.</summary>
            public required List<(ReservedTextureSlots Slot, string Name, RenderTexture Texture)> Textures { get; init; }
        }

        /// <summary>Gets the render attribute overrides applied to all draw calls in this scene.</summary>
        public Dictionary<string, byte> RenderAttributes { get; } = [];

        /// <summary>Gets the world lighting information including light probes, environment maps, and dynamic lights.</summary>
        public WorldLightingInfo LightingInfo { get; }

        /// <summary>Gets or sets the fog parameters for this scene.</summary>
        public WorldFogInfo FogInfo { get; set; } = new();

        /// <summary>Gets or sets the post-processing parameters for this scene.</summary>
        public WorldPostProcessInfo PostProcessInfo { get; set; } = new();

        /// <summary>Gets or sets the physics simulation world associated with this scene.</summary>
        public Rubikon? PhysicsWorld { get; set; }

        private UniformBuffer<LightingConstants>? lightingBuffer;
        private UniformBuffer<EnvMapArray>? envMapBuffer;
        private UniformBuffer<LightProbeVolumeArray>? lpvBuffer;
        private UniformBuffer<FrustumPlanesGpu>? frustumBuffer;

        /// <summary>Gets or sets the GPU buffer containing per-instance object data (tint, transform index, env map visibility).</summary>
        public StorageBuffer? InstanceBufferGpu { get; set; }

        /// <summary>Gets or sets the GPU buffer containing world-space transform matrices for all scene nodes.</summary>
        public StorageBuffer? TransformBufferGpu { get; set; }


        /// <summary>Gets or sets the GPU buffer containing per-draw-call bounding boxes for indirect culling.</summary>
        public StorageBuffer? DrawBoundsGpu { get; set; }

        /// <summary>Gets or sets the GPU buffer containing per-meshlet cull info (bounds and cone data).</summary>
        public StorageBuffer? MeshletDataGpu { get; set; }

        /// <summary>Gets or sets the GPU buffer containing the indirect draw commands for all meshlets.</summary>
        public StorageBuffer? IndirectDrawsGpu { get; set; }

        /// <summary>Gets or sets the GPU buffer that receives compacted indirect draw commands after culling.</summary>
        public StorageBuffer? CompactedDrawsGpu { get; set; }

        /// <summary>Gets or sets the GPU buffer that stores per-aggregate visible draw counts after compaction.</summary>
        public StorageBuffer? CompactedCountsGpu { get; set; }

        /// <summary>Gets or sets the GPU buffer containing compaction request descriptors (count and start index per aggregate).</summary>
        public StorageBuffer? CompactionRequestsGpu { get; set; }

        /// <summary>Gets the total number of meshlets across all indirect-draw-capable aggregates in the scene.</summary>
        public int SceneMeshletCount { get; private set; }

        private Shader? FrustumCullShader;
        private Shader? CompactionShader;

        private Shader? DepthPyramidShader;
        private Shader? DepthPyramidNpotShader;
        /// <summary>Gets or sets the hierarchical depth pyramid texture used for GPU occlusion culling.</summary>
        public RenderTexture? DepthPyramid { get; internal set; }

        /// <summary>Gets or sets the view-projection matrix that was used when the depth pyramid was last generated.</summary>
        public Matrix4x4 DepthPyramidViewProjection { get; internal set; }

        /// <summary>Gets or sets whether the depth pyramid is current and safe to use for occlusion culling this frame.</summary>
        public bool DepthPyramidValid { get; internal set; }

        /// <summary>Gets the renderer context providing shared GPU resources and shader loading.</summary>
        public RendererContext RendererContext { get; }

        /// <summary>Gets the octree used to spatially partition static scene nodes.</summary>
        public Octree StaticOctree { get; }

        /// <summary>Gets the octree used to spatially partition dynamic scene nodes.</summary>
        public Octree DynamicOctree { get; }

        /// <summary>Gets or sets whether materials flagged as tools-only are rendered.</summary>
        public bool ShowToolsMaterials { get; set; }

        /// <summary>Gets or sets whether scene fog is applied during rendering.</summary>
        public bool FogEnabled { get; set; } = true;

        /// <summary>Gets or sets whether a depth-only prepass is performed before the opaque pass to reduce overdraw.</summary>
        public bool EnableDepthPrepass { get; set; }

        /// <summary>Gets or sets whether CPU-side occlusion culling is enabled.</summary>
        public bool EnableOcclusionCulling { get; set; } = true;
        internal bool EnableOcclusionQueries { get; set; }

        /// <summary>Gets or sets whether occlusion culling debug visualization is active.</summary>
        public bool OcclusionDebugEnabled { get; set; }

        /// <summary>Gets or sets the occlusion debug renderer, or <see langword="null"/> if not initialized.</summary>
        public OcclusionDebugRenderer? OcclusionDebug { get; set; }

        /// <summary>Gets or sets whether GPU indirect drawing is used for eligible aggregate scene nodes.</summary>
        public bool EnableIndirectDraws { get; set; } = true;

        /// <summary>Gets or sets whether GPU draw compaction is applied after frustum culling to remove empty indirect draw commands.</summary>
        public bool EnableCompaction { get; set; } = true;

        internal bool DrawMeshletsIndirect { get; private set; }
        internal bool CompactMeshletDraws { get; private set; }

        /// <summary>Gets all static and dynamic scene nodes in the order they were added.</summary>
        public IEnumerable<SceneNode> AllNodes => staticNodes.Concat(dynamicNodes);

        private readonly List<SceneNode> staticNodes = [];
        private readonly List<SceneNode> dynamicNodes = [];

        private Shader? OutlineShader;

        /// <summary>
        /// Initializes a new scene with the given renderer context and optional octree size hint.
        /// </summary>
        /// <param name="context">The renderer context providing shared GPU resources.</param>
        /// <param name="sizeHint">The initial world-space extent used to size the octrees.</param>
        public Scene(RendererContext context, float sizeHint = 32768)
        {
            RendererContext = context;
            StaticOctree = new(sizeHint);
            DynamicOctree = new(sizeHint);

            LightingInfo = new(this);
        }

        /// <summary>
        /// Performs one-time GPU setup: builds octrees, allocates buffers, computes light probe and environment map bindings, and loads internal shaders.
        /// </summary>
        public void Initialize()
        {
            UpdateOctrees();
            UpdateNodeIndices();
            CreateBuffers();
            CalculateLightProbeBindings();
            CalculateEnvironmentMaps();
            CreateInstanceTransformBuffers(deletePrevious: true); // after calculating envmap and lpv

            UpdateBuffers();

            OutlineShader = RendererContext.ShaderLoader.LoadShader("vrf.outline");
            FrustumCullShader = RendererContext.ShaderLoader.LoadShader("vrf.frustum_cull");
            CompactionShader = RendererContext.ShaderLoader.LoadShader("vrf.compact_indirect_draws");
            DepthPyramidShader = RendererContext.ShaderLoader.LoadShader("vrf.depth_pyramid");
            DepthPyramidNpotShader = RendererContext.ShaderLoader.LoadShader("vrf.depth_pyramid", ("D_NPOT_DOWNSAMPLE", 1));

            EnableIndirectDraws = LightingInfo.LightingData.IsSkybox == 0u;

            // set render lists to their max capacity
            CollectSceneDrawCalls(new Camera(RendererContext), Frustum.CreateEmpty());
            SetupSceneShadows(new Camera(RendererContext), -1);
            // LightingInfo.BinBarnLights(Frustum.CreateEmpty(), Vector3.Zero);
        }

        /// <summary>
        /// Adds a node to the scene, placing it in either the static or dynamic partition.
        /// </summary>
        /// <param name="node">The node to add.</param>
        /// <param name="dynamic">When <see langword="true"/>, the node is placed in the dynamic octree; otherwise the static octree.</param>
        public void Add(SceneNode node, bool dynamic)
        {
            var (nodeList, octree) = dynamic
                ? (dynamicNodes, DynamicOctree)
                : (staticNodes, StaticOctree);

            nodeList.Add(node);
            octree.Dirty = true;
        }

        /// <summary>
        /// Removes a node from the scene's static or dynamic partition.
        /// </summary>
        /// <param name="node">The node to remove.</param>
        /// <param name="dynamic">When <see langword="true"/>, removes from the dynamic partition; otherwise the static partition.</param>
        public void Remove(SceneNode node, bool dynamic)
        {
            var (nodeList, octree) = dynamic
                ? (dynamicNodes, DynamicOctree)
                : (staticNodes, StaticOctree);

            nodeList.Remove(node);
            octree.Dirty = true;
        }

        /// <summary>Indicates which spatial partition a scene node belongs to.</summary>
        public enum NodeType
        {
            /// <summary>The node ID is not present in any partition.</summary>
            Unknown,

            /// <summary>The node resides in the static spatial partition.</summary>
            Static,

            /// <summary>The node resides in the dynamic spatial partition.</summary>
            Dynamic,
        }

        /// <summary>
        /// Resolves a scene-unique node ID to its partition type and local list index.
        /// </summary>
        /// <param name="id">The scene-unique node ID assigned by <see cref="UpdateNodeIndices"/>.</param>
        /// <returns>The node type and local index, or <c>(Unknown, -1)</c> if the ID is not found.</returns>
        public (NodeType Type, int LocalId) GetNodeTypeById(uint id)
        {
            if (id > 0)
            {
                var staticNodeIndex = (int)(id - 1);
                var dynamicNodeIndex = staticNodeIndex - staticNodes.Count;

                if (staticNodeIndex < staticNodes.Count)
                {
                    return (NodeType.Static, staticNodeIndex);
                }
                else if (dynamicNodeIndex < dynamicNodes.Count)
                {
                    return (NodeType.Dynamic, dynamicNodeIndex);
                }
            }

            return (NodeType.Unknown, -1);
        }

        /// <summary>
        /// Removes all nodes from the scene, also disposes loaded materials and gpu mesh buffers.
        /// </summary>
        public void Clear()
        {
            foreach (var item in dynamicNodes)
            {
                item.Delete();
            }
            dynamicNodes.Clear();

            foreach (var item in staticNodes)
            {
                item.Delete();
            }
            staticNodes.Clear();

            StaticOctree.Clear();
            DynamicOctree.Clear();

            RendererContext.MaterialLoader.Clear();
            RendererContext.MeshBufferCache.Clear();
        }

        /// <summary>
        /// Finds a scene node by its scene-unique ID.
        /// </summary>
        /// <param name="id">The node ID to look up.</param>
        /// <returns>The matching <see cref="SceneNode"/>, or <see langword="null"/> if not found.</returns>
        public SceneNode? Find(uint id)
        {
            var (type, localId) = GetNodeTypeById(id);

            if (type == NodeType.Static)
            {
                return staticNodes[localId];
            }
            else if (type == NodeType.Dynamic)
            {
                return dynamicNodes[localId];
            }

            return null;
        }

        /// <summary>
        /// Finds the first scene node whose entity data matches the given entity.
        /// </summary>
        /// <param name="entity">The entity to search for.</param>
        /// <returns>The matching <see cref="SceneNode"/>, or <see langword="null"/> if not found.</returns>
        public SceneNode? Find(EntityLump.Entity entity)
        {
            bool IsMatchingEntity(SceneNode node) => node.EntityData == entity;

            return staticNodes.Find(IsMatchingEntity) ?? dynamicNodes.Find(IsMatchingEntity);
        }

        /// <summary>
        /// Finds the first scene node whose entity data contains a property with the given key and value.
        /// </summary>
        /// <param name="keyToFind">The entity property key to match.</param>
        /// <param name="valueToFind">The expected string value (case-insensitive).</param>
        /// <returns>The matching <see cref="SceneNode"/>, or <see langword="null"/> if not found.</returns>
        public SceneNode? FindNodeByKeyValue(string keyToFind, string valueToFind)
        {
            bool IsMatchingEntity(SceneNode node)
            {
                if (node.EntityData == null)
                {
                    return false;
                }

                return node.EntityData.Properties.Properties.TryGetValue(keyToFind, out var value)
                    && value.Value is string outString
                    && valueToFind.Equals(outString, StringComparison.OrdinalIgnoreCase);
            }

            return staticNodes.Find(IsMatchingEntity) ?? dynamicNodes.Find(IsMatchingEntity);
        }

        /// <summary>
        /// Updates all scene nodes for the current frame, advancing animations and rebuilding octrees and GPU buffers if the scene changed.
        /// </summary>
        /// <param name="updateContext">Per-frame context data including camera and timestep.</param>
        public void Update(Scene.UpdateContext updateContext)
        {
            foreach (var node in staticNodes)
            {
                node.Update(updateContext);
            }

            if (StaticOctree.Dirty)
            {
                // we disabled or enabled some static node
                CreateIndirectDrawBuffers(true);
            }

            foreach (var node in dynamicNodes)
            {
                var oldBox = node.BoundingBox;
                node.Update(updateContext);

                if (node.LayerEnabled && !oldBox.Equals(node.BoundingBox))
                {
                    DynamicOctree.Update(node, oldBox);
                }
            }

            if (StaticOctree.Dirty || DynamicOctree.Dirty)
            {
                UpdateOctrees();
                UpdateNodeIndices();
                CreateInstanceTransformBuffers(deletePrevious: true);
            }
        }

        /// <summary>Allocates GPU uniform and storage buffers for lighting, environment maps, light probes, frustum planes, and indirect draws.</summary>
        public void CreateBuffers()
        {
            lightingBuffer ??= new(ReservedBufferSlots.Lighting);
            envMapBuffer ??= new(ReservedBufferSlots.EnvironmentMap);
            lpvBuffer ??= new(ReservedBufferSlots.LightProbe);
            frustumBuffer ??= new(ReservedBufferSlots.FrustumPlanes);

            lightingBuffer.Data = LightingInfo.LightingData;

            LightingInfo.CreateBarnLightBuffer();
            CreateIndirectDrawBuffers();
        }

        private void CreateInstanceTransformBuffers(bool deletePrevious = false)
        {
            if (deletePrevious)
            {
                InstanceBufferGpu?.Delete();
                TransformBufferGpu?.Delete();
            }

            var nodes = AllNodes.ToList();

            if (nodes.Count == 0)
            {
                return;
            }

            var maxId = nodes.Max(n => n.Id);

            var instanceData = new ObjectDataStandard[maxId + 1];
            var transformData = new List<OpenTK.Mathematics.Matrix3x4>(capacity: (int)maxId + 2)
            {
                // Reserve index 0 for identity transform
                Matrix4x4.Identity.To3x4()
            };

            foreach (var node in nodes)
            {
                var instanceTint = Vector4.One;
                if (node is SceneAggregate.Fragment fragment)
                {
                    instanceTint = fragment.RenderMesh.Tint * fragment.DrawCall.TintColor * fragment.Tint;
                }

                uint transformIndex;

                if (node is SceneAggregate { InstanceTransforms.Count: > 0 } aggregateWithInstances)
                {
                    transformIndex = (uint)transformData.Count;

                    foreach (var instanceTransform in aggregateWithInstances.InstanceTransforms)
                    {
                        transformData.Add(instanceTransform);
                    }
                }
                else if (node.Transform.IsIdentity)
                {
                    transformIndex = 0; // Reuse identity transform at index 0
                }
                else
                {
                    transformIndex = (uint)transformData.Count;
                    transformData.Add(node.Transform.To3x4());
                }

                instanceData[node.Id] = new ObjectDataStandard
                {
                    TintAlpha = Color32.FromVector4(instanceTint).PackedValue,
                    TransformIndex = transformIndex,
                    EnvMapVisibility = node.ShaderEnvMapVisibility,
                    VisibleLPV = (uint)(node.LightProbeBinding?.ShaderIndex ?? 0),
                    Identification = node.Id,
                };
            }

            InstanceBufferGpu = new StorageBuffer(ReservedBufferSlots.Objects);
            TransformBufferGpu = new StorageBuffer(ReservedBufferSlots.Transforms);

            InstanceBufferGpu.Create(instanceData, BufferUsageHint.StaticDraw);
            TransformBufferGpu.Create(CollectionsMarshal.AsSpan(transformData), BufferUsageHint.StaticDraw);
        }

        private void CreateIndirectDrawBuffers(bool deletePrevious = false)
        {
            var aggregateSceneNodes = staticNodes.OfType<SceneAggregate>().Where(agg => agg.CanDrawIndirect).ToList();
            var aggregateDrawCallCount = aggregateSceneNodes.Sum(agg => agg.Fragments.Count);
            var aggregateMeshletCount = aggregateSceneNodes.Sum(agg => agg.RenderMesh.Meshlets.Count);

            if (aggregateMeshletCount == 0)
            {
                return;
            }

            if (deletePrevious)
            {
                DrawBoundsGpu?.Delete();
                MeshletDataGpu?.Delete();
                IndirectDrawsGpu?.Delete();
                CompactedDrawsGpu?.Delete();
                CompactedCountsGpu?.Delete();
                CompactionRequestsGpu?.Delete();
                OcclusionDebug?.OccludedBoundsDebugGpu?.Delete();
            }

            // draw bounds
            {
                var drawBounds = new DrawBounds[aggregateDrawCallCount];
                var index = 0;
                foreach (var agg in aggregateSceneNodes)
                {
                    foreach (var fragment in agg.Fragments)
                    {
                        var drawCall = fragment.DrawCall;
                        Debug.Assert(drawCall.DrawBounds != null);
                        drawBounds[index].Min = drawCall.DrawBounds.Value.Min;
                        drawBounds[index].Max = drawCall.DrawBounds.Value.Max;
                        index++;
                    }
                }

                DrawBoundsGpu = new StorageBuffer(ReservedBufferSlots.AggregateDrawBounds);
                DrawBoundsGpu.Create(drawBounds, BufferUsageHint.StaticDraw);
            }

            // meshlets
            {
                var meshletDataGpu = new MeshletCullInfo[aggregateMeshletCount];
                var indirectDrawsGpu = new DrawElementsIndirectCommand[aggregateMeshletCount];

                var sceneDrawCount = 0;
                var sceneMeshletCount = 0;
                var aggregateIndex = 0;
                foreach (var agg in aggregateSceneNodes)
                {
                    agg.IndirectDrawByteOffset = sceneMeshletCount * Unsafe.SizeOf<DrawElementsIndirectCommand>();
                    agg.IndirectDrawCount = agg.RenderMesh.Meshlets.Count;
                    agg.CompactionIndex = aggregateIndex++;

                    var drawIndex = 0;
                    var indirectDrawCount = 0;
                    foreach (var fragment in agg.Fragments)
                    {
                        var fragmentInstanceId = fragment.Id;
                        var drawCall = fragment.DrawCall;

                        var start = drawCall.FirstMeshlet;
                        var stop = start + drawCall.NumMeshlets;

                        for (var drawMeshletIndex = start; drawMeshletIndex < stop; drawMeshletIndex++)
                        {
                            var meshlet = agg.RenderMesh.Meshlets[drawMeshletIndex];
                            meshletDataGpu[sceneMeshletCount] = new MeshletCullInfo
                            {
                                Bounds = meshlet.PackedAABB,
                                Cone = meshlet.CullingData,
                                ParentDrawBoundsIndex = (uint)(sceneDrawCount + drawIndex),
                            };

                            var count = meshlet.TriangleCount * 3;
                            var firstIndex = (uint)meshlet.TriangleOffset * 3;

                            if (count == 0 && firstIndex == 0)
                            {
                                // older meshlets
                                var tris = drawCall.IndexCount / 3;
                                var clusters = drawCall.NumMeshlets;
                                var trisPerCluster = tris / clusters;

                                count = (uint)trisPerCluster * 3;
                                firstIndex = (uint)(drawMeshletIndex * count);
                            }

                            if (fragment.LayerEnabled == false)
                            {
                                count = 0;
                            }

                            // what is meshlet.VertexOffset used for?

                            indirectDrawsGpu[sceneMeshletCount] = new DrawElementsIndirectCommand
                            {
                                Count = count,
                                InstanceCount = 1,
                                FirstIndex = firstIndex,
                                BaseVertex = drawCall.BaseVertex,
                                BaseInstance = fragmentInstanceId,
                            };

                            sceneMeshletCount++;
                            indirectDrawCount++;
                        }

                        drawIndex++;
                    }

                    // can be smaller than serialized meshlets due to LoD filtering
                    agg.IndirectDrawCount = indirectDrawCount;

                    sceneDrawCount += agg.Fragments.Count;
                }

                SceneMeshletCount = sceneMeshletCount;

                MeshletDataGpu = new StorageBuffer(ReservedBufferSlots.AggregateMeshlets);
                IndirectDrawsGpu = new StorageBuffer(ReservedBufferSlots.AggregateDraws);

                MeshletDataGpu.Create(meshletDataGpu, BufferUsageHint.StaticDraw);
                IndirectDrawsGpu.Create(indirectDrawsGpu, BufferUsageHint.DynamicDraw);

                // Create compaction buffers
                CompactedDrawsGpu = new StorageBuffer(ReservedBufferSlots.CompactedDraws);
                CompactedDrawsGpu.Create(indirectDrawsGpu, BufferUsageHint.DynamicDraw);

                CompactedCountsGpu = StorageBuffer.Allocate<uint>(ReservedBufferSlots.CompactedCounts, aggregateSceneNodes.Count, BufferUsageHint.DynamicDraw);

                // Create compaction requests (one per aggregate)
                var compactionRequests = new uint[aggregateSceneNodes.Count * 2];
                for (var i = 0; i < aggregateSceneNodes.Count; i++)
                {
                    var agg = aggregateSceneNodes[i];
                    var startIndex = agg.IndirectDrawByteOffset / Unsafe.SizeOf<DrawElementsIndirectCommand>();
                    var drawCount = agg.IndirectDrawCount;

                    compactionRequests[i * 2 + 0] = (uint)drawCount;
                    compactionRequests[i * 2 + 1] = (uint)startIndex;
                }

                CompactionRequestsGpu = new StorageBuffer(ReservedBufferSlots.CompactionRequests);
                CompactionRequestsGpu.Create(compactionRequests, BufferUsageHint.StaticDraw);
            }

            OcclusionDebug = new OcclusionDebugRenderer(this, RendererContext);
        }

        /// <summary>Uploads the latest lighting, environment map, and light probe data to their respective GPU uniform buffers.</summary>
        public void UpdateBuffers()
        {
            Debug.Assert(lightingBuffer is not null && envMapBuffer is not null && lpvBuffer is not null);

            lightingBuffer.Update();
            envMapBuffer.Update();
            lpvBuffer.Update();
        }

        /// <summary>Updates and binds the lighting, environment map, light probe, and barn light buffers to their reserved GPU binding slots.</summary>
        public void SetSceneBuffers()
        {
            Debug.Assert(lightingBuffer is not null && envMapBuffer is not null && lpvBuffer is not null);

            lightingBuffer.Update();
            lightingBuffer.BindBufferBase();
            envMapBuffer.BindBufferBase();
            lpvBuffer.BindBufferBase();
            LightingInfo.BindBarnLightBuffer();
        }

        private readonly List<SceneNode> CullResults = [];
        private int StaticCount;
        private int LastFrustum = -1;

        /// <summary>
        /// Returns all scene nodes whose bounding boxes intersect the given frustum, caching static results across frames when the frustum is unchanged.
        /// </summary>
        /// <param name="frustum">The view frustum to test against.</param>
        /// <returns>A list of visible scene nodes (valid until the next call to this method).</returns>
        public List<SceneNode> GetFrustumCullResults(Frustum frustum)
        {
            var currentFrustum = frustum.GetHashCode();

            // Optimization: Do not clear static culled results from last frame if:
            // 1. Frustum did not change
            // 2. Did not run occlusion queries

            if (LastFrustum != currentFrustum || occlusionDirty)
            {
                LastFrustum = currentFrustum;

                CullResults.Clear();
                CullResults.Capacity = staticNodes.Count + dynamicNodes.Count + 100;

                StaticOctree.Root.Query(frustum, CullResults);
                StaticCount = CullResults.Count;
            }
            else
            {
                CullResults.RemoveRange(StaticCount, CullResults.Count - StaticCount);
            }

            DynamicOctree.Root.Query(frustum, CullResults);
            return CullResults;
        }

        /// <summary>Gets or sets whether any translucent material in the collected draw calls samples the scene color texture.</summary>
        public bool WantsSceneColor { get; set; }

        /// <summary>Gets or sets whether any translucent material in the collected draw calls samples the scene depth texture.</summary>
        public bool WantsSceneDepth { get; set; }

        /// <summary>Gets whether there are any selected nodes queued for outline rendering.</summary>
        public bool HasOutlineObjects => renderLists[RenderPass.Outline].Count > 0;

        private readonly Dictionary<RenderPass, List<MeshBatchRenderer.Request>> renderLists = new()
        {
            [RenderPass.OpaqueAggregate] = [],
            [RenderPass.OpaqueFragments] = [],
            [RenderPass.Opaque] = [],
            [RenderPass.StaticOverlay] = [],
            [RenderPass.Water] = [],
            [RenderPass.Translucent] = [],
            [RenderPass.Outline] = [],
        };

        private Dictionary<DepthOnlyProgram, List<MeshBatchRenderer.Request>> depthOnlyDraws { get; } = new()
        {
            [DepthOnlyProgram.Static] = [],
            [DepthOnlyProgram.Animated] = [],
            [DepthOnlyProgram.AnimatedEightBones] = [],
            [DepthOnlyProgram.Unspecified] = [],
        };

        private void Add(MeshBatchRenderer.Request request, RenderPass renderPass)
        {
            Debug.Assert(request.Call is not null);

            if (!ShowToolsMaterials && request.Call.Material.IsToolsMaterial)
            {
                return;
            }

            if (renderPass > RenderPass.DepthOnly && request.Node.IsSelected)
            {
                renderLists[RenderPass.Outline].Add(request);
            }

            if (renderPass == RenderPass.OpaqueAggregate)
            {
                if (request.Node is SceneAggregate { CanDrawIndirect: true })
                {
                    if (EnableDepthPrepass)
                    {
                        var bucket = GetSpecializedDepthOnlyShader(false, request.Mesh, request.Call);
                        depthOnlyDraws[bucket].Add(request);
                    }
                }
            }

            if (renderPass == RenderPass.OpaqueFragments)
            {
                if (DrawMeshletsIndirect && request.Node is SceneAggregate.Fragment { Parent.CanDrawIndirect: true })
                {
                    return; // Skip individual fragment draws if aggregate can be drawn with indirect draw
                }

                renderPass = RenderPass.Opaque;
            }

            var queueList = renderLists[renderPass];

            if (renderPass == RenderPass.Translucent)
            {
                WantsSceneColor |= request.Call.Material.Shader.ReservedTexturesUsed.Contains("g_tSceneColor");
                WantsSceneDepth |= request.Call.Material.Shader.ReservedTexturesUsed.Contains("g_tSceneDepth");

                if (request.Call.Material.IsCs2Water)
                {
                    queueList = renderLists[RenderPass.Water];
                }
            }

            queueList.Add(request);
        }

        /// <summary>
        /// Frustum-culls the scene and populates the per-pass render lists for the upcoming frame.
        /// </summary>
        /// <param name="camera">The camera used to sort translucent draw calls by distance.</param>
        /// <param name="cullFrustum">An optional override frustum for culling; defaults to the camera's view frustum.</param>
        public void CollectSceneDrawCalls(Camera camera, Frustum? cullFrustum = null)
        {
            foreach (var bucket in renderLists.Values)
            {
                bucket.Clear();
            }

            foreach (var bucket in depthOnlyDraws.Values)
            {
                bucket.Clear();
            }

            WantsSceneColor = false;
            WantsSceneDepth = false;

            var frustum = cullFrustum ??= camera.ViewFrustum;
            var cullResults = GetFrustumCullResults(frustum);

            // Collect mesh calls
            foreach (var node in cullResults)
            {
                if (node is MeshCollectionNode meshCollection)
                {
                    foreach (var mesh in meshCollection.RenderableMeshes)
                    {
                        foreach (var call in mesh.DrawCallsOpaque)
                        {
                            Add(new MeshBatchRenderer.Request
                            {
                                Mesh = mesh,
                                Call = call,
                                Node = node,
                            }, RenderPass.Opaque);
                        }

                        foreach (var call in mesh.DrawCallsOverlay)
                        {
                            Add(new MeshBatchRenderer.Request
                            {
                                Mesh = mesh,
                                Call = call,
                                RenderOrder = node.OverlayRenderOrder,
                                Node = node,
                            }, RenderPass.StaticOverlay);
                        }

                        foreach (var call in mesh.DrawCallsBlended)
                        {
                            Add(new MeshBatchRenderer.Request
                            {
                                Mesh = mesh,
                                Call = call,
                                DistanceFromCamera = node.GetCameraDistance(camera),
                                Node = node,
                            }, RenderPass.Translucent);
                        }
                    }
                }
                else if (node is SceneAggregate.Fragment fragment)
                {
                    fragment.Parent.AnyChildrenVisible = true;
                    Add(new MeshBatchRenderer.Request
                    {
                        Mesh = fragment.RenderMesh,
                        Call = fragment.DrawCall,
                        Node = node,
                    }, RenderPass.OpaqueFragments);
                }
                else if (node is SceneAggregate aggregate)
                {
                    if (aggregate.InstanceTransforms.Count > 0)
                    {
                        Add(new MeshBatchRenderer.Request
                        {
                            Mesh = aggregate.RenderMesh,
                            Call = aggregate.RenderMesh.DrawCallsOpaque[0],
                            Node = node,
                        }, RenderPass.Opaque);
                    }
                    else if (DrawMeshletsIndirect && aggregate.CanDrawIndirect)
                    {
                        aggregate.AnyChildrenVisible = false;
                        Add(new MeshBatchRenderer.Request
                        {
                            Mesh = aggregate.RenderMesh,
                            Call = aggregate.RenderMesh.DrawCallsOpaque[0],
                            //DistanceFromCamera = aggregate.GetAverageCameraDistanceFragments(camera),
                            Node = node,
                        }, RenderPass.OpaqueAggregate);
                    }
                }
                else
                {
                    var customRender = new MeshBatchRenderer.Request
                    {
                        DistanceFromCamera = node is PhysSceneNode
                            ? 100000f - node.OverlayRenderOrder * 10f
                            : node.GetCameraDistance(camera),
                        Node = node,
                    };

                    renderLists[RenderPass.Opaque].Add(customRender);
                    renderLists[RenderPass.Translucent].Add(customRender);

                    if (node.IsSelected)
                    {
                        renderLists[RenderPass.Outline].Add(customRender);
                    }
                }
            }
        }

        private List<SceneNode> CulledShadowNodes { get; } = [];
        private readonly List<RenderableMesh> listWithSingleMesh = [null!];
        internal DepthOnlyDrawBuckets CulledShadowDrawCalls { get; } = CreateDepthOnlyDrawCallCollection();
        internal static DepthOnlyDrawBuckets CreateDepthOnlyDrawCallCollection() => new()
        {
            [DepthOnlyProgram.Static] = [],
            [DepthOnlyProgram.Animated] = [],
            [DepthOnlyProgram.AnimatedEightBones] = [],
            [DepthOnlyProgram.Unspecified] = [],
        };

        /// <summary>
        /// Updates the sun light shadow frustum and collects shadow draw calls for the directional light, if dynamic shadows are enabled.
        /// </summary>
        /// <param name="camera">The main camera used to fit the shadow frustum.</param>
        /// <param name="shadowMapSize">The shadow map resolution; pass -1 to produce an empty frustum (pre-warm pass).</param>
        public void SetupSceneShadows(Camera camera, int shadowMapSize)
        {
            if (!LightingInfo.EnableDynamicShadows)
            {
                return;
            }

            LightingInfo.UpdateSunLightFrustum(camera, shadowMapSize);

            if (shadowMapSize == -1)
            {
                LightingInfo.SunLightFrustum.SetEmpty();
            }

            CollectShadowDrawCalls(LightingInfo.SunLightFrustum,
                includeStatic: !LightingInfo.HasBakedShadowsFromLightmap,
                includeDynamic: true, CulledShadowDrawCalls);
        }

        /// <summary>Invalidates the cached shadow draw calls for all faces of the given barn light, forcing a rebuild next frame.</summary>
        /// <param name="light">The barn light whose shadow cache should be cleared.</param>
        public static void ClearShadowCache(SceneLight light)
        {
            for (var i = 0; i < light.BarnFaces.Length; i++)
            {
                ref var entry = ref CollectionsMarshal.GetValueRefOrNullRef(light.FaceShadowCache, i);
                if (!Unsafe.IsNullRef(ref entry))
                {
                    entry.FrustumHash = -1;
                }
            }
        }

        /// <summary>
        /// Ensures the shadow draw call cache for a single barn light face is up to date, rebuilding it if the light frustum changed.
        /// </summary>
        /// <param name="light">The barn light owning the shadow face.</param>
        /// <param name="faceIndex">The face index within the barn light to update.</param>
        /// <param name="lightFrustum">The frustum representing the light's view for this face.</param>
        public void SetupBarnLightFaceShadow(SceneLight light, int faceIndex, Frustum lightFrustum)
        {
            var barnLightFrustumHash = lightFrustum.GetHashCode();
            ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(light.FaceShadowCache, faceIndex, out _);

            if (entry.FrustumHash == barnLightFrustumHash && entry.DrawCalls is not null)
            {
                return;
            }

            entry.DrawCalls ??= CreateDepthOnlyDrawCallCollection();
            // Skip static geo for stationary lights
            CollectShadowDrawCalls(lightFrustum, includeStatic: light.DirectLight != SceneLight.DirectLightType.Stationary, includeDynamic: true, entry.DrawCalls);
            entry.FrustumHash = barnLightFrustumHash;
        }

        private void CollectShadowDrawCalls(Frustum frustum, bool includeStatic, bool includeDynamic, DepthOnlyDrawBuckets drawBuckets)
        {
            foreach (var bucket in drawBuckets.Values)
            {
                bucket.Clear();
            }

            if (includeStatic)
            {
                StaticOctree.Root.QueryNoOcclusion(frustum, CulledShadowNodes);
            }

            if (includeDynamic)
            {
                DynamicOctree.Root.QueryNoOcclusion(frustum, CulledShadowNodes);
            }

            foreach (var node in CulledShadowNodes)
            {
                const ObjectTypeFlags skipFlags = ObjectTypeFlags.NoShadows | ObjectTypeFlags.BlockLight;

                List<RenderableMesh> meshes;
                DrawCall? singleCall = null;

                if (node is MeshCollectionNode meshCollection)
                {
                    if ((node.Flags & skipFlags) != 0)
                    {
                        continue;
                    }

                    meshes = meshCollection.RenderableMeshes;
                }
                else if (node is SceneAggregate.Fragment fragment)
                {
                    if ((fragment.Flags & skipFlags) != 0)
                    {
                        continue;
                    }

                    listWithSingleMesh[0] = fragment.RenderMesh;
                    meshes = listWithSingleMesh;
                    singleCall = fragment.DrawCall;
                }
                else if (node is SceneAggregate aggregate)
                {
                    if ((aggregate.AllFlags & skipFlags) != 0)
                    {
                        continue;
                    }

                    if (aggregate.InstanceTransforms.Count == 0)
                    {
                        continue;
                    }

                    listWithSingleMesh[0] = aggregate.RenderMesh;
                    meshes = listWithSingleMesh;
                }
                else
                {
                    continue;
                }

                var animated = node is ModelSceneNode model && model.IsAnimated;

                foreach (var mesh in meshes)
                {
                    foreach (var opaqueCall in mesh.DrawCallsOpaque)
                    {
                        if (singleCall != null && opaqueCall != singleCall)
                        {
                            continue;
                        }

                        if (opaqueCall.Material.DoNotCastShadows)
                        {
                            continue;
                        }

                        var bucket = GetSpecializedDepthOnlyShader(animated, mesh, opaqueCall);

                        drawBuckets[bucket].Add(new MeshBatchRenderer.Request
                        {
                            Mesh = mesh,
                            Call = opaqueCall,
                            Node = node,
                        });
                    }
                }
            }

            CulledShadowNodes.Clear();
        }

        private static DepthOnlyProgram GetSpecializedDepthOnlyShader(bool animated, RenderableMesh mesh, DrawCall opaqueCall)
        {
            var renderWithUnoptimizedShader = opaqueCall.Material.VertexAnimation || opaqueCall.Material.IsAlphaTest;

            var bucket = (renderWithUnoptimizedShader, animated) switch
            {
                (true, _) => DepthOnlyProgram.Unspecified, // shader will be null
                (false, false) => DepthOnlyProgram.Static,
                (false, true) => DepthOnlyProgram.Animated,
            };

            if (mesh.BoneWeightCount > 4)
            {
                bucket = DepthOnlyProgram.AnimatedEightBones;
            }

            return bucket;
        }

        internal void UpdateIndirectRenderingState()
        {
            CompactMeshletDraws = false;
            DrawMeshletsIndirect = EnableIndirectDraws && SceneMeshletCount > 0 && IndirectDrawsGpu != null;
            EnableOcclusionQueries = EnableOcclusionCulling && !DrawMeshletsIndirect;

            if (DrawMeshletsIndirect)
            {
                Debug.Assert(IndirectDrawsGpu is not null);
                Debug.Assert(CompactedDrawsGpu is not null);

                CompactMeshletDraws = GLEnvironment.IndirectCountSupported && EnableCompaction;
                GL.BindBuffer(BufferTarget.DrawIndirectBuffer, CompactMeshletDraws
                    ? CompactedDrawsGpu.Handle
                    : IndirectDrawsGpu.Handle);

                if (CompactMeshletDraws)
                {
                    Debug.Assert(CompactedCountsGpu is not null);
                    GL.BindBuffer(BufferTarget.ParameterBuffer, CompactedCountsGpu.Handle);
                }
            }
        }

        /// <summary>
        /// Dispatches the GPU frustum (and optional occlusion) culling compute shader, writing surviving indirect draw commands to <see cref="IndirectDrawsGpu"/>.
        /// </summary>
        /// <param name="frustum">The view frustum used to cull meshlets.</param>
        public void MeshletCullGpu(Frustum frustum)
        {
            Debug.Assert(frustumBuffer is not null);
            Debug.Assert(FrustumCullShader is not null);

            Debug.Assert(DrawBoundsGpu is not null);
            Debug.Assert(MeshletDataGpu is not null);
            Debug.Assert(IndirectDrawsGpu is not null);

            using var _ = new GLDebugGroup("Cull Meshlet Draws");

            frustumBuffer.BindBufferBase();
            frustumBuffer.Data = new(frustum);

            FrustumCullShader.Use();

            // Set occlusion culling enabled flag
            var occlusionEnabled = DepthPyramidValid;
            FrustumCullShader.SetUniform1("g_bOcclusionCullEnabled", occlusionEnabled ? 1 : 0);

            // If occlusion culling is enabled, setup depth pyramid and bind texture
            if (occlusionEnabled)
            {
                Debug.Assert(DepthPyramid != null);

                FrustumCullShader.SetUniform1("g_nDepthPyramidMaxMip", DepthPyramid.NumMipLevels - 1);
                FrustumCullShader.SetUniform1("g_nDepthPyramidWidth", DepthPyramid.Width);
                FrustumCullShader.SetUniform1("g_nDepthPyramidHeight", DepthPyramid.Height);
                FrustumCullShader.SetUniform1("g_flDepthRangeMin", 0.05f);
                FrustumCullShader.SetUniform1("g_flDepthRangeMax", 1.0f);

                // Bind depth pyramid as texture for sampling
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(DepthPyramid.Target, DepthPyramid.Handle);
            }

            MeshletDataGpu.BindBufferBase();
            DrawBoundsGpu.BindBufferBase();
            IndirectDrawsGpu.BindBufferBase();

            // Bind debug buffer for occluded bounds visualization
            if (OcclusionDebugEnabled)
            {
                OcclusionDebug!.BindAndClearBuffer();
            }
            FrustumCullShader.SetUniform1("g_bOcclusionDebugEnabled", OcclusionDebugEnabled);

            var workGroups = (SceneMeshletCount + 63) / 64;
            GL.DispatchCompute(workGroups, 1, 1);

            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
        }

        /// <summary>
        /// Dispatches the GPU draw compaction compute shader, packing non-zero indirect draw commands into <see cref="CompactedDrawsGpu"/> to avoid empty draw calls.
        /// </summary>
        public void CompactIndirectDraws()
        {
            if (CompactionShader == null || CompactedDrawsGpu == null || CompactedCountsGpu == null || CompactionRequestsGpu == null)
            {
                return;
            }

            using var _ = new GLDebugGroup("Compact Meshlet Draws");

            CompactionShader.Use();

            IndirectDrawsGpu!.BindBufferBase();
            CompactedDrawsGpu.BindBufferBase();
            CompactedCountsGpu.BindBufferBase();
            CompactionRequestsGpu.BindBufferBase();

            var aggregateCount = CompactionRequestsGpu.Size / sizeof(uint) / 2; // 2 uints per aggregate
            var workGroups = (aggregateCount + 3) / 4; // 4 requests per workgroup (local_size_x = 4)
            GL.DispatchCompute(workGroups, 1, 1);

        }

        /// <summary>
        /// Generates the hierarchical depth pyramid from the given depth texture by downsampling through compute shaders.
        /// </summary>
        /// <param name="depthSource">The full-resolution depth texture to downsample.</param>
        public void GenerateDepthPyramid(RenderTexture depthSource)
        {
            if (DepthPyramid == null || DepthPyramidShader == null)
            {
                return;
            }

            using var _ = new GLDebugGroup("Generate Depth Pyramid");

            Debug.Assert(depthSource.Target == TextureTarget.Texture2D);
            var startMipLevel = 1;

            // Downsample from non power of two depth source
            {
                Debug.Assert(DepthPyramidNpotShader != null);
                DepthPyramidNpotShader.Use();
                DepthPyramidNpotShader.SetTexture(0, "g_tSourceDepthNpot", depthSource);
                DepthPyramidNpotShader.SetUniform1("g_nSourceDepthWidth", depthSource.Width);
                DepthPyramidNpotShader.SetUniform1("g_nSourceDepthHeight", depthSource.Height);

                DepthPyramidNpotShader.SetUniform1("g_nDestDepthWidth", DepthPyramid.Width);
                DepthPyramidNpotShader.SetUniform1("g_nDestDepthHeight", DepthPyramid.Height);

                GL.BindImageTexture(2, DepthPyramid.Handle, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.R32f);

                var groupsX = (DepthPyramid.Width + 7) / 8;
                var groupsY = (DepthPyramid.Height + 7) / 8;
                GL.DispatchCompute(groupsX, groupsY, 1);

                GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
            }

            // Generate mip levels down to 1x1
            DepthPyramidShader.Use();

            for (var mipLevel = startMipLevel; mipLevel < DepthPyramid.NumMipLevels; mipLevel++)
            {
                var destWidth = Math.Max(1, DepthPyramid.Width >> mipLevel);
                var destHeight = Math.Max(1, DepthPyramid.Height >> mipLevel);
                var sourceMip = mipLevel - 1;

                DepthPyramidShader.SetUniform1("g_nDestDepthWidth", destWidth);
                DepthPyramidShader.SetUniform1("g_nDestDepthHeight", destHeight);

                // Bind source mip level as read-only image
                GL.BindImageTexture(1, DepthPyramid.Handle, sourceMip, false, 0, TextureAccess.ReadOnly, SizedInternalFormat.R32f);

                // Bind destination mip level as write-only image
                GL.BindImageTexture(2, DepthPyramid.Handle, mipLevel, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.R32f);

                // Dispatch compute shader
                var groupsX = (destWidth + 7) / 8;
                var groupsY = (destHeight + 7) / 8;
                GL.DispatchCompute(groupsX, groupsY, 1);

                GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
            }

            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        /// <summary>
        /// Renders shadow depth passes for all draw call buckets using their corresponding specialized depth-only shaders.
        /// </summary>
        /// <param name="renderContext">The render context for this shadow pass.</param>
        /// <param name="depthOnlyShaders">A span of shaders indexed by <see cref="DepthOnlyProgram"/>.</param>
        /// <param name="drawCalls">The bucketed draw calls to render.</param>
        public static void RenderOpaqueShadows(RenderContext renderContext, Span<Shader> depthOnlyShaders, DepthOnlyDrawBuckets drawCalls)
        {
            renderContext.RenderPass = RenderPass.DepthOnly;

            foreach (var (program, calls) in drawCalls)
            {
                renderContext.ReplacementShader = depthOnlyShaders[(int)program];
                MeshBatchRenderer.Render(calls, renderContext);
            }
        }

        /// <summary>
        /// Renders the opaque pass, optionally with a depth prepass, followed by aggregate indirect draws and static overlay geometry.
        /// </summary>
        /// <param name="renderContext">The render context for this pass.</param>
        /// <param name="depthOnlyShaders">An optional span of depth-only shaders; when provided and <see cref="EnableDepthPrepass"/> is set, a depth prepass is performed.</param>
        public void RenderOpaqueLayer(RenderContext renderContext, Span<Shader> depthOnlyShaders = default)
        {
            var camera = renderContext.Camera;

            var depthPrepass = !depthOnlyShaders.IsEmpty && EnableDepthPrepass;

            if (DrawMeshletsIndirect)
            {
                // Memory barrier to ensure compute shader writes are visible to indirect draw commands
                GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit);
            }

            if (depthPrepass)
            {
                using (new GLDebugGroup("Depth Prepass"))
                {
                    GL.ColorMask(false, false, false, false);

                    renderContext.RenderPass = RenderPass.DepthOnly;
                    foreach (var (program, calls) in depthOnlyDraws)
                    {
                        renderContext.ReplacementShader = depthOnlyShaders[(int)program];
                        MeshBatchRenderer.Render(calls, renderContext);
                    }

                    GL.ColorMask(true, true, true, true);
                }

                using (new GLDebugGroup("Opaque Prepassed"))
                {
                    GL.DepthMask(false);
                    GL.DepthFunc(DepthFunction.Equal);

                    renderContext.RenderPass = RenderPass.OpaqueAggregate;
                    MeshBatchRenderer.Render(renderLists[renderContext.RenderPass], renderContext);

                    GL.DepthMask(true);
                    GL.DepthFunc(DepthFunction.Greater);
                }
            }

            if (!depthPrepass && DrawMeshletsIndirect)
            {
                using var _ = new GLDebugGroup("Meshlet Render");
                renderContext.RenderPass = RenderPass.OpaqueAggregate;
                MeshBatchRenderer.Render(renderLists[renderContext.RenderPass], renderContext);
            }

            using (new GLDebugGroup("Opaque Render"))
            {
                renderContext.RenderPass = RenderPass.Opaque;
                MeshBatchRenderer.Render(renderLists[renderContext.RenderPass], renderContext);
            }

            using (new GLDebugGroup("StaticOverlay Render"))
            {
                renderContext.RenderPass = RenderPass.StaticOverlay;
                MeshBatchRenderer.Render(renderLists[renderContext.RenderPass], renderContext);
            }
        }

        private bool occlusionDirty;

        static void ClearOccludedStateRecursive(Octree.Node node)
        {
            foreach (var child in node.Children)
            {
                child.OcclusionCulled = false;
                child.OcclusionQuerySubmitted = false;
                ClearOccludedStateRecursive(child);
            }
        }

        /// <summary>
        /// Submits GPU occlusion queries for static octree nodes using proxy geometry, to be retrieved the following frame.
        /// </summary>
        /// <param name="renderContext">The render context providing the camera position.</param>
        /// <param name="depthOnlyShader">The depth-only shader used to render the proxy bounding boxes.</param>
        public void RenderOcclusionProxies(RenderContext renderContext, Shader depthOnlyShader)
        {
            using var _ = new GLDebugGroup("Occlusion Tests");
            occlusionDirty = true;

            GL.ColorMask(false, false, false, false);
            GL.DepthMask(false);
            GL.Disable(EnableCap.CullFace);

            depthOnlyShader.Use();
            GL.BindVertexArray(RendererContext.MeshBufferCache.EmptyVAO);

            var maxTests = 1024;
            var startDepth = 3;
            var maxDepth = 8;
            TestOctantsRecursive(StaticOctree.Root, renderContext.Camera.Location, ref maxTests, startDepth, maxDepth);

            GL.UseProgram(0);
            GL.BindVertexArray(0);

            GL.ColorMask(true, true, true, true);
            GL.DepthMask(true);
            GL.Enable(EnableCap.CullFace);
        }

        private static void TestOctantsRecursive(Octree.Node parentNode, Vector3 cameraPosition, ref int maxTests, int startDepth, int maxDepth)
        {
            Span<bool> testChildren = stackalloc bool[8];

            if (maxTests < 0 || maxDepth < 0)
            {
                return;
            }

            for (var i = 0; i < parentNode.Children.Length; i++)
            {
                var node = parentNode.Children[i];
                if (node.FrustumCulled)
                {
                    node.OcclusionCulled = false;
                    node.OcclusionQuerySubmitted = false;
                    continue;
                }

                // This skips leaf octants, not sure if helps
                // if (!node.HasChildren)
                // {
                //     continue;
                // }

                if (node.Region.Intersects(new AABB(cameraPosition, 4f)))
                {
                    // if the camera is inside the octant, we can skip the occlusion test, however we still need to test the children
                    testChildren[i] = true;
                    node.OcclusionCulled = false;
                    node.OcclusionQuerySubmitted = false;
                    continue;
                }

                if (!node.OcclusionCulled)
                {
                    testChildren[i] = true;
                }

                if (startDepth > 0)
                {
                    continue;
                }

                // Queried on a previous frame, waiting for result
                if (node.OcclusionQuerySubmitted)
                {
                    //TryGetOcclusionTestResult(node);
                    continue;
                }

                // Octree node passed frustum test, contains subregions, and was not waiting for a previous query
                maxTests = SubmitOctreeNodeQuery(maxTests, maxDepth, node);
            }

            for (var i = 0; i < parentNode.Children.Length; i++)
            {
                if (testChildren[i])
                {
                    TestOctantsRecursive(parentNode.Children[i], cameraPosition, ref maxTests, startDepth - 1, maxDepth - 1);
                }
            }
        }

        private static int SubmitOctreeNodeQuery(int maxTests, int maxDepth, Octree.Node octreeNode)
        {
            if (octreeNode.OcclusionQueryHandle == -1)
            {
                octreeNode.OcclusionQueryHandle = GL.GenQuery();
            }

            octreeNode.OcclusionQuerySubmitted = true;
            maxTests--;

            GL.VertexAttrib4(
                0,
                octreeNode.Region.Min.X,
                octreeNode.Region.Min.Y,
                octreeNode.Region.Min.Z,
                octreeNode.Region.Size.X
            );

#if DEBUG
            GL.VertexAttribI2(1, maxDepth, maxTests);
#endif

            GL.BeginQuery(QueryTarget.AnySamplesPassedConservative, octreeNode.OcclusionQueryHandle);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
            GL.EndQuery(QueryTarget.AnySamplesPassedConservative);

            return maxTests;
        }

        /// <summary>
        /// Retrieves non-blocking GPU occlusion query results from the previous frame and marks octree nodes as occluded or visible.
        /// </summary>
        public void GetOcclusionTestResults()
        {
            if (!occlusionDirty)
            {
                return;
            }

            if (!EnableOcclusionQueries)
            {
                ClearOccludedStateRecursive(StaticOctree.Root);
                occlusionDirty = false;
                LastFrustum = -1;
                return;
            }

            static void CheckOcclusionQueries(Octree.Node root)
            {
                foreach (var child in root.Children)
                {
                    if (child.OcclusionQuerySubmitted)
                    {
                        TryGetOcclusionTestResult(child);
                    }

                    if (child.HasChildren)
                    {
                        CheckOcclusionQueries(child);
                    }
                }
            }

            CheckOcclusionQueries(StaticOctree.Root);
        }

        private static bool TryGetOcclusionTestResult(Octree.Node node)
        {
            var visible = -1;
            GL.GetQueryObject(
                node.OcclusionQueryHandle,
                GetQueryObjectParam.QueryResultNoWait,
                out visible
            );

            node.OcclusionCulled = visible == 0;
            node.OcclusionQuerySubmitted = visible == -1;
            return visible != -1;
        }

        /// <summary>Renders all translucent draw calls collected during <see cref="CollectSceneDrawCalls"/>.</summary>
        /// <param name="renderContext">The render context for this pass.</param>
        public void RenderTranslucentLayer(RenderContext renderContext)
        {
            using (new GLDebugGroup("Translucent Render"))
            {
                renderContext.RenderPass = RenderPass.Translucent;
                MeshBatchRenderer.Render(renderLists[RenderPass.Translucent], renderContext);
            }
        }

        /// <summary>Renders water draw calls collected during <see cref="CollectSceneDrawCalls"/>.</summary>
        /// <param name="renderContext">The render context for this pass.</param>
        public void RenderWaterLayer(RenderContext renderContext)
        {
            using (new GLDebugGroup("Fancy Water Render"))
            {
                renderContext.RenderPass = RenderPass.Water;
                MeshBatchRenderer.Render(renderLists[RenderPass.Water], renderContext);
            }
        }

        /// <summary>Renders all selected nodes using the outline shader to produce selection highlights.</summary>
        /// <param name="renderContext">The render context for this pass.</param>
        public void RenderOutlineLayer(RenderContext renderContext)
        {
            renderContext.RenderPass = RenderPass.Outline;
            renderContext.ReplacementShader = OutlineShader;

            MeshBatchRenderer.Render(renderLists[RenderPass.Outline], renderContext);

            renderContext.ReplacementShader = null;
        }

        /// <summary>
        /// Enables or disables scene nodes based on whether their layer name is present in the given set.
        /// </summary>
        /// <param name="layers">The set of layer names that should be visible.</param>
        public void SetEnabledLayers(HashSet<string> layers)
        {
            foreach (var renderer in AllNodes)
            {
                if (renderer.LayerName == null)
                {
                    renderer.LayerEnabled = false;
                    continue;
                }

                if (renderer.LayerName.StartsWith("LightProbeGrid", StringComparison.Ordinal))
                {
                    continue;
                }

                renderer.LayerEnabled = layers.Contains(renderer.LayerName);
            }
        }

        /// <summary>
        /// Marks the octree that owns the given node as dirty so it will be rebuilt on the next update.
        /// Also clears barn light shadow caches.
        /// </summary>
        /// <param name="node">The node whose owning octree should be dirtied.</param>
        /// <returns><see langword="true"/> if the node was found and its octree was dirtied; <see langword="false"/> if the node is not part of this scene.</returns>
        public bool MarkParentOctreeDirty(SceneNode node)
        {
            var nodeType = GetNodeTypeById(node.Id).Type;
            if (nodeType == NodeType.Unknown)
            {
                return false;
            }

            LightingInfo.ClearBarnShadowCache();

            var octree = nodeType == NodeType.Static ? StaticOctree : DynamicOctree;
            octree.Dirty = true;
            return true;
        }

        /// <summary>Rebuilds dirty static and dynamic octrees from their current node sets.</summary>
        public void UpdateOctrees()
        {
            LastFrustum = -1;

            if (StaticOctree.Dirty)
            {
                // static octree is tightly wrapped around the scene
                var maxBounds = new AABB();
                var hasBounds = false;

                foreach (var node in staticNodes)
                {
                    if (node.LayerEnabled)
                    {
                        maxBounds = hasBounds ? maxBounds.Union(node.BoundingBox) : node.BoundingBox;
                        hasBounds = true;
                    }
                }

                StaticOctree.Clear(maxBounds);

                foreach (var node in staticNodes)
                {
                    if (node.LayerEnabled)
                    {
                        StaticOctree.Insert(node);
                    }
                }

                StaticOctree.DebugRenderer?.StaticBuild();
                StaticOctree.Dirty = false;
            }

            if (DynamicOctree.Dirty)
            {
                DynamicOctree.Clear();

                foreach (var node in dynamicNodes)
                {
                    if (node.LayerEnabled)
                    {
                        DynamicOctree.Insert(node);
                    }
                }

                DynamicOctree.Dirty = false;
            }
        }

        /// <summary>Assigns sequential scene-unique IDs to all static and dynamic nodes, starting at 1 (0 is reserved as an invalid ID).</summary>
        public void UpdateNodeIndices()
        {
            uint index = 1; // 0 is reserved for invalid index

            foreach (var node in staticNodes)
            {
                node.Id = index;
                index++;
            }

            foreach (var node in dynamicNodes)
            {
                node.Id = index;
                index++;
            }
        }

        /// <summary>Writes the scene fog parameters into the provided view constants structure.</summary>
        /// <param name="viewConstants">The view constants to update with fog uniforms.</param>
        public void SetFogConstants(ViewConstants viewConstants)
        {
            FogInfo.SetFogUniforms(viewConstants, FogEnabled);
        }

        /// <summary>
        /// Assigns each scene node its best-matching light probe volume and uploads probe data to the GPU light probe uniform buffer.
        /// </summary>
        public void CalculateLightProbeBindings()
        {
            Debug.Assert(lpvBuffer is not null);

            if (LightingInfo.LightProbes.Count == 0)
            {
                return;
            }

            LightingInfo.LightProbes.Sort((a, b) => a.HandShake.CompareTo(b.HandShake));

            foreach (var node in AllNodes)
            {
                var precomputedHandshake = node.LightProbeVolumePrecomputedHandshake;
                if (precomputedHandshake == 0)
                {
                    continue;
                }

                if (LightingInfo.LightmapGameVersionNumber == 0 && precomputedHandshake <= LightingInfo.LightProbes.Count)
                {
                    // SteamVR Home node handshake as probe index
                    node.LightProbeBinding = LightingInfo.LightProbes[precomputedHandshake - 1];
                    continue;
                }

                if (LightingInfo.ProbeHandshakes.TryGetValue(precomputedHandshake, out var precomputedProbe))
                {
                    node.LightProbeBinding = precomputedProbe;
                    continue;
                }
            }

            var isAtlas = LightingInfo.LightProbeType == LightProbeType.ProbeAtlas;

            static bool IsValid(SceneLightProbe probe, bool isAtlas) => isAtlas switch
            {
                true => probe is { Irradiance: not null, DirectLightShadows: not null },
                false => true,
            };

            var sortedLightProbes = LightingInfo.LightProbes
                .Where(probe => IsValid(probe, isAtlas))
                .OrderByDescending(static lpv => lpv.IndoorOutdoorLevel)
                .ThenBy(static lpv => lpv.AtlasSize.LengthSquared())
                .ToList();

            var nodes = new List<SceneNode>();

            var i = 0;
            foreach (var probe in sortedLightProbes)
            {
                StaticOctree.Root.Query(probe.BoundingBox, nodes);
                DynamicOctree.Root.Query(probe.BoundingBox, nodes); // TODO: This should actually be done dynamically

                foreach (var node in nodes)
                {
                    node.LightProbeBinding ??= probe;
                }

                probe.ShaderIndex = i;
                var data = probe.CalculateGpuProbeData(isAtlas);
                lpvBuffer.Data.Probes[i] = data;

                nodes.Clear();
                i++;

                if (i == LightProbeVolumeArray.MAX_PROBES)
                {
                    break;
                }
            }

            if (sortedLightProbes.Count == 0)
            {
                // remove baked lighting from probe attribute?
                return;
            }

            // Fall back to the global probe
            var globalProbe = sortedLightProbes[^1];

            foreach (var node in AllNodes)
            {
                node.LightProbeBinding ??= globalProbe;
            }
        }

        /// <summary>
        /// Assigns environment maps to scene nodes based on spatial overlap and precomputed handshakes, and uploads env map data to the GPU uniform buffer.
        /// </summary>
        public void CalculateEnvironmentMaps()
        {
            if (LightingInfo.EnvMaps.Count == 0)
            {
                return;
            }

            var firstTexture = LightingInfo.EnvMaps.First().EnvMapTexture;

            LightingInfo.LightingData.EnvMapSizeConstants = new Vector4(firstTexture.NumMipLevels - 1, firstTexture.Depth, 0, 0);

            int IndoorPriorityCompare(SceneEnvMap a, SceneEnvMap b) => b.IndoorOutdoorLevel.CompareTo(a.IndoorOutdoorLevel);
            int HandShakeCompare(SceneEnvMap a, SceneEnvMap b) => a.HandShake.CompareTo(b.HandShake);

            LightingInfo.EnvMaps.Sort(LightingInfo.CubemapType switch
            {
                CubemapType.CubemapArray => IndoorPriorityCompare,
                _ => HandShakeCompare
            });

            var nodes = new List<SceneNode>();
            var i = 0;

            foreach (var envMap in LightingInfo.EnvMaps)
            {
                if (i >= EnvMapArray.MAX_ENVMAPS)
                {
                    RendererContext.Logger.LogError("Envmap array index {Index} is too large, skipping! Max: {MaxEnvMaps}", i, EnvMapArray.MAX_ENVMAPS);
                    continue;
                }

                StaticOctree.Root.Query(envMap.BoundingBox, nodes);
                DynamicOctree.Root.Query(envMap.BoundingBox, nodes); // TODO: This should actually be done dynamically

                foreach (var node in nodes)
                {
                    node.EnvMaps.Add(envMap);
                }

                UpdateGpuEnvmapData(envMap, i);
                envMap.ShaderIndex = i;
                i++;

                nodes.Clear();
            }

            foreach (var node in AllNodes)
            {
                var precomputedHandshake = node.CubeMapPrecomputedHandshake;
                SceneEnvMap? preComputed = default;

                if (precomputedHandshake > 0)
                {
                    if (LightingInfo.CubemapType == CubemapType.IndividualCubemaps
                        && precomputedHandshake <= LightingInfo.EnvMaps.Count)
                    {
                        // SteamVR Home node handshake as envmap index
                        node.EnvMaps.Clear();
                        node.EnvMaps.Add(LightingInfo.EnvMaps[precomputedHandshake - 1]);
                    }
                    else if (LightingInfo.EnvMapHandshakes.TryGetValue(precomputedHandshake, out preComputed))
                    {
                        node.EnvMaps.Clear();
                        node.EnvMaps.Add(preComputed);
                    }
                    else
                    {
#if DEBUG
                        RendererContext.Logger.LogDebug("A envmap with handshake [{Handshake}] does not exist for node at {Center}", precomputedHandshake, node.BoundingBox.Center);
#endif
                    }
                }

                var lightingOrigin = node.LightingOrigin ?? Vector3.Zero;
                if (node.LightingOrigin.HasValue)
                {
                    if (LightingInfo.LightmapGameVersionNumber <= 1)
                    {
                        node.EnvMaps.Clear();
                        foreach (var envMap in LightingInfo.EnvMaps)
                        {
                            if (envMap.BoundingBox.Contains(lightingOrigin))
                            {
                                node.EnvMaps.Add(envMap);
                            }
                        }
                    }
                    else if (LightingInfo.LightmapGameVersionNumber >= 2)
                    {
                        // CS2 Mapping docs say that the lighting origin should point at an exact cubemap.
                        foreach (var envMap in LightingInfo.EnvMaps)
                        {
                            if ((envMap.Transform.Translation - lightingOrigin).LengthSquared() < 0.01f)
                            {
                                node.EnvMaps.Clear();
                                node.EnvMaps.Add(envMap);
                                break;
                            }
                        }
                    }
                }

                node.EnvMaps.Sort((a, b) =>
                {
                    var result = b.IndoorOutdoorLevel.CompareTo(a.IndoorOutdoorLevel);
                    if (result != 0)
                    {
                        return result;
                    }

                    var aDistance = Vector3.Distance(node.BoundingBox.Center, a.BoundingBox.Center);
                    var bDistance = Vector3.Distance(node.BoundingBox.Center, b.BoundingBox.Center);

                    return aDistance.CompareTo(bDistance);
                });

                node.ShaderEnvMapVisibility = node.ShaderEnvMapVisibility.Store(node.EnvMaps);

#if DEBUG
                if (preComputed != default)
                {
                    var vrfComputed = node.EnvMaps.FirstOrDefault();
                    if (vrfComputed is null)
                    {
                        RendererContext.Logger.LogDebug("Could not find any envmaps for node {DebugName}. Valve precomputed envmap is at {Center} [{Handshake}]", node.DebugName, preComputed.BoundingBox.Center, precomputedHandshake);
                        continue;
                    }

                    if (vrfComputed.HandShake == precomputedHandshake)
                    {
                        continue;
                    }

                    var vrfDistance = Vector3.Distance(lightingOrigin, vrfComputed.BoundingBox.Center);
                    var preComputedDistance = Vector3.Distance(lightingOrigin, LightingInfo.EnvMapHandshakes[precomputedHandshake].BoundingBox.Center);

                    var anyIndex = node.EnvMaps.FindIndex(x => x.HandShake == precomputedHandshake);

                    RendererContext.Logger.LogDebug("Topmost calculated envmap doesn't match with the precomputed one (dists: vrf={VrfDistance} s2={PreComputedDistance}) for node at {Center} [{Handshake}]{IterateInfo}",
                        vrfDistance, preComputedDistance, node.BoundingBox.Center, precomputedHandshake,
                        anyIndex > 0 ? $" (however it's still binned at a higher iterate index {anyIndex})" : string.Empty);
                }
#endif
                if (LightingInfo.CubemapType == CubemapType.CubemapArray)
                {
                    node.EnvMaps.Clear(); // no longer needed
                    node.EnvMaps.TrimExcess();
                }
            }
        }

        private void UpdateGpuEnvmapData(SceneEnvMap envMap, int index)
        {
            Debug.Assert(envMapBuffer is not null);

            if (!Matrix4x4.Invert(envMap.Transform, out var worldToLocal))
            {
                throw new InvalidOperationException("Matrix invert failed");
            }

            var boundsExtend = new Vector3(0.02f);

            envMapBuffer.Data.EnvMaps[index] = new EnvMapData
            {
                WorldToLocal = worldToLocal,
                BoxMins = envMap.LocalBoundingBox.Min - boundsExtend,
                ArrayIndex = (uint)envMap.ArrayIndex,
                BoxMaxs = envMap.LocalBoundingBox.Max + boundsExtend,
                InvEdgeWidth = new Vector4(Vector3.One / (envMap.EdgeFadeDists + boundsExtend), 0),
                Origin = envMap.Transform.Translation,
                ProjectionType = (uint)envMap.ProjectionMode,
                Color = envMap.Tint,
                NormalizationSH = new Vector4(0, 0, 0, 1)
            };
        }

        /// <summary>
        /// Applies a rotation delta to the first environment map's world-to-local transform to simulate sun angle changes.
        /// </summary>
        /// <param name="delta">The rotation matrix to multiply into the env map transform.</param>
        public void AdjustEnvMapSunAngle(Matrix4x4 delta)
        {
            Debug.Assert(envMapBuffer != null);

            envMapBuffer.Data.EnvMaps[0].WorldToLocal *= delta;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Releases managed GPU resources owned by the scene.</summary>
        /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                frustumBuffer?.Dispose();
                lightingBuffer?.Dispose();
                lpvBuffer?.Dispose();
                envMapBuffer?.Dispose();
                LightingInfo.DisposeBarnLights();
            }
        }
    }
}
