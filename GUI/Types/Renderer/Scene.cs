using System.Collections.Generic;
using GUI.Utils;

namespace GUI.Types.Renderer
{
    internal class Scene
    {
        public class UpdateContext
        {
            public float Timestep { get; }

            public UpdateContext(float timestep)
            {
                Timestep = timestep;
            }
        }

        public class RenderContext
        {
            public Camera Camera { get; }
            public RenderPass RenderPass { get; }

            public RenderContext(Camera camera, RenderPass renderPass)
            {
                Camera = camera;
                RenderPass = RenderPass;
            }
        }

        public Camera MainCamera { get; set; }
        public VrfGuiContext GuiContext { get; }

        public IEnumerable<SceneNode> AllNodes => sceneNodes;

        private readonly HashSet<string> VisibleOnSpawnWorldLayers = new HashSet<string>();

        private readonly List<SceneNode> sceneNodes = new List<SceneNode>();
        private readonly Octree<SceneNode> staticOctree;
        private readonly Octree<SceneNode> dynamicOctree;

        public Scene(VrfGuiContext context, float sizeHint = 32768)
        {
            GuiContext = context;
            staticOctree = new Octree<SceneNode>(sizeHint);
            dynamicOctree = new Octree<SceneNode>(sizeHint);
        }

        public void Add(SceneNode node, bool dynamic)
        {
            sceneNodes.Add(node);

            if (dynamic)
            {
                dynamicOctree.Insert(node, node.BoundingBox);
            }
            else
            {
                staticOctree.Insert(node, node.BoundingBox);
            }
        }

        public void Update(float timestep)
        {
            var updateContext = new UpdateContext(timestep);
            foreach (var node in sceneNodes)
            {
                node.Update(updateContext);
            }
        }

        public void Render()
        {
            RenderWithCamera(MainCamera);
        }

        public void RenderWithCamera(Camera camera)
        {
            var allNodes = staticOctree.Query(camera.ViewFrustum);
            allNodes.AddRange(dynamicOctree.Query(camera.ViewFrustum));

            allNodes.Sort((a, b) =>
            {
                var aLength = (a.BoundingBox.Center - camera.Location).LengthSquared();
                var bLength = (b.BoundingBox.Center - camera.Location).LengthSquared();
                return bLength.CompareTo(aLength);
            });

            // Opaque render pass, front to back
            var opaqueRenderContext = new RenderContext(MainCamera, RenderPass.Opaque);
            foreach (var node in allNodes)
            {
                node.Render(opaqueRenderContext);
            }

            // Translucent render pass, back to front
            var translucentRenderContext = new RenderContext(MainCamera, RenderPass.Translucent);
            for (var i = allNodes.Count - 1; i >= 0; i--)
            {
                allNodes[i].Render(translucentRenderContext);
            }
        }
    }
}
