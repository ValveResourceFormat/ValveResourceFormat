using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.World
{
    /// <summary>
    /// Loads scene objects and aggregates from a world node resource.
    /// </summary>
    public class WorldNodeLoader
    {
        private readonly WorldNode node;
        private readonly ResourceExtRefList? externalReferences;
        private readonly RendererContext RendererContext;
        /// <summary>Gets the layer names defined in this world node.</summary>
        public IReadOnlyList<string> LayerNames { get; }

        /// <summary>
        /// Initializes a new <see cref="WorldNodeLoader"/> for the given world node.
        /// </summary>
        /// <param name="rendererContext">The renderer context used for loading resources.</param>
        /// <param name="node">The world node data to load objects from.</param>
        /// <param name="externalReferences">Optional external resource reference list for parallel preloading.</param>
        public WorldNodeLoader(RendererContext rendererContext, WorldNode node, ValveResourceFormat.Blocks.ResourceExtRefList? externalReferences = null)
        {
            this.node = node;
            this.externalReferences = externalReferences;
            RendererContext = rendererContext;
            LayerNames = node.LayerNames;
        }

        /// <summary>
        /// Loads all scene objects and aggregates from the world node into the given scene.
        /// </summary>
        /// <param name="scene">The scene to add loaded objects to.</param>
        /// <param name="rootTransform">Transform applied to the whole node, identity when <see langword="null"/>.</param>
        public void Load(Scene scene, Matrix4x4? rootTransform = null)
        {
            var root = rootTransform ?? Matrix4x4.Identity;

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

            var defaultLightingOrigin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var sceneObjectLayerIndices = node.SceneObjectLayerIndices;
            var sceneObjects = node.SceneObjects;
            var extraVertexStreams = new ExtraVertexStreams(RendererContext, node);
            var materialOverrides = node.MaterialOverrides.ToLookup(static materialOverride => materialOverride.SceneObjectIndex);

            // Output is WorldNode_t we need to iterate m_sceneObjects inside it
            for (var sceneObjectIndex = 0; sceneObjectIndex < sceneObjects.Count; sceneObjectIndex++)
            {
                var sceneObject = sceneObjects[sceneObjectIndex];
                var layerIndex = (int)(sceneObjectLayerIndices?[sceneObjectIndex] ?? -1);

                // m_vCubeMapOrigin in older files
                var lightingOrigin = sceneObject.ContainsKey("m_vLightingOrigin") ? sceneObject.GetSubCollection("m_vLightingOrigin").ToVector3() : defaultLightingOrigin;
                var overlayRenderOrder = sceneObject.GetInt32Property("m_nOverlayRenderOrder");
                var cubeMapPrecomputedHandshake = sceneObject.GetInt32Property("m_nCubeMapPrecomputedHandshake");
                var lightProbeVolumePrecomputedHandshake = sceneObject.GetInt32Property("m_nLightProbeVolumePrecomputedHandshake");

                // sceneObject is SceneObject_t
                var renderableModel = sceneObject.GetStringProperty("m_renderableModel");
                var matrix = sceneObject.GetArray("m_vTransform").ToMatrix4x4() * root;
                var flags = sceneObject.GetEnumValue<ObjectTypeFlags>("m_nObjectTypeFlags", normalize: true);
                var visClusters = node.GetSceneObjectVisClusters(sceneObject);

                // Per-placement forced LoD baked by the compiler (-1 = automatic).
                var lodOverride = sceneObject.ContainsKey("m_nLODOverride") ? sceneObject.GetInt32Property("m_nLODOverride") : -1;

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

                    var skin = sceneObject.GetStringProperty("m_skin");

                    var model = (Model?)newResource.DataBlock;
                    Debug.Assert(model != null);
                    var modelNode = new ModelSceneNode(scene, model, skin)
                    {
                        Transform = matrix,
                        TintAlpha = tintColor,
                        LayerName = layerIndex > -1 ? LayerNames[layerIndex] : "No layer",
                        Name = renderableModel,
                        LightingOrigin = lightingOrigin == defaultLightingOrigin ? null : Vector3.Transform(lightingOrigin, root),
                        OverlayRenderOrder = overlayRenderOrder,
                        CubeMapPrecomputedHandshake = cubeMapPrecomputedHandshake,
                        LightProbeVolumePrecomputedHandshake = lightProbeVolumePrecomputedHandshake,
                        Flags = flags,
                        PrecomputedVisClusters = visClusters,
                    };

                    if (lodOverride >= 0)
                    {
                        modelNode.SetOverrideLod(lodOverride);
                    }

                    foreach (var materialOverride in materialOverrides[sceneObjectIndex])
                    {
                        var (mesh, drawCall) = FindDrawCall(modelNode, materialOverride.SubSceneObject, materialOverride.DrawCallIndex);

                        if (mesh != null && drawCall != null)
                        {
                            mesh.ReplaceMaterial(drawCall, materialOverride.Material);
                        }
                    }

                    extraVertexStreams.Apply(modelNode, sceneObjectIndex);
                    scene.Add(modelNode, false);
                }

                var renderable = sceneObject.GetStringProperty("m_renderable");

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
                        TintAlpha = tintColor,
                        LayerName = layerIndex > -1 ? LayerNames[layerIndex] : "No layer",
                        Name = renderable,
                        CubeMapPrecomputedHandshake = cubeMapPrecomputedHandshake,
                        LightProbeVolumePrecomputedHandshake = lightProbeVolumePrecomputedHandshake,
                        Flags = flags,
                        PrecomputedVisClusters = visClusters,
                    };

                    scene.Add(meshNode, false);
                }
            }

            foreach (var sceneObject in node.AggregateSceneObjects)
            {
                var renderableModel = sceneObject.GetStringProperty("m_renderableModel");

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
                        LayerName = LayerNames[(int)layerIndex],
                        Name = renderableModel,
                        AllFlags = sceneObject.GetEnumValue<ObjectTypeFlags>("m_allFlags", normalize: true),
                        AnyFlags = sceneObject.GetEnumValue<ObjectTypeFlags>("m_anyFlags", normalize: true),
                    };

                    scene.Add(aggregate, false);
                    aggregate.LoadFragments(sceneObject, root, node);
                }
            }

            foreach (var clutterData in node.ClutterSceneObjects)
            {
                LoadClutter(scene, new ClutterSceneObject(clutterData), root);
            }
        }

        /// <remarks>
        /// Instances share one tint per draw, so each distinct instance tint gets an aggregate of its own.
        /// </remarks>
        private void LoadClutter(Scene scene, ClutterSceneObject clutter, Matrix4x4 root)
        {
            if (clutter.RenderableModel == null || clutter.InstanceCount == 0)
            {
                return;
            }

            if (RendererContext.FileLoader.LoadFileCompiled(clutter.RenderableModel)?.DataBlock is not Model model)
            {
                return;
            }

            var instancesByTint = new Dictionary<Color32, List<int>>();

            for (var i = 0; i < clutter.InstanceCount; i++)
            {
                var tint = clutter.InstanceTints[i];

                if (!instancesByTint.TryGetValue(tint, out var instances))
                {
                    instances = [];
                    instancesByTint[tint] = instances;
                }

                instances.Add(i);
            }

            foreach (var (tint, instances) in instancesByTint)
            {
                var aggregate = new SceneAggregate(scene, model, clutter.MaterialGroup)
                {
                    LayerName = LayerNames[clutter.Layer],
                    Name = clutter.RenderableModel,
                    Flags = clutter.Flags,
                    AllFlags = clutter.Flags,
                    AnyFlags = clutter.Flags,
                    Tint = new Vector3(tint.R, tint.G, tint.B) / 255f,
                };

                var meshBounds = aggregate.RenderMesh.BoundingBox;
                AABB? bounds = null;

                aggregate.InstanceTransforms.EnsureCapacity(instances.Count);

                foreach (var instance in instances)
                {
                    var transform = clutter.GetInstanceTransform(instance) * root;
                    var instanceBounds = meshBounds.Transform(transform);

                    aggregate.InstanceTransforms.Add(transform.To3x4());
                    bounds = bounds?.Union(instanceBounds) ?? instanceBounds;
                }

                aggregate.LocalBoundingBox = bounds!.Value;

                scene.Add(aggregate, false);
            }
        }

        private static (RenderableMesh? Mesh, DrawCall? DrawCall) FindDrawCall(ModelSceneNode modelNode, int subSceneObject, int drawCallIndex)
        {
            foreach (var mesh in modelNode.AllRenderableMeshes)
            {
                if (mesh.MeshIndex != subSceneObject)
                {
                    continue;
                }

                var drawCall = mesh.DrawCalls.FirstOrDefault(call => call.Index == drawCallIndex);

                if (drawCall != null)
                {
                    return (mesh, drawCall);
                }
            }

            return (null, null);
        }

        private sealed class ExtraVertexStreams
        {
            private readonly ILookup<int, WorldNode.ExtraVertexStreamOverride> OverridesBySceneObject;
            private readonly VBIB Streams;
            private readonly GPUMeshBuffers Buffers;

            public ExtraVertexStreams(RendererContext rendererContext, WorldNode node)
            {
                OverridesBySceneObject = node.ExtraVertexStreamOverrides.ToLookup(static streamOverride => streamOverride.SceneObjectIndex);
                Streams = node.GetExtraVertexStreams();
                Buffers = rendererContext.MeshBufferCache.CreateVertexIndexBuffers($"{node.Resource.FileName} extra vertex streams", Streams);
            }

            public void Apply(ModelSceneNode modelNode, int sceneObjectIndex)
            {
                foreach (var streamOverride in OverridesBySceneObject[sceneObjectIndex])
                {
                    var stream = Streams.VertexBuffers[streamOverride.BufferIndex];
                    var (_, drawCall) = FindDrawCall(modelNode, streamOverride.SubSceneObject, streamOverride.DrawCallIndex);

                    // Streams painted on an older version of the model no longer match its vertex count
                    if (drawCall?.VertexCount != stream.ElementCount)
                    {
                        continue;
                    }

                    drawCall.AddVertexBuffer(new VertexDrawBuffer
                    {
                        Handle = Buffers.VertexBuffers[streamOverride.BufferIndex],
                        BufferIndex = streamOverride.BufferIndex,
                        ElementSizeInBytes = stream.ElementSizeInBytes,
                        InputLayoutFields = stream.InputLayoutFields,
                    });
                }
            }
        }
    }
}
