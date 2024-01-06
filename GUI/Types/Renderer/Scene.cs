using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using GUI.Types.Renderer.UniformBuffers;
using GUI.Utils;

namespace GUI.Types.Renderer
{
    partial class Scene
    {
        public readonly struct UpdateContext
        {
            public float Timestep { get; }

            public UpdateContext(float timestep)
            {
                Timestep = timestep;
            }
        }

        public struct RenderContext
        {
            public GLSceneViewer View { get; init; }
            public Scene Scene { get; set; }
            public Camera Camera { get; set; }
            public int Framebuffer { get; set; }
            public RenderPass RenderPass { get; set; }
            public Shader ReplacementShader { get; set; }
        }

        public Camera MainCamera { get; set; }
        public SceneSky Sky { get; set; }
        public WorldLightingInfo LightingInfo { get; }
        public WorldFogInfo FogInfo { get; set; } = new();
        public Dictionary<string, byte> RenderAttributes { get; } = [];
        public VrfGuiContext GuiContext { get; }
        public Octree<SceneNode> StaticOctree { get; }
        public Octree<SceneNode> DynamicOctree { get; }
        public Vector3 WorldOffset { get; set; } = Vector3.Zero;
        public float WorldScale { get; set; } = 1.0f;
        // TODO: also store skybox reference rotation

        public bool ShowToolsMaterials { get; set; }
        public bool FogEnabled { get; set; } = true;

        public IEnumerable<SceneNode> AllNodes => staticNodes.Concat(dynamicNodes);

        private readonly List<SceneNode> staticNodes = [];
        private readonly List<SceneNode> dynamicNodes = [];

        public Scene(VrfGuiContext context, float sizeHint = 32768)
        {
            GuiContext = context;
            StaticOctree = new Octree<SceneNode>(sizeHint);
            DynamicOctree = new Octree<SceneNode>(sizeHint);

            LightingInfo = new(this);
        }

        public void Add(SceneNode node, bool dynamic)
        {
            if (dynamic)
            {
                dynamicNodes.Add(node);
                DynamicOctree.Insert(node, node.BoundingBox);
                node.Id = (uint)dynamicNodes.Count * 2 - 1;
            }
            else
            {
                staticNodes.Add(node);
                StaticOctree.Insert(node, node.BoundingBox);
                node.Id = (uint)staticNodes.Count * 2;
            }
        }

        public SceneNode Find(uint id)
        {
            if (id == 0)
            {
                return null;
            }

            if (id % 2 == 1)
            {
                var index = ((int)id + 1) / 2 - 1;

                if (index >= dynamicNodes.Count)
                {
                    return null;
                }

                return dynamicNodes[index];
            }
            else
            {
                var index = (int)id / 2 - 1;

                if (index >= staticNodes.Count)
                {
                    return null;
                }

                return staticNodes[index];
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

        // Since we only ever draw one scene at a time, these can be static
        // And they are fields here so they only ever grow without having to re-allocate these arrays every frame
        private readonly static List<SceneNode> cullingResult = [];
        private readonly static List<MeshBatchRenderer.Request> renderLooseNodes = [];
        private readonly static List<MeshBatchRenderer.Request> renderOpaqueDrawCalls = [];
        private readonly static List<MeshBatchRenderer.Request> renderStaticOverlays = [];
        private readonly static List<MeshBatchRenderer.Request> renderTranslucentDrawCalls = [];

        public void RenderWithCamera(RenderContext renderContext, Frustum cullFrustum = null)
        {
            var camera = renderContext.Camera;
            cullFrustum ??= camera.ViewFrustum;

            StaticOctree.Root.Query(cullFrustum, cullingResult);
            DynamicOctree.Root.Query(cullFrustum, cullingResult);

            // Collect mesh calls
            foreach (var node in cullingResult)
            {
                if (node is IRenderableMeshCollection meshCollection)
                {
                    foreach (var mesh in meshCollection.RenderableMeshes)
                    {
                        foreach (var call in mesh.DrawCallsOpaque)
                        {
                            renderOpaqueDrawCalls.Add(new MeshBatchRenderer.Request
                            {
                                Transform = node.Transform,
                                Mesh = mesh,
                                Call = call,
                                Node = node,
                            });
                        }

                        foreach (var call in mesh.DrawCallsOverlay)
                        {
                            renderStaticOverlays.Add(new MeshBatchRenderer.Request
                            {
                                Transform = node.Transform,
                                Mesh = mesh,
                                Call = call,
                                RenderOrder = node.OverlayRenderOrder,
                                Node = node,
                            });
                        }

                        foreach (var call in mesh.DrawCallsBlended)
                        {
                            renderTranslucentDrawCalls.Add(new MeshBatchRenderer.Request
                            {
                                Transform = node.Transform,
                                Mesh = mesh,
                                Call = call,
                                DistanceFromCamera = (node.BoundingBox.Center - camera.Location).LengthSquared(),
                                Node = node,
                            });
                        }
                    }
                }
                else if (node is SceneAggregate aggregate)
                {
                }
                else if (node is SceneAggregate.Fragment fragment)
                {
                    renderOpaqueDrawCalls.Add(new MeshBatchRenderer.Request
                    {
                        Transform = fragment.Transform,
                        Mesh = fragment.RenderMesh,
                        Call = fragment.DrawCall,
                        Node = node,
                    });
                }
                else
                {
                    renderLooseNodes.Add(new MeshBatchRenderer.Request
                    {
                        DistanceFromCamera = (node.BoundingBox.Center - camera.Location).LengthSquared(),
                        Node = node,
                    });
                }
            }

            cullingResult.Clear();

            renderLooseNodes.Sort(MeshBatchRenderer.CompareCameraDistance);

            renderContext.RenderPass = RenderPass.Opaque;
            MeshBatchRenderer.Render(renderOpaqueDrawCalls, renderContext);
            renderOpaqueDrawCalls.Clear();

            renderContext.RenderPass = RenderPass.StaticOverlay;
            MeshBatchRenderer.Render(renderStaticOverlays, renderContext);
            renderStaticOverlays.Clear();

            renderContext.RenderPass = RenderPass.AfterOpaque;
            foreach (var request in renderLooseNodes)
            {
                request.Node.Render(renderContext);
            }

            renderContext.RenderPass = RenderPass.Translucent;
            MeshBatchRenderer.Render(renderTranslucentDrawCalls, renderContext);
            renderTranslucentDrawCalls.Clear();

            for (var i = renderLooseNodes.Count - 1; i >= 0; i--)
            {
                renderLooseNodes[i].Node.Render(renderContext);
            }
            renderLooseNodes.Clear();
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

        public void SetFogConstants(ViewConstants viewConstants)
        {
            FogInfo.SetFogUniforms(viewConstants, FogEnabled, WorldOffset, WorldScale);
        }

        public void CalculateEnvironmentMaps()
        {
            if (LightingInfo.EnvMaps.Count == 0)
            {
                return;
            }

            var firstTexture = LightingInfo.EnvMaps.First().EnvMapTexture;

            LightingInfo.LightingData.EnvMapSizeConstants = new Vector4(firstTexture.NumMipLevels - 1, firstTexture.Depth, 0, 0);

            int ArrayIndexCompare(SceneEnvMap a, SceneEnvMap b) => a.ArrayIndex.CompareTo(b.ArrayIndex);
            int HandShakeCompare(SceneEnvMap a, SceneEnvMap b) => a.HandShake.CompareTo(b.HandShake);

            LightingInfo.EnvMaps.Sort(LightingInfo.CubemapType switch
            {
                CubemapType.CubemapArray => ArrayIndexCompare,
                _ => HandShakeCompare
            });

            var i = 0;
            foreach (var envMap in LightingInfo.EnvMaps)
            {
                if (i >= LightingConstants.MAX_ENVMAPS)
                {
                    Log.Error(nameof(WorldLoader), $"Envmap array index {i} is too large, skipping! Max: {LightingConstants.MAX_ENVMAPS}");
                    continue;
                }

                if (LightingInfo.CubemapType == CubemapType.CubemapArray)
                {
                    Debug.Assert(envMap.ArrayIndex == i, "Envmap array index mismatch");
                }

                var nodes = StaticOctree.Query(envMap.BoundingBox);

                foreach (var node in nodes)
                {
                    node.EnvMaps.Add(envMap);
                }

                nodes = DynamicOctree.Query(envMap.BoundingBox); // TODO: This should actually be done dynamically

                foreach (var node in nodes)
                {
                    node.EnvMaps.Add(envMap);
                }

                UpdateGpuEnvmapData(envMap, i);
                i++;
            }

            foreach (var node in AllNodes)
            {
                var preComputedHandshake = node.CubeMapPrecomputedHandshake;
                SceneEnvMap preComputed = default;

                if (preComputedHandshake > 0)
                {
                    if (LightingInfo.CubemapType == CubemapType.IndividualCubemaps
                        && preComputedHandshake <= LightingInfo.EnvMaps.Count)
                    {
                        // SteamVR Home node handshake as envmap index
                        node.EnvMaps.Clear();
                        node.EnvMaps.Add(LightingInfo.EnvMaps[preComputedHandshake - 1]);
                    }
                    else if (LightingInfo.EnvMapHandshakes.TryGetValue(preComputedHandshake, out preComputed))
                    {
                        node.EnvMaps.Clear();
                        node.EnvMaps.Add(preComputed);
                    }
                    else
                    {
#if DEBUG
                        Log.Debug(nameof(Scene), $"A envmap with handshake [{preComputedHandshake}] does not exist for node at {node.BoundingBox.Center}");
#endif
                    }
                }

                var lightingOrigin = node.LightingOrigin ?? Vector3.Zero;
                if (node.LightingOrigin.HasValue)
                {
                    // in source2 this is a dynamic combo D_SPECULAR_CUBEMAP_STATIC=1, and i guess without a loop (similar to SCENE_CUBEMAP_TYPE=1)
                    node.EnvMaps = node.EnvMaps
                        .OrderBy((envMap) => Vector3.Distance(lightingOrigin, envMap.BoundingBox.Center))
                        .ToList();
                }
                else
                {
                    node.EnvMaps = node.EnvMaps
                        .OrderByDescending((envMap) => envMap.IndoorOutdoorLevel)
                        .ThenBy((envMap) => Vector3.Distance(node.BoundingBox.Center, envMap.BoundingBox.Center))
                        .ToList();
                }

                var max = 16;
                if (node.EnvMaps.Count > max)
                {
                    Log.Warn("Renderer", $"Performance warning: more than {max} envmaps binned for node {node.DebugName}");
                }

                node.EnvMapIds = LightingInfo.CubemapType switch
                {
                    CubemapType.CubemapArray => node.EnvMaps.Select(x => x.ArrayIndex).ToArray(),
                    _ => node.EnvMaps.Select(e => LightingInfo.EnvMaps.IndexOf(e)).ToArray()
                };

#if DEBUG
                if (preComputed != default)
                {
                    var vrfComputed = node.EnvMaps.FirstOrDefault();
                    if (vrfComputed is null)
                    {
                        Log.Debug(nameof(Scene), $"Could not find any envmaps for node {node.DebugName}. Valve precomputed envmap is at {preComputed.BoundingBox.Center} [{preComputedHandshake}]");
                        continue;
                    }

                    if (vrfComputed.HandShake == preComputedHandshake)
                    {
                        continue;
                    }

                    var vrfDistance = Vector3.Distance(lightingOrigin, vrfComputed.BoundingBox.Center);
                    var preComputedDistance = Vector3.Distance(lightingOrigin, LightingInfo.EnvMapHandshakes[preComputedHandshake].BoundingBox.Center);

                    var anyIndex = node.EnvMaps.FindIndex(x => x.HandShake == preComputedHandshake);

                    Log.Debug(nameof(Scene), $"Topmost calculated envmap doesn't match with the precomputed one" +
                        $" (dists: vrf={vrfDistance} s2={preComputedDistance}) for node at {node.BoundingBox.Center} [{preComputedHandshake}]" +
                        (anyIndex > 0 ? $" (however it's still binned at a higher iterate index {anyIndex})" : string.Empty));
                }
#endif
                if (LightingInfo.CubemapType == CubemapType.CubemapArray)
                {
                    node.EnvMaps = null; // no longer need
                }
            }
        }

        private void UpdateGpuEnvmapData(SceneEnvMap envMap, int index)
        {
            Matrix4x4.Invert(envMap.Transform, out var invertedTransform);

            LightingInfo.LightingData.EnvMapWorldToLocal[index] = invertedTransform;
            LightingInfo.LightingData.EnvMapBoxMins[index] = new Vector4(envMap.LocalBoundingBox.Min, 0);
            LightingInfo.LightingData.EnvMapBoxMaxs[index] = new Vector4(envMap.LocalBoundingBox.Max, 0);
            LightingInfo.LightingData.EnvMapEdgeInvEdgeWidth[index] = new Vector4(envMap.EdgeFadeDists, 0);
            LightingInfo.LightingData.EnvMapProxySphere[index] = new Vector4(envMap.Transform.Translation, envMap.ProjectionMode);
            LightingInfo.LightingData.EnvMapColorRotated[index] = new Vector4(envMap.Tint, 0);

            // TODO
            LightingInfo.LightingData.EnvMapNormalizationSH[index] = new Vector4(0, 0, 0, 1);
        }
    }
}
