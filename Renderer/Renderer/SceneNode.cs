using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Which passes of the frame beyond <see cref="Scene.UpdatePhase.Place"/> a node takes part in.
    /// </summary>
    public enum NodeSimulation
    {
        /// <summary>The node has nothing to advance once the scene has been updated.</summary>
        None,

        /// <summary> The node update can be parallelized. Affects only inner state.</summary>
        Parallel,
    }

    /// <summary>Additional flags for <see cref="SceneNode"/>s.</summary>
    [Flags]
    public enum SceneNodeFlags
    {
        /// <summary>No flags set.</summary>
        None = 0,

        /// <summary>
        /// The node's vertices are already in world space, so it draws with the identity transform while its
        /// <see cref="SceneNode.Transform"/> only places it.
        /// </summary>
        PreTransformedVertices = 1 << 0,
    }

    /// <summary>
    /// Base class for all objects in the scene graph.
    /// </summary>
#if DEBUG
    [System.Diagnostics.DebuggerDisplay("{DebugName,nq}")]
#endif
    public abstract class SceneNode
    {
        /// <summary>Gets or sets the color multiplier this node draws with, in gamma space.</summary>
        public Vector3 Tint { get; set; } = Vector3.One;

        /// <summary>Gets or sets the opacity this node draws with.</summary>
        public float Alpha { get; set; } = 1f;

        /// <summary>Gets or sets <see cref="Tint"/> in XYZ and <see cref="Alpha"/> in W.</summary>
        public Vector4 TintAlpha
        {
            get => new(Tint, Alpha);
            set
            {
                Tint = new Vector3(value.X, value.Y, value.Z);
                Alpha = value.W;
            }
        }

        /// <summary>
        /// Gets or sets the world transform. Setting this also updates <see cref="BoundingBox"/>.
        /// </summary>
        public Matrix4x4 Transform
        {
            get => transform;
            set
            {
                transform = value;
                BoundingBox = LocalBoundingBox.Transform(transform);
            }
        }

        /// <summary>
        /// Gets or sets the visibility layer name.
        /// </summary>
        public string? LayerName { get; set; }

        /// <summary>
        /// Gets or sets whether this node's layer is enabled. Queues a rebuild on the parent spatial structure.
        /// </summary>
        public virtual bool LayerEnabled
        {
            get;
            set
            {
                var valueChanged = value != field;
                field = value;
                if (valueChanged)
                {
                    Scene.MarkParentOctreeDirty(this);
                }
            }
        } = true;

        /// <summary>
        /// Gets or sets whether the node itself wants to be drawn, independently of its layer.
        /// The node remains in the scene graph and is checked each frame.
        /// </summary>
        public bool Visible { get; set; } = true;

        /// <summary>
        /// Gets the world-space axis-aligned bounding box. Recomputed from <see cref="LocalBoundingBox"/> and
        /// <see cref="Transform"/> when either is set; nodes whose content lives in world space set it directly.
        /// </summary>
        public AABB BoundingBox { get; protected set; }

        /// <summary>
        /// Gets or sets the local-space axis-aligned bounding box. Setting this also updates <see cref="BoundingBox"/>.
        /// </summary>
        public AABB LocalBoundingBox
        {
            get => localBoundingBox;
            set
            {
                localBoundingBox = value;
                BoundingBox = LocalBoundingBox.Transform(transform);
            }
        }

        /// <summary>
        /// Gets the name of this node.
        /// </summary>
        public string? Name { get; init; }

        /// <summary>
        /// Gets or sets the unique identifier for this node.
        /// </summary>
        public uint Id { get; set; }

        /// <summary>
        /// Gets or sets whether this node is currently selected.
        /// </summary>
        public bool IsSelected { get; set; }

        /// <summary>
        /// Gets or sets the object type flags.
        /// </summary>
        public ObjectTypeFlags Flags { get; set; }

        /// <summary>
        /// Gets or sets additional non-standard flags.
        /// </summary>
        public SceneNodeFlags AdditionalFlags { get; set; }

        /// <summary>
        /// Flags for when should this node be drawn and where.
        /// </summary>
        public CustomRenderPasses RenderPasses { get; set; } = CustomRenderPasses.Default;

        /// <summary>Uploads this node's buffers. Called on visible nodes only, before any pass draws.</summary>
        /// <param name="camera">The camera the frame is drawn with.</param>
        public virtual void UpdateBuffers(Camera camera)
        {
        }

#if DEBUG
        /// <summary>
        /// Gets a human-readable debug name including type, name, id, and position.
        /// </summary>
        public string DebugName => $"{GetType().Name.Replace("SceneNode", "", StringComparison.Ordinal)}{(string.IsNullOrEmpty(Name) ? "" : " ")}{Name} ({Id}) at {BoundingBox.Center.X:F2} {BoundingBox.Center.Y:F2} {BoundingBox.Center.Z:F2}";
#endif

        /// <summary>
        /// Gets the scene this node belongs to.
        /// </summary>
        public Scene Scene { get; }

        /// <summary>
        /// How large this node is drawn next to whatever places it. Only an editor marker differs, and
        /// only where its scene is magnified; a line joining two markers keeps the magnification, since
        /// it has to span the distance between them.
        /// </summary>
        internal float PlacementScale => LayerName == World.EditorEntityNode.LayerName && this is not SceneNodes.LineSceneNode
            ? Scene.MarkerScale
            : 1f;

        /// <summary>Shrinks a transform this node is placed at by <see cref="PlacementScale"/>.</summary>
        internal Matrix4x4 ApplyPlacementScale(in Matrix4x4 transform)
            => PlacementScale == 1f ? transform : Matrix4x4.CreateScale(PlacementScale) * transform;

        /// <summary>
        /// The parent node.
        /// </summary>
        public SceneNode? Parent { get; set; }

        /// <summary>
        /// Gets the environment maps affecting this node.
        /// </summary>
        public List<SceneEnvMap> EnvMaps { get; private set; } = [];

        /// <summary>
        /// Gets or sets the precomputed environment map visibility bitfield for shaders.
        /// </summary>
        public SceneEnvMap.EnvMapVisibility128 ShaderEnvMapVisibility { get; set; }

        /// <summary>
        /// Gets or sets a custom lighting origin override for environment map and light probe sampling.
        /// </summary>
        public Vector3? LightingOrigin { get; set; }

        /// <summary>
        /// Gets or sets the render order for overlay nodes.
        /// </summary>
        public int OverlayRenderOrder { get; set; }

        /// <summary>
        /// Gets or sets the precomputed handshake value for cubemap assignment.
        /// </summary>
        public int CubeMapPrecomputedHandshake { get; set; }

        /// <summary>
        /// Gets or sets the precomputed handshake value for light probe volume assignment.
        /// </summary>
        public int LightProbeVolumePrecomputedHandshake { get; set; }

        /// <summary>
        /// Gets or sets the bound light probe for this node.
        /// </summary>
        public SceneLightProbe? LightProbeBinding { get; set; }

        /// <summary>
        /// Gets or sets the associated entity data from the map.
        /// </summary>
        public EntityLump.Entity? EntityData { get; set; }

        /// <summary>
        /// Gets the entity that owns this node and drives its transform, or <see langword="null"/> when
        /// nothing simulates it. Where <see cref="EntityData"/> is what the map authored, this is the live
        /// entity built from it.
        /// </summary>
        public Entities.BaseEntity? EntityInstance { get; internal set; }

        private AABB localBoundingBox;
        private Matrix4x4 transform = Matrix4x4.Identity;

        private ushort[]? visClusters;
        private int visClusterCount;
        private AABB visClusterBounds;

        /// <summary>
        /// This node's slot in the scene's <see cref="Scene.DynamicOctree"/>, or -1 when it is not in one.
        /// </summary>
        internal int DynamicSetIndex { get; set; } = -1;

        /// <summary>
        /// Gets or sets the visibility clusters the map compiler assigned to this node, which replace the
        /// clusters its bounding box overlaps. <see langword="null"/> when it has none.
        /// </summary>
        internal ushort[]? PrecomputedVisClusters { get; set; }

        /// <summary>
        /// Gets the precomputed visibility clusters of this node when it has them, otherwise the clusters its
        /// bounding box overlaps, recomputing them when it has moved or grown since the last query. A node
        /// with no clusters at all is never visible.
        /// </summary>
        /// <param name="voxelVisibility">The scene's visibility data.</param>
        internal ReadOnlySpan<ushort> GetVisClusters(IWorldVisibility voxelVisibility)
        {
            if (PrecomputedVisClusters != null)
            {
                return PrecomputedVisClusters;
            }

            if (visClusters != null && visClusterBounds.Equals(BoundingBox))
            {
                return visClusters.AsSpan(0, visClusterCount);
            }

            var wordCount = voxelVisibility.ClusterBitfieldWordCount;
            Span<uint> scratch = stackalloc uint[VoxelVisibility.ClusterBitfieldWords];

            var clusterBits = wordCount <= scratch.Length ? scratch[..wordCount] : new uint[wordCount];

            voxelVisibility.GetVisClustersForBox(BoundingBox.Min, BoundingBox.Max, clusterBits);

            var count = 0;

            foreach (var word in clusterBits)
            {
                count += BitOperations.PopCount(word);
            }

            // Anything that moves requeries every frame, so grow the list rather than replacing it
            if (visClusters == null || visClusters.Length < count)
            {
                visClusters = new ushort[count];
            }

            visClusterCount = count;
            count = 0;

            for (var i = 0; i < clusterBits.Length; i++)
            {
                var word = clusterBits[i];

                while (word != 0)
                {
                    visClusters[count++] = (ushort)(i * 32 + BitOperations.TrailingZeroCount(word));
                    word &= word - 1;
                }
            }

            visClusterBounds = BoundingBox;

            return visClusters.AsSpan(0, visClusterCount);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneNode"/> class.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        protected SceneNode(Scene scene)
        {
            Scene = scene;
        }

        /// <summary>
        /// Called each frame to update this node's state.
        /// </summary>
        /// <param name="context">The current update context.</param>
        public virtual void Update(Scene.UpdateContext context)
        {
        }

        /// <summary>
        /// Gets or sets which passes after <see cref="Scene.UpdatePhase.Place"/> this node takes part
        /// in. Simulating nodes get <see cref="Scene.UpdatePhase.Act"/> too, and belong in the dynamic
        /// partition even if they never move.
        /// </summary>
        public NodeSimulation Simulation { get; protected set; }

        /// <summary>
        /// Called each frame to render this node.
        /// </summary>
        /// <param name="context">The current render context.</param>
        public virtual void Render(Scene.RenderContext context)
        {
        }

        /// <summary>
        /// Returns the render modes supported by this node.
        /// </summary>
        public virtual IEnumerable<string> GetSupportedRenderModes() => [];

        /// <summary>
        /// Sets the active render mode for this node.
        /// </summary>
        /// <param name="mode">The render mode name to activate.</param>
        public virtual void SetRenderMode(string mode)
        {
        }

#if DEBUG
        /// <summary>
        /// Recreates vertex array objects. Debug-only, used for hot-reloading shaders.
        /// </summary>
        public virtual void UpdateVertexArrayObjects()
        {
        }
#endif

        /// <summary>
        /// Releases resources held by this node.
        /// </summary>
        public virtual void Delete()
        {
        }

        /// <summary>
        /// Gets the squared distance from this node's bounding box center to the camera.
        /// </summary>
        /// <param name="camera">The camera to measure distance from.</param>
        /// <returns>The squared distance to the camera.</returns>
        public float GetCameraDistance(Camera camera)
        {
            return Vector3.DistanceSquared(BoundingBox.Center, camera.Location);
        }
    }
}
