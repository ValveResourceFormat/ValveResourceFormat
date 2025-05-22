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

#if DEBUG
        public override void UpdateVertexArrayObjects() => RenderableMeshes[0].UpdateVertexArrayObjects();
#endif
    }
}
