using System.Collections.Generic;
using System.Numerics;
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

        public List<RenderableMesh> RenderableMeshes { get; } = new(1);

        public MeshSceneNode(Scene scene, Mesh mesh, int meshIndex, Dictionary<string, string> skinMaterials = null)
            : base(scene)
        {
            var meshRenderer = new RenderableMesh(mesh, meshIndex, Scene, skinMaterials);
            RenderableMeshes.Add(meshRenderer);
            LocalBoundingBox = meshRenderer.BoundingBox;
        }

        public override IEnumerable<string> GetSupportedRenderModes() => RenderableMeshes[0].GetSupportedRenderModes();

        public override void SetRenderMode(string renderMode)
        {
            RenderableMeshes[0].SetRenderMode(renderMode);
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
