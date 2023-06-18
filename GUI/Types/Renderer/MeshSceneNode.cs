using System.Collections.Generic;
using System.Numerics;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    class MeshSceneNode : SceneNode, IRenderableMeshCollection
    {
        public Vector4 Tint
        {
            get => meshRenderer.Tint;
            set => meshRenderer.Tint = value;
        }

        public IEnumerable<RenderableMesh> RenderableMeshes
        {
            get
            {
                yield return meshRenderer;
            }
        }

        private readonly RenderableMesh meshRenderer;

        public MeshSceneNode(Scene scene, Mesh mesh, int meshIndex, Dictionary<string, string> skinMaterials = null)
            : base(scene)
        {
            meshRenderer = new RenderableMesh(mesh, meshIndex, Scene, skinMaterials);
            LocalBoundingBox = meshRenderer.BoundingBox;
        }

        public override IEnumerable<string> GetSupportedRenderModes() => meshRenderer.GetSupportedRenderModes();

        public override void SetRenderMode(string renderMode)
        {
            meshRenderer.SetRenderMode(renderMode);
        }

        public override void Update(Scene.UpdateContext context)
        {
        }

        public override void Render(Scene.RenderContext context)
        {
            // This node does not render itself; it uses the batching system via IRenderableMeshCollection
        }
    }
}
