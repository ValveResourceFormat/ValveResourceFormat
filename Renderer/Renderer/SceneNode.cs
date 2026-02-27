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
        public Matrix4x4 Transform
        {
            get => transform;
            set
            {
                transform = value;
                BoundingBox = LocalBoundingBox.Transform(transform);
            }
        }

        const uint SelfLayerBit = 1u << 31;
        private uint LayerMask { get; set; } = SelfLayerBit;

        public string? LayerName
        {
            get => Scene.GetLeafiestLayerName(LayerMask);
            set => LayerMask |= Scene.GetLayerMask(value, true);
        }

        public string[] Layers
        {
            get => Scene.GetLayerNames(LayerMask);
            set
            {
                LayerMask = SelfLayerBit;
                foreach (var layer in value)
                {
                    LayerMask |= Scene.GetLayerMask(layer, true);
                }
            }
        }

        public bool LayerEnabled
        {
            get => Scene.IsLayerEnabled(LayerMask);
        }

        public bool Enabled
        {
            get => (LayerMask & SelfLayerBit) != 0;
            set { LayerMask = value ? (LayerMask | SelfLayerBit) : (LayerMask & ~SelfLayerBit); Scene.MarkParentOctreeDirty(this); }
        }

        public AABB BoundingBox { get; private set; }
        public AABB LocalBoundingBox
        {
            get => localBoundingBox;
            protected set
            {
                localBoundingBox = value;
                BoundingBox = LocalBoundingBox.Transform(transform);
            }
        }

        public string? Name { get; init; }
        public uint Id { get; set; }
        public bool IsSelected { get; set; }
        public ObjectTypeFlags Flags { get; set; }

        public string DebugName => $"{GetType().Name.Replace("SceneNode", "", StringComparison.Ordinal)}{(string.IsNullOrEmpty(Name) ? "" : " ")}{Name} ({Id}) at {BoundingBox.Center.X:F2} {BoundingBox.Center.Y:F2} {BoundingBox.Center.Z:F2}";

        public Scene Scene { get; }

        public List<SceneEnvMap> EnvMaps { get; private set; } = [];
        public SceneEnvMap.EnvMapVisibility128 ShaderEnvMapVisibility { get; set; }
        public Vector3? LightingOrigin { get; set; }
        public int OverlayRenderOrder { get; set; }
        public int CubeMapPrecomputedHandshake { get; set; }
        public int LightProbeVolumePrecomputedHandshake { get; set; }
        public SceneLightProbe? LightProbeBinding { get; set; }

        public EntityLump.Entity? EntityData { get; set; }

        private AABB localBoundingBox;
        private Matrix4x4 transform = Matrix4x4.Identity;

        protected SceneNode(Scene scene)
        {
            Scene = scene;
        }

        public virtual void Update(Scene.UpdateContext context)
        {
        }

        public virtual void Render(Scene.RenderContext context)
        {
        }

        public virtual IEnumerable<string> GetSupportedRenderModes() => [];

        public virtual void SetRenderMode(string mode)
        {
        }

#if DEBUG
        public virtual void UpdateVertexArrayObjects()
        {
        }
#endif

        public virtual void Delete()
        {
        }

        public float GetCameraDistance(Camera camera)
        {
            return (BoundingBox.Center - camera.Location).LengthSquared();
        }
    }
}
