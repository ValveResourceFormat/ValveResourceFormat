using System.Diagnostics;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Base class for all objects in the scene graph.
    /// </summary>
    [DebuggerDisplay("{DebugName,nq}")]
    public abstract class SceneNode
    {
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

        private bool layerEnabledField = true;

        /// <summary>
        /// Gets or sets whether this node's layer is enabled. Marks the parent octree dirty on change.
        /// </summary>
        public virtual bool LayerEnabled
        {
            get => layerEnabledField;
            set { layerEnabledField = value; Scene.MarkParentOctreeDirty(this); }
        }

        /// <summary>
        /// Gets the world-space axis-aligned bounding box, computed from <see cref="LocalBoundingBox"/> and <see cref="Transform"/>.
        /// </summary>
        public AABB BoundingBox { get; private set; }

        /// <summary>
        /// Gets or sets the local-space axis-aligned bounding box. Setting this also updates <see cref="BoundingBox"/>.
        /// </summary>
        public AABB LocalBoundingBox
        {
            get => localBoundingBox;
            protected set
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
        /// Gets or sets the object type flags used for filtering.
        /// </summary>
        public ObjectTypeFlags Flags { get; set; }

        /// <summary>
        /// Gets a human-readable debug name including type, name, id, and position.
        /// </summary>
        public string DebugName => $"{GetType().Name.Replace("SceneNode", "", StringComparison.Ordinal)}{(string.IsNullOrEmpty(Name) ? "" : " ")}{Name} ({Id}) at {BoundingBox.Center.X:F2} {BoundingBox.Center.Y:F2} {BoundingBox.Center.Z:F2}";

        /// <summary>
        /// Gets the scene this node belongs to.
        /// </summary>
        public Scene Scene { get; }

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

        private AABB localBoundingBox;
        private Matrix4x4 transform = Matrix4x4.Identity;

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
            return (BoundingBox.Center - camera.Location).LengthSquared();
        }
    }
}
