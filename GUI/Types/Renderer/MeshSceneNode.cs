using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    internal class MeshSceneNode : SceneNode
    {
        public Vector4 Tint
        {
            get => meshRenderer.Tint;
            set => meshRenderer.Tint = value;
        }

        private MeshRenderer meshRenderer;

        public MeshSceneNode(Scene scene, Mesh mesh, Dictionary<string, string> skinMaterials = null)
            : base(scene)
        {
            meshRenderer = new MeshRenderer(mesh, Scene.GuiContext, skinMaterials);
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
            meshRenderer.Render(context.Camera, Transform, context.RenderPass);
        }
    }
}
