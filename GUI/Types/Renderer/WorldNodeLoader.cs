using GUI.Utils;
using ValveResourceFormat;
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
                LayerNames = [];
            }
        }

        public void Load(Scene scene)
        {
            var i = 0;
            var defaultLightingOrigin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            // Output is WorldNode_t we need to iterate m_sceneObjects inside it
            foreach (var sceneObject in node.SceneObjects)
            {
                var layerIndex = (int)(node.SceneObjectLayerIndices?[i++] ?? -1);

                // m_vCubeMapOrigin in older files
                var lightingOrigin = sceneObject.ContainsKey("m_vLightingOrigin") ? sceneObject.GetSubCollection("m_vLightingOrigin").ToVector3() : defaultLightingOrigin;
                var overlayRenderOrder = sceneObject.GetInt32Property("m_nOverlayRenderOrder");
                var cubeMapPrecomputedHandshake = sceneObject.GetInt32Property("m_nCubeMapPrecomputedHandshake");
                var lightProbeVolumePrecomputedHandshake = sceneObject.GetInt32Property("m_nLightProbeVolumePrecomputedHandshake");

                // sceneObject is SceneObject_t
                var renderableModel = sceneObject.GetProperty<string>("m_renderableModel");
                var matrix = sceneObject.GetArray("m_vTransform").ToMatrix4x4();
                var flags = sceneObject.GetEnumValue<ObjectTypeFlags>("m_nObjectTypeFlags", normalize: true);

                var tintColor = sceneObject.GetSubCollection("m_vTintColor").ToVector4();
                if (tintColor.W == 0)
                {
                    // Ignoring tintColor, it will fuck things up.
                    tintColor = Vector4.One;
                }

                if (renderableModel != null)
                {
                    var newResource = guiContext.LoadFileCompiled(renderableModel);

                    if (newResource == null)
                    {
                        continue;
                    }

                    var modelNode = new ModelSceneNode(scene, (Model)newResource.DataBlock, null)
                    {
                        Transform = matrix,
                        Tint = tintColor,
                        LayerName = layerIndex > -1 ? node.LayerNames[layerIndex] : "No layer",
                        Name = renderableModel,
                        LightingOrigin = lightingOrigin == defaultLightingOrigin ? null : lightingOrigin,
                        OverlayRenderOrder = overlayRenderOrder,
                        CubeMapPrecomputedHandshake = cubeMapPrecomputedHandshake,
                        LightProbeVolumePrecomputedHandshake = lightProbeVolumePrecomputedHandshake,
                        Flags = flags,
                    };

                    scene.Add(modelNode, false);
                }

                var renderable = sceneObject.GetProperty<string>("m_renderable");

                if (!string.IsNullOrEmpty(renderable))
                {
                    var newResource = guiContext.LoadFileCompiled(renderable);

                    if (newResource == null)
                    {
                        continue;
                    }

                    var meshNode = new MeshSceneNode(scene, (Mesh)newResource.DataBlock, 0)
                    {
                        Transform = matrix,
                        Tint = tintColor,
                        LayerName = layerIndex > -1 ? node.LayerNames[layerIndex] : "No layer",
                        Name = renderable,
                        CubeMapPrecomputedHandshake = cubeMapPrecomputedHandshake,
                        LightProbeVolumePrecomputedHandshake = lightProbeVolumePrecomputedHandshake,
                        Flags = flags,
                    };

                    scene.Add(meshNode, false);
                }
            }

            foreach (var sceneObject in node.AggregateSceneObjects)
            {
                var renderableModel = sceneObject.GetProperty<string>("m_renderableModel");

                if (renderableModel != null)
                {
                    var newResource = guiContext.LoadFileCompiled(renderableModel);
                    if (newResource == null)
                    {
                        continue;
                    }

                    var layerIndex = sceneObject.GetIntegerProperty("m_nLayer");
                    var aggregate = new SceneAggregate(scene, (Model)newResource.DataBlock)
                    {
                        LayerName = node.LayerNames[(int)layerIndex],
                        Name = renderableModel,
                        AllFlags = sceneObject.GetEnumValue<ObjectTypeFlags>("m_allFlags", normalize: true),
                        AnyFlags = sceneObject.GetEnumValue<ObjectTypeFlags>("m_anyFlags", normalize: true),
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
