using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GUI.Types.Renderer.UniformBuffers;
using GUI.Utils;

namespace GUI.Types.Renderer
{
    class Scene
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
            public Scene Scene { get; init; }
            public Camera Camera { get; init; }
            public RenderPass RenderPass { get; set; }
            public Shader ReplacementShader { get; set; }
        }

        public Camera MainCamera { get; set; }
        public SceneSky Sky { get; set; }
        public WorldLightingInfo LightingInfo { get; } = new();
        public WorldFogInfo FogInfo { get; set; } = new();
        public Dictionary<string, byte> RenderAttributes { get; } = new();
        public VrfGuiContext GuiContext { get; }
        public Octree<SceneNode> StaticOctree { get; }
        public Octree<SceneNode> DynamicOctree { get; }
        public Vector3 WorldOffset { get; set; } = Vector3.Zero;
        public float WorldScale { get; set; } = 1.0f;
        // TODO: also store skybox reference rotation

        public bool ShowToolsMaterials { get; set; }
        public bool FogEnabled { get; set; } = true;

        public IEnumerable<SceneNode> AllNodes => staticNodes.Concat(dynamicNodes);

        private readonly List<SceneNode> staticNodes = new();
        private readonly List<SceneNode> dynamicNodes = new();

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
        private readonly static List<SceneNode> cullingResult = new();
        private readonly static List<MeshBatchRenderer.Request> renderLooseNodes = new();
        private readonly static List<MeshBatchRenderer.Request> renderOpaqueDrawCalls = new();
        private readonly static List<MeshBatchRenderer.Request> renderStaticOverlays = new();
        private readonly static List<MeshBatchRenderer.Request> renderTranslucentDrawCalls = new();

        public void RenderWithCamera(Camera camera, GLSceneViewer view, Frustum cullFrustum = null)
        {
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

            // Opaque render pass
            var renderContext = new RenderContext
            {
                Scene = this,
                Camera = camera,
                View = view,
                RenderPass = RenderPass.Opaque,
            };

            if (camera.Picker is not null)
            {
                if (camera.Picker.IsActive)
                {
                    camera.Picker.Render();
                    renderContext.ReplacementShader = camera.Picker.Shader;
                }
                else if (camera.Picker.DebugShader is not null)
                {
                    renderContext.ReplacementShader = camera.Picker.DebugShader;
                }
            }

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

            if (camera.Picker is not null && camera.Picker.IsActive)
            {
                camera.Picker.Finish();
                RenderWithCamera(camera, view, cullFrustum);
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

            var firstTexture = LightingInfo.EnvMaps.Values.First().EnvMapTexture;

            LightingInfo.LightingData.EnvMapSizeConstants = new Vector4(firstTexture.NumMipLevels - 1, firstTexture.Depth, 0, 0);

            foreach (var envMap in LightingInfo.EnvMaps.Values)
            {
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

                if (envMap.ArrayIndex < 0)
                {
                    continue;
                }

                Matrix4x4.Invert(envMap.Transform, out var invertedTransform);

                LightingInfo.LightingData.EnvMapWorldToLocal[envMap.ArrayIndex] = invertedTransform;
                LightingInfo.LightingData.EnvMapBoxMins[envMap.ArrayIndex] = new Vector4(envMap.LocalBoundingBox.Min, 0);
                LightingInfo.LightingData.EnvMapBoxMaxs[envMap.ArrayIndex] = new Vector4(envMap.LocalBoundingBox.Max, 0);
                LightingInfo.LightingData.EnvMapEdgeInvEdgeWidth[envMap.ArrayIndex] = new Vector4(envMap.EdgeFadeDists, 0);
                LightingInfo.LightingData.EnvMapProxySphere[envMap.ArrayIndex] = new Vector4(envMap.Transform.Translation, envMap.ProjectionMode);
                LightingInfo.LightingData.EnvMapColorRotated[envMap.ArrayIndex] = new Vector4(envMap.Tint, 0);

                // TODO
                LightingInfo.LightingData.EnvMapNormalizationSH[envMap.ArrayIndex] = new Vector4(0, 0, 0, 1);
            }

            foreach (var node in AllNodes)
            {
                SceneEnvMap preCalculated = default;

                if (node.CubeMapPrecomputedHandshake > 0)
                {
                    if (!LightingInfo.EnvMaps.TryGetValue(node.CubeMapPrecomputedHandshake, out preCalculated))
                    {
#if DEBUG
                        Log.Debug(nameof(Scene), $"A envmap with handshake [{node.CubeMapPrecomputedHandshake}] does not exist for node at {node.BoundingBox.Center}");
#endif
                        continue;
                    }

                    // Prefer precalculated env map for legacy games (steamvr)
                    if (preCalculated.EnvMapTexture.Target == OpenTK.Graphics.OpenGL.TextureTarget.TextureCubeMap)
                    {
                        node.EnvMaps.Clear();
                        node.EnvMaps.Add(preCalculated);
                        continue;
                    }
                }

                var lightingOrigin = node.LightingOrigin ?? Vector3.Zero;
                if (node.LightingOrigin.HasValue)
                {
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

                node.EnvMapIds = node.EnvMaps.Select(x => x.ArrayIndex).ToArray();

#if DEBUG
                if (preCalculated != default)
                {
                    var vrfCalculated = node.EnvMaps.FirstOrDefault();
                    if (vrfCalculated is null)
                    {
                        Log.Debug(nameof(Scene), $"Could not find any envmaps for node at {node.BoundingBox.Center}. Valve precalculated envmap is at {preCalculated.BoundingBox.Center} [{node.CubeMapPrecomputedHandshake}]");
                        continue;
                    }

                    if (vrfCalculated.HandShake == node.CubeMapPrecomputedHandshake)
                    {
                        continue;
                    }

                    var vrfDistance = Vector3.Distance(lightingOrigin, vrfCalculated.BoundingBox.Center);
                    var precalculatedDistance = Vector3.Distance(lightingOrigin, LightingInfo.EnvMaps[node.CubeMapPrecomputedHandshake].BoundingBox.Center);

                    Log.Debug(nameof(Scene), $"Calculated envmap doesn't match with the precalculated one" +
                        $" (dists: vrf={vrfDistance} s2={precalculatedDistance}) for node at {node.BoundingBox.Center} [{node.CubeMapPrecomputedHandshake}]");
                }
#endif
            }
        }
    }
}
