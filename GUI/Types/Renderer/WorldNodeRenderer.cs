using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    internal class WorldNodeRenderer : IMeshRenderer
    {
        private WorldNode WorldNode { get; }

        public AABB BoundingBox { get; private set; }

        public IEnumerable<string> Layers { get; private set; }

        private readonly VrfGuiContext guiContext;

        private readonly List<IMeshRenderer> meshRenderers = new List<IMeshRenderer>();

        private readonly Octree<IRenderer> meshOctree;

        public WorldNodeRenderer(WorldNode worldNode, VrfGuiContext vrfGuiContext, Octree<IRenderer> externalOctree)
        {
            WorldNode = worldNode;
            guiContext = vrfGuiContext;
            meshOctree = externalOctree ?? new Octree<IRenderer>(16384);

            // Do setup
            SetupMeshRenderers();
        }

        public void Render(Camera camera, RenderPass renderPass)
        {
            foreach (var renderer in meshOctree.Query(camera.ViewFrustum))
            {
                renderer.Render(camera, renderPass);
            }
        }

        public void Update(float frameTime)
        {
            // Nothing to do here
        }

        private void SetupMeshRenderers()
        {
            var data = WorldNode.GetData();

            if (data.ContainsKey("m_layerNames"))
            {
                Layers = data.GetArray<string>("m_layerNames");
            }

            var sceneObjectLayerIndices = data.ContainsKey("m_sceneObjectLayerIndices") ? data.GetIntegerArray("m_sceneObjectLayerIndices") : null;
            var sceneObjects = data.GetArray("m_sceneObjects");
            var i = 0;

            // Output is WorldNode_t we need to iterate m_sceneObjects inside it
            foreach (var sceneObject in sceneObjects)
            {
                var layerIndex = sceneObjectLayerIndices?[i++] ?? -1;

                // sceneObject is SceneObject_t
                var renderableModel = sceneObject.GetProperty<string>("m_renderableModel");
                var matrix = sceneObject.GetArray("m_vTransform").ToMatrix4x4();

                var tintColorWrongVector = sceneObject.GetSubCollection("m_vTintColor").ToVector4();

                Vector4 tintColor;
                if (tintColorWrongVector.W == 0)
                {
                    // Ignoring tintColor, it will fuck things up.
                    tintColor = Vector4.One;
                }
                else
                {
                    tintColor = new Vector4(tintColorWrongVector.X, tintColorWrongVector.Y, tintColorWrongVector.Z, tintColorWrongVector.W);
                }

                if (renderableModel != null)
                {
                    var newResource = guiContext.LoadFileByAnyMeansNecessary(renderableModel + "_c");

                    if (newResource == null)
                    {
                        continue;
                    }

                    var renderer = new ModelRenderer(new Model(newResource), guiContext, null, false);
                    renderer.SetMeshTransform(matrix);
                    renderer.SetTint(tintColor);
                    renderer.SetLayerIndex(layerIndex);
                    meshRenderers.Add(renderer);

                    BoundingBox = BoundingBox.IsZero ? renderer.BoundingBox : BoundingBox.Union(renderer.BoundingBox);
                    meshOctree.Insert(renderer, renderer.BoundingBox);
                }

                var renderable = sceneObject.GetProperty<string>("m_renderable");

                if (renderable != null)
                {
                    var newResource = guiContext.LoadFileByAnyMeansNecessary(renderable + "_c");

                    if (newResource == null)
                    {
                        continue;
                    }

                    var renderer = new MeshRenderer(new Mesh(newResource), guiContext)
                    {
                        Transform = matrix,
                        Tint = tintColor,
                        LayerIndex = layerIndex,
                    };
                    meshRenderers.Add(renderer);

                    BoundingBox = BoundingBox.IsZero ? renderer.BoundingBox : BoundingBox.Union(renderer.BoundingBox);
                    meshOctree.Insert(renderer, renderer.BoundingBox);
                }
            }
        }

        public IEnumerable<string> GetWorldLayerNames()
            => Layers ?? Enumerable.Empty<string>();

        public IEnumerable<string> GetSupportedRenderModes()
            => meshRenderers.SelectMany(r => r.GetSupportedRenderModes()).Distinct();

        public void SetRenderMode(string renderMode)
        {
            foreach (var renderer in meshRenderers)
            {
                renderer.SetRenderMode(renderMode);
            }
        }
    }
}
