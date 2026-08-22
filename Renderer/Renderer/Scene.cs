using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.Entities;
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

            /// <summary> Gets the renderer's total elapsed time in seconds.</summary>
            public float Uptime { get; init; }
        }

        /// <summary>
        /// Context data passed to scene nodes and renderers during draw calls.
        /// </summary>
        public struct RenderContext
        {
            /// <summary>Gets or sets the scene being rendered.</summary>
            public required Scene Scene { get; set; }

            /// <summary>Gets or sets the camera providing view and projection matrices.</summary>
            public required Camera Camera { get; set; }

            /// <summary>Gets or sets the framebuffer that is the render target.</summary>
            public required Framebuffer Framebuffer { get; set; }

            /// <summary>Gets or sets the current render pass being executed.</summary>
            public RenderPass RenderPass { get; set; }

            /// <summary>Gets or sets which layer the pass is drawing into.</summary>
            public RenderLayer Layer { get; set; }

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

        /// <summary>
        /// Gets the entity context for this scene. Simulated entities live here and are ticked by
        /// <see cref="Update"/> before the scene nodes are updated.
        /// </summary>
        public EntitySystem EntitySystem { get; }

        /// <summary>Gets or sets the voxel visibility data.</summary>
        public VoxelVisibility? VoxelVisibility { get; set; }

        /// <summary>Gets or sets whether PVS culling is enabled for this scene.</summary>
        public bool EnablePvsCulling { get; set; }

        /// <summary>Gets or sets the PVS bitfield for the cluster at the current camera position.</summary>
        public byte[]? CurrentFramePvs { get; set; }

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

        /// <summary>Gets or sets the GPU buffer mapping each aggregate indirect draw command to the meshlet it draws.</summary>
        public StorageBuffer? CommandMeshletsGpu { get; set; }

        /// <summary>Gets or sets the GPU buffer holding per-object LOD masks and setup indices.</summary>
        public StorageBuffer? ObjectLodGpu { get; set; }

        /// <summary>Gets or sets the GPU buffer holding one mask per LOD setup in the scene, with the bit of the level that setup selected this frame set.</summary>
        public StorageBuffer? ActiveLodBitsGpu { get; set; }

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

        /// <summary>Gets the tile and depth bin cull passes for this scene.</summary>
        public LightBinner LightBinner { get; }

        private Shader? DepthPyramidShader;
        private Shader? DepthPyramidNpotShader;
        /// <summary>Gets the hierarchical depth pyramid texture used for GPU occlusion culling.</summary>
        public RenderTexture? DepthPyramid { get; internal set; }

        /// <summary>Gets the view-projection matrix that was used when the depth pyramid was last generated.</summary>
        public Matrix4x4 DepthPyramidViewProjection { get; internal set; }

        /// <summary>Gets whether the depth pyramid is current and safe to use for occlusion culling this frame.</summary>
        public bool DepthPyramidValid { get; internal set; }

        /// <summary>Gets the renderer context providing shared GPU resources and shader loading.</summary>
        public RendererContext RendererContext { get; }

        /// <summary>Gets the octree used to spatially partition static scene nodes.</summary>
        public Octree StaticOctree { get; }

        /// <summary>Gets the flat spatial set holding dynamic scene nodes.</summary>
        public SpatialNodeSet DynamicOctree { get; } = new();

        /// <summary>Gets or sets whether materials flagged as tools-only are rendered.</summary>
        public bool ShowToolsMaterials { get; set; }

        /// <summary>Gets or sets whether scene fog is applied during rendering.</summary>
        public bool FogEnabled { get; set; } = true;

        /// <summary>Gets or sets whether a depth-only prepass is performed before the opaque pass to reduce overdraw.</summary>
        public bool EnableDepthPrepass { get; set; }

        /// <summary>Gets or sets whether GPU occlusion culling is enabled.</summary>
        public bool EnableOcclusionCulling { get; set; } = true;

        /// <summary>Gets or sets whether occlusion culling debug visualization is active.</summary>
        public bool OcclusionDebugEnabled { get; set; }

        /// <summary>Gets or sets the occlusion debug renderer, or <see langword="null"/> if not initialized.</summary>
        public OcclusionDebugRenderer? OcclusionDebug { get; set; }

        /// <summary>Gets or sets whether GPU indirect drawing is used for eligible aggregate scene nodes.</summary>
        public bool EnableIndirectDraws { get; set; } = true;

        /// <summary>Whether this is the 3d sky scene, which draws behind everything the main scene draws.</summary>
        internal bool IsSkybox => LightingInfo.LightingData.IsSkybox != 0u;

        /// <summary>Gets or sets whether GPU draw compaction is applied after frustum culling to remove empty indirect draw commands.</summary>
        public bool EnableCompaction { get; set; } = true;

        /// <summary>Gets or sets whether lights are binned to screen tiles so shaders iterate only what reaches them.</summary>
        /// <remarks>
        /// <see cref="Renderer"/> reads the main scene's copy when driving every binner, so that one also
        /// governs the 3D skybox. The skybox scene's own copy is never what the viewer toggles.
        /// </remarks>
        public bool EnableTiledLightCulling { get; set; } = true;

        internal bool DrawMeshletsIndirect { get; private set; }
        internal bool CompactMeshletDraws { get; private set; }

        /// <summary>Gets all static and dynamic scene nodes in the order they were added.</summary>
        public IEnumerable<SceneNode> AllNodes => staticNodes.Concat(dynamicNodes);

        private readonly List<SceneNode> staticNodes = [];
        private readonly List<SceneNode> dynamicNodes = [];

        /// <summary>
        /// The dynamic particle nodes, kept alongside <see cref="dynamicNodes"/> as nodes come and go
        /// so that stepping them needs no per-frame partitioning pass.
        /// </summary>
        private readonly List<ParticleSceneNode> particleNodes = [];

        /// <summary>How many particle nodes make the thread pool worth its overhead.</summary>
        private const int ParallelParticleUpdateThreshold = 8;

        private readonly List<SceneNode> CullResults = [];
        private int StaticCount;
        private int LastFrustum = -1;

        private List<SceneNode> CulledShadowNodes { get; } = [];
        private readonly List<RenderableMesh> listWithSingleMesh = [null!];

        private ObjectDataStandard[]? instanceDataCpu;
        private readonly List<SceneAggregate> lodAggregates = [];
        private uint[] activeLodBits = [];

        private Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>>? barnShadowDrawCalls;

        // Bound probes in precedence order: most indoor first, then smallest, so the first volume
        // containing a point is the best one
        private List<SceneLightProbe>? boundLightProbes;

        private Shader? OutlineShader;

        /// <summary>
        /// Initializes a new scene with the given renderer context and optional spatial size hint.
        /// </summary>
        /// <param name="context">The renderer context providing shared GPU resources.</param>
        /// <param name="sizeHint">The initial world-space extent used to size the static octree.</param>
        public Scene(RendererContext context, float sizeHint = 32768)
        {
            RendererContext = context;
            StaticOctree = new(sizeHint);

            LightingInfo = new(this);
            LightBinner = new(this);
            EntitySystem = new(this);
        }

        /// <summary>
        /// Performs one-time GPU setup: builds acceleration structures, allocates buffers, computes light probe and environment map bindings, and loads internal shaders.
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

            OutlineShader = RendererContext.ShaderLoader.LoadShader("outline");
            FrustumCullShader = RendererContext.ShaderLoader.LoadShader("frustum_cull");
            CompactionShader = RendererContext.ShaderLoader.LoadShader("compact_indirect_draws");
            DepthPyramidShader = RendererContext.ShaderLoader.LoadShader("depth_pyramid");
            DepthPyramidNpotShader = RendererContext.ShaderLoader.LoadShader("depth_pyramid", ("D_NPOT_DOWNSAMPLE", 1));
            LightBinner.LoadShaders();

            // set render lists to their max capacity
            CollectSceneDrawCalls(new Camera(), Frustum.CreateEmpty());
            SetupSceneShadows(new Camera(), -1);
        }

        /// <summary>
        /// Adds a node to the scene, placing it in either the static or dynamic partition.
        /// </summary>
        /// <param name="node">The node to add.</param>
        /// <param name="dynamic">When <see langword="true"/>, the node is placed in <see cref="DynamicOctree"/>; otherwise in <see cref="StaticOctree"/>.</param>
        public void Add(SceneNode node, bool dynamic)
        {
            if (dynamic)
            {
                dynamicNodes.Add(node);
                DynamicOctree.Dirty = true;

                if (node is ParticleSceneNode particleNode)
                {
                    particleNodes.Add(particleNode);
                }
            }
            else
            {
                staticNodes.Add(node);
                StaticOctree.Dirty = true;
            }
        }

        /// <summary>
        /// Removes a node from the scene's static or dynamic partition.
        /// </summary>
        /// <param name="node">The node to remove.</param>
        /// <param name="dynamic">When <see langword="true"/>, removes from the dynamic partition; otherwise the static partition.</param>
        public void Remove(SceneNode node, bool dynamic)
        {
            if (dynamic)
            {
                dynamicNodes.Remove(node);
                DynamicOctree.Dirty = true;

                if (node is ParticleSceneNode particleNode)
                {
                    particleNodes.Remove(particleNode);
                }
            }
            else
            {
                staticNodes.Remove(node);
                StaticOctree.Dirty = true;
            }
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
            particleNodes.Clear();

            foreach (var item in staticNodes)
            {
                item.Delete();
            }
            staticNodes.Clear();

            EntitySystem.Clear();

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

                return node.EntityData.TryGetValue(keyToFind, out var value)
                    && value.ValueType == ValveKeyValue.KVValueType.String
                    && valueToFind.Equals((string)value, StringComparison.OrdinalIgnoreCase);
            }

            return staticNodes.Find(IsMatchingEntity) ?? dynamicNodes.Find(IsMatchingEntity);
        }

        /// <summary>
        /// Finds the first scene node whose entity name matches with the given pattern.
        /// </summary>
        /// <param name="pattern">Targetname to match against, may contain wildcards: `*` and `?` (e.g. <c>door_*</c>).</param>
        /// <returns>The matching <see cref="SceneNode"/>, or <see langword="null"/> if not found.</returns>
        public SceneNode? FindNodeByTargetName(string pattern)
        {
            bool IsMatchingEntity(SceneNode node)
            {
                if (node.EntityData == null)
                {
                    return false;
                }

                return node.EntityData.TryGetValue("targetname", out var value)
                    && value.ValueType == ValveKeyValue.KVValueType.String
                    && EntityLump.EntityNameMatches(pattern, (string)value);
            }

            return staticNodes.Find(IsMatchingEntity) ?? dynamicNodes.Find(IsMatchingEntity);
        }

        /// <summary>
        /// Steps this frame's particle systems. One system's simulation touches nothing another's
        /// does: each owns its particle storage, its control points and its own random stream, and a
        /// system's children are stepped from inside it rather than from here. That makes the systems
        /// independent of one another, so they are stepped across the thread pool once there are
        /// enough to be worth it.
        /// </summary>
        private void UpdateParticleNodes(Scene.UpdateContext updateContext)
        {
            // A node with a parent is stepped by that parent, the same rule the loop above follows.
            // Parent is assigned after the node joins the scene, so it is read here and not at insertion.
            void Step(ParticleSceneNode node)
            {
                if (node.Parent == null)
                {
                    node.Update(updateContext);
                }
            }

            if (particleNodes.Count < ParallelParticleUpdateThreshold)
            {
                foreach (var node in particleNodes)
                {
                    Step(node);
                }

                return;
            }

            Parallel.ForEach(particleNodes, Step);
        }

        /// <summary>
        /// Updates all scene nodes for the current frame, advancing animations and rebuilding spatial sets and GPU buffers if the scene changed.
        /// </summary>
        /// <param name="updateContext">Per-frame context data including camera and timestep.</param>
        public void Update(Scene.UpdateContext updateContext)
        {
            // Entities simulate on their own fixed tick, then their scene nodes pick the result up below
            EntitySystem.Update(updateContext.Timestep);

            foreach (var node in staticNodes)
            {
                node.Update(updateContext);
            }

            foreach (var node in dynamicNodes)
            {
                if (node.Parent != null)
                {
                    continue; // child nodes are updated by their parent
                }

                if (node is ParticleSceneNode)
                {
                    continue; // stepped together below, once the nodes they may be bound to have settled
                }

                node.Update(updateContext);
            }

            UpdateParticleNodes(updateContext);

            foreach (var node in dynamicNodes)
            {
                DynamicOctree.Update(node);
            }

            UpdateDynamicInstanceData();

            if (StaticOctree.Dirty || DynamicOctree.Dirty)
            {
                // Indirect draw commands bake node ids, so recreate them only after reindexing
                var staticDirty = StaticOctree.Dirty;

                UpdateOctrees();
                UpdateNodeIndices();
                CreateInstanceTransformBuffers(deletePrevious: true);

                if (staticDirty)
                {
                    // a static node was disabled, enabled, added, or removed
                    CreateIndirectDrawBuffers(true);
                }
            }

            UpdateActiveLodBits();
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
                ObjectLodGpu?.Delete();
                ActiveLodBitsGpu?.Delete();
            }

            var nodes = AllNodes.ToList();

            if (nodes.Count == 0)
            {
                return;
            }

            var maxId = nodes.Max(n => n.Id);

            // Setups are numbered scene wide so a fragment can name its own with a single index
            lodAggregates.Clear();
            var lodSetupCount = 0;

            foreach (var node in nodes)
            {
                if (node is SceneAggregate { LodSetups.Length: > 0 } lodAggregate)
                {
                    lodAggregate.LodSetupBase = lodSetupCount;
                    lodSetupCount += lodAggregate.LodSetups.Length;
                    lodAggregates.Add(lodAggregate);
                }
            }

            var instanceData = new ObjectDataStandard[maxId + 1];
            var lodData = new ObjectLodInfo[maxId + 1];
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
                    // Content can author out-of-range tints; the packed byte color can only represent [0, 1].
                    instanceTint = Vector4.Clamp(fragment.RenderMesh.Tint * fragment.DrawCall.TintColor * fragment.Tint, Vector4.Zero, Vector4.One);
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

                // Everything else keeps a zero mask, which the cull shader reads as always drawn
                if (node is SceneAggregate.Fragment { LodGroupMask: > 0 } lodFragment && lodFragment.LodSetupIndex >= 0)
                {
                    lodData[node.Id] = new ObjectLodInfo
                    {
                        LodGroupMask = lodFragment.LodGroupMask,
                        LodSetupIndex = (uint)(lodFragment.Parent.LodSetupBase + lodFragment.LodSetupIndex),
                    };
                }

                instanceData[node.Id] = new ObjectDataStandard
                {
                    TintAlpha = Color32.FromVector4(instanceTint).PackedValue,
                    TransformIndex = transformIndex,
                    VisibleLPV = (uint)(node.LightProbeBinding?.ShaderIndex ?? 0)
                        | (node.ShaderEnvMapVisibility.GetFirstShaderIndex() << 16),
                    EnvMapVisibility = node.ShaderEnvMapVisibility,
                    Identification = node.Id,
                };
            }

            InstanceBufferGpu = new StorageBuffer(ReservedBufferSlots.Objects, nameof(ReservedBufferSlots.Objects));
            TransformBufferGpu = new StorageBuffer(ReservedBufferSlots.Transforms, nameof(ReservedBufferSlots.Transforms));

            InstanceBufferGpu.Create(instanceData, BufferUsage.Static);
            TransformBufferGpu.Create(CollectionsMarshal.AsSpan(transformData), BufferUsage.Static);

            activeLodBits = new uint[Math.Max(1, lodSetupCount)];
            Array.Fill(activeLodBits, 1u);

            ObjectLodGpu = new StorageBuffer(ReservedBufferSlots.BufferSlot2, "ObjectLod");
            ActiveLodBitsGpu = new StorageBuffer(ReservedBufferSlots.BufferSlot3, "ActiveLodBits");

            ObjectLodGpu.Create(lodData, BufferUsage.Static);
            ActiveLodBitsGpu.Create(activeLodBits, BufferUsage.Dynamic);

            instanceDataCpu = instanceData;
        }


        /// <summary>
        /// Uploads the LOD level each setup selected this frame, which the cull shader tests fragments against.
        /// </summary>
        private void UpdateActiveLodBits()
        {
            if (lodAggregates.Count == 0 || ActiveLodBitsGpu == null)
            {
                return;
            }

            foreach (var aggregate in lodAggregates)
            {
                aggregate.WriteActiveLodBits(activeLodBits);
            }

            ActiveLodBitsGpu.Update<uint>(activeLodBits, 0);
        }

        private void CreateIndirectDrawBuffers(bool deletePrevious = false)
        {
            var aggregateSceneNodes = staticNodes.OfType<SceneAggregate>().Where(agg => agg.CanDrawIndirect).ToList();
            var aggregateDrawCallCount = aggregateSceneNodes.Sum(agg => agg.RenderMesh.DrawCallsOpaque.Count);
            var aggregateMeshletCount = aggregateSceneNodes.Sum(agg => agg.RenderMesh.Meshlets.Count);

            // Instanced fragments reuse one draw call with a transform each, so a fragment issues its own
            // commands but shares the cull data they point at
            var aggregateCommandCount = aggregateSceneNodes.Sum(agg => agg.Fragments.Sum(f => f.DrawCall.NumMeshlets));

            if (aggregateMeshletCount == 0)
            {
                return;
            }

            if (deletePrevious)
            {
                DrawBoundsGpu?.Delete();
                MeshletDataGpu?.Delete();
                CommandMeshletsGpu?.Delete();
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
                    foreach (var drawCall in agg.RenderMesh.DrawCallsOpaque)
                    {
                        // the cull shader transforms these, for instanced fragments and the 3d skybox
                        var bounds = drawCall.DrawBounds ?? agg.RenderMesh.BoundingBox;

                        drawBounds[index].Min = bounds.Min;
                        drawBounds[index].Max = bounds.Max;
                        index++;
                    }
                }

                DrawBoundsGpu = new StorageBuffer(ReservedBufferSlots.AggregateDrawBounds, nameof(ReservedBufferSlots.AggregateDrawBounds));
                DrawBoundsGpu.Create(drawBounds, BufferUsage.Static);
            }

            // meshlets
            {
                var meshletDataGpu = new MeshletCullInfo[aggregateMeshletCount];
                var commandMeshlets = new uint[aggregateCommandCount];
                var indirectDrawsGpu = new DrawElementsIndirectCommand[aggregateCommandCount];

                // Commands are laid out fragment by fragment, so each draw call multidraws its
                // [FirstMeshlet, FirstMeshlet + NumMeshlets) range once per fragment drawing it
                var sceneDrawCount = 0;
                var sceneMeshletCount = 0;
                var sceneCommandCount = 0;
                var compactionRequestList = new List<uint>();

                foreach (var agg in aggregateSceneNodes)
                {
                    var aggregateCommandStart = sceneCommandCount;

                    agg.IndirectDrawByteOffset = aggregateCommandStart * Unsafe.SizeOf<DrawElementsIndirectCommand>();
                    agg.CompactionIndex = compactionRequestList.Count / 2;

                    // Cull data is shared by every fragment drawing the draw call, so it stays in aggregate space
                    for (var drawCallIndex = 0; drawCallIndex < agg.RenderMesh.DrawCallsOpaque.Count; drawCallIndex++)
                    {
                        var sharedCall = agg.RenderMesh.DrawCallsOpaque[drawCallIndex];
                        var lastMeshlet = sharedCall.FirstMeshlet + sharedCall.NumMeshlets;

                        for (var meshletIndex = sharedCall.FirstMeshlet; meshletIndex < lastMeshlet; meshletIndex++)
                        {
                            meshletDataGpu[sceneMeshletCount + meshletIndex] = new MeshletCullInfo
                            {
                                Bounds = agg.RenderMesh.Meshlets[meshletIndex].PackedAABB,
                                Cone = agg.RenderMesh.Meshlets[meshletIndex].CullingData,
                                ParentDrawBoundsIndex = (uint)(sceneDrawCount + drawCallIndex),
                            };
                        }
                    }

                    foreach (var fragment in agg.Fragments)
                    {
                        var fragmentInstanceId = fragment.Id;
                        var drawCall = fragment.DrawCall;

                        var start = drawCall.FirstMeshlet;
                        var stop = start + drawCall.NumMeshlets;

                        for (var drawMeshletIndex = start; drawMeshletIndex < stop; drawMeshletIndex++)
                        {
                            var meshlet = agg.RenderMesh.Meshlets[drawMeshletIndex];
                            var commandIndex = sceneCommandCount++;

                            commandMeshlets[commandIndex] = (uint)(sceneMeshletCount + drawMeshletIndex);

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

                            indirectDrawsGpu[commandIndex] = new DrawElementsIndirectCommand
                            {
                                Count = count,
                                InstanceCount = 1,
                                FirstIndex = firstIndex,
                                BaseVertex = drawCall.BaseVertex,
                                BaseInstance = fragmentInstanceId,
                            };
                        }
                    }

                    agg.IndirectDrawCount = sceneCommandCount - aggregateCommandStart;

                    compactionRequestList.Add((uint)agg.IndirectDrawCount);
                    compactionRequestList.Add((uint)aggregateCommandStart);

                    sceneMeshletCount += agg.RenderMesh.Meshlets.Count;
                    sceneDrawCount += agg.RenderMesh.DrawCallsOpaque.Count;
                }

                SceneMeshletCount = sceneCommandCount;

                CommandMeshletsGpu = new StorageBuffer(ReservedBufferSlots.AggregateCommandMeshlets, nameof(ReservedBufferSlots.AggregateCommandMeshlets));
                CommandMeshletsGpu.Create(commandMeshlets, BufferUsage.Static);

                MeshletDataGpu = new StorageBuffer(ReservedBufferSlots.AggregateMeshlets, nameof(ReservedBufferSlots.AggregateMeshlets));
                IndirectDrawsGpu = new StorageBuffer(ReservedBufferSlots.AggregateDraws, nameof(ReservedBufferSlots.AggregateDraws));

                MeshletDataGpu.Create(meshletDataGpu, BufferUsage.Static);
                IndirectDrawsGpu.Create(indirectDrawsGpu, BufferUsage.GpuOnly);

                // Create compaction buffers
                CompactedDrawsGpu = new StorageBuffer(ReservedBufferSlots.CompactedDraws, nameof(ReservedBufferSlots.CompactedDraws));
                CompactedDrawsGpu.Create(indirectDrawsGpu, BufferUsage.GpuOnly);

                var compactedCounts = new uint[compactionRequestList.Count / 2];

                for (var request = 0; request < compactedCounts.Length; request++)
                {
                    compactedCounts[request] = compactionRequestList[request * 2];
                }

                CompactedCountsGpu = new StorageBuffer(ReservedBufferSlots.CompactedCounts, nameof(ReservedBufferSlots.CompactedCounts));
                CompactedCountsGpu.Create(compactedCounts, BufferUsage.GpuOnly);

                CompactionRequestsGpu = new StorageBuffer(ReservedBufferSlots.BufferSlot2, "CompactionRequests");
                CompactionRequestsGpu.Create(compactionRequestList, BufferUsage.Static);
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

        /// <summary>Updates the lighting buffer, then binds the lighting, environment map, light probe, and barn light buffers to their reserved GPU binding slots.</summary>
        public void SetSceneBuffers()
        {
            Debug.Assert(lightingBuffer is not null && envMapBuffer is not null && lpvBuffer is not null);

            lightingBuffer.Update();
            lightingBuffer.BindBufferBase();
            envMapBuffer.BindBufferBase();
            lpvBuffer.BindBufferBase();
            LightingInfo.BindBarnLightBuffer();

            LightBinner.Bind();
        }


        /// <summary>
        /// Returns all scene nodes whose bounding boxes intersect the given frustum, caching static results across frames when the frustum is unchanged.
        /// </summary>
        /// <param name="frustum">The view frustum to test against.</param>
        /// <returns>A list of visible scene nodes (valid until the next call to this method).</returns>
        public List<SceneNode> GetFrustumCullResults(Frustum frustum)
        {
            var currentFrustum = frustum.GetHashCode();

            // Optimization: Do not clear static culled results from last frame if the frustum did not change
            if (LastFrustum != currentFrustum)
            {
                LastFrustum = currentFrustum;

                CullResults.Clear();
                CullResults.Capacity = staticNodes.Count + dynamicNodes.Count + 100;

                StaticOctree.Query(frustum, CullResults);
                StaticCount = CullResults.Count;
            }
            else
            {
                CullResults.RemoveRange(StaticCount, CullResults.Count - StaticCount);
            }

            DynamicOctree.Query(frustum, CullResults);
            return CullResults;
        }

        /// <summary>Gets or sets whether any translucent material in the collected draw calls samples the scene color texture.</summary>
        public bool WantsSceneColor { get; set; }

        /// <summary>Gets or sets whether any translucent material in the collected draw calls samples the scene depth texture.</summary>
        public bool WantsSceneDepth { get; set; }

        /// <summary>Gets whether there are any selected nodes queued for outline rendering.</summary>
        public bool HasOutlineObjects => renderLists[RenderPass.Outline].Count > 0;

        /// <summary>Gets whether anything is queued to draw into the water effects map this frame.</summary>
        public bool HasWaterEffects => waterEffectsRenderList.Count > 0;

        /// <summary>Gets whether any water surface is queued to draw this frame.</summary>
        public bool HasWater => renderLists[RenderPass.Water].Count > 0;

        private readonly Dictionary<RenderPass, List<MeshBatchRenderer.Request>> renderLists = new()
        {
            [RenderPass.OpaqueAggregate] = [],
            [RenderPass.OpaqueFragments] = [],
            [RenderPass.Opaque] = [],
            [RenderPass.StaticOverlay] = [],
            [RenderPass.OpaqueRefract] = [],
            [RenderPass.Water] = [],
            [RenderPass.Translucent] = [],
            [RenderPass.Outline] = [],
        };

        /// <summary>
        /// Draw calls for first-person layer geometry.
        /// </summary>
        /// <summary>Translucent draws that go to the water effects map instead of the scene.</summary>
        private readonly List<MeshBatchRenderer.Request> waterEffectsRenderList = [];

        /// <summary>Visible nodes that draw themselves, listed once each however many passes they draw in.</summary>
        private readonly List<SceneNode> customBufferNodes = [];

        private readonly Dictionary<RenderPass, List<MeshBatchRenderer.Request>> viewmodelRenderLists = new()
        {
            [RenderPass.Opaque] = [],
            [RenderPass.Translucent] = [],
        };

        private Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>> depthOnlyDraws { get; } = CreateDepthOnlyDrawCallCollection();

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

            // Aggregated geometry is opaque world detail that never samples the scene color, and the refract
            // pass is the one place it cannot go: it has neither the depth prepass nor the indirect draw path.
            var isAggregated = request.Node is SceneAggregate or SceneAggregate.Fragment;
            var readsSceneColor = !isAggregated && request.Call.Material.ReadsSceneColor;

            if (renderPass == RenderPass.OpaqueAggregate)
            {
                if (request.Node is SceneAggregate { CanDrawIndirect: true })
                {
                    if (EnableDepthPrepass)
                    {
                        var bucket = GetDepthOnlyBucket(request.Call);
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

            var isViewmodelLayer = (request.Node.RenderPasses & CustomRenderPasses.Viewmodel) != 0
                && viewmodelRenderLists.ContainsKey(renderPass);

            var queueList = isViewmodelLayer
                ? viewmodelRenderLists[renderPass]
                : renderLists[renderPass];

            var isLatePass = renderPass == RenderPass.Translucent;

            if ((readsSceneColor || request.Call.Material.IsCs2Water) && !isViewmodelLayer && renderPass != RenderPass.StaticOverlay)
            {
                queueList = renderLists[request.Call.Material.IsTranslucent
                    ? RenderPass.Water
                    : RenderPass.OpaqueRefract];

                isLatePass = true;
            }

            // Only draws that happen after the grab can make use of the resolved copies.
            if (isLatePass)
            {
                WantsSceneColor |= readsSceneColor;
                WantsSceneDepth |= request.Call.Material.Shader.ReservedTexturesUsed.Contains("g_tSceneDepth");
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

            foreach (var bucket in viewmodelRenderLists.Values)
            {
                bucket.Clear();
            }

            waterEffectsRenderList.Clear();
            customBufferNodes.Clear();

            foreach (var bucket in depthOnlyDraws.Values)
            {
                bucket.Clear();
            }

            WantsSceneColor = false;
            WantsSceneDepth = false;

            var frustum = cullFrustum ??= camera.ViewFrustum;
            var cullResults = GetFrustumCullResults(frustum);

            PerfStats.Active.Count(Counter.SceneObjectInView, cullResults.Count);

            foreach (var node in cullResults)
            {
                if (node is SceneAggregate resetAggregate)
                {
                    resetAggregate.AnyChildrenVisible = false;
                }
            }

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
                    if (!fragment.Parent.IsFragmentInActiveLod(fragment))
                    {
                        continue;
                    }

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
                    if (node is SceneLight light)
                    {
                        PerfStats.Active.CountLightInView(light);
                    }

                    var customRender = new MeshBatchRenderer.Request
                    {
                        DistanceFromCamera = node is PhysSceneNode
                            ? 100000f - node.OverlayRenderOrder * 10f
                            : node.GetCameraDistance(camera),
                        Node = node,
                    };

                    var customPasses = node.RenderPasses;

                    if (customPasses != CustomRenderPasses.None)
                    {
                        customBufferNodes.Add(node);
                    }

                    var customLists = (customPasses & CustomRenderPasses.Viewmodel) != 0
                        ? viewmodelRenderLists
                        : renderLists;

                    if ((customPasses & CustomRenderPasses.Opaque) != 0)
                    {
                        customLists[RenderPass.Opaque].Add(customRender);
                    }

                    if ((customPasses & CustomRenderPasses.Translucent) != 0)
                    {
                        customLists[RenderPass.Translucent].Add(customRender);
                    }

                    if ((customPasses & CustomRenderPasses.WaterEffects) != 0)
                    {
                        waterEffectsRenderList.Add(customRender);
                    }

                    if (node.IsSelected)
                    {
                        renderLists[RenderPass.Outline].Add(customRender);
                    }
                }
            }

            // avoid buffer updates mid rendering
            foreach (var node in customBufferNodes)
            {
                node.UpdateBuffers(camera);
            }
        }

        internal Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>>[] CulledShadowDrawCallsCascades { get; } = CreateSunCascadeDrawCallCollections();
        internal static Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>> CreateDepthOnlyDrawCallCollection()
            => Enum.GetValues<DepthOnlyBucket>().ToDictionary(static bucket => bucket, static _ => new List<MeshBatchRenderer.Request>());

        private static Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>>[] CreateSunCascadeDrawCallCollections()
        {
            var buckets = new Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>>[WorldLightingInfo.SunCascadeCount];

            for (var i = 0; i < buckets.Length; i++)
            {
                buckets[i] = CreateDepthOnlyDrawCallCollection();
            }

            return buckets;
        }

        /// <summary>Updates the sun light shadow cascades and collects shadow draw calls for each of them, if dynamic shadows are enabled.</summary>
        /// <param name="camera">The main camera used to fit the shadow cascades.</param>
        /// <param name="shadowMapSize">The shadow map resolution; pass -1 to produce empty frustums (pre-warm pass).</param>
        public void SetupSceneShadows(Camera camera, int shadowMapSize)
        {
            if (!LightingInfo.EnableDynamicShadows)
            {
                return;
            }

            LightingInfo.UpdateSunLightFrustum(camera, shadowMapSize);

            for (var cascade = 0; cascade < WorldLightingInfo.SunCascadeCount; cascade++)
            {
                if (cascade >= LightingInfo.ActiveSunCascadeCount)
                {
                    foreach (var bucket in CulledShadowDrawCallsCascades[cascade].Values)
                    {
                        bucket.Clear();
                    }

                    continue;
                }

                if (shadowMapSize == -1)
                {
                    LightingInfo.SunLightFrustums[cascade].SetEmpty();
                }

                CollectShadowDrawCalls(LightingInfo.SunLightFrustums[cascade],
                    includeStatic: !LightingInfo.HasBakedShadowsFromLightmap,
                    includeDynamic: true, CulledShadowDrawCallsCascades[cascade],
                    LightingInfo.SunCastDirection, out var casterDepthMin, out var casterDepthMax);

                LightingInfo.FitSunLightDepthRange(cascade, casterDepthMin, casterDepthMax);
            }
        }


        /// <summary>
        /// Collects the shadow draw calls for a single barn light face. The returned buckets are
        /// scratch, valid until the next call.
        /// </summary>
        /// <param name="light">The barn light owning the shadow face.</param>
        /// <param name="lightFrustum">The frustum representing the light's view for this face.</param>
        public Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>> SetupBarnLightFaceShadow(SceneLight light, Frustum lightFrustum)
        {
            barnShadowDrawCalls ??= CreateDepthOnlyDrawCallCollection();

            // Skip static geo for stationary lights
            CollectShadowDrawCalls(lightFrustum, includeStatic: light.DirectLight != SceneLight.DirectLightType.Stationary, includeDynamic: true, barnShadowDrawCalls);

            return barnShadowDrawCalls;
        }

        private void CollectShadowDrawCalls(Frustum frustum, bool includeStatic, bool includeDynamic, Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>> drawBuckets)
            => CollectShadowDrawCalls(frustum, includeStatic, includeDynamic, drawBuckets, Vector3.Zero, out _, out _);

        private void CollectShadowDrawCalls(Frustum frustum, bool includeStatic, bool includeDynamic, Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>> drawBuckets,
            Vector3 depthFitAxis, out float casterDepthMin, out float casterDepthMax)
        {
            // Extent of the accepted casters along the fit axis, for tightening the light's depth range
            var depthMin = float.MaxValue;
            var depthMax = float.MinValue;

            void AccumulateDepthFit(SceneNode casterNode)
            {
                if (depthFitAxis == Vector3.Zero)
                {
                    return;
                }

                var bounds = casterNode.BoundingBox;
                var center = Vector3.Dot(bounds.Center, depthFitAxis);
                var extent = Vector3.Dot(bounds.Size, Vector3.Abs(depthFitAxis)) * 0.5f;

                depthMin = Math.Min(depthMin, center - extent);
                depthMax = Math.Max(depthMax, center + extent);
            }

            foreach (var bucket in drawBuckets.Values)
            {
                bucket.Clear();
            }

            if (includeStatic)
            {
                StaticOctree.Query(frustum, CulledShadowNodes);
            }

            if (includeDynamic)
            {
                DynamicOctree.Query(frustum, CulledShadowNodes);
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

                    if (!fragment.Parent.IsFragmentInActiveLod(fragment))
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
                    // Nodes that draw their own solid geometry cast shadows by drawing into the depth pass
                    // with their own shaders, which is what this bucket is for.
                    if ((node.Flags & skipFlags) == 0 && (node.RenderPasses & CustomRenderPasses.Opaque) != 0)
                    {
                        AccumulateDepthFit(node);

                        drawBuckets[DepthOnlyBucket.MaterialShader].Add(new MeshBatchRenderer.Request
                        {
                            Node = node,
                        });
                    }

                    continue;
                }

                AccumulateDepthFit(node);

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

                        var bucket = GetDepthOnlyBucket(opaqueCall);

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

            casterDepthMin = depthMin;
            casterDepthMax = depthMax;
        }

        // The skinning variant is picked per draw, so the bucket only says which shader draws it
        private static DepthOnlyBucket GetDepthOnlyBucket(DrawCall opaqueCall)
        {
            return opaqueCall.Material.VertexAnimation ? DepthOnlyBucket.MaterialShader
                : opaqueCall.Material.IsAlphaTest ? DepthOnlyBucket.AlphaTest
                : DepthOnlyBucket.Specialized;
        }

        /// <summary>Picks the shader that replaces material shaders for a depth-only bucket, or <see langword="null"/> for the bucket that keeps the material's own shader.</summary>
        private static Shader? GetDepthOnlyReplacementShader(DepthOnlyBucket bucket, Shader depthOnlyShader) => bucket switch
        {
            DepthOnlyBucket.AlphaTest => depthOnlyShader.WithCombo("F_ALPHA_TEST", 1),
            DepthOnlyBucket.MaterialShader => null,
            _ => depthOnlyShader,
        };

        internal void UpdateIndirectRenderingState()
        {
            CompactMeshletDraws = false;
            DrawMeshletsIndirect = EnableIndirectDraws && SceneMeshletCount > 0 && IndirectDrawsGpu != null;

            if (DrawMeshletsIndirect)
            {
                CompactMeshletDraws = GLEnvironment.IndirectCountSupported && EnableCompaction;
            }
        }

        /// <summary>Binds the indirect draw buffers chosen by <see cref="UpdateIndirectRenderingState"/>.</summary>
        internal void BindIndirectDrawBuffers()
        {
            if (!DrawMeshletsIndirect)
            {
                return;
            }

            Debug.Assert(IndirectDrawsGpu is not null);
            Debug.Assert(CompactedDrawsGpu is not null);

            GL.BindBuffer(BufferTarget.DrawIndirectBuffer, CompactMeshletDraws
                ? CompactedDrawsGpu.Handle
                : IndirectDrawsGpu.Handle);

            if (CompactMeshletDraws)
            {
                Debug.Assert(CompactedCountsGpu is not null);
                GL.BindBuffer(BufferTarget.ParameterBuffer, CompactedCountsGpu.Handle);
            }
        }

        /// <summary>
        /// Binds the depth pyramid and sets the constants every occlusion test reads. Shared by the
        /// meshlet cull and the light tile cull so the two cannot test against different state.
        /// </summary>
        /// <param name="shader">The shader whose occlusion uniforms to set. Must already be in use.</param>
        /// <returns>Whether occlusion culling is active this frame.</returns>
        internal bool SetOcclusionUniforms(Shader shader)
        {
            var pyramid = DepthPyramid;
            var enabled = DepthPyramidValid && pyramid != null;

            shader.SetUniform("g_bOcclusionCullEnabled", enabled ? 1 : 0);
            shader.SetUniform("g_bSkyOcclusion", IsSkybox ? 1 : 0);

            if (!enabled)
            {
                shader.SetTexture(RenderMaterial.TextureUnitStart, "g_tDepthPyramid", RendererContext.MaterialLoader.GetDefaultMask());
                return false;
            }

            Debug.Assert(pyramid != null);

            shader.SetUniform("g_nDepthPyramidMaxMip", pyramid.NumMipLevels - 1);
            shader.SetUniform("g_nDepthPyramidWidth", pyramid.Width);
            shader.SetUniform("g_nDepthPyramidHeight", pyramid.Height);
            shader.SetUniform("g_flDepthRangeMin", Renderer.DepthRange.Scene.Near);
            shader.SetUniform("g_flDepthRangeMax", Renderer.DepthRange.Scene.Far);
            shader.SetTexture(RenderMaterial.TextureUnitStart, "g_tDepthPyramid", pyramid);

            return true;
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
            Debug.Assert(CommandMeshletsGpu is not null);
            Debug.Assert(IndirectDrawsGpu is not null);
            Debug.Assert(InstanceBufferGpu is not null);
            Debug.Assert(TransformBufferGpu is not null);
            Debug.Assert(ObjectLodGpu is not null);
            Debug.Assert(ActiveLodBitsGpu is not null);

            frustumBuffer.BindBufferBase();
            frustumBuffer.Data = new(frustum);

            FrustumCullShader.Use();

            SetOcclusionUniforms(FrustumCullShader);

            MeshletDataGpu.BindBufferBase();
            DrawBoundsGpu.BindBufferBase();
            CommandMeshletsGpu.BindBufferBase();
            IndirectDrawsGpu.BindBufferBase();

            // Instance transforms move each fragment's shared cull data into world space
            InstanceBufferGpu.BindBufferBase();
            TransformBufferGpu.BindBufferBase();

            // Scratch slots, rebound by the compaction and light cull dispatches that follow
            ObjectLodGpu.BindBufferBase();
            ActiveLodBitsGpu.BindBufferBase();

            var occlusionDebugEnabled = OcclusionDebugEnabled && OcclusionDebug != null;

            // Bind debug buffer for occluded bounds visualization
            if (occlusionDebugEnabled)
            {
                OcclusionDebug!.BindAndClearBuffer();
            }
            FrustumCullShader.SetUniform("g_bOcclusionDebugEnabled", occlusionDebugEnabled);

            var workGroups = (SceneMeshletCount + 63) / 64;
            GL.DispatchCompute(workGroups, 1, 1);

            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);

            if (occlusionDebugEnabled)
            {
                // Converts occludedCount into a DrawArraysIndirectCommand on the GPU so
                // OcclusionDebugRenderer.Render() can draw without a CPU readback/stall.
                // The barrier above makes occludedCount visible to this dispatch; the
                // CommandBarrierBit barrier in RenderOpaqueLayer covers its own output
                // being visible to the later indirect draw.
                OcclusionDebug!.DispatchFinalize();
            }
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
                DepthPyramidNpotShader.SetUniform("g_nSourceDepthWidth", depthSource.Width);
                DepthPyramidNpotShader.SetUniform("g_nSourceDepthHeight", depthSource.Height);

                DepthPyramidNpotShader.SetUniform("g_nDestDepthWidth", DepthPyramid.Width);
                DepthPyramidNpotShader.SetUniform("g_nDestDepthHeight", DepthPyramid.Height);

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

                DepthPyramidShader.SetUniform("g_nDestDepthWidth", destWidth);
                DepthPyramidShader.SetUniform("g_nDestDepthHeight", destHeight);

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
        /// <param name="depthOnlyShader">The depth-only shader, which the pass takes skinning variants of.</param>
        /// <param name="drawCalls">The bucketed draw calls to render.</param>
        public static void RenderOpaqueShadows(RenderContext renderContext, Shader depthOnlyShader, Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>> drawCalls)
        {
            renderContext.RenderPass = RenderPass.DepthOnly;

            PerfStats.Active.SuspendTriangleCounter();

            foreach (var (bucket, calls) in drawCalls)
            {
                if (calls.Count == 0)
                {
                    continue;
                }

                renderContext.ReplacementShader = GetDepthOnlyReplacementShader(bucket, depthOnlyShader);
                MeshBatchRenderer.Render(calls, renderContext);
            }

            PerfStats.Active.ResumeTriangleCounter();
        }

        /// <summary>
        /// Renders the opaque pass, optionally with a depth prepass, followed by aggregate indirect draws and static overlay geometry.
        /// </summary>
        /// <param name="renderContext">The render context for this pass.</param>
        /// <param name="depthOnlyShader">Optional depth-only shader; when provided and <see cref="EnableDepthPrepass"/> is set, a depth prepass is performed.</param>
        public void RenderOpaqueLayer(RenderContext renderContext, Shader? depthOnlyShader = null)
        {
            using var passScope = GraphicsContext.RenderState.Scope();

            var camera = renderContext.Camera;

            var depthPrepass = depthOnlyShader != null && EnableDepthPrepass;

            if (DrawMeshletsIndirect)
            {
                BindIndirectDrawBuffers();

                // CommandBarrierBit is defined over the indirect buffer only, not the compacted count buffer
                GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit
                    | MemoryBarrierFlags.BufferUpdateBarrierBit);
            }

            if (depthPrepass)
            {
                using (new GLDebugGroup("Depth Prepass"))
                using (GraphicsContext.RenderState.Scope(colorWriteMask: RsColorWriteEnableBits.None))
                {
                    PerfStats.Active.SuspendTriangleCounter();

                    renderContext.RenderPass = RenderPass.DepthOnly;
                    foreach (var (bucket, calls) in depthOnlyDraws)
                    {
                        renderContext.ReplacementShader = bucket == DepthOnlyBucket.Specialized ? depthOnlyShader : null;
                        MeshBatchRenderer.Render(calls, renderContext);
                    }

                    PerfStats.Active.ResumeTriangleCounter();
                }

                using (new GLDebugGroup("Opaque Prepassed"))
                using (GraphicsContext.RenderState.Scope(depthWrite: false, depthFunc: RsComparison.Equal))
                {
                    renderContext.RenderPass = RenderPass.OpaqueAggregate;
                    MeshBatchRenderer.Render(renderLists[renderContext.RenderPass], renderContext);
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

        /// <summary>
        /// Renders the opaque first-person viewmodel layer collected during <see cref="CollectSceneDrawCalls"/>.
        /// Rendered before the main scene so its reserved near depth range can never be overtaken by world geometry.
        /// </summary>
        /// <param name="renderContext">The render context for this pass, expected to use the dedicated viewmodel camera and depth range.</param>
        public void RenderViewmodelOpaqueLayer(RenderContext renderContext)
        {
            using var _ = GraphicsContext.RenderState.Scope();

            renderContext.RenderPass = RenderPass.Opaque;
            MeshBatchRenderer.Render(viewmodelRenderLists[RenderPass.Opaque], renderContext);
        }

        /// <summary>
        /// Renders the translucent first-person viewmodel layer collected during <see cref="CollectSceneDrawCalls"/>.
        /// Rendered after the main scene (and 3D sky) translucent passes so it composites correctly on top of them.
        /// </summary>
        /// <param name="renderContext">The render context for this pass, expected to use the dedicated viewmodel camera and depth range.</param>
        public void RenderViewmodelTranslucentLayer(RenderContext renderContext)
        {
            using var _ = GraphicsContext.RenderState.Scope(depthWrite: false, blend: true);

            renderContext.RenderPass = RenderPass.Translucent;
            MeshBatchRenderer.Render(viewmodelRenderLists[RenderPass.Translucent], renderContext);
        }

        /// <summary>
        /// Renders depth-writing geometry that samples the scene color, collected during <see cref="CollectSceneDrawCalls"/>.
        /// Runs after the framebuffer grab but before water and translucents, so those still sort against its depth.
        /// </summary>
        /// <param name="renderContext">The render context for this pass.</param>
        public void RenderOpaqueRefractLayer(RenderContext renderContext)
        {
            var requests = renderLists[RenderPass.OpaqueRefract];

            if (requests.Count == 0)
            {
                return;
            }

            using (new GLDebugGroup("Opaque Refract Render"))
            using (GraphicsContext.RenderState.Scope())
            {
                renderContext.RenderPass = RenderPass.OpaqueRefract;
                MeshBatchRenderer.Render(requests, renderContext);
            }
        }

        /// <summary>Renders the draw calls that fill the water effects map; the caller owns the render target.</summary>
        /// <param name="renderContext">The render context for this pass.</param>
        public void RenderWaterEffectsLayer(RenderContext renderContext)
        {
            renderContext.RenderPass = RenderPass.Translucent;
            renderContext.Layer = RenderLayer.WaterEffects;
            MeshBatchRenderer.Render(waterEffectsRenderList, renderContext);
        }

        /// <summary>Renders water draw calls collected during <see cref="CollectSceneDrawCalls"/>.</summary>
        /// <param name="renderContext">The render context for this pass.</param>
        public void RenderWaterLayer(RenderContext renderContext)
        {
            var requests = renderLists[RenderPass.Water];

            if (requests.Count == 0)
            {
                return;
            }

            using (new GLDebugGroup("Fancy Water Render"))
            using (GraphicsContext.RenderState.Scope())
            {
                renderContext.RenderPass = RenderPass.Water;
                MeshBatchRenderer.Render(requests, renderContext);
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

        internal void ActivateLayer(string layerName)
        {
            foreach (var node in AllNodes)
            {
                if (node.LayerName == layerName)
                {
                    node.LayerEnabled = true;
                }
            }
        }

        internal void DeactivateLayer(string layerName)
        {
            foreach (var node in AllNodes)
            {
                if (node.LayerName == layerName)
                {
                    node.LayerEnabled = false;
                }
            }
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

                if (renderer.LayerName.StartsWith("Internal -", StringComparison.Ordinal))
                {
                    continue;
                }

                renderer.LayerEnabled = layers.Contains(renderer.LayerName);
            }
        }

        /// <summary>
        /// Marks the spatial set that owns the given node as dirty so it will be rebuilt on the next update.
        /// Also clears barn light shadow caches.
        /// </summary>
        /// <param name="node">The node whose owning set should be dirtied.</param>
        /// <returns><see langword="true"/> if the node was found and its octree was dirtied; <see langword="false"/> if the node is not part of this scene.</returns>
        public bool MarkParentOctreeDirty(SceneNode node)
        {
            var nodeType = GetNodeTypeById(node.Id).Type;
            if (nodeType == NodeType.Unknown)
            {
                return false;
            }

            if (nodeType == NodeType.Static)
            {
                StaticOctree.Dirty = true;
            }
            else
            {
                DynamicOctree.Dirty = true;
            }

            return true;
        }

        /// <summary>Rebuilds the static octree and the dynamic node set from their current node lists, if dirty.</summary>
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

        /// <summary>
        /// Wetness coverage, drying amount, rain strength and puddle ripple strength, read from the map's
        /// <c>info_map_parameters</c>. Holds that entity's own defaults when the map has none.
        /// </summary>
        public Vector4 EnvironmentWetness { get; set; } = new(1f, 0f, 1f, 1f);

        /// <summary>Puddle ripple direction, over 0 to 1 for a full turn.</summary>
        public float PuddleWindDirection { get; set; }

        /// <summary>Writes the scene's fog and weather parameters into the provided view constants structure.</summary>
        /// <param name="viewConstants">The view constants to update.</param>
        public void SetFogConstants(ViewConstants viewConstants)
        {
            FogInfo.SetFogUniforms(viewConstants, FogEnabled);

            viewConstants.EnvWetness = EnvironmentWetness;
            viewConstants.EnvWetnessRipple = new Vector4(PuddleWindDirection, 0f, 0f, 0f);
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
                if (node.EntityData is { } entityData
                    && LightingInfo.LightProbes.Find(p => ReferenceEquals(p.EntityData, entityData)) is { } selfProbe)
                {
                    node.LightProbeBinding = selfProbe;
                    continue;
                }

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
                .Take(LightProbeVolumeArray.MAX_PROBES)
                .ToList();

            var i = 0;
            foreach (var probe in sortedLightProbes)
            {
                probe.ShaderIndex = i;
                lpvBuffer.Data.Probes[i] = probe.CalculateGpuProbeData(isAtlas);
                i++;
            }

            boundLightProbes = sortedLightProbes;

            if (sortedLightProbes.Count == 0)
            {
                // remove baked lighting from probe attribute?
                return;
            }

            // Fall back to the global probe
            var globalProbe = sortedLightProbes[^1];

            foreach (var node in AllNodes)
            {
                if (node.Flags.HasFlag(ObjectTypeFlags.DisableVisCulling))
                {
                    node.LightProbeBinding = globalProbe;
                    continue;
                }

                node.LightProbeBinding ??= FindLightProbe(node.BoundingBox.Center) ?? globalProbe;
            }
        }


        /// <summary>
        /// Returns the best probe volume containing the given position, or <see langword="null"/> when
        /// none does.
        /// </summary>
        public SceneLightProbe? FindLightProbe(Vector3 position)
        {
            if (boundLightProbes == null)
            {
                return null;
            }

            foreach (var probe in boundLightProbes)
            {
                if (probe.BoundingBox.Contains(position))
                {
                    return probe;
                }
            }

            return null;
        }

        /// <summary>
        /// Refreshes the instance buffer entries of the dynamic nodes: the probe volume they are
        /// currently inside, their envmap visibility and their tint.
        /// </summary>
        private void UpdateDynamicInstanceData()
        {
            if (boundLightProbes is not { Count: > 0 })
            {
                return;
            }

            var globalProbe = boundLightProbes[^1];

            if (instanceDataCpu == null || InstanceBufferGpu == null)
            {
                return;
            }

            // Dynamic node ids are assigned after the statics, so the touched entries form one span
            var minId = uint.MaxValue;
            var maxId = 0u;

            foreach (var node in dynamicNodes)
            {
                if (node.LightProbeVolumePrecomputedHandshake != 0
                    || node.Id == 0
                    || node.Id >= instanceDataCpu.Length)
                {
                    continue;
                }

                node.LightProbeBinding = FindLightProbe(node.BoundingBox.Center) ?? globalProbe;

                ref var entry = ref instanceDataCpu[node.Id];
                entry.VisibleLPV = (uint)node.LightProbeBinding.ShaderIndex
                    | (node.ShaderEnvMapVisibility.GetFirstShaderIndex() << 16);
                entry.EnvMapVisibility = node.ShaderEnvMapVisibility;

                if (node is MeshCollectionNode meshNode)
                {
                    entry.TintAlpha = Color32.FromVector4Clamped(meshNode.Tint).PackedValue;
                }

                minId = Math.Min(minId, node.Id);
                maxId = Math.Max(maxId, node.Id);
            }

            if (minId <= maxId)
            {
                var stride = Unsafe.SizeOf<ObjectDataStandard>();
                InstanceBufferGpu.Update<ObjectDataStandard>(
                    instanceDataCpu.AsSpan((int)minId, (int)(maxId - minId + 1)), (int)minId * stride);
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

            static int IndoorPriorityCompare(SceneEnvMap a, SceneEnvMap b)
            {
                var indoor = b.IndoorOutdoorLevel.CompareTo(a.IndoorOutdoorLevel);
                return indoor != 0 ? indoor : a.ArrayIndex.CompareTo(b.ArrayIndex);
            }

            static int HandShakeCompare(SceneEnvMap a, SceneEnvMap b) => a.HandShake.CompareTo(b.HandShake);

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

                StaticOctree.Query(envMap.BoundingBox, nodes);
                DynamicOctree.Query(envMap.BoundingBox, nodes); // TODO: This should actually be done dynamically

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

                if (node.EntityData is { } entityData
                    && LightingInfo.EnvMaps.Find(e => ReferenceEquals(e.EntityData, entityData)) is { } selfEnvMap)
                {
                    node.EnvMaps.Clear();
                    node.EnvMaps.Add(selfEnvMap);
                }
                else if (precomputedHandshake > 0)
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
                        RendererContext.Logger.LogDebug("An envmap with handshake [{Handshake}] does not exist for node at {Center}", precomputedHandshake, node.BoundingBox.Center);
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

                // Rebuilt from scratch rather than added to: Store only sets bits, so a node that lost a
                // probe since the last call would keep it.
                node.ShaderEnvMapVisibility = default(SceneEnvMap.EnvMapVisibility128).Store(node.EnvMaps);

                // all cubemaps visible
                if (node.Flags.HasFlag(ObjectTypeFlags.DisableVisCulling))
                {
                    node.ShaderEnvMapVisibility = node.ShaderEnvMapVisibility.Store(LightingInfo.EnvMaps);
                }

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

            var boundsExtend = new Vector3(SceneEnvMap.BoundsExtend);

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
                NormalizationSH = envMap.NormalizationSH
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
                LightBinner.Dispose();
                lightingBuffer?.Dispose();
                lpvBuffer?.Dispose();
                envMapBuffer?.Dispose();
                LightingInfo.DisposeBarnLights();
            }
        }
    }
}
