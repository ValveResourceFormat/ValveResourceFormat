using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    class MeshSceneNode : SceneNode, IRenderableMeshCollection
    {
        public Vector4 Tint
        {
            get => RenderableMeshes[0].Tint;
            set => RenderableMeshes[0].Tint = value;
        }

        public List<RenderableMesh> RenderableMeshes { get; init; }

        public MeshSceneNode(Scene scene, Mesh mesh, int meshIndex)
            : base(scene)
        {
            var meshRenderer = new RenderableMesh(mesh, meshIndex, Scene);
            RenderableMeshes = [meshRenderer];
            LocalBoundingBox = meshRenderer.BoundingBox;
        }

        public override IEnumerable<string> GetSupportedRenderModes() => RenderableMeshes[0].GetSupportedRenderModes();

        public override void SetRenderMode(string renderMode)
        {
        }

        public override void Update(Scene.UpdateContext context)
        {
        }

        public override void Render(Scene.RenderContext context)
        {
            // This node does not render itself; it uses the batching system via IRenderableMeshCollection
        }

#if DEBUG
        public override void UpdateVertexArrayObjects() => RenderableMeshes[0].UpdateVertexArrayObjects();
#endif
    }
}
