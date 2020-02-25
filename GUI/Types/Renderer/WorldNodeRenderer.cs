using System.Collections.Generic;
using System.Linq;
using GUI.Utils;
using OpenTK;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    internal class WorldNodeRenderer : IMeshRenderer, IOctreeElement
    {
        private WorldNode WorldNode { get; }

        public AABB BoundingBox { get; private set; }

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

            var sceneObjectLayerIndices = data.ContainsKey("m_sceneObjectLayerIndices") ? data.GetIntegerArray("m_sceneObjectLayerIndices") : null;
            var sceneObjects = data.GetArray("m_sceneObjects");
            var i = 0;

            // Output is WorldNode_t we need to iterate m_sceneObjects inside it
            foreach (var sceneObject in sceneObjects)
            {
                if (sceneObjectLayerIndices != null)
                {
                    var layerIndex = sceneObjectLayerIndices[i];
                    i++;

                    // TODO: We want UI for this
                    if (layerIndex == 2 || layerIndex == 4)
                    {
                        continue;
                    }
                }

                // sceneObject is SceneObject_t
                var renderableModel = sceneObject.GetProperty<string>("m_renderableModel");
                var transform = sceneObject.GetArray("m_vTransform");

                var matrix = Matrix4.Identity;

                // Copy transform matrix to right type
                for (var x = 0; x < transform.Length; x++)
                {
                    var a = transform[x].ToVector4();

                    switch (x)
                    {
                        case 0: matrix.Column0 = new Vector4(a.X, a.Y, a.Z, a.W); break;
                        case 1: matrix.Column1 = new Vector4(a.X, a.Y, a.Z, a.W); break;
                        case 2: matrix.Column2 = new Vector4(a.X, a.Y, a.Z, a.W); break;
                        case 3: matrix.Column3 = new Vector4(a.X, a.Y, a.Z, a.W); break;
                    }
                }

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
                    meshRenderers.Add(renderer);

                    BoundingBox = BoundingBox.IsZero ? renderer.BoundingBox : BoundingBox.Union(renderer.BoundingBox);
                    meshOctree.Insert(renderer);
                }

                var renderable = sceneObject.GetProperty<string>("m_renderable");

                if (renderable != null)
                {
                    var newResource = guiContext.LoadFileByAnyMeansNecessary(renderable + "_c");

                    if (newResource == null)
                    {
                        continue;
                    }

                    var renderer = new MeshRenderer(new Mesh(newResource), guiContext);
                    renderer.Transform = matrix;
                    renderer.Tint = tintColor;
                    meshRenderers.Add(renderer);

                    BoundingBox = BoundingBox.IsZero ? renderer.BoundingBox : BoundingBox.Union(renderer.BoundingBox);
                    meshOctree.Insert(renderer);
                }
            }
        }

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
