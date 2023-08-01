using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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
            public Camera Camera { get; init; }
            public IEnumerable<UniformBuffers.IBlockBindableBuffer> Buffers { get; set; }
            public WorldLightingInfo LightingInfo { get; set; }
            public WorldFogInfo FogInfo { get; set; }
            public RenderPass RenderPass { get; set; }
            public Shader ReplacementShader { get; set; }
            public bool RenderToolsMaterials { get; init; }
            public bool EnableFog { get; init; }
            public Vector3 WorldOffset { get; set; } // skybox, for fog
            public float WorldScale { get; set; } // skybox
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

        public bool IsSkybox { get; set; }
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

        public void RenderWithCamera(Camera camera, IEnumerable<UniformBuffers.IBlockBindableBuffer> buffers, Frustum cullFrustum = null)
        {
            var allNodes = StaticOctree.Query(cullFrustum ?? camera.ViewFrustum);
            allNodes.AddRange(DynamicOctree.Query(cullFrustum ?? camera.ViewFrustum));

            // Collect mesh calls
            var opaqueDrawCalls = new List<MeshBatchRenderer.Request>();
            var translucentDrawCalls = new List<MeshBatchRenderer.Request>();
            var looseNodes = new List<SceneNode>();
            foreach (var node in allNodes)
            {
                if (node is IRenderableMeshCollection meshCollection)
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
                                Node = node,
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
                    opaqueDrawCalls.Add(new MeshBatchRenderer.Request
                    {
                        Transform = fragment.Transform,
                        Mesh = fragment.RenderMesh,
                        Call = fragment.DrawCall,
                        DistanceFromCamera = (node.BoundingBox.Center - camera.Location).LengthSquared(),
                        Node = node,
                    });
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
            var renderContext = new RenderContext
            {
                Camera = camera,
                Buffers = buffers,
                LightingInfo = LightingInfo,
                FogInfo = FogInfo,
                RenderPass = RenderPass.Opaque,
                RenderToolsMaterials = ShowToolsMaterials,
                EnableFog = FogEnabled,
                WorldOffset = WorldOffset,
                WorldScale = WorldScale,
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

            MeshBatchRenderer.Render(opaqueDrawCalls, renderContext);
            foreach (var node in looseNodes)
            {
                node.Render(renderContext);
            }

            // Translucent render pass, back to front for loose nodes
            renderContext.RenderPass = RenderPass.Translucent;

            MeshBatchRenderer.Render(translucentDrawCalls, renderContext);
            foreach (var node in Enumerable.Reverse(looseNodes))
            {
                node.Render(renderContext);
            }

            if (camera.Picker is not null && camera.Picker.IsActive)
            {
                camera.Picker.Finish();
                RenderWithCamera(camera, buffers, cullFrustum);
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

        public void CalculateEnvironmentMaps()
        {
            if (LightingInfo.EnvMaps.Count == 0)
            {
                return;
            }

            var firstTexture = LightingInfo.EnvMaps.Values.First().EnvMapTexture;

            LightingInfo.LightingData = LightingInfo.LightingData with
            {
                EnvMapSizeConstants = new Vector4(firstTexture.NumMipLevels - 1, firstTexture.Depth, 0, 0),
            };

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
                        Console.WriteLine($"A envmap with handshake [{node.CubeMapPrecomputedHandshake}] does not exist for node at {node.BoundingBox.Center}");
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

#if DEBUG
                if (preCalculated != default)
                {
                    var vrfCalculated = node.EnvMaps.FirstOrDefault();
                    if (vrfCalculated is null)
                    {
                        Console.WriteLine($"Could not find any envmaps for node at {node.BoundingBox.Center}. Valve precalculated envmap is at {preCalculated.BoundingBox.Center} [{node.CubeMapPrecomputedHandshake}]");
                        continue;
                    }

                    if (vrfCalculated.HandShake == node.CubeMapPrecomputedHandshake)
                    {
                        continue;
                    }

                    var vrfDistance = Vector3.Distance(lightingOrigin, vrfCalculated.BoundingBox.Center);
                    var precalculatedDistance = Vector3.Distance(lightingOrigin, LightingInfo.EnvMaps[node.CubeMapPrecomputedHandshake].BoundingBox.Center);

                    Console.WriteLine($"Calculated envmap doesn't match with the precalculated one" +
                        $" (dists: vrf={vrfDistance} s2={precalculatedDistance}) for node at {node.BoundingBox.Center} [{node.CubeMapPrecomputedHandshake}]");
                }
#endif
            }
        }
    }
}
