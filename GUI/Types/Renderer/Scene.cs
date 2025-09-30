using System.Diagnostics;
using System.Linq;
using GUI.Types.GLViewers;
using GUI.Types.Renderer.Buffers;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    partial class Scene : IDisposable
    {
        public readonly struct UpdateContext
        {
            public required GLSceneViewer View { get; init; }
            public required TextRenderer TextRenderer { get; init; }
            public required float Timestep { get; init; }
        }

        public struct RenderContext
        {
            public required Scene Scene { get; set; }
            public required Camera Camera { get; init; }
            public required Framebuffer Framebuffer { get; set; }
            public RenderPass RenderPass { get; set; }
            public Shader? ReplacementShader { get; set; }
            public required List<(ReservedTextureSlots Slot, string Name, RenderTexture Texture)> Textures { get; init; }
        }

        public Dictionary<string, byte> RenderAttributes { get; } = [];
        public WorldLightingInfo LightingInfo { get; }
        public WorldFogInfo FogInfo { get; set; } = new();
        public WorldPostProcessInfo PostProcessInfo { get; set; } = new();

        public Rubikon? PhysicsWorld { get; set; }

        private UniformBuffer<LightingConstants>? lightingBuffer;
        public UniformBuffer<EnvMapArray>? envMapBuffer;
        private UniformBuffer<LightProbeVolumeArray>? lpvBuffer;

        public VrfGuiContext GuiContext { get; }
        public Octree StaticOctree { get; }
        public Octree DynamicOctree { get; }

        public bool ShowToolsMaterials { get; set; }
        public bool FogEnabled { get; set; } = true;
        public bool EnableOcclusionCulling { get; set; }

        public IEnumerable<SceneNode> AllNodes => staticNodes.Concat(dynamicNodes);

        private readonly List<SceneNode> staticNodes = [];
        private readonly List<SceneNode> dynamicNodes = [];

        private Shader? OutlineShader;

        public Scene(VrfGuiContext context, float sizeHint = 32768)
        {
            GuiContext = context;
            StaticOctree = new(sizeHint);
            DynamicOctree = new(sizeHint);

            LightingInfo = new(this);
        }

        public void Initialize()
        {
            UpdateOctrees();
            CreateBuffers();
            CalculateLightProbeBindings();
            CalculateEnvironmentMaps();

            UpdateBuffers();

            OutlineShader = GuiContext.ShaderLoader.LoadShader("vrf.outline");
        }

        public void Add(SceneNode node, bool dynamic)
        {
            var (nodeList, octree, indexOffset) = dynamic
                ? (dynamicNodes, DynamicOctree, 1u)
                : (staticNodes, StaticOctree, 0u);

            nodeList.Add(node);
            node.Id = (uint)nodeList.Count * 2 - indexOffset;

            octree.Insert(node);
        }

        public SceneNode? Find(uint id)
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

        public SceneNode? Find(EntityLump.Entity entity)
        {
            bool IsMatchingEntity(SceneNode node) => node.EntityData == entity;

            return staticNodes.Find(IsMatchingEntity) ?? dynamicNodes.Find(IsMatchingEntity);
        }

        public SceneNode? FindNodeByKeyValue(string keyToFind, string valueToFind)
        {
            bool IsMatchingEntity(SceneNode node)
            {
                if (node.EntityData == null)
                {
                    return false;
                }

                return node.EntityData.Properties.Properties.TryGetValue(keyToFind, out var value)
                    && value.Value is string outString
                    && valueToFind.Equals(outString, StringComparison.OrdinalIgnoreCase);
            }

            return staticNodes.Find(IsMatchingEntity) ?? dynamicNodes.Find(IsMatchingEntity);
        }

        public void Update(Scene.UpdateContext updateContext)
        {
            foreach (var node in staticNodes)
            {
                node.Update(updateContext);
            }

            if (OctreeDirty)
            {
                UpdateOctrees();
                OctreeDirty = false;
            }

            foreach (var node in dynamicNodes)
            {
                var oldBox = node.BoundingBox;
                node.Update(updateContext);

                if (!oldBox.Equals(node.BoundingBox))
                {
                    DynamicOctree.Update(node, oldBox);
                }
            }
        }

        public void CreateBuffers()
        {
            lightingBuffer = new(ReservedBufferSlots.Lighting)
            {
                Data = LightingInfo.LightingData
            };

            envMapBuffer = new(ReservedBufferSlots.EnvironmentMap);
            lpvBuffer = new(ReservedBufferSlots.LightProbe);
        }

        public void UpdateBuffers()
        {
            Debug.Assert(lightingBuffer is not null && envMapBuffer is not null && lpvBuffer is not null);

            lightingBuffer.Update();
            envMapBuffer.Update();
            lpvBuffer.Update();
        }

        public void SetSceneBuffers()
        {
            Debug.Assert(lightingBuffer is not null && envMapBuffer is not null && lpvBuffer is not null);

            lightingBuffer.BindBufferBase();
            envMapBuffer.BindBufferBase();
            lpvBuffer.BindBufferBase();
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

        public bool WantsSceneColor { get; set; }
        public bool WantsSceneDepth { get; set; }

        private readonly Dictionary<RenderPass, List<MeshBatchRenderer.Request>> renderLists = new()
        {
            [RenderPass.Opaque] = [],
            [RenderPass.StaticOverlay] = [],
            [RenderPass.Water] = [],
            [RenderPass.Translucent] = [],
            [RenderPass.Outline] = [],
        };

        private void Add(MeshBatchRenderer.Request request, RenderPass renderPass)
        {
            Debug.Assert(request.Call is not null);

            if (!ShowToolsMaterials && request.Call.Material.IsToolsMaterial)
            {
                return;
            }

            var queueList = renderPass switch
            {
                RenderPass.Opaque => renderLists[RenderPass.Opaque],
                RenderPass.StaticOverlay => renderLists[RenderPass.StaticOverlay],
                RenderPass.Translucent => renderLists[RenderPass.Translucent],
                _ => throw new ArgumentOutOfRangeException(nameof(renderPass), renderPass, "Unhandled render pass")
            };

            if (renderPass == RenderPass.Translucent)
            {
                WantsSceneColor |= request.Call.Material.Shader.ReservedTexuresUsed.Contains("g_tSceneColor");
                WantsSceneDepth |= request.Call.Material.Shader.ReservedTexuresUsed.Contains("g_tSceneDepth");

                if (request.Call.Material.IsCs2Water)
                {
                    queueList = renderLists[RenderPass.Water];
                }
            }

            if (renderPass > RenderPass.DepthOnly && request.Node.IsSelected)
            {
                renderLists[RenderPass.Outline].Add(request);
            }

            queueList.Add(request);
        }

        static float GetCameraDistance(Camera camera, SceneNode node)
        {
            return (node.BoundingBox.Center - camera.Location).LengthSquared();
        }

        public void CollectSceneDrawCalls(Camera camera, Frustum? cullFrustum = null)
        {
            foreach (var bucket in renderLists.Values)
            {
                bucket.Clear();
            }

            WantsSceneColor = false;
            WantsSceneDepth = false;

            cullFrustum ??= camera.ViewFrustum;
            var cullResults = GetFrustumCullResults(cullFrustum);

            // Collect mesh calls
            foreach (var node in cullResults)
            {
                if (node is MeshCollectionNode meshCollection)
                {
                    foreach (var mesh in meshCollection.RenderableMeshes)
                    {
                        foreach (var call in mesh.DrawCallsOpaque)
                        {
                            Add(new MeshBatchRenderer.Request
                            {
                                Mesh = mesh,
                                Call = call,
                                Node = node,
                            }, RenderPass.Opaque);
                        }

                        foreach (var call in mesh.DrawCallsOverlay)
                        {
                            Add(new MeshBatchRenderer.Request
                            {
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
                                Mesh = mesh,
                                Call = call,
                                DistanceFromCamera = GetCameraDistance(camera, node),
                                Node = node,
                            }, RenderPass.Translucent);
                        }
                    }
                }
                else if (node is SceneAggregate.Fragment fragment)
                {
                    Add(new MeshBatchRenderer.Request
                    {
                        Mesh = fragment.RenderMesh,
                        Call = fragment.DrawCall,
                        Node = node,
                    }, RenderPass.Opaque);
                }
                else if (node is SceneAggregate aggregate)
                {
                    if (aggregate.InstanceTransforms.Count > 0)
                    {
                        Add(new MeshBatchRenderer.Request
                        {
                            Mesh = aggregate.RenderMesh,
                            Call = aggregate.RenderMesh.DrawCallsOpaque[0],
                            Node = node,
                        }, RenderPass.Opaque);
                    }
                }
                else
                {
                    var customRender = new MeshBatchRenderer.Request
                    {
                        DistanceFromCamera = node is PhysSceneNode
                            ? 100000f - node.OverlayRenderOrder * 10f
                            : GetCameraDistance(camera, node),
                        Node = node,
                    };

                    renderLists[RenderPass.Opaque].Add(customRender);
                    renderLists[RenderPass.Translucent].Add(customRender);

                    if (node.IsSelected)
                    {
                        renderLists[RenderPass.Outline].Add(customRender);
                    }
                }
            }
        }

        private List<SceneNode> CulledShadowNodes { get; } = [];
        private readonly List<RenderableMesh> listWithSingleMesh = [null!];
        private Dictionary<DepthOnlyProgram, List<MeshBatchRenderer.Request>> CulledShadowDrawCalls { get; } = new()
        {
            [DepthOnlyProgram.Static] = [],
            [DepthOnlyProgram.StaticAlphaTest] = [],
            [DepthOnlyProgram.Animated] = [],
            [DepthOnlyProgram.AnimatedEightBones] = [],
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

            if (!LightingInfo.HasBakedShadowsFromLightmap)
            {
                StaticOctree.Root.Query(LightingInfo.SunLightFrustum, CulledShadowNodes);
            }

            DynamicOctree.Root.Query(LightingInfo.SunLightFrustum, CulledShadowNodes);

            foreach (var node in CulledShadowNodes)
            {
                List<RenderableMesh> meshes;

                if (node is MeshCollectionNode meshCollection)
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
                        if (opaqueCall.Material.DoNotCastShadows)
                        {
                            continue;
                        }

                        var bucket = (opaqueCall.Material.IsAlphaTest, animated) switch
                        {
                            (false, false) => DepthOnlyProgram.Static,
                            (true, _) => DepthOnlyProgram.StaticAlphaTest,
                            (false, true) => DepthOnlyProgram.Animated,
                        };

                        if (mesh.BoneWeightCount > 4)
                        {
                            bucket = DepthOnlyProgram.AnimatedEightBones;
                        }

                        CulledShadowDrawCalls[bucket].Add(new MeshBatchRenderer.Request
                        {
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
                MeshBatchRenderer.Render(renderLists[renderContext.RenderPass], renderContext);
            }

            using (new GLDebugGroup("StaticOverlay Render"))
            {
                renderContext.RenderPass = RenderPass.StaticOverlay;
                MeshBatchRenderer.Render(renderLists[renderContext.RenderPass], renderContext);
            }
        }

        private bool occlusionDirty;

        static void ClearOccludedStateRecursive(Octree.Node node)
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
            if (!EnableOcclusionCulling)
            {
                return;
            }

            occlusionDirty = true;

            GL.ColorMask(false, false, false, false);
            GL.DepthMask(false);
            GL.Disable(EnableCap.CullFace);

            depthOnlyShader.Use();
            GL.BindVertexArray(GuiContext.MeshBufferCache.EmptyVAO);

            var maxTests = 1024;
            var startDepth = 3;
            var maxDepth = 8;
            TestOctantsRecursive(StaticOctree.Root, renderContext.Camera.Location, ref maxTests, startDepth, maxDepth);

            GL.UseProgram(0);
            GL.BindVertexArray(0);

            GL.ColorMask(true, true, true, true);
            GL.DepthMask(true);
            GL.Enable(EnableCap.CullFace);
        }

        private static void TestOctantsRecursive(Octree.Node parentNode, Vector3 cameraPosition, ref int maxTests, int startDepth, int maxDepth)
        {
            Span<bool> testChildren = stackalloc bool[8];

            if (maxTests < 0 || maxDepth < 0)
            {
                return;
            }

            for (var i = 0; i < parentNode.Children.Length; i++)
            {
                var node = parentNode.Children[i];
                if (node.FrustumCulled)
                {
                    node.OcclusionCulled = false;
                    node.OcculsionQuerySubmitted = false;
                    continue;
                }

                // This skips leaf octants, not sure if helps
                // if (!node.HasChildren)
                // {
                //     continue;
                // }

                if (node.Region.Intersects(new AABB(cameraPosition, 4f)))
                {
                    // if the camera is inside the octant, we can skip the occlusion test, however we still need to test the children
                    testChildren[i] = true;
                    node.OcclusionCulled = false;
                    node.OcculsionQuerySubmitted = false;
                    continue;
                }

                if (!node.OcclusionCulled)
                {
                    testChildren[i] = true;
                }

                if (startDepth > 0)
                {
                    continue;
                }

                // Queried on a previous frame, waiting for result
                if (node.OcculsionQuerySubmitted)
                {
                    //TryGetOcclusionTestResult(node);
                    continue;
                }

                // Octree node passed frustum test, contains subregions, and was not waiting for a previous query
                maxTests = SubmitOctreeNodeQuery(maxTests, maxDepth, node);
            }

            for (var i = 0; i < parentNode.Children.Length; i++)
            {
                if (testChildren[i])
                {
                    TestOctantsRecursive(parentNode.Children[i], cameraPosition, ref maxTests, startDepth - 1, maxDepth - 1);
                }
            }
        }

        private static int SubmitOctreeNodeQuery(int maxTests, int maxDepth, Octree.Node octreeNode)
        {
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

#if DEBUG
            GL.VertexAttribI2(1, maxDepth, maxTests);
#endif

            GL.BeginQuery(QueryTarget.AnySamplesPassedConservative, octreeNode.OcclusionQueryHandle);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
            GL.EndQuery(QueryTarget.AnySamplesPassedConservative);

            return maxTests;
        }

        public void GetOcclusionTestResults()
        {
            if (!occlusionDirty)
            {
                return;
            }

            if (!EnableOcclusionCulling)
            {
                ClearOccludedStateRecursive(StaticOctree.Root);
                occlusionDirty = false;
                LastFrustum = -1;
                return;
            }

            static void CheckOcclusionQueries(Octree.Node root)
            {
                foreach (var child in root.Children)
                {
                    if (child.OcculsionQuerySubmitted)
                    {
                        TryGetOcclusionTestResult(child);
                    }

                    if (child.HasChildren)
                    {
                        CheckOcclusionQueries(child);
                    }
                }
            }

            CheckOcclusionQueries(StaticOctree.Root);
        }

        private static bool TryGetOcclusionTestResult(Octree.Node node)
        {
            var visible = -1;
            GL.GetQueryObject(
                node.OcclusionQueryHandle,
                GetQueryObjectParam.QueryResultNoWait,
                out visible
            );

            node.OcclusionCulled = visible == 0;
            node.OcculsionQuerySubmitted = visible == -1;
            return visible != -1;
        }

        public void RenderTranslucentLayer(RenderContext renderContext)
        {
            using (new GLDebugGroup("Translucent Render"))
            {
                renderContext.RenderPass = RenderPass.Translucent;
                MeshBatchRenderer.Render(renderLists[RenderPass.Translucent], renderContext);
            }
        }

        public void RenderWaterLayer(RenderContext renderContext)
        {
            using (new GLDebugGroup("Fancy Water Render"))
            {
                renderContext.RenderPass = RenderPass.Water;
                MeshBatchRenderer.Render(renderLists[RenderPass.Water], renderContext);
            }
        }

        public void RenderOutlineLayer(RenderContext renderContext)
        {
            renderContext.RenderPass = RenderPass.Outline;
            renderContext.ReplacementShader = OutlineShader;

            MeshBatchRenderer.Render(renderLists[RenderPass.Outline], renderContext);

            renderContext.ReplacementShader = null;
        }

        public void SetEnabledLayers(HashSet<string> layers)
        {
            foreach (var renderer in AllNodes)
            {
                if (renderer.LayerName == null)
                {
                    renderer.LayerEnabled = false;
                    continue;
                }

                if (renderer.LayerName.StartsWith("LightProbeGrid", StringComparison.Ordinal))
                {
                    continue;
                }

                renderer.LayerEnabled = layers.Contains(renderer.LayerName);
            }
        }

        public bool OctreeDirty { get; set; }

        public void UpdateOctrees()
        {
            LastFrustum = -1;
            StaticOctree.Clear();
            DynamicOctree.Clear();

            foreach (var node in staticNodes)
            {
                if (node.LayerEnabled)
                {
                    StaticOctree.Insert(node);
                }
            }

            foreach (var node in dynamicNodes)
            {
                if (node.LayerEnabled)
                {
                    DynamicOctree.Insert(node);
                }
            }
        }

        public void SetFogConstants(ViewConstants viewConstants)
        {
            FogInfo.SetFogUniforms(viewConstants, FogEnabled);
        }

        public void CalculateLightProbeBindings()
        {
            Debug.Assert(lpvBuffer is not null);

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

            var i = 0;
            foreach (var probe in sortedLightProbes)
            {
                StaticOctree.Root.Query(probe.BoundingBox, nodes);
                DynamicOctree.Root.Query(probe.BoundingBox, nodes); // TODO: This should actually be done dynamically

                foreach (var node in nodes)
                {
                    node.LightProbeBinding ??= probe;
                }

                probe.ShaderIndex = i;
                var data = probe.CalculateGpuProbeData(LightingInfo.LightProbeType == LightProbeType.ProbeAtlas);
                lpvBuffer.Data.Probes[i] = data;

                nodes.Clear();
                i++;
            }

            // Assign random probe to any node that does not have any light probes to fix the flickering,
            // this isn't ideal, and a proper fix would be to remove D_BAKED_LIGHTING_FROM_PROBE from the shader
            var firstProbe = LightingInfo.LightProbes[0];

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

            int IndoorPriorityCompare(SceneEnvMap a, SceneEnvMap b) => b.IndoorOutdoorLevel.CompareTo(a.IndoorOutdoorLevel);
            int HandShakeCompare(SceneEnvMap a, SceneEnvMap b) => a.HandShake.CompareTo(b.HandShake);

            LightingInfo.EnvMaps.Sort(LightingInfo.CubemapType switch
            {
                CubemapType.CubemapArray => IndoorPriorityCompare,
                _ => HandShakeCompare
            });

            var nodes = new List<SceneNode>();
            var i = 0;

            foreach (var envMap in LightingInfo.EnvMaps)
            {
                if (i >= EnvMapArray.MAX_ENVMAPS)
                {
                    Log.Error(nameof(WorldLoader), $"Envmap array index {i} is too large, skipping! Max: {EnvMapArray.MAX_ENVMAPS}");
                    continue;
                }

                StaticOctree.Root.Query(envMap.BoundingBox, nodes);
                DynamicOctree.Root.Query(envMap.BoundingBox, nodes); // TODO: This should actually be done dynamically

                foreach (var node in nodes)
                {
                    node.EnvMaps.Add(envMap);
                }

                UpdateGpuEnvmapData(envMap, i);
                envMap.ShaderIndex = i;
                i++;

                nodes.Clear();
            }

            foreach (var node in AllNodes)
            {
                var precomputedHandshake = node.CubeMapPrecomputedHandshake;
                SceneEnvMap? preComputed = default;

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
                    if (LightingInfo.LightmapGameVersionNumber <= 1)
                    {
                        node.EnvMaps.Clear();
                        foreach (var envMap in LightingInfo.EnvMaps)
                        {
                            if (envMap.BoundingBox.Contains(lightingOrigin))
                            {
                                node.EnvMaps.Add(envMap);
                            }
                        }
                    }
                    else if (LightingInfo.LightmapGameVersionNumber >= 2)
                    {
                        // CS2 Mapping docs say that the lighting origin should point at an exact cubemap.
                        foreach (var envMap in LightingInfo.EnvMaps)
                        {
                            if ((envMap.Transform.Translation - lightingOrigin).LengthSquared() < 0.01f)
                            {
                                node.EnvMaps.Clear();
                                node.EnvMaps.Add(envMap);
                                break;
                            }
                        }
                    }
                }

                node.EnvMaps.Sort((a, b) =>
                {
                    var result = b.IndoorOutdoorLevel.CompareTo(a.IndoorOutdoorLevel);
                    if (result != 0)
                    {
                        return result;
                    }

                    var aDistance = Vector3.Distance(node.BoundingBox.Center, a.BoundingBox.Center);
                    var bDistance = Vector3.Distance(node.BoundingBox.Center, b.BoundingBox.Center);

                    return aDistance.CompareTo(bDistance);
                });

                node.ShaderEnvMapVisibility = node.ShaderEnvMapVisibility.Store(node.EnvMaps);

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
                    node.EnvMaps = []; // no longer needed
                }
            }
        }

        private void UpdateGpuEnvmapData(SceneEnvMap envMap, int index)
        {
            Debug.Assert(envMapBuffer is not null);

            if (!Matrix4x4.Invert(envMap.Transform, out var worldToLocal))
            {
                throw new InvalidOperationException("Matrix invert failed");
            }

            var boundsExtend = new Vector3(0.02f);

            envMapBuffer.Data.EnvMaps[index] = new EnvMapData
            {
                WorldToLocal = worldToLocal,
                BoxMins = envMap.LocalBoundingBox.Min - boundsExtend,
                ArrayIndex = (uint)envMap.ArrayIndex,
                BoxMaxs = envMap.LocalBoundingBox.Max + boundsExtend,
                InvEdgeWidth = new Vector4(Vector3.One / (envMap.EdgeFadeDists + boundsExtend), 0),
                Origin = envMap.Transform.Translation,
                ProjectionType = (uint)envMap.ProjectionMode,
                Color = envMap.Tint,
                NormalizationSH = new Vector4(0, 0, 0, 1)
            };
        }

        public void Dispose()
        {
            lightingBuffer?.Dispose();
            lpvBuffer?.Dispose();
            envMapBuffer?.Dispose();
        }
    }
}
