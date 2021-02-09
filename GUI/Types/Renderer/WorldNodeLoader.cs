using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    internal class WorldNodeLoader
    {
        private readonly WorldNode node;
        private readonly VrfGuiContext guiContext;

        public WorldNodeLoader(VrfGuiContext vrfGuiContext, WorldNode node)
        {
            this.node = node;
            guiContext = vrfGuiContext;
        }

        public void Load(Scene scene)
        {
            var data = node.Data;

            string[] worldLayers;

            if (data.ContainsKey("m_layerNames"))
            {
                worldLayers = data.GetArray<string>("m_layerNames");
            }
            else
            {
                worldLayers = Array.Empty<string>();
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

                    var modelNode = new ModelSceneNode(scene, (Model)newResource.DataBlock, null, false)
                    {
                        Transform = matrix,
                        Tint = tintColor,
                        LayerName = layerIndex > -1 ? worldLayers[layerIndex] : "No layer",
                    };

                    scene.Add(modelNode, false);
                }

                var renderable = sceneObject.GetProperty<string>("m_renderable");

                if (renderable != null)
                {
                    var newResource = guiContext.LoadFileByAnyMeansNecessary(renderable + "_c");

                    if (newResource == null)
                    {
                        continue;
                    }

                    var meshNode = new MeshSceneNode(scene, new Mesh(newResource))
                    {
                        Transform = matrix,
                        Tint = tintColor,
                        LayerName = layerIndex > -1 ? worldLayers[layerIndex] : "No layer",
                    };

                    scene.Add(meshNode, false);
                }
            }
        }
    }
}
