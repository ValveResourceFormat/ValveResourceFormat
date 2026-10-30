using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.Decals;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.Particles;
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

            /// <summary>Gets or sets the per-view state of the scene being drawn.</summary>
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

            /// <summary>
            /// Gets or sets whether the renderer draws the fills of shapes with <see cref="ShapeSceneNode.HasOrderIndependentFill"/>
            /// with order independent transparency after the translucent layer, so they skip them in it.
            /// </summary>
            internal bool OrderIndependentTranslucency { get; set; }

            /// <summary>Gets the list of scene-level textures bound to reserved texture slots.</summary>
            public required List<(ReservedTextureSlots Slot, string Name, RenderTexture Texture)> Textures { get; init; }
        }

        /// <summary>Gets the render attribute overrides applied to all draw calls in this scene.</summary>
        public Dictionary<string, byte> RenderAttributes { get; } = [];

        /// <summary>Gets the world lighting information including light probes, environment maps, and dynamic lights.</summary>
        public WorldLightingInfo LightingInfo { get; }

        /// <summary>Gets or sets the player in this scene, if there is one.</summary>
        public PlayerEntity? Player { get; set; }

        internal ParticleScenePlayer ParticlePlayer => field ??= new(this);

        /// <summary>Gets the light styles of the game by name, loaded on first use.</summary>
        internal IReadOnlyDictionary<string, CurveLightStyle> LightStyles => field ??= CurveLightStyle.LoadAll(RendererContext.FileLoader);

        /// <summary>Gets or sets the fog parameters for this scene.</summary>
        public WorldFogInfo FogInfo { get; set; } = new();

        /// <summary>Gets or sets the post-processing parameters for this scene.</summary>
        public WorldPostProcessInfo PostProcessInfo { get; set; } = new();

        /// <summary>Gets or sets the 2D sky the map's sky entities provide, or <see langword="null"/> when it has none.</summary>
        public SceneSkybox2D? Skybox2D { get; set; }

        /// <summary>Gets the world group this scene belongs to, such as <c>skyboxWorldGroup0</c>, or <see langword="null"/> for the main world.</summary>
        public string? WorldGroup { get; init; }

        /// <summary>
        /// How large an editor marker is drawn here next to the entity it marks. A 3D sky is magnified by
        /// the camera it is drawn through, so its markers shrink to come back out at their normal size.
        /// </summary>
        internal float MarkerScale { get; set; } = 1f;

        /// <summary>Maps this scene's space to where the main camera sees it. Identity, except for a 3D sky.</summary>
        public Matrix4x4 ToViewerWorld { get; internal set; } = Matrix4x4.Identity;

        /// <summary>Gets or sets the voxel visibility data.</summary>
        public IWorldVisibility? VoxelVisibility
        {
            get;
            set
            {
                field = value;
                StaticOctree.Dirty = true;
            }
        }

        /// <summary>Gets or sets the transform from scene space into the space <see cref="VoxelVisibility"/> was compiled in.</summary>
        public Matrix4x4 WorldToVisibility
        {
            get;
            set
            {
                field = value;
                StaticOctree.Dirty = true;
            }
        } = Matrix4x4.Identity;

        /// <summary>Gets the <c>info_visibility_box</c> volumes spawned in this scene.</summary>
        public VisibilityBoxSet VisibilityBoxes { get; } = new();

        private UniformBuffer<LightingConstants>? lightingBuffer;
        private UniformBuffer<EnvMapArray>? envMapBuffer;
        private UniformBuffer<LightProbeVolumeArray>? lpvBuffer;

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

        internal DrawElementsIndirectCommand[]? IndirectDrawTemplate { get; private set; }

        internal uint[]? CompactedCountsTemplate { get; private set; }

        internal int IndirectLayoutVersion { get; private set; }

        /// <summary>Gets the debug views of this scene's baked lighting.</summary>
        public SceneLightingDebug LightingDebug { get; }

        internal int OctreeVersion { get; private set; }

        internal Shader? FrustumCullShader { get; private set; }
        internal Shader? CompactionShader { get; private set; }

        /// <summary>Gets the GPU thinning of the scene's clutter, or <see langword="null"/> when it has none.</summary>
        internal ClutterCuller? ClutterCuller { get; private set; }

        internal LightBinner? ShadingLightBinner { get; set; }

        /// <summary>Gets the renderer context providing shared GPU resources and shader loading.</summary>
        public RendererContext RendererContext { get; }

        /// <summary>Gets the octree used to spatially partition static scene nodes.</summary>
        public Octree StaticOctree { get; }

        /// <summary>Gets the flat spatial set holding dynamic scene nodes.</summary>
        public SpatialNodeSet DynamicOctree { get; } = new();

        /// <summary>Gets the rendering toggles.</summary>
        public RenderSettings Settings { get; internal set; } = new();

        /// <summary>Gets or sets the occlusion debug renderer, or <see langword="null"/> if not initialized.</summary>
        public OcclusionDebugRenderer? OcclusionDebug { get; set; }

        internal bool DrawMeshletsIndirect { get; private set; }
        internal bool CompactMeshletDraws { get; private set; }

        /// <summary>Gets all static and dynamic scene nodes in the order they were added.</summary>
        public IEnumerable<SceneNode> AllNodes => staticNodes.Concat(dynamicNodes);

        private readonly List<SceneNode> staticNodes = [];
        private readonly List<SceneNode> dynamicNodes = [];

        internal int NodeCount => staticNodes.Count + dynamicNodes.Count;

        internal int ObjectEntryCount => objectEntryCount;

        private readonly ParallelDispatch simulationDispatch = new();
        private SimulationWork? simulationWork;

        private readonly List<(SceneNode Node, bool Dynamic)> pendingAdds = [];
        private readonly Dictionary<SceneNode, bool> pendingRemovals = [];
        private bool updating;

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

        // Bound probes in precedence order: most indoor first, then smallest, so the first volume
        // containing a point is the best one
        private List<SceneLightProbe>? boundLightProbes;

        /// <summary>
        /// Initializes a new scene with the given renderer context, its static octree sized to the game's whole map extent.
        /// </summary>
        /// <param name="context">The renderer context providing shared GPU resources.</param>
        public Scene(RendererContext context)
        {
            RendererContext = context;

            var gameInfo = context.FileLoader.GameInfo;
            StaticOctree = new(gameInfo.MapMaxCoord * 2f);

            var defaultExposure = new ExposureSettings();
            PostProcessInfo.DefaultExposureSettings = defaultExposure with
            {
                ExposureMin = gameInfo.DefaultAutoExposureMin ?? defaultExposure.ExposureMin,
                ExposureMax = gameInfo.DefaultAutoExposureMax ?? defaultExposure.ExposureMax,
            };

            LightingInfo = new(this);
            ProjectedDecals = new(this);
            ParticleCollision = new(this);
            LightingDebug = new(this);
        }

        /// <summary>
        /// Gets or sets the physics world holding this scene's collision, which particle systems in the
        /// scene trace against. Null when the scene has no world of its own.
        /// </summary>
        public PhysicsWorld? PhysicsWorld { get; set; }

        /// <summary>Gets the collision every particle system in the scene traces against.</summary>
        internal ParticleSceneCollision ParticleCollision { get; }

        /// <summary>Gets the decals projected onto the scene's depth, such as bullet impacts.</summary>
        public ProjectedDecalSystem ProjectedDecals { get; }

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

            FrustumCullShader = RendererContext.ShaderLoader.LoadShader("frustum_cull");
            CompactionShader = RendererContext.ShaderLoader.LoadShader("compact_indirect_draws");
        }

        /// <summary>
        /// Adds a node to the scene, placing it in either the static or dynamic partition.
        /// </summary>
        /// <remarks>
        /// Called while the scene is updating, such as from a node's own update, the node joins after the
        /// update and is first updated and drawn on the next frame. Adding a node removed during the same
        /// update keeps it in the scene instead.
        /// </remarks>
        /// <param name="node">The node to add.</param>
        /// <param name="dynamic">When <see langword="true"/>, the node is placed in <see cref="DynamicOctree"/>; otherwise in <see cref="StaticOctree"/>.</param>
        public void Add(SceneNode node, bool dynamic)
        {
            if (updating)
            {
                if (!pendingRemovals.Remove(node))
                {
                    pendingAdds.Add((node, dynamic));
                }

                return;
            }

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
        /// <remarks>
        /// Called while the scene is updating, the node is no longer updated from then on and leaves before
        /// the frame draws, so it may be deleted right away.
        /// </remarks>
        /// <param name="node">The node to remove.</param>
        /// <param name="dynamic">When <see langword="true"/>, removes from the dynamic partition; otherwise the static partition.</param>
        /// <returns>Whether the node was in the partition, or is leaving it when the scene is updating.</returns>
        public bool Remove(SceneNode node, bool dynamic)
        {
            if (updating)
            {
                var pendingAdd = pendingAdds.FindIndex(pending => pending.Node == node);

                if (pendingAdd >= 0)
                {
                    pendingAdds.RemoveAt(pendingAdd);
                }
                else
                {
                    pendingRemovals[node] = dynamic;
                }

                return true;
            }

            node.DetachFromParent();

            if (dynamic)
            {
                DynamicOctree.Dirty = true;
                return dynamicNodes.Remove(node);
            }

            StaticOctree.Dirty = true;
            return staticNodes.Remove(node);
        }

        /// <summary>Whether <paramref name="node"/> was removed during the update in progress, and so is no longer updated.</summary>
        internal bool IsPendingRemoval(SceneNode node) => pendingRemovals.Count > 0 && pendingRemovals.ContainsKey(node);

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

        internal SceneNode GetNodeById(uint id)
        {
            var index = (int)id - 1;

            return index < staticNodes.Count ? staticNodes[index] : dynamicNodes[index - staticNodes.Count];
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

        /// <summary>Removes and deletes every node and the 2D sky, keeping shared materials and mesh buffers loaded.</summary>
        /// <remarks>
        /// The partitions are emptied before any node is deleted, so a node that removes and deletes nodes it
        /// owns finds them already gone from the scene and leaves their deletion to it.
        /// </remarks>
        public void DeleteNodes()
        {
            LightingDebug.Clear();

            SceneNode[] nodes = [.. dynamicNodes, .. staticNodes];

            dynamicNodes.Clear();
            staticNodes.Clear();

            foreach (var node in nodes)
            {
                node.Delete();
            }

            StaticOctree.Clear();
            DynamicOctree.Clear();

            Skybox2D?.Delete();
            Skybox2D = null;
            ProjectedDecals.Clear();
        }

        /// <summary>
        /// Forgets what the scene's entities registered with it and left on it, once they are removed: the
        /// environment maps and light probe volumes, post processing and decals.
        /// </summary>
        internal void ClearEntityState()
        {
            LightingDebug.Clear();
            LightingInfo.ClearVolumes();
            PostProcessInfo.Clear();
            ProjectedDecals.Clear();
        }

        /// <summary>
        /// Binds every node to the light probe volumes and environment maps again, once the entities that
        /// register them have spawned again.
        /// </summary>
        public void RebindLighting()
        {
            foreach (var node in AllNodes)
            {
                node.LightProbeBinding = null;
                node.EnvMapBinding = null;
            }

            UpdateOctrees();
            CalculateLightProbeBindings();
            CalculateEnvironmentMaps();
            UpdateBuffers();

            // The bindings reach the shaders through each node's entry, written when the nodes are laid out
            DynamicOctree.Dirty = true;
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
        private sealed class SimulationWork(Scene scene, List<SceneNode> nodes) : IParallelWork
        {
            public UpdateContext Context;

            public void Execute(int index)
            {
                var node = nodes[index];

                // Parented nodes are placed by their parent, but simulate here like every other one
                if (node.Simulation == NodeSimulation.Parallel && !scene.IsPendingRemoval(node))
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
                simulationWork ??= new SimulationWork(this, dynamicNodes);
                simulationWork.Context = simulate;

                // The dispatch publishes the context, and runs counts too small to fan out inline
                simulationDispatch.Run(simulationWork, dynamicNodes.Count);
            }

            var act = updateContext with { Phase = UpdatePhase.Act };

            foreach (var node in dynamicNodes)
            {
                if (node.Simulation == NodeSimulation.None || IsPendingRemoval(node))
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
            LightingDebug.Update(updateContext);

            updating = true;

            try
            {
                UpdateNodes(updateContext);
            }
            finally
            {
                updating = false;
            }

            foreach (var (node, dynamic) in pendingRemovals)
            {
                Remove(node, dynamic);
            }

            pendingRemovals.Clear();

            LightingInfo.StoreChangedLights();

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

            foreach (var (node, dynamic) in pendingAdds)
            {
                Add(node, dynamic);
            }

            pendingAdds.Clear();
        }

        private void UpdateNodes(Scene.UpdateContext updateContext)
        {
            foreach (var node in staticNodes)
            {
                if (node.Parent == null)
                {
                    node.UpdateHierarchy(updateContext);
                }
            }

            foreach (var node in dynamicNodes)
            {
                if (node.Parent != null)
                {
                    continue; // child nodes are updated by their parent
                }

                node.UpdateHierarchy(updateContext);
            }

            SimulateNodes(updateContext);
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
                else if ((node.AdditionalFlags & SceneNodeFlags.PreTransformedVertices) != 0 || (neverChangesTransform && node.Transform.IsIdentity))
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

            ClutterCuller?.Delete();
            ClutterCuller = nodes.OfType<SceneClutter>().ToList() is { Count: > 0 } clutterNodes
                ? new ClutterCuller(RendererContext, clutterNodes, instanceData)
                : null;

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
            var readsNodeBindings = LightingInfo.LightmapGameVersionNumber < 2;
            var rebindProbes = readsNodeBindings && boundLightProbes is { Count: > 0 };
            var rebindEnvMaps = readsNodeBindings && LightingInfo.EnvMaps.Count > 0;

            foreach (var node in dynamicNodes)
            {
                // Nodes the last layout has not seen have no entries yet
                if (node.Id == 0 || node.Id >= objectEntryCount)
                {
                    continue;
                }

                if (rebindProbes || rebindEnvMaps)
                {
                    RebindMovedNodeLighting(node, rebindProbes, rebindEnvMaps);
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

        private void RebindMovedNodeLighting(SceneNode node, bool rebindProbes, bool rebindEnvMaps)
        {
            var lightingPosition = LightingPosition(node);

            if (node.LightingBindPosition == lightingPosition)
            {
                return;
            }

            node.LightingBindPosition = lightingPosition;

            if (rebindProbes && node.LightProbeVolumePrecomputedHandshake == 0 && !BindsByHandshake(node))
            {
                node.LightProbeBinding = ChooseLightProbeVolume(lightingPosition, node.BoundingBox)!;
            }

            if (rebindEnvMaps && node.CubeMapPrecomputedHandshake == 0 && !BindsByHandshake(node))
            {
                node.EnvMapBinding = ChooseEnvironmentMap(lightingPosition);
            }
        }

        private static void UploadRange<T>(StorageBuffer buffer, ReadOnlySpan<T> data, int start, int end) where T : unmanaged
        {
            if (end > start)
            {
                buffer.Update<T>(data[start..end], start * Unsafe.SizeOf<T>());
            }
        }

        private static ObjectDataStandard ObjectEntry(SceneNode node)
        {
            var envMapIndex = node.EnvMapBinding is { } envMap ? (uint)envMap.ShaderIndex : node.ShaderEnvMapVisibility.GetFirstShaderIndex();

            if (node.HasNoEnvMap)
            {
                envMapIndex = SceneEnvMap.NoEnvMapIndex;
            }

            var lightProbeIndex = node.LightProbeBinding is { } probe ? (uint)probe.ShaderIndex : SceneLightProbe.NoLightProbeIndex;

            return new()
            {
                VisibleLPV = lightProbeIndex | (envMapIndex << 16),
                EnvMapVisibility = node.ShaderEnvMapVisibility,
            };
        }

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
            OcclusionDebug = null;

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

        /// <summary>Uploads the lighting constants, which <see cref="SetSceneBuffers"/> binds.</summary>
        internal void UpdateLightingBuffer() => lightingBuffer?.Update();

        /// <summary>Binds the lighting and draw buffers to their reserved slots.</summary>
        public void SetSceneBuffers()
        {
            Debug.Assert(lightingBuffer is not null && envMapBuffer is not null && lpvBuffer is not null);

            lightingBuffer.BindBufferBase();
            envMapBuffer.BindBufferBase();
            lpvBuffer.BindBufferBase();
            BindDrawBuffers();
            LightingInfo.BindBarnLightBuffer();
        }

        // Nodes that draw themselves read these by id without binding them, so rebind on every scene switch
        internal void BindDrawBuffers()
        {
            InstanceBufferGpu?.BindBufferBase();
            TransformBufferGpu?.BindBufferBase();
            ObjectBufferGpu?.BindBufferBase();
        }

        /// <summary>
        /// Tests a node against a visibility row, such as a PVS or the sun row. A node that touches no
        /// visibility cluster is never vis culled, and an empty row passes everything.
        /// </summary>
        /// <param name="node">The node to test.</param>
        /// <param name="visibilityRow">A cluster bitfield, one bit per cluster id.</param>
        /// <returns>Whether the node may draw.</returns>
        public bool IsNodeInPvs(SceneNode node, ReadOnlySpan<byte> visibilityRow)
        {
            if (visibilityRow.IsEmpty || VoxelVisibility == null || (node.Flags & ObjectTypeFlags.DisableVisCulling) != 0)
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

        /// <summary>Gets whether a draw of a node is drawn into the views, see <see cref="RenderSettings.DrawFilter"/>.</summary>
        internal bool IsDrawn(RenderMaterial material, SceneNode node)
            => Settings.DrawFilter?.GetVisibility(node, material) is null or DrawVisibility.Drawn;

        // Geometry that blocks light casts shadows whatever its material says
        internal bool CastsShadows(RenderMaterial material, SceneNode node)
            => Settings.DrawFilter?.GetVisibility(node, material) switch
            {
                DrawVisibility.Hidden => false,
                DrawVisibility.ShadowOnly => true,
                _ => material.BlocksLight || !material.DoNotCastShadows,
            };

        internal void UpdateIndirectRenderingState()
        {
            CompactMeshletDraws = false;
            DrawMeshletsIndirect = Settings.EnableIndirectDraws && SceneMeshletCount > 0 && IndirectDrawTemplate != null;

            if (DrawMeshletsIndirect)
            {
                CompactMeshletDraws = GLEnvironment.IndirectCountSupported && Settings.EnableCompaction;
            }
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

        /// <summary>
        /// Gets whether a layer is internal to the renderer. Internal layers are switched one at a time rather
        /// than by <see cref="SetEnabledLayers"/>, and are not offered to the user.
        /// </summary>
        /// <param name="layerName">The layer name of a node.</param>
        internal static bool IsInternalLayer(string layerName) => layerName.StartsWith("Internal -", StringComparison.Ordinal);

        /// <summary>
        /// Gets whether the nodes of a layer are shown by the layers last given to <see cref="SetEnabledLayers"/>,
        /// which shows every layer until it is first called.
        /// </summary>
        /// <param name="layerName">The name of the layer.</param>
        /// <returns>Whether the layer is shown.</returns>
        public bool IsLayerEnabled(string layerName)
        {
            ArgumentNullException.ThrowIfNull(layerName);

            return enabledLayers == null || IsInternalLayer(layerName) || enabledLayers.Contains(layerName);
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

            if (IsInternalLayer(node.LayerName))
            {
                return;
            }

            node.LayerEnabled = enabledLayers.Contains(node.LayerName);
        }

        /// <summary>
        /// Marks the spatial set that owns the given node as dirty so it will be rebuilt on the next update.
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

                StaticOctree.Clear(maxBounds, RendererContext.FileLoader.GameInfo.MapMaxCoord);

                foreach (var node in staticNodes)
                {
                    if (node.LayerEnabled)
                    {
                        StaticOctree.Insert(node);
                    }
                }

                StaticOctree.BuildVisClusters(VoxelVisibility, WorldToVisibility);
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

            fog.SetFogUniforms(viewConstants, Settings.FogEnabled, fogSpace);

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
            LightingInfo.BuildProbeAtlas();

            var sortedLightProbes = LightingInfo.LightProbes
                .Where(LightingInfo.IsInProbeAtlas)
                .OrderByDescending(static lpv => lpv.IndoorOutdoorLevel)
                .ThenBy(static lpv => lpv.LocalBoundingBox.Size.X * lpv.LocalBoundingBox.Size.Y * lpv.LocalBoundingBox.Size.Z)
                .Take(LightProbeVolumeArray.MAX_PROBES)
                .ToList();

            var i = 0;
            foreach (var probe in sortedLightProbes)
            {
                probe.ShaderIndex = i;
                lpvBuffer.Data.Probes[i] = probe.CalculateGpuProbeData();
                i++;
            }

            boundLightProbes = sortedLightProbes;

            if (sortedLightProbes.Count == 0)
            {
                // remove baked lighting from probe attribute?
                return;
            }

            // A node is only bound to a volume that can be sampled, and is otherwise given the one around it
            var boundSet = sortedLightProbes.ToHashSet();

            foreach (var node in AllNodes)
            {
                node.LightingBindPosition = new Vector3(float.NaN);

                var precomputedProbe = FindPrecomputedLightProbe(node);

                if (precomputedProbe != null && boundSet.Contains(precomputedProbe))
                {
                    node.LightProbeBinding = precomputedProbe;
                }
                else if (precomputedProbe == null && BindsByHandshake(node))
                {
                    node.LightProbeBinding = null;
                }
                else if (node.LightProbeBinding == null || !boundSet.Contains(node.LightProbeBinding))
                {
                    node.LightProbeBinding = ChooseLightProbeVolume(LightingPosition(node), node.BoundingBox);
                }
            }
        }

        private bool BindsByHandshake(SceneNode node)
            => LightingInfo.LightmapGameVersionNumber == 0
                && (node.LightProbeVolumePrecomputedHandshake != 0 || node.CubeMapPrecomputedHandshake != 0);

        private SceneLightProbe? FindPrecomputedLightProbe(SceneNode node)
        {
            if (node.EntityData is { } entityData
                && LightingInfo.LightProbes.Find(p => ReferenceEquals(p.EntityData, entityData)) is { } selfProbe)
            {
                return selfProbe;
            }

            var precomputedHandshake = node.LightProbeVolumePrecomputedHandshake;
            if (precomputedHandshake == 0)
            {
                return null;
            }

            return LightingInfo.ProbeHandshakes.GetValueOrDefault(precomputedHandshake);
        }

        internal IReadOnlyList<SceneLightProbe> ProbeAtlasVolumes
            => boundLightProbes != null ? boundLightProbes : [];

        /// <summary>
        /// Returns the environment map that best covers a position: one whose volume contains it, the
        /// highest priority among those and then the nearest to its origin, otherwise the one whose
        /// volume is nearest.
        /// </summary>
        /// <param name="position">A position in this scene.</param>
        /// <returns>The environment map, or <see langword="null"/> when the scene has none.</returns>
        public SceneEnvMap? ChooseEnvironmentMap(Vector3 position)
        {
            SceneEnvMap? best = null;
            var bestPriority = int.MinValue;
            var bestScore = float.MaxValue;

            // Only the uploaded ones, which have their inverse transform and a shader index
            var envMaps = CollectionsMarshal.AsSpan(LightingInfo.EnvMaps);
            envMaps = envMaps[..Math.Min(envMaps.Length, EnvMapArray.MAX_ENVMAPS)];

            foreach (var envMap in envMaps)
            {
                // The world bounds enclose the volume, so outside them the position cannot be inside it
                if (!envMap.BoundingBox.Contains(position))
                {
                    continue;
                }

                var score = EnvMapDistanceSquared(envMap, position, out var inside);

                if (!inside
                    || envMap.IndoorOutdoorLevel < bestPriority
                    || (envMap.IndoorOutdoorLevel == bestPriority && score > bestScore))
                {
                    continue;
                }

                best = envMap;
                bestPriority = envMap.IndoorOutdoorLevel;
                bestScore = score;
            }

            if (best != null)
            {
                return best;
            }

            // No volume contains it, so choose the nearest one
            foreach (var envMap in envMaps)
            {
                var score = EnvMapDistanceSquared(envMap, position, out _);

                if (score > bestScore)
                {
                    continue;
                }

                best = envMap;
                bestScore = score;
            }

            return best;
        }

        // Squared distance from the env map's volume, or from its origin when the volume contains the position
        private static float EnvMapDistanceSquared(SceneEnvMap envMap, Vector3 position, out bool inside)
        {
            var local = Vector3.Transform(position, envMap.WorldToLocal);
            var originDistance = local.LengthSquared();

            if (envMap.ProjectionMode == 0)
            {
                // A sphere's bounds are its influence radius on every side
                var radius = envMap.LocalBoundingBox.Max.X;
                inside = originDistance < radius * radius;

                if (inside)
                {
                    return originDistance;
                }

                var outside = MathF.Sqrt(originDistance) - radius;
                return outside * outside;
            }

            var distance = envMap.LocalBoundingBox.DistanceSquared(local);
            inside = distance == 0f;

            return inside ? originDistance : distance;
        }

        /// <summary>
        /// Returns the probe volume that best covers a position: one that contains it, the highest priority among
        /// those and then the nearest to its origin, otherwise the nearest one that reaches the object.
        /// </summary>
        /// <param name="position">A position in this scene.</param>
        /// <param name="objectBounds">
        /// The bounds of the object lit at the position. A volume that does not contain the position is only chosen
        /// when it is within half the diagonal of the bounds from their center, or always when this is <see langword="null"/>.
        /// </param>
        /// <returns>The probe volume, or <see langword="null"/> when the scene has none.</returns>
        public SceneLightProbe? ChooseLightProbeVolume(Vector3 position, AABB? objectBounds = null)
        {
            if (boundLightProbes == null)
            {
                return null;
            }

            SceneLightProbe? best = null;
            var bestInside = false;
            var bestPriority = int.MinValue;
            var bestScore = float.MaxValue;

            var boundsCenter = objectBounds?.Center ?? position;
            var maxReachSquared = objectBounds is { } bounds ? (boundsCenter - bounds.Min).LengthSquared() : float.MaxValue;

            foreach (var probe in CollectionsMarshal.AsSpan(boundLightProbes))
            {
                // The world box contains the oriented one, so a probe it neither contains nor reaches is out of reach
                if (!probe.BoundingBox.Contains(position) && probe.BoundingBox.DistanceSquared(boundsCenter) > maxReachSquared)
                {
                    continue;
                }

                var local = Vector3.Transform(position, probe.WorldToLocal);
                var outsideDistance = probe.LocalBoundingBox.DistanceSquared(local);
                var inside = outsideDistance == 0f;

                if (!inside && objectBounds != null)
                {
                    var centerLocal = Vector3.Transform(boundsCenter, probe.WorldToLocal);

                    if (probe.LocalBoundingBox.DistanceSquared(centerLocal) > maxReachSquared)
                    {
                        continue;
                    }
                }

                var score = inside ? local.LengthSquared() : outsideDistance;

                if (inside)
                {
                    if (bestInside)
                    {
                        if (probe.IndoorOutdoorLevel < bestPriority)
                        {
                            continue;
                        }

                        if (probe.IndoorOutdoorLevel == bestPriority && score > bestScore)
                        {
                            continue;
                        }
                    }
                }
                else
                {
                    if (bestInside || score > bestScore)
                    {
                        continue;
                    }
                }

                best = probe;
                bestInside = inside;
                bestPriority = probe.IndoorOutdoorLevel;
                bestScore = score;
            }

            return best;
        }

        /// <summary>Adds the environment maps a node is shaded with, every one its visibility mask selects.</summary>
        /// <param name="node">A node in this scene.</param>
        /// <param name="envMaps">The list the environment maps are added to.</param>
        public void GetEnvMapsUsedBy(SceneNode node, List<SceneEnvMap> envMaps)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(envMaps);

            // Shader indices are assigned in order over the sorted list
            foreach (var shaderIndex in node.ShaderEnvMapVisibility.GetVisibleShaderIndices())
            {
                if (shaderIndex < LightingInfo.EnvMaps.Count)
                {
                    envMaps.Add(LightingInfo.EnvMaps[shaderIndex]);
                }
            }
        }

        /// <summary>
        /// Assigns environment maps to scene nodes based on spatial overlap and precomputed handshakes, and uploads env map data to the GPU uniform buffer.
        /// </summary>
        public void CalculateEnvironmentMaps()
        {
            LightingInfo.BuildEnvMapArray();

            if (LightingInfo.EnvMaps.Count == 0)
            {
                // Zero environment maps tells the shaders to use a flat reflection
                LightingInfo.LightingData.EnvMapSizeConstants = Vector4.Zero;
                return;
            }

            var firstTexture = LightingInfo.EnvMaps.First().EnvMapTexture;

            var isRgbm = RendererContext.FileLoader.GameInfo.EnvironmentMapsAreRgbm
                || (LightingInfo.LightmapVersionNumber > 0 && LightingInfo.LightmapGameVersionNumber == 0);

            LightingInfo.LightingData.EnvMapSizeConstants = new Vector4(firstTexture.NumMipLevels - 1, firstTexture.Depth, isRgbm ? 1 : 0, 0);

            static int IndoorPriorityCompare(SceneEnvMap a, SceneEnvMap b)
            {
                var indoor = b.IndoorOutdoorLevel.CompareTo(a.IndoorOutdoorLevel);
                if (indoor != 0)
                {
                    return indoor;
                }

                var volume = a.InfluenceVolume.CompareTo(b.InfluenceVolume);
                return volume != 0 ? volume : a.ArrayIndex.CompareTo(b.ArrayIndex);
            }

            static int HandShakeCompare(SceneEnvMap a, SceneEnvMap b) => a.HandShake.CompareTo(b.HandShake);

            // SteamVR Home nodes name their environment map by its index in handshake order
            LightingInfo.EnvMaps.Sort(LightingInfo.LightmapGameVersionNumber == 0 ? HandShakeCompare : IndoorPriorityCompare);

            var nodes = new List<SceneNode>();
            var overlapping = new Dictionary<SceneNode, List<SceneEnvMap>>();
            var i = 0;

            foreach (var envMap in LightingInfo.EnvMaps)
            {
                if (i >= EnvMapArray.MAX_ENVMAPS)
                {
                    RendererContext.Logger.LogError("Envmap array index {Index} is too large, skipping! Max: {MaxEnvMaps}", i, EnvMapArray.MAX_ENVMAPS);
                    continue;
                }

                StaticOctree.Query(envMap.BoundingBox, nodes);

                foreach (var node in nodes)
                {
                    if (!overlapping.TryGetValue(node, out var envMaps))
                    {
                        overlapping[node] = envMaps = [];
                    }

                    envMaps.Add(envMap);
                }

                UpdateGpuEnvmapData(envMap, i);
                envMap.ShaderIndex = i;
                i++;

                nodes.Clear();
            }

            foreach (var node in AllNodes)
            {
                node.LightingBindPosition = new Vector3(float.NaN);
                AssignEnvironmentMaps(node, overlapping.GetValueOrDefault(node) ?? []);
            }
        }

        /// <summary>
        /// Picks a node's environment maps again, for when its <see cref="SceneNode.LightingOrigin"/> changed after
        /// the scene was lit. A dynamic node's light probe volume follows on its own, in <see cref="UpdateInstanceTransformBuffers"/>.
        /// </summary>
        public void UpdateNodeEnvironmentMaps(SceneNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            if (LightingInfo.EnvMaps.Count == 0)
            {
                return;
            }

            AssignEnvironmentMaps(node, [.. LightingInfo.EnvMaps.Take(EnvMapArray.MAX_ENVMAPS).Where(envMap => envMap.BoundingBox.Intersects(node.BoundingBox))]);
        }

        // Where a node samples lighting from: its lighting origin, or else its bounds center
        private static Vector3 LightingPosition(SceneNode node) => node.LightingOrigin ?? node.BoundingBox.Center;

        // Narrows the env maps a node overlaps to the ones it samples, and builds the visibility the shader reads
        private void AssignEnvironmentMaps(SceneNode node, List<SceneEnvMap> envMaps)
        {
            var precomputedHandshake = node.CubeMapPrecomputedHandshake;
            SceneEnvMap? preComputed = default;
            node.HasNoEnvMap = false;

            if (node.EntityData is { } entityData
                && LightingInfo.EnvMaps.Find(e => ReferenceEquals(e.EntityData, entityData)) is { } selfEnvMap)
            {
                envMaps.Clear();
                envMaps.Add(selfEnvMap);
            }
            else if (precomputedHandshake > 0)
            {
                if (LightingInfo.LightmapGameVersionNumber == 0 && !LightingInfo.EnvMapHandshakes.ContainsKey(precomputedHandshake))
                {
                    // The engine only binds an environment map whose handshake the node names, and has none otherwise
                    envMaps.Clear();
                    node.HasNoEnvMap = true;
                }
                else if (LightingInfo.EnvMapHandshakes.TryGetValue(precomputedHandshake, out preComputed))
                {
                    envMaps.Clear();
                    envMaps.Add(preComputed);
                }
                else
                {
#if DEBUG
                    RendererContext.Logger.LogDebug("An envmap with handshake [{Handshake}] does not exist for node at {Center}", precomputedHandshake, node.BoundingBox.Center);
#endif
                }
            }
            else if (BindsByHandshake(node))
            {
                envMaps.Clear();
                node.HasNoEnvMap = true;
            }

            var lightingOrigin = node.LightingOrigin ?? Vector3.Zero;
            if (node.LightingOrigin.HasValue)
            {
                if (LightingInfo.LightmapGameVersionNumber <= 1)
                {
                    envMaps.Clear();
                    foreach (var envMap in LightingInfo.EnvMaps)
                    {
                        if (envMap.BoundingBox.Contains(lightingOrigin))
                        {
                            envMaps.Add(envMap);
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
                            envMaps.Clear();
                            envMaps.Add(envMap);
                            break;
                        }
                    }
                }
            }

            envMaps.Sort((a, b) =>
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

            node.ShaderEnvMapVisibility = envMaps.Count > 0
                ? default(SceneEnvMap.EnvMapVisibility128).Store(envMaps)
                : SceneEnvMap.EnvMapVisibility128.Full;

#if DEBUG
            if (preComputed != default)
            {
                var vrfComputed = envMaps.FirstOrDefault();
                if (vrfComputed is null)
                {
                    RendererContext.Logger.LogDebug("Could not find any envmaps for node {DebugName}. Valve precomputed envmap is at {Center} [{Handshake}]", node.DebugName, preComputed.BoundingBox.Center, precomputedHandshake);
                    return;
                }

                if (vrfComputed.HandShake == precomputedHandshake)
                {
                    return;
                }

                var vrfDistance = Vector3.Distance(lightingOrigin, vrfComputed.BoundingBox.Center);
                var preComputedDistance = Vector3.Distance(lightingOrigin, LightingInfo.EnvMapHandshakes[precomputedHandshake].BoundingBox.Center);

                var anyIndex = envMaps.FindIndex(x => x.HandShake == precomputedHandshake);

                RendererContext.Logger.LogDebug("Topmost calculated envmap doesn't match with the precomputed one (dists: vrf={VrfDistance} s2={PreComputedDistance}) for node at {Center} [{Handshake}]{IterateInfo}",
                    vrfDistance, preComputedDistance, node.BoundingBox.Center, precomputedHandshake,
                    anyIndex > 0 ? $" (however it's still binned at a higher iterate index {anyIndex})" : string.Empty);
            }
#endif
        }

        private void UpdateGpuEnvmapData(SceneEnvMap envMap, int index)
        {
            Debug.Assert(envMapBuffer is not null);

            if (!Matrix4x4.Invert(envMap.Transform, out var worldToLocal))
            {
                throw new InvalidOperationException("Matrix invert failed");
            }

            envMap.WorldToLocal = worldToLocal;

            var boundsExtend = new Vector3(SceneEnvMap.BoundsExtend);
            var projectionBounds = envMap.ProjectionBounds ?? envMap.LocalBoundingBox;
            var radius = envMap.LocalBoundingBox.Max.X;

            envMapBuffer.Data.EnvMaps[index] = new EnvMapData
            {
                WorldToLocal = worldToLocal,
                BoxMins = envMap.LocalBoundingBox.Min - boundsExtend,
                ArrayIndex = (uint)envMap.ArrayIndex,
                BoxMaxs = envMap.LocalBoundingBox.Max + boundsExtend,
                ProjectionMins = projectionBounds.Min,
                InvRadius = radius > 0f ? 1f / radius : 0f,
                ProjectionMaxs = projectionBounds.Max,
                InvEdgeWidth = new Vector4(SceneEnvMap.GetInverseEdgeFade(envMap.EdgeFadeDists), 0),
                Origin = envMap.Transform.Translation,
                ProjectionType = (uint)envMap.ProjectionMode,
                Color = envMap.Tint,
                AssociatedLPV = GetBoundLightProbeIndex(LightingInfo.ProbeHandshakes.GetValueOrDefault(envMap.HandShake)),
                NormalizationSH = envMap.NormalizationSH
            };
        }

        private int GetBoundLightProbeIndex(SceneLightProbe? probe)
        {
            if (probe == null || boundLightProbes == null)
            {
                return -1;
            }

            var index = probe.ShaderIndex;
            var isBound = index < boundLightProbes.Count && boundLightProbes[index] == probe;

            return isBound ? index : -1;
        }

        /// <summary>
        /// Switches reflections between each environment map's texture and its debug colour.
        /// </summary>
        /// <param name="enabled">Whether reflections show debug colours.</param>
        internal void SetEnvMapDebugColors(bool enabled)
        {
            if (lightingBuffer == null || envMapBuffer == null)
            {
                return;
            }

            lightingBuffer.Data.EnvMapDebugColors = enabled ? 1u : 0u;

            // The first entries of the sorted list are the ones uploaded, in order
            var count = Math.Min(LightingInfo.EnvMaps.Count, EnvMapArray.MAX_ENVMAPS);

            for (var i = 0; i < count; i++)
            {
                var envMap = LightingInfo.EnvMaps[i];

                envMapBuffer.Data.EnvMaps[i].Color = enabled
                    ? LightingDebugColor.FromOrigin(envMap.Transform.Translation).ToLinearColor().AsVector3()
                    : envMap.Tint;
            }

            envMapBuffer.Update();
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
                ProjectedDecals.Delete();
                FrustumBuffer?.Delete();
                Skybox2D?.Delete();
                Skybox2D = null;
                lightingBuffer?.Delete();
                lpvBuffer?.Delete();
                envMapBuffer?.Delete();
                simulationDispatch.Dispose();
                LightingInfo.DisposeBarnLights();
                LightingInfo.DeleteProbeAtlas();
                LightingInfo.DeleteEnvMapArray();

                DeleteIndirectDrawBuffers();
                ClutterCuller?.Delete();
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
                ClutterCuller = null;
            }
        }
    }
}
