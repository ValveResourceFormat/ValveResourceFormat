using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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
                RenderPass = renderPass;
            }
        }

        public Camera MainCamera { get; set; }
        public VrfGuiContext GuiContext { get; }
        public Octree<SceneNode> StaticOctree { get; }
        public Octree<SceneNode> DynamicOctree { get; }

        public IEnumerable<SceneNode> AllNodes => Enumerable.Concat(staticNodes, dynamicNodes);

        private readonly HashSet<string> VisibleOnSpawnWorldLayers = new HashSet<string>();

        private readonly List<SceneNode> staticNodes = new List<SceneNode>();
        private readonly List<SceneNode> dynamicNodes = new List<SceneNode>();

        public Scene(VrfGuiContext context, float sizeHint = 32768)
        {
            GuiContext = context;
            StaticOctree = new Octree<SceneNode>(sizeHint);
            DynamicOctree = new Octree<SceneNode>(sizeHint);
        }

        public void Add(SceneNode node, bool dynamic)
        {
            if (dynamic)
            {
                dynamicNodes.Add(node);
                DynamicOctree.Insert(node, node.BoundingBox);
            }
            else
            {
                staticNodes.Add(node);
                StaticOctree.Insert(node, node.BoundingBox);
            }
        }

        public void Update(float timestep)
        {
            var updateContext = new UpdateContext(timestep);
            foreach (var node in staticNodes)
            {
                node.Update(updateContext);
            }

            foreach (var node in dynamicNodes)
            {
                var oldBox = node.BoundingBox;
                node.Update(updateContext);
                DynamicOctree.Update(node, oldBox, node.BoundingBox);
            }
        }

        public void Render()
        {
            RenderWithCamera(MainCamera);
        }

        public void RenderWithCamera(Camera camera, Frustum cullFrustum = null)
        {
            var allNodes = StaticOctree.Query(cullFrustum ?? camera.ViewFrustum);
            allNodes.AddRange(DynamicOctree.Query(cullFrustum ?? camera.ViewFrustum));

            // Collect mesh calls
            var opaqueDrawCalls = new List<MeshBatchRenderer.Request>();
            var translucentDrawCalls = new List<MeshBatchRenderer.Request>();
            var looseNodes = new List<SceneNode>();
            foreach (var node in allNodes)
            {
                var meshCollection = node as IRenderableMeshCollection;
                if (meshCollection != null)
                {
                    foreach (var mesh in meshCollection.RenderableMeshes)
                    {
                        foreach (var call in mesh.DrawCallsOpaque)
                        {
                            opaqueDrawCalls.Add(new MeshBatchRenderer.Request
                            {
                                Transform = node.Transform,
                                Mesh = mesh,
                                Call = call,
                                DistanceFromCamera = (node.BoundingBox.Center - camera.Location).LengthSquared(),
                            });
                        }

                        foreach (var call in mesh.DrawCallsBlended)
                        {
                            translucentDrawCalls.Add(new MeshBatchRenderer.Request
                            {
                                Transform = node.Transform,
                                Mesh = mesh,
                                Call = call,
                                DistanceFromCamera = (node.BoundingBox.Center - camera.Location).LengthSquared(),
                            });
                        }
                    }
                }
                else
                {
                    looseNodes.Add(node);
                }
            }

            // Sort loose nodes by distance from camera
            looseNodes.Sort((a, b) =>
            {
                var aLength = (a.BoundingBox.Center - camera.Location).LengthSquared();
                var bLength = (b.BoundingBox.Center - camera.Location).LengthSquared();
                return bLength.CompareTo(aLength);
            });

            // Opaque render pass
            var opaqueRenderContext = new RenderContext(camera, RenderPass.Opaque);

            MeshBatchRenderer.Render(opaqueDrawCalls, opaqueRenderContext);
            foreach (var node in looseNodes)
            {
                node.Render(opaqueRenderContext);
            }

            // Translucent render pass, back to front for loose nodes
            var translucentRenderContext = new RenderContext(camera, RenderPass.Translucent);

            MeshBatchRenderer.Render(translucentDrawCalls, translucentRenderContext);
            foreach (var node in Enumerable.Reverse(looseNodes))
            {
                node.Render(translucentRenderContext);
            }
        }

        public void SetEnabledLayers(HashSet<string> layers)
        {
            foreach (var renderer in AllNodes)
            {
                renderer.LayerEnabled = layers.Contains(renderer.LayerName);
            }

            StaticOctree.Clear();
            DynamicOctree.Clear();

            foreach (var node in staticNodes)
            {
                if (node.LayerEnabled)
                {
                    StaticOctree.Insert(node, node.BoundingBox);
                }
            }

            foreach (var node in dynamicNodes)
            {
                if (node.LayerEnabled)
                {
                    DynamicOctree.Insert(node, node.BoundingBox);
                }
            }
        }
    }
}
