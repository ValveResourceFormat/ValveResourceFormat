using System.Diagnostics;
using System.Linq;
using GUI.Types.Renderer.UniformBuffers;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using static GUI.Types.Renderer.GLSceneViewer;

namespace GUI.Types.Renderer
{
    partial class Scene : IDisposable
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
            public Framebuffer Framebuffer { get; set; }
            public RenderPass RenderPass { get; set; }
            public Shader ReplacementShader { get; set; }
        }

        public Dictionary<string, byte> RenderAttributes { get; } = [];
        public WorldLightingInfo LightingInfo { get; }
        public WorldFogInfo FogInfo { get; set; } = new();
        public WorldPostProcessInfo PostProcessInfo { get; set; } = new();
        private UniformBuffer<LightingConstants> lightingBuffer;

        public VrfGuiContext GuiContext { get; }
        public Octree<SceneNode> StaticOctree { get; }
        public Octree<SceneNode> DynamicOctree { get; }

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

        public void Initialize()
        {
            UpdateOctrees();
            CalculateLightProbeBindings();
            CalculateEnvironmentMaps();
            CreateBuffers();
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

        public void CreateBuffers()
        {
            lightingBuffer = new(ReservedBufferSlots.Lighting)
            {
                Data = LightingInfo.LightingData
            };
        }

        public void UpdateBuffers()
        {
            lightingBuffer.Update();
        }

        public void SetSceneBuffers()
        {
            lightingBuffer.BindBufferBase();
        }

        private readonly List<SceneNode> CullResults = [];
        private int StaticCount;
        private int LastFrustum = -1;

        public List<SceneNode> GetFrustumCullResults(Frustum frustum)
        {
            var currentFrustum = frustum.GetHashCode();

            // Optimization: Do not clear static culled results from last frame if:
            // 1. Frustum did not change
            // 2. Did not run occlusion queries

            if (LastFrustum != currentFrustum || occlusionDirty)
            {
                LastFrustum = currentFrustum;

                CullResults.Clear();
                CullResults.Capacity = staticNodes.Count + dynamicNodes.Count + 100;

                StaticOctree.Root.Query(frustum, CullResults);
                StaticCount = CullResults.Count;
            }
            else
            {
                CullResults.RemoveRange(StaticCount, CullResults.Count - StaticCount);
            }

            DynamicOctree.Root.Query(frustum, CullResults);
            return CullResults;
        }

        private readonly List<MeshBatchRenderer.Request> renderLooseNodes = [];
        private readonly List<MeshBatchRenderer.Request> renderOpaqueDrawCalls = [];
        private readonly List<MeshBatchRenderer.Request> renderStaticOverlays = [];
        private readonly List<MeshBatchRenderer.Request> renderTranslucentDrawCalls = [];

        private void Add(MeshBatchRenderer.Request request, RenderPass renderPass)
        {
            if (renderPass != RenderPass.AfterOpaque && !ShowToolsMaterials && request.Call.Material.IsToolsMaterial)
            {
                return;
            }

            var queueList = renderPass switch
            {
                RenderPass.Opaque => renderOpaqueDrawCalls,
                RenderPass.StaticOverlay => renderStaticOverlays,
                RenderPass.Translucent => renderTranslucentDrawCalls,
                _ => renderLooseNodes,
            };

            queueList.Add(request);
        }

        public void CollectSceneDrawCalls(Camera camera, Frustum cullFrustum = null)
        {
            renderOpaqueDrawCalls.Clear();
            renderStaticOverlays.Clear();
            renderTranslucentDrawCalls.Clear();
            renderLooseNodes.Clear();

            cullFrustum ??= camera.ViewFrustum;
            var cullResults = GetFrustumCullResults(cullFrustum);

            // Collect mesh calls
            foreach (var node in cullResults)
            {
                if (node is IRenderableMeshCollection meshCollection)
                {
                    foreach (var mesh in meshCollection.RenderableMeshes)
                    {
                        foreach (var call in mesh.DrawCallsOpaque)
                        {
                            Add(new MeshBatchRenderer.Request
                            {
                                Transform = node.Transform,
                                Mesh = mesh,
                                Call = call,
                                Node = node,
                            }, RenderPass.Opaque);
                        }

                        foreach (var call in mesh.DrawCallsOverlay)
                        {
                            Add(new MeshBatchRenderer.Request
                            {
                                Transform = node.Transform,
                                Mesh = mesh,
                                Call = call,
                                RenderOrder = node.OverlayRenderOrder,
                                Node = node,
                            }, RenderPass.StaticOverlay);
                        }

                        foreach (var call in mesh.DrawCallsBlended)
                        {
                            Add(new MeshBatchRenderer.Request
                            {
                                Transform = node.Transform,
                                Mesh = mesh,
                                Call = call,
                                DistanceFromCamera = (node.BoundingBox.Center - camera.Location).LengthSquared(),
                                Node = node,
                            }, RenderPass.Translucent);
                        }
                    }
                }
                else if (node is SceneAggregate.Fragment fragment)
                {
                    Add(new MeshBatchRenderer.Request
                    {
                        Transform = fragment.Transform,
                        Mesh = fragment.RenderMesh,
                        Call = fragment.DrawCall,
                        Node = node,
                    }, RenderPass.Opaque);
                }
                else
                {
                    Add(new MeshBatchRenderer.Request
                    {
                        DistanceFromCamera = (node.BoundingBox.Center - camera.Location).LengthSquared(),
                        Node = node,
                    }, RenderPass.AfterOpaque);
                }
            }

            renderLooseNodes.Sort(MeshBatchRenderer.CompareCameraDistance);
        }

        private List<SceneNode> CulledShadowNodes { get; } = [];
        private readonly List<RenderableMesh> listWithSingleMesh = [null];
        private Dictionary<DepthOnlyProgram, List<MeshBatchRenderer.Request>> CulledShadowDrawCalls { get; } = new()
        {
            [DepthOnlyProgram.Static] = [],
            [DepthOnlyProgram.StaticAlphaTest] = [],
            [DepthOnlyProgram.Animated] = [],
            [DepthOnlyProgram.AnimatedEightBones1] = [],
            [DepthOnlyProgram.AnimatedEightBones2] = [],
        };

        public void SetupSceneShadows(Camera camera, int shadowMapSize)
        {
            if (!LightingInfo.EnableDynamicShadows)
            {
                return;
            }

            LightingInfo.UpdateSunLightFrustum(camera, shadowMapSize);

            foreach (var bucket in CulledShadowDrawCalls.Values)
            {
                bucket.Clear();
            }

            DynamicOctree.Root.Query(LightingInfo.SunLightFrustum, CulledShadowNodes);

            foreach (var node in CulledShadowNodes)
            {
                List<RenderableMesh> meshes;

                if (node is IRenderableMeshCollection meshCollection)
                {
                    meshes = meshCollection.RenderableMeshes;
                }
                else if (node is SceneAggregate aggregate)
                {
                    listWithSingleMesh[0] = aggregate.RenderMesh;
                    meshes = listWithSingleMesh;
                }
                else
                {
                    continue;
                }

                var animated = node is ModelSceneNode model && model.IsAnimated;

                foreach (var mesh in meshes)
                {
                    foreach (var opaqueCall in mesh.DrawCallsOpaque)
                    {
                        var bucket = (opaqueCall.Material.IsAlphaTest, animated) switch
                        {
                            (false, false) => DepthOnlyProgram.Static,
                            (true, _) => DepthOnlyProgram.StaticAlphaTest,
                            (false, true) => DepthOnlyProgram.Animated,
                        };

                        if (mesh.BoneWeightCount > 4)
                        {
                            bucket = opaqueCall.Material.Shader.Parameters.GetValueOrDefault("D_EIGHT_BONE_BLENDING") switch
                            {
                                1 => DepthOnlyProgram.AnimatedEightBones1,
                                2 => DepthOnlyProgram.AnimatedEightBones2,
                                _ => bucket,
                            };
                        }

                        CulledShadowDrawCalls[bucket].Add(new MeshBatchRenderer.Request
                        {
                            Transform = node.Transform,
                            Mesh = mesh,
                            Call = opaqueCall,
                            Node = node,
                        });
                    }
                }
            }

            CulledShadowNodes.Clear();
        }

        public void RenderOpaqueShadows(RenderContext renderContext, Span<Shader> depthOnlyShaders)
        {
            using (new GLDebugGroup("Scene Shadows"))
            {
                renderContext.RenderPass = RenderPass.DepthOnly;

                foreach (var (program, calls) in CulledShadowDrawCalls)
                {
                    renderContext.ReplacementShader = depthOnlyShaders[(int)program];
                    MeshBatchRenderer.Render(calls, renderContext);
                }
            }
        }

        public void RenderOpaqueLayer(RenderContext renderContext)
        {
            var camera = renderContext.Camera;

            using (new GLDebugGroup("Opaque Render"))
            {
                renderContext.RenderPass = RenderPass.Opaque;
                MeshBatchRenderer.Render(renderOpaqueDrawCalls, renderContext);
            }

            using (new GLDebugGroup("StaticOverlay Render"))
            {
                renderContext.RenderPass = RenderPass.StaticOverlay;
                MeshBatchRenderer.Render(renderStaticOverlays, renderContext);
            }

            using (new GLDebugGroup("AfterOpaque RenderLoose"))
            {
                renderContext.RenderPass = RenderPass.AfterOpaque;
                foreach (var request in renderLooseNodes)
                {
                    request.Node.Render(renderContext);
                }
            }
        }

        private bool occlusionDirty;

        static void ClearOccludedStateRecursive(Octree<SceneNode>.Node node)
        {
            foreach (var child in node.Children)
            {
                child.OcclusionCulled = false;
                child.OcculsionQuerySubmitted = false;
                ClearOccludedStateRecursive(child);
            }
        }

        public void RenderOcclusionProxies(RenderContext renderContext, Shader depthOnlyShader)
        {
            if (!renderContext.View.EnableOcclusionCulling)
            {
                if (occlusionDirty)
                {
                    ClearOccludedStateRecursive(StaticOctree.Root);
                    occlusionDirty = false;
                    LastFrustum = -1;
                }

                return;
            }

            occlusionDirty = true;

            GL.ColorMask(false, false, false, false);
            GL.DepthMask(false);
            GL.Disable(EnableCap.CullFace);

            GL.UseProgram(depthOnlyShader.Program);
            GL.BindVertexArray(GuiContext.MeshBufferCache.EmptyVAO);

            var maxTests = 128;
            var maxDepth = 8;
            TestOctantsRecursive(StaticOctree.Root, renderContext.Camera.Location, ref maxTests, maxDepth);

            GL.UseProgram(0);
            GL.BindVertexArray(0);

            GL.ColorMask(true, true, true, true);
            GL.DepthMask(true);
            GL.Enable(EnableCap.CullFace);
        }

        private static void TestOctantsRecursive(Octree<SceneNode>.Node octant, Vector3 cameraPosition, ref int maxTests, int maxDepth)
        {
            foreach (var octreeNode in octant.Children)
            {
                if (octreeNode.FrustumCulled)
                {
                    octreeNode.OcclusionCulled = false;
                    octreeNode.OcculsionQuerySubmitted = false;
                    continue;
                }

                if (!octreeNode.HasChildren)
                {
                    continue;
                }

                if (octreeNode.Region.Contains(cameraPosition))
                {
                    // if the camera is inside the octant, we can skip the occlusion test, however we still need to test the children
                    TestOctantsRecursive(octreeNode, cameraPosition, ref maxTests, --maxDepth);

                    octreeNode.OcclusionCulled = false;
                    octreeNode.OcculsionQuerySubmitted = false;
                    continue;
                }

                if (octreeNode.OcculsionQuerySubmitted)
                {
                    var visible = -1;
                    GL.GetQueryObject(
                        octreeNode.OcclusionQueryHandle,
                        GetQueryObjectParam.QueryResultNoWait,
                        out visible
                    );

                    octreeNode.OcclusionCulled = visible == 0;
                    octreeNode.OcculsionQuerySubmitted = visible == -1;

                    if (visible == 1)
                    {
                        TestOctantsRecursive(octreeNode, cameraPosition, ref maxTests, --maxDepth);
                    }

                    continue;
                }

                // Octree node passed frustum test, contains elements, and was not waiting for a previous query

                if (octreeNode.OcclusionQueryHandle == -1)
                {
                    octreeNode.OcclusionQueryHandle = GL.GenQuery();
                }

                octreeNode.OcculsionQuerySubmitted = true;
                maxTests--;

                GL.VertexAttrib4(
                    0,
                    octreeNode.Region.Min.X,
                    octreeNode.Region.Min.Y,
                    octreeNode.Region.Min.Z,
                    octreeNode.Region.Size.X
                );

                GL.VertexAttribI2(1, maxDepth, maxTests);

                GL.BeginQuery(QueryTarget.AnySamplesPassedConservative, octreeNode.OcclusionQueryHandle);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
                GL.EndQuery(QueryTarget.AnySamplesPassedConservative);
            }

            if (maxTests < 0 || maxDepth < 0)
            {
                return;
            }
        }

        public void RenderTranslucentLayer(RenderContext renderContext)
        {
            using (new GLDebugGroup("Translucent RenderLoose"))
            {
                renderContext.RenderPass = RenderPass.Translucent;
                foreach (var request in renderLooseNodes)
                {
                    request.Node.Render(renderContext);
                }
            }

            using (new GLDebugGroup("Translucent Render"))
            {
                MeshBatchRenderer.Render(renderTranslucentDrawCalls, renderContext);
            }
        }

        public void SetEnabledLayers(HashSet<string> layers, bool skipUpdate = false)
        {
            foreach (var renderer in AllNodes)
            {
                renderer.LayerEnabled = layers.Contains(renderer.LayerName);
            }

            if (!skipUpdate)
            {
                UpdateOctrees();
            }
        }

        public void UpdateOctrees()
        {
            LastFrustum = -1;
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
            FogInfo.SetFogUniforms(viewConstants, FogEnabled);
        }

        public void CalculateLightProbeBindings()
        {
            if (LightingInfo.LightProbes.Count == 0)
            {
                return;
            }

            LightingInfo.LightProbes.Sort((a, b) => a.HandShake.CompareTo(b.HandShake));

            foreach (var node in AllNodes)
            {
                var precomputedHandshake = node.LightProbeVolumePrecomputedHandshake;
                if (precomputedHandshake == 0)
                {
                    continue;
                }

                if (LightingInfo.LightmapGameVersionNumber == 0 && precomputedHandshake <= LightingInfo.LightProbes.Count)
                {
                    // SteamVR Home node handshake as probe index
                    node.LightProbeBinding = LightingInfo.LightProbes[precomputedHandshake - 1];
                    continue;
                }

                if (LightingInfo.ProbeHandshakes.TryGetValue(precomputedHandshake, out var precomputedProbe))
                {
                    node.LightProbeBinding = precomputedProbe;
                    continue;
                }
            }

            var sortedLightProbes = LightingInfo.LightProbes
                .OrderByDescending(static lpv => lpv.IndoorOutdoorLevel)
                .ThenBy(static lpv => lpv.AtlasSize.LengthSquared());

            var nodes = new List<SceneNode>();

            foreach (var probe in sortedLightProbes)
            {
                StaticOctree.Root.Query(probe.BoundingBox, nodes);
                DynamicOctree.Root.Query(probe.BoundingBox, nodes); // TODO: This should actually be done dynamically

                foreach (var node in nodes)
                {
                    node.LightProbeBinding ??= probe;
                }

                nodes.Clear();
            }

            // Assign random probe to any node that does not have any light probes to fix the flickering,
            // this isn't ideal, and a proper fix would be to remove D_BAKED_LIGHTING_FROM_PROBE from the shader
            var firstProbe = LightingInfo.ProbeHandshakes.Values.First();

            foreach (var node in AllNodes)
            {
                node.LightProbeBinding ??= firstProbe;
            }
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

            var nodes = new List<SceneNode>();
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

                StaticOctree.Root.Query(envMap.BoundingBox, nodes);
                DynamicOctree.Root.Query(envMap.BoundingBox, nodes); // TODO: This should actually be done dynamically

                foreach (var node in nodes)
                {
                    node.EnvMaps.Add(envMap);
                }

                UpdateGpuEnvmapData(envMap, i);
                i++;

                nodes.Clear();
            }

            foreach (var node in AllNodes)
            {
                var precomputedHandshake = node.CubeMapPrecomputedHandshake;
                SceneEnvMap preComputed = default;

                if (precomputedHandshake > 0)
                {
                    if (LightingInfo.CubemapType == CubemapType.IndividualCubemaps
                        && precomputedHandshake <= LightingInfo.EnvMaps.Count)
                    {
                        // SteamVR Home node handshake as envmap index
                        node.EnvMaps.Clear();
                        node.EnvMaps.Add(LightingInfo.EnvMaps[precomputedHandshake - 1]);
                    }
                    else if (LightingInfo.EnvMapHandshakes.TryGetValue(precomputedHandshake, out preComputed))
                    {
                        node.EnvMaps.Clear();
                        node.EnvMaps.Add(preComputed);
                    }
                    else
                    {
#if DEBUG
                        Log.Debug(nameof(Scene), $"A envmap with handshake [{precomputedHandshake}] does not exist for node at {node.BoundingBox.Center}");
#endif
                    }
                }

                var lightingOrigin = node.LightingOrigin ?? Vector3.Zero;
                if (node.LightingOrigin.HasValue)
                {
                    // in source2 this is a dynamic combo D_SPECULAR_CUBEMAP_STATIC=1, and i guess without a loop (similar to SCENE_CUBEMAP_TYPE=1)
                    node.EnvMaps = [.. node.EnvMaps.OrderBy((envMap) => Vector3.Distance(lightingOrigin, envMap.BoundingBox.Center))];
                }
                else
                {
                    node.EnvMaps = [.. node.EnvMaps
                        .OrderByDescending((envMap) => envMap.IndoorOutdoorLevel)
                        .ThenBy((envMap) => Vector3.Distance(node.BoundingBox.Center, envMap.BoundingBox.Center))
                    ];
                }

                /*
                const int max = 16;
                if (node.EnvMaps.Count > max)
                {
                    Log.Warn("Renderer", $"Performance warning: more than {max} envmaps binned for node {node.DebugName}");
                }
                */

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
                        Log.Debug(nameof(Scene), $"Could not find any envmaps for node {node.DebugName}. Valve precomputed envmap is at {preComputed.BoundingBox.Center} [{precomputedHandshake}]");
                        continue;
                    }

                    if (vrfComputed.HandShake == precomputedHandshake)
                    {
                        continue;
                    }

                    var vrfDistance = Vector3.Distance(lightingOrigin, vrfComputed.BoundingBox.Center);
                    var preComputedDistance = Vector3.Distance(lightingOrigin, LightingInfo.EnvMapHandshakes[precomputedHandshake].BoundingBox.Center);

                    var anyIndex = node.EnvMaps.FindIndex(x => x.HandShake == precomputedHandshake);

                    Log.Debug(nameof(Scene), $"Topmost calculated envmap doesn't match with the precomputed one" +
                        $" (dists: vrf={vrfDistance} s2={preComputedDistance}) for node at {node.BoundingBox.Center} [{precomputedHandshake}]" +
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

        public void Dispose()
        {
            lightingBuffer?.Dispose();
        }
    }
}
