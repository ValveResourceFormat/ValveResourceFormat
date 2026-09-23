using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
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
        /// <summary>Which pass of the frame an update belongs to.</summary>
        public enum UpdatePhase
        {
            /// <summary>One node at a time, in scene order.</summary>
            Place,

            /// <summary>Across the thread pool. Touch only what the node owns.</summary>
            Simulate,

            /// <summary>Finish work done on simulate. Back on the calling thread.</summary>
            Act,
        }

        /// <summary>
        /// Context data passed to scene nodes during per-frame update.
        /// </summary>
        public readonly struct UpdateContext
        {
            /// <summary>Gets which pass of the frame this update is.</summary>
            public UpdatePhase Phase { get; init; }

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

            /// <summary>Gets or sets what the view being drawn keeps of <see cref="Scene"/>, set by the renderer.</summary>
            public SceneViewState? View { get; set; }

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

            /// <summary>Gets or sets the fallback depth only shader, set for the passes that only lay down depth.</summary>
            public Shader? DepthOnlyShader { get; set; }

            /// <summary>Gets or sets the fallback counting shader, set for the pass that counts quad overdraw.</summary>
            public Shader? OverdrawShader { get; set; }

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

        /// <summary>Gets or sets the 2D sky the map's sky entities provide, or <see langword="null"/> when it has none.</summary>
        public SceneSkybox2D? Skybox2D { get; set; }

        /// <summary>
        /// Gets the world group this scene belongs to, such as a 3D sky's <c>skyboxWorldGroup0</c>, or
        /// <see langword="null"/> for the map's own. Entities see and touch only those of their world group,
        /// and each view draws one.
        /// </summary>
        public string? WorldGroup { get; init; }

        /// <summary>
        /// Whether the entities drawn here take part in collision. A spawn group placed inside a map, such
        /// as a 3D sky, is scenery: nothing can reach it, so its entities never build a collider.
        /// </summary>
        internal bool EntitiesCollide { get; set; } = true;

        /// <summary>
        /// How large an editor marker is drawn here next to the entity it marks. A 3D sky is magnified by
        /// the camera it is drawn through, so its markers shrink to come back out at their normal size.
        /// </summary>
        internal float MarkerScale { get; set; } = 1f;

        /// <summary>Maps this scene's space to where the main camera sees it. Identity, except for a 3D sky.</summary>
        public Matrix4x4 ToViewerWorld { get; internal set; } = Matrix4x4.Identity;

        /// <summary>Gets or sets the voxel visibility data.</summary>
        public IWorldVisibility? VoxelVisibility { get; set; }

        /// <summary>
        /// Gets or sets the transform from this scene's space into the space <see cref="VoxelVisibility"/> was
        /// compiled in. Identity, except for a map placed somewhere else than it was built.
        /// </summary>
        public Matrix4x4 WorldToVisibility { get; set; } = Matrix4x4.Identity;

        /// <summary>
        /// Gets or sets whether PVS culling is enabled for this scene. Has no effect without <see cref="VoxelVisibility"/>.
        /// <see cref="Renderer"/> reads the main scene's copy for the PVS of every view.
        /// </summary>
        public bool EnablePvsCulling { get; set; } = true;

        private UniformBuffer<LightingConstants>? lightingBuffer;
        private UniformBuffer<EnvMapArray>? envMapBuffer;
        private UniformBuffer<LightProbeVolumeArray>? lpvBuffer;

        /// <summary>Gets the frustum planes the meshlet cull reads, uploaded right before each view's dispatch.</summary>
        internal UniformBuffer<FrustumPlanesGpu>? FrustumBuffer { get; private set; }

        /// <summary>Gets or sets the GPU buffer each draw reads at its base instance (tint, transform index, skinning).</summary>
        public StorageBuffer? InstanceBufferGpu { get; set; }

        /// <summary>Gets or sets the GPU buffer holding each scene node's lighting (light probe, env map visibility).</summary>
        public StorageBuffer? ObjectBufferGpu { get; set; }

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

        /// <summary>Gets or sets the GPU buffer containing compaction request descriptors (count and start index per aggregate).</summary>
        public StorageBuffer? CompactionRequestsGpu { get; set; }

        /// <summary>Gets the total number of meshlets across all indirect-draw-capable aggregates in the scene.</summary>
        public int SceneMeshletCount { get; private set; }

        /// <summary>
        /// Gets the indirect draw commands every view's cull starts from, one per meshlet, or
        /// <see langword="null"/> when nothing draws indirectly.
        /// </summary>
        internal DrawElementsIndirectCommand[]? IndirectDrawTemplate { get; private set; }

        /// <summary>Gets the per aggregate draw counts compaction starts from, alongside <see cref="IndirectDrawTemplate"/>.</summary>
        internal uint[]? CompactedCountsTemplate { get; private set; }

        /// <summary>Gets a number that changes whenever the indirect draw layout is rebuilt.</summary>
        internal int IndirectLayoutVersion { get; private set; }

        /// <summary>Gets a number that changes whenever the octrees are rebuilt, invalidating cached cull results.</summary>
        internal int OctreeVersion { get; private set; }

        internal Shader? FrustumCullShader { get; private set; }
        internal Shader? CompactionShader { get; private set; }
        internal Shader? OutlineShader { get; private set; }

        /// <summary>
        /// Gets the tile and depth bin cull passes of the view this scene is shaded for, which barn light
        /// visibility is read back from.
        /// </summary>
        internal LightBinner? ShadingLightBinner { get; set; }

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

        /// <summary>Gets or sets whether GPU draw compaction is applied after frustum culling to remove empty indirect draw commands.</summary>
        public bool EnableCompaction { get; set; } = true;

        /// <summary>Gets or sets whether lights are binned to screen tiles so shaders iterate only what reaches them.</summary>
        /// <remarks>
        /// <see cref="Renderer"/> reads the main scene's copy when driving every binner, so that one also
        /// governs every spawn group. The spawn groups' own copies are never what the viewer toggles.
        /// </remarks>
        public bool EnableTiledLightCulling { get; set; } = true;

        internal bool DrawMeshletsIndirect { get; private set; }
        internal bool CompactMeshletDraws { get; private set; }

        /// <summary>Gets all static and dynamic scene nodes in the order they were added.</summary>
        public IEnumerable<SceneNode> AllNodes => staticNodes.Concat(dynamicNodes);

        private readonly List<SceneNode> staticNodes = [];
        private readonly List<SceneNode> dynamicNodes = [];

        /// <summary>Gets the number of static and dynamic nodes in the scene.</summary>
        internal int NodeCount => staticNodes.Count + dynamicNodes.Count;

        /// <summary>Gets the number of object entries the last buffer layout gave the nodes, one past the highest node id.</summary>
        internal int ObjectEntryCount => objectEntryCount;

        private readonly ParallelDispatch simulationDispatch = new();
        private SimulationWork? simulationWork;

        private List<SceneNode> CulledShadowNodes { get; } = [];
        private readonly List<RenderableMesh> listWithSingleMesh = [null!];

        /// <summary>The layer the map's own particle systems are put on.</summary>
        public const string ParticlesLayerName = "Particles";

        private HashSet<string>? enabledLayers;
        private InstanceDataStandard[]? instanceDataCpu;
        private ObjectDataStandard[]? objectDataCpu;
        private ObjectLodInfo[]? lodDataCpu;
        private readonly List<SceneNode> nodeScratch = [];
        private List<OpenTK.Mathematics.Matrix3x4>? transformDataCpu;
        private int transformUploadStart = int.MaxValue;
        private readonly List<SceneAggregate> lodAggregates = [];
        private uint[] activeLodBits = [];
        private int objectEntryCount;
        private int firstDynamicId;
        private int dynamicTransformStart;
        private int dynamicDrawEntryStart;
        private int drawEntryEnd;
        private int morphAtlasLayoutVersion;

        private Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>>? barnShadowDrawCalls;

        // Bound probes in precedence order: most indoor first, then smallest, so the first volume
        // containing a point is the best one
        private List<SceneLightProbe>? boundLightProbes;

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
            CreateInstanceTransformBuffers(); // after calculating envmap and lpv

            UpdateBuffers();

            OutlineShader = RendererContext.ShaderLoader.LoadShader("outline");
            FrustumCullShader = RendererContext.ShaderLoader.LoadShader("frustum_cull");
            CompactionShader = RendererContext.ShaderLoader.LoadShader("compact_indirect_draws");

            // set shadow lists to their max capacity
            SetupSceneShadows(new Camera(), -1);
        }

        /// <summary>
        /// Adds a node to the scene, placing it in either the static or dynamic partition.
        /// </summary>
        /// <param name="node">The node to add.</param>
        /// <param name="dynamic">When <see langword="true"/>, the node is placed in <see cref="DynamicOctree"/>; otherwise in <see cref="StaticOctree"/>.</param>
        public void Add(SceneNode node, bool dynamic)
        {
            ApplyLayerVisibility(node);

            if (dynamic)
            {
                dynamicNodes.Add(node);
                DynamicOctree.Dirty = true;
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
            DeleteNodes();

            RendererContext.MaterialLoader.Clear();
            RendererContext.MeshBufferCache.Clear();
        }

        /// <summary>
        /// Removes and deletes every node of the scene, and its 2D sky, leaving the materials and mesh buffers
        /// shared with other scenes loaded.
        /// </summary>
        public void DeleteNodes()
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

            Skybox2D?.Delete();
            Skybox2D = null;
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
        /// An entity can own several nodes, such as its model and the collision hulls drawn for it, and the
        /// one to hand out for it is the one it is drawn as, whichever was added to the scene first.
        /// </summary>
        private static SceneNode? PreferRootNode(SceneNode? node) => node?.EntityInstance?.RootNode ?? node;

        /// <summary>
        /// Finds the scene node of the given entity: the node a spawned entity is drawn as, otherwise the
        /// first node carrying its data.
        /// </summary>
        /// <param name="entity">The entity to search for.</param>
        /// <returns>The matching <see cref="SceneNode"/>, or <see langword="null"/> if not found.</returns>
        public SceneNode? Find(EntityLump.Entity entity)
        {
            bool IsMatchingEntity(SceneNode node) => node.EntityData == entity;

            return PreferRootNode(staticNodes.Find(IsMatchingEntity) ?? dynamicNodes.Find(IsMatchingEntity));
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

            return PreferRootNode(staticNodes.Find(IsMatchingEntity) ?? dynamicNodes.Find(IsMatchingEntity));
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

            return PreferRootNode(staticNodes.Find(IsMatchingEntity) ?? dynamicNodes.Find(IsMatchingEntity));
        }

        /// <summary>
        /// The dynamic nodes are the work: the index picks one, so no filtered list is kept in step
        /// with the scene. Holds the list itself, not a copy, so running it allocates nothing.
        /// </summary>
        private sealed class SimulationWork(List<SceneNode> nodes) : IParallelWork
        {
            public UpdateContext Context;

            public void Execute(int index)
            {
                var node = nodes[index];

                // Parented nodes are placed by their parent, but simulate here like every other one
                if (node.Simulation == NodeSimulation.Parallel)
                {
                    node.Update(Context);
                }
            }
        }

        /// <summary>Runs the two passes after placement, for the nodes that take part in them.</summary>
        private void SimulateNodes(UpdateContext updateContext)
        {
            var parallelSimulation = RendererContext.ParallelSimulation;

            var simulate = updateContext with { Phase = UpdatePhase.Simulate };

            if (parallelSimulation)
            {
                simulationWork ??= new SimulationWork(dynamicNodes);
                simulationWork.Context = simulate;

                // The dispatch publishes the context, and runs counts too small to fan out inline
                simulationDispatch.Run(simulationWork, dynamicNodes.Count);
            }

            var act = updateContext with { Phase = UpdatePhase.Act };

            foreach (var node in dynamicNodes)
            {
                if (node.Simulation == NodeSimulation.None)
                {
                    continue;
                }

                if (!parallelSimulation)
                {
                    node.Update(simulate);
                }

                node.Update(act);
            }
        }

        /// <summary>
        /// Updates all scene nodes for the current frame, advancing animations and rebuilding spatial sets and GPU buffers if the scene changed.
        /// </summary>
        /// <param name="updateContext">Per-frame context data including camera and timestep.</param>
        public void Update(Scene.UpdateContext updateContext)
        {
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

                node.Update(updateContext);
            }

            SimulateNodes(updateContext);

            foreach (var node in dynamicNodes)
            {
                DynamicOctree.Update(node);
            }

            if (StaticOctree.Dirty || DynamicOctree.Dirty)
            {
                // Indirect draw commands bake node ids, so recreate them only after reindexing
                var staticDirty = StaticOctree.Dirty;

                UpdateOctrees();
                UpdateNodeIndices();
                CreateInstanceTransformBuffers();

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
            FrustumBuffer ??= new(ReservedBufferSlots.FrustumPlanes);

            lightingBuffer.Data = LightingInfo.LightingData;

            LightingInfo.CreateBarnLightBuffer();
            CreateIndirectDrawBuffers();
        }

        private static int CapacityFor(int count) => (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(64, count + (count / 10)));

        private void CreateInstanceTransformBuffers()
        {
            nodeScratch.Clear();
            nodeScratch.AddRange(staticNodes);
            nodeScratch.AddRange(dynamicNodes);

            var nodes = nodeScratch;

            if (nodes.Count == 0)
            {
                return;
            }

            var maxId = 0u;

            foreach (var node in nodes)
            {
                maxId = Math.Max(maxId, node.Id);
            }

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

            var entryCount = (int)maxId + 1;
            objectEntryCount = entryCount;
            firstDynamicId = entryCount;

            // Mesh draw calls get entries of their own behind the node entries, since tint and skinning differ
            // per draw. The dynamic nodes' come last, so the per frame refresh uploads one span.
            var totalEntryCount = entryCount;

            AssignObjectIndices(staticNodes, ref totalEntryCount);
            dynamicDrawEntryStart = totalEntryCount;
            AssignObjectIndices(dynamicNodes, ref totalEntryCount);
            drawEntryEnd = totalEntryCount;

            if (instanceDataCpu == null || instanceDataCpu.Length < totalEntryCount)
            {
                instanceDataCpu = new InstanceDataStandard[CapacityFor(totalEntryCount)];
            }

            if (objectDataCpu == null || objectDataCpu.Length < entryCount)
            {
                objectDataCpu = new ObjectDataStandard[CapacityFor(entryCount)];
                lodDataCpu = new ObjectLodInfo[objectDataCpu.Length];
            }

            var instanceData = instanceDataCpu;
            var objectData = objectDataCpu;
            var lodData = lodDataCpu!;

            Array.Clear(instanceData, 0, totalEntryCount);
            Array.Clear(objectData, 0, entryCount);
            Array.Clear(lodData, 0, entryCount);

            transformDataCpu ??= new List<OpenTK.Mathematics.Matrix3x4>(capacity: CapacityFor(entryCount + 1));
            var transformData = transformDataCpu;
            transformData.Clear();

            // Reserve index 0 for identity transform
            transformData.Add(Matrix4x4.Identity.To3x4());

            for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                var node = nodes[nodeIndex];

                if (nodeIndex == staticNodes.Count)
                {
                    firstDynamicId = (int)node.Id;
                    dynamicTransformStart = transformData.Count;
                }

                var neverChangesTransform = nodeIndex < staticNodes.Count && node is not ModelSceneNode { SkinningTransformCount: > 0 };

                uint transformIndex;

                if (node is SceneAggregate { InstanceTransforms.Count: > 0 } aggregateWithInstances)
                {
                    transformIndex = (uint)transformData.Count;

                    foreach (var instanceTransform in aggregateWithInstances.InstanceTransforms)
                    {
                        transformData.Add(instanceTransform);
                    }
                }
                else if (node.AdditionalFlags.HasFlag(SceneNodeFlags.PreTransformedVertices) || (neverChangesTransform && node.Transform.IsIdentity))
                {
                    transformIndex = 0; // Reuse identity transform at index 0
                }
                else
                {
                    transformIndex = (uint)transformData.Count;
                    transformData.Add(node.Transform.To3x4());
                }

                if (node is ModelSceneNode skinnedModel)
                {
                    AppendSkinningTransforms(skinnedModel, transformIndex, transformData);
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

                instanceData[node.Id] = new InstanceDataStandard
                {
                    TintAlpha = EntryTint(node),
                    TransformIndex = transformIndex,
                    Identification = node.Id,
                    MorphVertexIdOffset = -1,
                };

                objectData[node.Id] = ObjectEntry(node);

                if (node is MeshCollectionNode meshNode)
                {
                    WriteDrawEntries(meshNode);
                }
            }

            InstanceBufferGpu = Upload<InstanceDataStandard>(InstanceBufferGpu, instanceData.AsSpan(0, totalEntryCount),
                ReservedBufferSlots.Instances, nameof(ReservedBufferSlots.Instances));
            ObjectBufferGpu = Upload<ObjectDataStandard>(ObjectBufferGpu, objectData.AsSpan(0, entryCount),
                ReservedBufferSlots.Objects, nameof(ReservedBufferSlots.Objects));
            TransformBufferGpu = Upload<OpenTK.Mathematics.Matrix3x4>(TransformBufferGpu, CollectionsMarshal.AsSpan(transformData),
                ReservedBufferSlots.Transforms, nameof(ReservedBufferSlots.Transforms));
            ObjectLodGpu = Upload<ObjectLodInfo>(ObjectLodGpu, lodData.AsSpan(0, entryCount),
                ReservedBufferSlots.BufferSlot15, "ObjectLod");

            var setupCount = Math.Max(1, lodSetupCount);

            if (activeLodBits.Length != setupCount)
            {
                activeLodBits = new uint[setupCount];
            }

            Array.Fill(activeLodBits, 1u);

            ActiveLodBitsGpu = Upload<uint>(ActiveLodBitsGpu, activeLodBits, ReservedBufferSlots.BufferSlot11, "ActiveLodBits");

            transformUploadStart = int.MaxValue;

            if (nodes.Count == staticNodes.Count)
            {
                dynamicTransformStart = transformData.Count;
            }

            morphAtlasLayoutVersion = RendererContext.MorphAtlas.LayoutVersion;
        }

        internal static StorageBuffer Upload<T>(StorageBuffer? buffer, ReadOnlySpan<T> data, ReservedBufferSlots slot, string name)
            where T : struct
        {
            if (buffer == null || buffer.Size < data.Length * Unsafe.SizeOf<T>())
            {
                buffer?.Delete();
                buffer = StorageBuffer.Allocate<T>(slot, name, CapacityFor(data.Length), BufferUsage.Dynamic);
            }

            buffer.Update(data, 0);

            return buffer;
        }

        // Gives each draw call of the mesh nodes an object entry of its own, from nextIndex on
        private static void AssignObjectIndices(List<SceneNode> nodes, ref int nextIndex)
        {
            foreach (var node in nodes)
            {
                if (node is not MeshCollectionNode meshNode)
                {
                    continue;
                }

                foreach (var mesh in meshNode.AllRenderableMeshes)
                {
                    Assign(mesh.DrawCallsOpaque, ref nextIndex);
                    Assign(mesh.DrawCallsOverlay, ref nextIndex);
                    Assign(mesh.DrawCallsBlended, ref nextIndex);
                }
            }

            static void Assign(List<DrawCall> calls, ref int nextIndex)
            {
                foreach (var call in calls)
                {
                    call.InstanceBufferIndex = (uint)nextIndex++;
                }
            }
        }

        // Fills a mesh node's draw entries from its own entry, with the tint, skinning and morph rect of each draw
        private void WriteDrawEntries(MeshCollectionNode node)
        {
            var entries = instanceDataCpu!;

            foreach (var mesh in node.AllRenderableMeshes)
            {
                var entry = entries[node.Id];
                entry.MeshBoneData = InstanceDataStandard.PackMeshBoneData(mesh);

                var composite = mesh.FlexStateManager?.MorphComposite;
                var morphed = composite is { IsPlaced: true };

                if (morphed)
                {
                    entry.MorphAtlasOrigin = (uint)composite!.AtlasX | ((uint)composite.AtlasY << 16);
                    entry.MorphAtlasStride = (uint)composite.Width;
                }

                Write(entries, node.TintAlpha, mesh.DrawCallsOpaque, entry, morphed);
                Write(entries, node.TintAlpha, mesh.DrawCallsOverlay, entry, morphed);
                Write(entries, node.TintAlpha, mesh.DrawCallsBlended, entry, morphed);
            }

            static void Write(InstanceDataStandard[] entries, Vector4 tint, List<DrawCall> calls, InstanceDataStandard entry, bool morphed)
            {
                foreach (var call in calls)
                {
                    entry.TintAlpha = PackTint(tint * call.TintColor);
                    entry.MorphVertexIdOffset = morphed ? call.VertexIdOffset : -1;

                    entries[call.InstanceBufferIndex] = entry;
                }
            }
        }

        /// <summary>
        /// Update GPU draw and object data.
        /// </summary>
        public void UpdateInstanceTransformBuffers()
        {
            if (instanceDataCpu == null || objectDataCpu == null || transformDataCpu == null
                || InstanceBufferGpu == null || ObjectBufferGpu == null || TransformBufferGpu == null)
            {
                return;
            }

            var transforms = CollectionsMarshal.AsSpan(transformDataCpu);
            var rebindProbes = boundLightProbes is { Count: > 0 };

            foreach (var node in dynamicNodes)
            {
                // Nodes the last layout has not seen have no entries yet
                if (node.Id == 0 || node.Id >= objectEntryCount)
                {
                    continue;
                }

                if (rebindProbes && node.LightProbeVolumePrecomputedHandshake == 0
                    && (node.LightProbeBinding is not { } probe || !VolumeContains(probe, node.BoundingBox.Center)))
                {
                    node.LightProbeBinding = ChooseLightProbeVolume(node.BoundingBox.Center)!;
                }

                objectDataCpu[node.Id] = ObjectEntry(node);

                ref var entry = ref instanceDataCpu[node.Id];
                entry.TintAlpha = EntryTint(node);

                // An aggregate's slot starts its own run of instance transforms
                if (entry.TransformIndex != 0 && node is not SceneAggregate { InstanceTransforms.Count: > 0 })
                {
                    transforms[(int)entry.TransformIndex] = node.Transform.To3x4();
                }

                if (node is MeshCollectionNode meshNode)
                {
                    WriteDrawEntries(meshNode);
                }
            }

            var drawEntryStart = dynamicDrawEntryStart;
            var atlasVersion = RendererContext.MorphAtlas.LayoutVersion;

            if (morphAtlasLayoutVersion != atlasVersion)
            {
                morphAtlasLayoutVersion = atlasVersion;
                drawEntryStart = objectEntryCount;

                foreach (var node in staticNodes)
                {
                    if (node is MeshCollectionNode meshNode && node.Id != 0 && node.Id < objectEntryCount)
                    {
                        WriteDrawEntries(meshNode);
                    }
                }
            }

            UploadRange(InstanceBufferGpu, instanceDataCpu, firstDynamicId, objectEntryCount);
            UploadRange(InstanceBufferGpu, instanceDataCpu, drawEntryStart, drawEntryEnd);
            UploadRange(ObjectBufferGpu, objectDataCpu, firstDynamicId, objectEntryCount);

            // Static skinned models' bones sit before the dynamic span, and move it down when they changed
            transformUploadStart = Math.Min(transformUploadStart, dynamicTransformStart);
            UploadRange(TransformBufferGpu, transforms, transformUploadStart, transforms.Length);
            transformUploadStart = int.MaxValue;
        }

        private static void UploadRange<T>(StorageBuffer buffer, ReadOnlySpan<T> data, int start, int end) where T : unmanaged
        {
            if (end > start)
            {
                buffer.Update<T>(data[start..end], start * Unsafe.SizeOf<T>());
            }
        }

        private static ObjectDataStandard ObjectEntry(SceneNode node) => new()
        {
            VisibleLPV = (uint)(node.LightProbeBinding?.ShaderIndex ?? 0) | (node.ShaderEnvMapVisibility.GetFirstShaderIndex() << 16),
            EnvMapVisibility = node.ShaderEnvMapVisibility,
        };

        // A fragment is a single draw call, so its node entry carries that draw's tint along with its own
        private static uint EntryTint(SceneNode node)
            => PackTint(node is SceneAggregate.Fragment fragment ? node.TintAlpha * fragment.DrawCall.TintColor : node.TintAlpha);

        // Content can author out-of-range tints; the packed byte color can only represent [0, 1]
        private static uint PackTint(Vector4 tint) => Color32.FromVector4Clamped(tint).PackedValue;

        private static void AppendSkinningTransforms(ModelSceneNode model, uint transformIndex,
            List<OpenTK.Mathematics.Matrix3x4> transformData)
        {
            model.TransformSlot = transformIndex;

            var skinningSlots = model.SkinningTransformCount;

            if (skinningSlots == 0)
            {
                return;
            }

            var runStart = (int)transformIndex + 1;
            CollectionsMarshal.SetCount(transformData, runStart + skinningSlots);

            var run = CollectionsMarshal.AsSpan(transformData).Slice(runStart, skinningSlots);
            run.Clear();

            model.WriteSkinningTransforms(run);
        }

        internal void UpdateSkinningTransforms(ModelSceneNode model)
        {
            var skinningSlots = model.SkinningTransformCount;

            if (transformDataCpu == null || skinningSlots == 0 || model.TransformSlot == 0)
            {
                return; // The table has not been laid out for this model yet
            }

            var boneStart = (int)model.TransformSlot + 1;
            var transforms = CollectionsMarshal.AsSpan(transformDataCpu);

            Debug.Assert(boneStart + skinningSlots <= transforms.Length);

            model.WriteSkinningTransforms(transforms.Slice(boneStart, skinningSlots));

            transformUploadStart = Math.Min(transformUploadStart, boneStart);
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
            var aggregateDrawCallCount = 0;
            var aggregateMeshletCount = 0;
            var aggregateCommandCount = 0;

            foreach (var agg in aggregateSceneNodes)
            {
                aggregateDrawCallCount += agg.RenderMesh.DrawCallsOpaque.Count;
                aggregateMeshletCount += agg.RenderMesh.Meshlets.Count;

                // Instanced fragments reuse one draw call with a transform each, so a fragment issues its own
                // commands but shares the cull data they point at
                foreach (var fragment in agg.Fragments)
                {
                    aggregateCommandCount += fragment.DrawCall.NumMeshlets;
                }
            }

            if (deletePrevious)
            {
                DeleteIndirectDrawBuffers();
            }

            IndirectLayoutVersion++;

            if (aggregateMeshletCount == 0)
            {
                return;
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

                            if (!fragment.LayerEnabled)
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
                MeshletDataGpu.Create(meshletDataGpu, BufferUsage.Static);

                // Every view culls into buffers of its own, starting from these commands and counts
                var compactedCounts = new uint[compactionRequestList.Count / 2];

                for (var request = 0; request < compactedCounts.Length; request++)
                {
                    compactedCounts[request] = compactionRequestList[request * 2];
                }

                IndirectDrawTemplate = indirectDrawsGpu;
                CompactedCountsTemplate = compactedCounts;

                CompactionRequestsGpu = new StorageBuffer(ReservedBufferSlots.BufferSlot15, "CompactionRequests");
                CompactionRequestsGpu.Create(compactionRequestList, BufferUsage.Static);
            }

            OcclusionDebug = new OcclusionDebugRenderer(this, RendererContext);
        }

        private void DeleteIndirectDrawBuffers()
        {
            DrawBoundsGpu?.Delete();
            MeshletDataGpu?.Delete();
            CommandMeshletsGpu?.Delete();
            CompactionRequestsGpu?.Delete();
            OcclusionDebug?.OccludedBoundsDebugGpu?.Delete();

            DrawBoundsGpu = null;
            MeshletDataGpu = null;
            CommandMeshletsGpu = null;
            CompactionRequestsGpu = null;
            IndirectDrawTemplate = null;
            CompactedCountsTemplate = null;
            SceneMeshletCount = 0;
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
            ObjectBufferGpu?.BindBufferBase();
            LightingInfo.BindBarnLightBuffer();
        }

        /// <summary>
        /// Tests a node against a visibility row: a view's PVS, or the sun row that says where sunlight
        /// reaches. A node belongs to every visibility cluster its bounding box touches and survives as long
        /// as one of them is visible. An empty row means there is nothing to cull with and everything passes.
        /// </summary>
        /// <param name="node">The node to test.</param>
        /// <param name="visibilityRow">A cluster bitfield, one bit per cluster id.</param>
        /// <returns>Whether the node may draw.</returns>
        public bool IsNodeInPvs(SceneNode node, ReadOnlySpan<byte> visibilityRow)
        {
            if (visibilityRow.IsEmpty || VoxelVisibility == null)
            {
                return true;
            }

            var clusters = node.GetVisClusters(VoxelVisibility, WorldToVisibility);

            if (clusters.IsEmpty)
            {
                return true;
            }

            foreach (var cluster in clusters)
            {
                if (cluster < visibilityRow.Length * 8 && MathUtils.GetBit(visibilityRow, cluster))
                {
                    return true;
                }
            }

            return false;
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
            => SetupSunShadows(LightingInfo, [this], camera, shadowMapSize);

        /// <summary>
        /// Fits the sun light shadow cascades of one scene's sun around what several scenes draw, and collects
        /// the shadow draw calls of each of them, if that sun casts dynamic shadows.
        /// </summary>
        /// <param name="sun">The lighting whose sun and cascades are used.</param>
        /// <param name="casters">Every scene that casts into the cascades.</param>
        /// <param name="camera">The main camera used to fit the shadow cascades.</param>
        /// <param name="shadowMapSize">The shadow map resolution; pass -1 to produce empty frustums (pre-warm pass).</param>
        internal static void SetupSunShadows(WorldLightingInfo sun, IEnumerable<Scene> casters, Camera camera, int shadowMapSize)
        {
            if (!sun.EnableDynamicShadows)
            {
                return;
            }

            sun.UpdateSunLightFrustum(camera, shadowMapSize);

            for (var cascade = 0; cascade < WorldLightingInfo.SunCascadeCount; cascade++)
            {
                var active = cascade < sun.ActiveSunCascadeCount;

                if (active && shadowMapSize == -1)
                {
                    sun.SunLightFrustums[cascade].SetEmpty();
                }

                var casterDepthMin = float.MaxValue;
                var casterDepthMax = float.MinValue;

                foreach (var scene in casters)
                {
                    if (!active)
                    {
                        foreach (var bucket in scene.CulledShadowDrawCallsCascades[cascade].Values)
                        {
                            bucket.Clear();
                        }

                        continue;
                    }

                    var sunVisibility = scene.EnablePvsCulling && scene.VoxelVisibility != null
                        ? scene.VoxelVisibility.SunVisibility
                        : default;

                    scene.CollectShadowDrawCalls(sun.SunLightFrustums[cascade],
                        includeStatic: !scene.LightingInfo.HasBakedShadowsFromLightmap,
                        includeDynamic: true, skipFlags: ObjectTypeFlags.NoShadows,
                        scene.CulledShadowDrawCallsCascades[cascade],
                        sun.SunCastDirection, out var sceneDepthMin, out var sceneDepthMax,
                        sunVisibility);

                    casterDepthMin = MathF.Min(casterDepthMin, sceneDepthMin);
                    casterDepthMax = MathF.Max(casterDepthMax, sceneDepthMax);
                }

                if (active)
                {
                    sun.FitSunLightDepthRange(cascade, casterDepthMin, casterDepthMax);
                }
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
            CollectShadowDrawCalls(lightFrustum, includeStatic: light.DirectLight != SceneLight.DirectLightType.Stationary, includeDynamic: true, skipFlags: ObjectTypeFlags.None, barnShadowDrawCalls);

            return barnShadowDrawCalls;
        }

        private void CollectShadowDrawCalls(Frustum frustum, bool includeStatic, bool includeDynamic, ObjectTypeFlags skipFlags, Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>> drawBuckets)
            => CollectShadowDrawCalls(frustum, includeStatic, includeDynamic, skipFlags, drawBuckets, Vector3.Zero, out _, out _, default);

        private void CollectShadowDrawCalls(Frustum frustum, bool includeStatic, bool includeDynamic, ObjectTypeFlags skipFlags, Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>> drawBuckets,
            Vector3 depthFitAxis, out float casterDepthMin, out float casterDepthMax, ReadOnlyMemory<byte> casterVisibility)
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
                if (!node.Visible)
                {
                    continue;
                }

                if (!IsNodeInPvs(node, casterVisibility.Span))
                {
                    PerfStats.Active.Count(Counter.ShadowCasterCulledByPvs, 1);
                    continue;
                }

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
                    if ((node.Flags & skipFlags) == 0 && (node.RenderPasses & CustomRenderPasses.DepthOnly) != 0)
                    {
                        AccumulateDepthFit(node);

                        drawBuckets[DepthOnlyBucket.MaterialDepthMode].Add(new MeshBatchRenderer.Request
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
        internal static DepthOnlyBucket GetDepthOnlyBucket(DrawCall opaqueCall)
        {
            return opaqueCall.Material.VertexAnimation ? DepthOnlyBucket.MaterialDepthMode
                : opaqueCall.Material.IsAlphaTest ? DepthOnlyBucket.AlphaTest
                : DepthOnlyBucket.Specialized;
        }

        /// <summary>Picks the shader for a depth-only bucket, or <see langword="null"/> to resolve one per draw.</summary>
        private static Shader? GetDepthOnlyReplacementShader(DepthOnlyBucket bucket, Shader depthOnlyShader) => bucket switch
        {
            DepthOnlyBucket.AlphaTest => depthOnlyShader.WithCombo(Shader.AlphaTestCombo, 1),
            DepthOnlyBucket.MaterialDepthMode => null,
            _ => depthOnlyShader,
        };

        internal void UpdateIndirectRenderingState()
        {
            CompactMeshletDraws = false;
            DrawMeshletsIndirect = EnableIndirectDraws && SceneMeshletCount > 0 && IndirectDrawTemplate != null;

            if (DrawMeshletsIndirect)
            {
                CompactMeshletDraws = GLEnvironment.IndirectCountSupported && EnableCompaction;
            }
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
            renderContext.DepthOnlyShader = depthOnlyShader;

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
            ArgumentNullException.ThrowIfNull(layers);

            enabledLayers = [.. layers];

            foreach (var renderer in AllNodes)
            {
                ApplyLayerVisibility(renderer);
            }
        }

        private void ApplyLayerVisibility(SceneNode node)
        {
            if (enabledLayers == null)
            {
                return;
            }

            if (node.LayerName == null)
            {
                node.LayerEnabled = false;
                return;
            }

            if (node.LayerName.StartsWith("Internal -", StringComparison.Ordinal))
            {
                return;
            }

            node.LayerEnabled = enabledLayers.Contains(node.LayerName);
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
            OctreeVersion++;

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
            => SetFogConstants(viewConstants, FogInfo, FogSpace.World);

        /// <summary>
        /// Writes the scene's weather parameters and the fog a view of it draws with into the provided
        /// view constants structure.
        /// </summary>
        /// <param name="viewConstants">The view constants to update.</param>
        /// <param name="fog">The fog the view draws with.</param>
        /// <param name="fogSpace">Converts authored fog distances and heights into the view's space.</param>
        internal void SetFogConstants(ViewConstants viewConstants, WorldFogInfo fog, FogSpace fogSpace)
        {
            ArgumentNullException.ThrowIfNull(fog);

            fog.SetFogUniforms(viewConstants, FogEnabled, fogSpace);

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

                node.LightProbeBinding ??= ChooseLightProbeVolume(node.BoundingBox.Center);
            }
        }

        internal IReadOnlyList<SceneLightProbe> ProbeAtlasVolumes
            => LightingInfo.LightProbeType == LightProbeType.ProbeAtlas && boundLightProbes != null ? boundLightProbes : [];

        internal static bool VolumeContains(SceneLightProbe probe, Vector3 position)
        {
            return probe.BoundingBox.Contains(position)
                && probe.LocalBoundingBox.Contains(Vector3.Transform(position, probe.WorldToLocal));
        }

        /// <summary>
        /// Returns the best probe volume containing the given position, or <see langword="null"/> when
        /// none does.
        /// </summary>
        public SceneLightProbe? ChooseLightProbeVolume(Vector3 position)
        {
            if (boundLightProbes == null)
            {
                return null;
            }

            SceneLightProbe? best = null;
            var bestPriority = int.MinValue;
            var bestScore = float.MaxValue;

            foreach (var probe in CollectionsMarshal.AsSpan(boundLightProbes))
            {
                if (!probe.BoundingBox.Contains(position))
                {
                    continue;
                }

                var local = Vector3.Transform(position, probe.WorldToLocal);
                var bounds = probe.LocalBoundingBox;

                if (!bounds.Contains(local))
                {
                    continue;
                }

                var score = (local - bounds.Center).LengthSquared();

                if (probe.IndoorOutdoorLevel < bestPriority
                    || (probe.IndoorOutdoorLevel == bestPriority && score >= bestScore))
                {
                    continue;
                }

                best = probe;
                bestPriority = probe.IndoorOutdoorLevel;
                bestScore = score;
            }

            if (best != null)
            {
                return best;
            }

            // Choose a nearby volume
            foreach (var probe in CollectionsMarshal.AsSpan(boundLightProbes))
            {
                var score = probe.BoundingBox.DistanceSquared(position);

                if (probe.IndoorOutdoorLevel < bestPriority
                    || (probe.IndoorOutdoorLevel == bestPriority && score >= bestScore))
                {
                    continue;
                }

                best = probe;
                bestPriority = probe.IndoorOutdoorLevel;
                bestScore = score;
            }

            return best;
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
                            if (Vector3.DistanceSquared(envMap.Transform.Translation, lightingOrigin) < 0.01f)
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
                FrustumBuffer?.Dispose();
                Skybox2D?.Delete();
                Skybox2D = null;
                lightingBuffer?.Dispose();
                lpvBuffer?.Dispose();
                envMapBuffer?.Dispose();
                simulationDispatch.Dispose();
                LightingInfo.DisposeBarnLights();

                DeleteIndirectDrawBuffers();
                InstanceBufferGpu?.Delete();
                ObjectBufferGpu?.Delete();
                TransformBufferGpu?.Delete();
                ObjectLodGpu?.Delete();
                ActiveLodBitsGpu?.Delete();

                InstanceBufferGpu = null;
                ObjectBufferGpu = null;
                TransformBufferGpu = null;
                ObjectLodGpu = null;
                ActiveLodBitsGpu = null;
            }
        }
    }
}
