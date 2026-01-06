using System.Diagnostics;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
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

        public string? LayerName { get; set; }

        private bool layerEnabledField = true;
        public virtual bool LayerEnabled
        {
            get => layerEnabledField;
            set { layerEnabledField = value; Scene.OctreeDirty = true; }
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
    }
}
