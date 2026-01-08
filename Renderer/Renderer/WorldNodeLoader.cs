using System.Diagnostics;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Renderer
{
    public class WorldNodeLoader
    {
        private readonly WorldNode node;
        private readonly ResourceExtRefList? externalReferences;
        private readonly RendererContext RendererContext;
        public string[] LayerNames { get; }

        public WorldNodeLoader(RendererContext rendererContext, WorldNode node, ValveResourceFormat.Blocks.ResourceExtRefList? externalReferences = null)
        {
            this.node = node;
            this.externalReferences = externalReferences;
            RendererContext = rendererContext;

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
            if (externalReferences is not null)
            {
                Parallel.ForEach(externalReferences.ResourceRefInfoList, resourceReference =>
                {
                    var resource = RendererContext.FileLoader.LoadFileCompiled(resourceReference.Name);
                    if (resource is { DataBlock: Model model })
                    {
                        foreach (var mesh in model.GetEmbeddedMeshes())
                        {
                            var _ = mesh.Mesh.VBIB;
                        }
                    }
                });
            }

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
                    var newResource = RendererContext.FileLoader.LoadFileCompiled(renderableModel);

                    if (newResource == null)
                    {
                        continue;
                    }

                    var model = (Model?)newResource.DataBlock;
                    Debug.Assert(model != null);
                    var modelNode = new ModelSceneNode(scene, model, null)
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
                    var newResource = RendererContext.FileLoader.LoadFileCompiled(renderable);

                    if (newResource == null)
                    {
                        continue;
                    }

                    var mesh = (Mesh?)newResource.DataBlock;
                    Debug.Assert(mesh != null);
                    var meshNode = new MeshSceneNode(scene, mesh, 0)
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
                    var newResource = RendererContext.FileLoader.LoadFileCompiled(renderableModel);
                    if (newResource == null)
                    {
                        continue;
                    }

                    var model = (Model?)newResource.DataBlock;
                    Debug.Assert(model != null);

                    var layerIndex = sceneObject.GetIntegerProperty("m_nLayer");
                    var aggregate = new SceneAggregate(scene, model)
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
