using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    class WorldNodeLoader
    {
        private readonly WorldNode node;
        private readonly VrfGuiContext guiContext;
        public string[] LayerNames { get; }

        public WorldNodeLoader(VrfGuiContext vrfGuiContext, WorldNode node)
        {
            this.node = node;
            guiContext = vrfGuiContext;

            if (node.Data.ContainsKey("m_layerNames"))
            {
                LayerNames = node.Data.GetArray<string>("m_layerNames");
            }
            else
            {
                LayerNames = Array.Empty<string>();
            }
        }

        public void Load(Scene scene)
        {
            var data = node.Data;
            var sceneObjectLayerIndices = data.ContainsKey("m_sceneObjectLayerIndices") ? data.GetIntegerArray("m_sceneObjectLayerIndices") : null;
            var sceneObjects = data.GetArray("m_sceneObjects");
            var i = 0;

            // Output is WorldNode_t we need to iterate m_sceneObjects inside it
            foreach (var sceneObject in sceneObjects)
            {
                var layerIndex = sceneObjectLayerIndices?[i++] ?? -1;

                var cubeMapPrecomputedHandshake = sceneObject.GetInt32Property("m_nCubeMapPrecomputedHandshake");
                var lightProbeVolumePrecomputedHandshake = sceneObject.GetInt32Property("m_nLightProbeVolumePrecomputedHandshake");

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
                        LayerName = layerIndex > -1 ? LayerNames[layerIndex] : "No layer",
                        Name = renderableModel,
                        CubeMapPrecomputedHandshake = cubeMapPrecomputedHandshake,
                        LightProbeVolumePrecomputedHandshake = lightProbeVolumePrecomputedHandshake,
                    };

                    scene.Add(modelNode, false);
                }

                var renderable = sceneObject.GetProperty<string>("m_renderable");

                if (!string.IsNullOrEmpty(renderable))
                {
                    var newResource = guiContext.LoadFileByAnyMeansNecessary(renderable + "_c");

                    if (newResource == null)
                    {
                        continue;
                    }

                    var meshNode = new MeshSceneNode(scene, (Mesh)newResource.DataBlock, 0)
                    {
                        Transform = matrix,
                        Tint = tintColor,
                        LayerName = layerIndex > -1 ? LayerNames[layerIndex] : "No layer",
                        Name = renderable,
                        CubeMapPrecomputedHandshake = cubeMapPrecomputedHandshake,
                        LightProbeVolumePrecomputedHandshake = lightProbeVolumePrecomputedHandshake,
                    };

                    scene.Add(meshNode, false);
                }
            }

            if (!data.ContainsKey("m_aggregateSceneObjects"))
            {
                return;
            }

            var aggregateSceneObjects = data.GetArray("m_aggregateSceneObjects");

            foreach (var sceneObject in aggregateSceneObjects)
            {
                var renderableModel = sceneObject.GetProperty<string>("m_renderableModel");

                if (renderableModel != null)
                {
                    var newResource = guiContext.LoadFileByAnyMeansNecessary(renderableModel + "_c");
                    if (newResource == null)
                    {
                        continue;
                    }

                    var layerIndex = sceneObject.GetIntegerProperty("m_nLayer");
                    var aggregate = new SceneAggregate(scene, (Model)newResource.DataBlock)
                    {
                        LayerName = LayerNames[layerIndex],
                        Name = renderableModel,
                    };

                    scene.Add(aggregate, false);
                    foreach (var fragment in aggregate.CreateFragments(sceneObject))
                    {
                        scene.Add(fragment, false);
                    }
                }
            }
        }
    }
}
