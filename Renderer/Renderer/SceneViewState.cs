using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer;

/// <summary>Culling results and draw lists for drawing a scene through one view.</summary>
public sealed class SceneViewState : IDisposable
{
    /// <summary>Gets the scene this state draws.</summary>
    public Scene Scene { get; }

    /// <summary>Gets the tile and depth bin cull passes for this view of the scene.</summary>
    public LightBinner LightBinner { get; }

    /// <summary>Gets or sets the PVS bitfield for the cluster this view looks from, empty to cull nothing.</summary>
    public ReadOnlyMemory<byte> Pvs { get; set; }

    /// <summary>Gets whether any material drawn after the framebuffer grab samples the scene color.</summary>
    public bool WantsSceneColor { get; private set; }

    /// <summary>Gets whether anything drawn after the framebuffer grab samples the scene depth.</summary>
    public bool WantsSceneDepth { get; private set; }

    /// <summary>Gets whether there are any selected nodes queued for outline rendering.</summary>
    public bool HasOutlineObjects => renderLists[RenderPass.Outline].Count > 0;

    /// <summary>Gets whether anything is queued to draw into the water effects map this frame.</summary>
    public bool HasWaterEffects => waterEffectsRenderList.Count > 0;

    /// <summary>Gets whether any water surface is queued to draw this frame.</summary>
    public bool HasWater => renderLists[RenderPass.Water].Count > 0;

    internal RenderTexture? DepthPyramid { get; set; }

    internal Matrix4x4 DepthPyramidViewProjection { get; set; }

    internal bool DepthPyramidValid { get; set; }

    private StorageBuffer? indirectDraws;
    private StorageBuffer? compactedDraws;
    private StorageBuffer? compactedCounts;
    private int indirectLayoutVersion = -1;

    private StorageBuffer? pvsHiddenGpu;
    private bool cpuHiddenActive;
    private uint[] pvsHiddenBits = [];

    private readonly List<SceneNode> cullResults = [];
    private int staticCullCount;
    private readonly Plane[] lastFrustumPlanes = new Plane[6];
    private bool hasLastFrustum;
    private int lastOctreeVersion = -1;

    private readonly Dictionary<RenderPass, List<MeshBatchRenderer.Request>> renderLists = new()
    {
        [RenderPass.OpaqueAggregate] = [],
        [RenderPass.OpaqueFragments] = [],
        [RenderPass.Opaque] = [],
        [RenderPass.StaticOverlay] = [],
        [RenderPass.OpaqueRefract] = [],
        [RenderPass.Water] = [],
        [RenderPass.Translucent] = [],
        [RenderPass.Outline] = [],
    };

    private readonly Dictionary<RenderPass, List<MeshBatchRenderer.Request>> viewmodelRenderLists = new()
    {
        [RenderPass.Opaque] = [],
        [RenderPass.Translucent] = [],
    };

    /// <summary>Translucent draws that go to the water effects map instead of the scene.</summary>
    private readonly List<MeshBatchRenderer.Request> waterEffectsRenderList = [];

    /// <summary>Visible nodes that draw themselves, listed once each however many passes they draw in.</summary>
    private readonly List<SceneNode> customBufferNodes = [];

    private readonly HashSet<SceneAggregate> aggregatesWithVisibleFragments = [];

    private readonly Predicate<MeshBatchRenderer.Request> hasNoVisibleFragments;

    private readonly Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>> depthOnlyDraws = Scene.CreateDepthOnlyDrawCallCollection();

    /// <summary>Alpha tested draws, held out of the opaque passes for <see cref="RenderAlphaTestGeometry"/>.</summary>
    private readonly List<MeshBatchRenderer.Request> alphaTestAggregateDraws = [];

    /// <inheritdoc cref="alphaTestAggregateDraws"/>
    private readonly List<MeshBatchRenderer.Request> alphaTestOpaqueDraws = [];

    /// <summary>Initializes a view of an already initialized <paramref name="scene"/>.</summary>
    public SceneViewState(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        Scene = scene;
        hasNoVisibleFragments = request => request.Node is SceneAggregate aggregate && !aggregatesWithVisibleFragments.Contains(aggregate);
        LightBinner = new LightBinner(this);
        LightBinner.LoadShaders();

        // Presizes the lists so the first frames do not grow them
        CollectSceneDrawCalls(new Camera(), Frustum.CreateEmpty());
    }

    /// <summary>
    /// Returns all scene nodes whose bounding boxes intersect the given frustum, caching static results across frames when the frustum is unchanged.
    /// </summary>
    /// <param name="frustum">The view frustum to test against.</param>
    /// <returns>A list of visible scene nodes (valid until the next call to this method).</returns>
    public List<SceneNode> GetFrustumCullResults(Frustum frustum)
    {
        // Optimization: Do not clear static culled results from last frame if the frustum did not change
        if (!hasLastFrustum || !frustum.Planes.AsSpan().SequenceEqual(lastFrustumPlanes) || lastOctreeVersion != Scene.OctreeVersion)
        {
            frustum.Planes.CopyTo(lastFrustumPlanes, 0);
            hasLastFrustum = true;
            lastOctreeVersion = Scene.OctreeVersion;

            cullResults.Clear();
            cullResults.Capacity = Math.Max(cullResults.Capacity, Scene.NodeCount + 100);

            Scene.StaticOctree.Query(frustum, cullResults);
            staticCullCount = cullResults.Count;
        }
        else
        {
            cullResults.RemoveRange(staticCullCount, cullResults.Count - staticCullCount);
        }

        Scene.DynamicOctree.Query(frustum, cullResults);
        return cullResults;
    }

    private void ResetPvsHiddenBits()
    {
        var objectEntryCount = Scene.ObjectEntryCount;

        var pvsCullActive = !Pvs.IsEmpty && Scene.VoxelVisibility != null;
        cpuHiddenActive = (pvsCullActive || Scene.VisibilityBoxes.View.IsActive) && objectEntryCount > 0;

        if (!cpuHiddenActive)
        {
            return;
        }

        var bitWords = MathUtils.DivideRoundUp(objectEntryCount, 32);

        if (pvsHiddenBits.Length < bitWords)
        {
            pvsHiddenBits = new uint[bitWords];
        }

        Array.Clear(pvsHiddenBits, 0, bitWords);
    }

    private void MarkNodeHiddenGpu(SceneNode node)
    {
        if (cpuHiddenActive && node is SceneAggregate.Fragment && node.Id < (uint)Scene.ObjectEntryCount)
        {
            MathUtils.SetBit(pvsHiddenBits, (int)node.Id);
        }
    }

    private void Add(in MeshBatchRenderer.Request request, RenderPass renderPass)
    {
        Debug.Assert(request.Call is not null);

        if (!Scene.ShowToolsMaterials && request.Call.Material.IsToolsMaterial)
        {
            return;
        }

        if (renderPass > RenderPass.DepthOnly && request.Node.IsSelected)
        {
            renderLists[RenderPass.Outline].Add(request);
        }

        // Aggregated geometry is opaque world detail that never samples the scene color, and the refract
        // pass is the one place it cannot go: it has neither the depth prepass nor the indirect draw path.
        var isAggregated = request.Node is SceneAggregate or SceneAggregate.Fragment;
        var readsSceneColor = !isAggregated && request.Call.Material.ReadsSceneColor;

        if (renderPass == RenderPass.OpaqueAggregate)
        {
            if (request.Node is SceneAggregate { CanDrawIndirect: true })
            {
                var material = request.Call.Material;

                if (material.IsOverlay)
                {
                    renderPass = RenderPass.StaticOverlay;
                }
                else if (material is { IsAlphaTest: true, CanPrimeDepth: true })
                {
                    alphaTestAggregateDraws.Add(request);
                    return;
                }
                else if (Scene.EnableDepthPrepass && material.CanPrimeDepth)
                {
                    var bucket = Scene.GetDepthOnlyBucket(request.Call);
                    depthOnlyDraws[bucket].Add(request);
                }
            }
        }

        if (renderPass == RenderPass.OpaqueFragments)
        {
            if (Scene.DrawMeshletsIndirect && request.Node is SceneAggregate.Fragment { Parent.CanDrawIndirect: true })
            {
                return; // Skip individual fragment draws if aggregate can be drawn with indirect draw
            }

            renderPass = RenderPass.Opaque;
        }

        var isViewmodelLayer = (request.Node.RenderPasses & CustomRenderPasses.Viewmodel) != 0
            && viewmodelRenderLists.ContainsKey(renderPass);

        var queueList = isViewmodelLayer
            ? viewmodelRenderLists[renderPass]
            : renderLists[renderPass];

        var isLatePass = renderPass == RenderPass.Translucent;

        if ((readsSceneColor || request.Call.Material.IsCs2Water) && !isViewmodelLayer && renderPass != RenderPass.StaticOverlay)
        {
            queueList = renderLists[request.Call.Material.IsTranslucent
                ? RenderPass.Water
                : RenderPass.OpaqueRefract];

            isLatePass = true;
        }

        // Not the ones rerouted above: those draw after the framebuffer grab, past every depth pass
        if (renderPass == RenderPass.Opaque && !isViewmodelLayer && !isLatePass
            && request.Call.Material is { IsAlphaTest: true, CanPrimeDepth: true })
        {
            queueList = alphaTestOpaqueDraws;
        }

        // Only draws that happen after the grab can make use of the resolved copies.
        if (isLatePass)
        {
            WantsSceneColor |= readsSceneColor;
            WantsSceneDepth |= request.Call.Material.Shader.ReservedTexturesUsed.Contains("g_tSceneDepth");
        }

        queueList.Add(request);
    }

    /// <summary>
    /// Frustum-culls the scene and populates the per-pass render lists for the upcoming frame.
    /// </summary>
    /// <param name="camera">The camera used to sort translucent draw calls by distance.</param>
    /// <param name="cullFrustum">An optional override frustum for culling; defaults to the camera frustum.</param>
    public void CollectSceneDrawCalls(Camera camera, Frustum? cullFrustum = null)
    {
        ArgumentNullException.ThrowIfNull(camera);

        foreach (var bucket in renderLists.Values)
        {
            bucket.Clear();
        }

        foreach (var bucket in viewmodelRenderLists.Values)
        {
            bucket.Clear();
        }

        waterEffectsRenderList.Clear();
        customBufferNodes.Clear();
        alphaTestAggregateDraws.Clear();
        alphaTestOpaqueDraws.Clear();

        foreach (var bucket in depthOnlyDraws.Values)
        {
            bucket.Clear();
        }

        WantsSceneColor = false;
        WantsSceneDepth = false;

        ResetPvsHiddenBits();

        var frustum = cullFrustum ?? camera.ViewFrustum;
        var visibleNodes = GetFrustumCullResults(frustum);
        var pvs = Pvs.Span;

        PerfStats.Active.Count(Counter.SceneObjectInView, visibleNodes.Count);

        aggregatesWithVisibleFragments.Clear();

        // Collect mesh calls
        foreach (var node in visibleNodes)
        {
            if (!node.Visible)
            {
                continue;
            }

            if (!Scene.IsNodeInPvs(node, pvs))
            {
                PerfStats.Active.Count(Counter.SceneObjectCulledByPvs, 1);
                MarkNodeHiddenGpu(node);
                continue;
            }

            // The viewmodel is drawn at the eye, not where it is placed
            var isViewmodel = (node.RenderPasses & CustomRenderPasses.Viewmodel) != 0;

            if (!isViewmodel && Scene.VisibilityBoxes.View.IsCulled(node.BoundingBox))
            {
                PerfStats.Active.Count(Counter.SceneObjectCulledByVisibilityBox, 1);
                MarkNodeHiddenGpu(node);
                continue;
            }

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
                            DistanceFromCamera = node.GetCameraDistance(camera),
                            Node = node,
                        }, RenderPass.Translucent);
                    }
                }
            }
            else if (node is SceneAggregate.Fragment fragment)
            {
                if (!fragment.Parent.IsFragmentInActiveLod(fragment))
                {
                    continue;
                }

                aggregatesWithVisibleFragments.Add(fragment.Parent);
                Add(new MeshBatchRenderer.Request
                {
                    Mesh = fragment.RenderMesh,
                    Call = fragment.DrawCall,
                    Node = node,
                }, RenderPass.OpaqueFragments);
            }
            else if (node is SceneAggregate aggregate)
            {
                if (aggregate.InstanceTransforms.Count > 0)
                {
                    foreach (var call in aggregate.RenderMesh.DrawCallsOpaque)
                    {
                        Add(new MeshBatchRenderer.Request
                        {
                            Mesh = aggregate.RenderMesh,
                            Call = call,
                            Node = node,
                        }, RenderPass.Opaque);
                    }
                }
                else if (Scene.DrawMeshletsIndirect && aggregate.CanDrawIndirect)
                {
                    Add(new MeshBatchRenderer.Request
                    {
                        Mesh = aggregate.RenderMesh,
                        Call = aggregate.RenderMesh.DrawCallsOpaque[0],
                        Node = node,
                    }, RenderPass.OpaqueAggregate);
                }
            }
            else
            {
                if (node is SceneLight light)
                {
                    PerfStats.Active.CountLightInView(light);
                }

                var customRender = new MeshBatchRenderer.Request
                {
                    DistanceFromCamera = node is PhysSceneNode
                        ? 100000f - node.OverlayRenderOrder * 10f
                        : node.GetCameraDistance(camera),
                    Node = node,
                };

                WantsSceneDepth |= node is ParticleSceneNode { WantsSceneDepth: true };

                var customPasses = node.RenderPasses;

                if (customPasses != CustomRenderPasses.None)
                {
                    customBufferNodes.Add(node);
                }

                var customLists = (customPasses & CustomRenderPasses.Viewmodel) != 0
                    ? viewmodelRenderLists
                    : renderLists;

                if ((customPasses & CustomRenderPasses.Opaque) != 0)
                {
                    customLists[RenderPass.Opaque].Add(customRender);
                }

                if ((customPasses & CustomRenderPasses.Translucent) != 0)
                {
                    customLists[RenderPass.Translucent].Add(customRender);
                }

                if ((customPasses & CustomRenderPasses.WaterEffects) != 0)
                {
                    waterEffectsRenderList.Add(customRender);
                }

                if (node.IsSelected)
                {
                    renderLists[RenderPass.Outline].Add(customRender);
                }
            }
        }

        renderLists[RenderPass.OpaqueAggregate].RemoveAll(hasNoVisibleFragments);
        alphaTestAggregateDraws.RemoveAll(hasNoVisibleFragments);

        // avoid buffer updates mid rendering
        foreach (var node in customBufferNodes)
        {
            node.UpdateBuffers(camera);
        }
    }

    private void EnsureIndirectDrawBuffers()
    {
        if (indirectLayoutVersion == Scene.IndirectLayoutVersion)
        {
            return;
        }

        indirectLayoutVersion = Scene.IndirectLayoutVersion;

        indirectDraws?.Delete();
        compactedDraws?.Delete();
        compactedCounts?.Delete();

        indirectDraws = null;
        compactedDraws = null;
        compactedCounts = null;

        if (Scene.IndirectDrawTemplate is not { } template || Scene.CompactedCountsTemplate is not { } counts)
        {
            return;
        }

        indirectDraws = new StorageBuffer(ReservedBufferSlots.AggregateDraws, nameof(ReservedBufferSlots.AggregateDraws));
        indirectDraws.Create(template, BufferUsage.GpuOnly);

        compactedDraws = new StorageBuffer(ReservedBufferSlots.CompactedDraws, nameof(ReservedBufferSlots.CompactedDraws));
        compactedDraws.Create(template, BufferUsage.GpuOnly);

        compactedCounts = new StorageBuffer(ReservedBufferSlots.CompactedCounts, nameof(ReservedBufferSlots.CompactedCounts));
        compactedCounts.Create(counts, BufferUsage.GpuOnly);
    }

    internal void BindIndirectDrawBuffers()
    {
        if (!Scene.DrawMeshletsIndirect)
        {
            return;
        }

        EnsureIndirectDrawBuffers();

        Debug.Assert(indirectDraws is not null);
        Debug.Assert(compactedDraws is not null);

        GL.BindBuffer(BufferTarget.DrawIndirectBuffer, Scene.CompactMeshletDraws
            ? compactedDraws.Handle
            : indirectDraws.Handle);

        if (Scene.CompactMeshletDraws)
        {
            Debug.Assert(compactedCounts is not null);
            GL.BindBuffer(BufferTarget.ParameterBuffer, compactedCounts.Handle);
        }
    }

    /// <summary>
    /// Binds the depth pyramid and sets the constants every occlusion test reads. Shared by the
    /// meshlet cull and the light tile cull so the two cannot test against different state.
    /// </summary>
    /// <param name="shader">The shader whose occlusion uniforms to set. Must already be in use.</param>
    /// <returns>Whether occlusion culling is active this frame.</returns>
    internal bool SetOcclusionUniforms(Shader shader)
    {
        var pyramid = DepthPyramid;
        var enabled = DepthPyramidValid && pyramid != null;

        shader.SetUniform("g_bOcclusionCullEnabled", enabled ? 1 : 0);

        if (!enabled)
        {
            shader.SetTexture(RenderMaterial.TextureUnitStart, "g_tDepthPyramid", Scene.RendererContext.MaterialLoader.GetDefaultMask());
            return false;
        }

        Debug.Assert(pyramid != null);

        shader.SetUniform("g_nDepthPyramidMaxMip", pyramid.NumMipLevels - 1);
        shader.SetUniform("g_nDepthPyramidWidth", pyramid.Width);
        shader.SetUniform("g_nDepthPyramidHeight", pyramid.Height);
        shader.SetUniform("g_flDepthRangeMin", Renderer.DepthRange.Scene.Near);
        shader.SetUniform("g_flDepthRangeMax", Renderer.DepthRange.Scene.Far);
        shader.SetTexture(RenderMaterial.TextureUnitStart, "g_tDepthPyramid", pyramid);

        return true;
    }

    /// <summary>Dispatches the GPU frustum (and optional occlusion) culling compute shader.</summary>
    /// <param name="frustum">The view frustum used to cull meshlets.</param>
    public void MeshletCullGpu(Frustum frustum)
    {
        EnsureIndirectDrawBuffers();

        var scene = Scene;
        var frustumBuffer = scene.FrustumBuffer;
        var cullShader = scene.FrustumCullShader;

        Debug.Assert(frustumBuffer is not null);
        Debug.Assert(cullShader is not null);

        Debug.Assert(scene.DrawBoundsGpu is not null);
        Debug.Assert(scene.MeshletDataGpu is not null);
        Debug.Assert(scene.CommandMeshletsGpu is not null);
        Debug.Assert(indirectDraws is not null);
        Debug.Assert(scene.InstanceBufferGpu is not null);
        Debug.Assert(scene.TransformBufferGpu is not null);
        Debug.Assert(scene.ObjectLodGpu is not null);
        Debug.Assert(scene.ActiveLodBitsGpu is not null);

        frustumBuffer.BindBufferBase();
        frustumBuffer.Data = new(frustum);

        cullShader.Use();

        SetOcclusionUniforms(cullShader);

        scene.MeshletDataGpu.BindBufferBase();
        scene.DrawBoundsGpu.BindBufferBase();
        scene.CommandMeshletsGpu.BindBufferBase();
        indirectDraws.BindBufferBase();

        // Fragments share cull data in model space; instance transforms move it into world space
        scene.InstanceBufferGpu.BindBufferBase();
        scene.TransformBufferGpu.BindBufferBase();

        // Scratch slots, rebound by the compaction and light cull dispatches that follow
        scene.ObjectLodGpu.BindBufferBase();
        scene.ActiveLodBitsGpu.BindBufferBase();

        var pvsWords = cpuHiddenActive ? MathUtils.DivideRoundUp(Math.Max(scene.ObjectEntryCount, 1), 32) : 1;

        if (pvsHiddenBits.Length < pvsWords)
        {
            pvsHiddenBits = new uint[pvsWords];
        }

        pvsHiddenGpu = Scene.Upload<uint>(pvsHiddenGpu, pvsHiddenBits.AsSpan(0, pvsWords),
            ReservedBufferSlots.BufferSlot10, "PvsHidden");
        pvsHiddenGpu.BindBufferBase();

        cullShader.SetUniform("g_bCpuHiddenEnabled", cpuHiddenActive);

        var occlusionDebugEnabled = scene.OcclusionDebugEnabled && scene.OcclusionDebug != null;

        // Bind debug buffer for occluded bounds visualization
        if (occlusionDebugEnabled)
        {
            scene.OcclusionDebug!.BindAndClearBuffer();
        }

        cullShader.SetUniform("g_bOcclusionDebugEnabled", occlusionDebugEnabled);

        var workGroups = MathUtils.DivideRoundUp(scene.SceneMeshletCount, 64);
        GL.DispatchCompute(workGroups, 1, 1);

        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);

        if (occlusionDebugEnabled)
        {
            // Converts occludedCount into a DrawArraysIndirectCommand on the GPU so
            // OcclusionDebugRenderer.Render() can draw without a CPU readback/stall.
            // The barrier above makes occludedCount visible to this dispatch; the
            // CommandBarrierBit barrier in RenderOpaqueLayer covers its own output
            // being visible to the later indirect draw.
            scene.OcclusionDebug!.DispatchFinalize();
        }
    }

    /// <summary>Packs non-zero indirect draw commands together on the GPU to avoid empty draw calls.</summary>
    public void CompactIndirectDraws()
    {
        EnsureIndirectDrawBuffers();

        var compactionShader = Scene.CompactionShader;
        var requests = Scene.CompactionRequestsGpu;

        if (compactionShader == null || indirectDraws == null || compactedDraws == null || compactedCounts == null || requests == null)
        {
            return;
        }

        compactionShader.Use();

        indirectDraws.BindBufferBase();
        compactedDraws.BindBufferBase();
        compactedCounts.BindBufferBase();
        requests.BindBufferBase();

        var aggregateCount = requests.Size / sizeof(uint) / 2; // 2 uints per aggregate
        var workGroups = MathUtils.DivideRoundUp(aggregateCount, 4); // 4 requests per workgroup (local_size_x = 4)
        GL.DispatchCompute(workGroups, 1, 1);
    }

    /// <summary>
    /// Renders the opaque pass, optionally with a depth prepass, followed by aggregate indirect draws and static overlay geometry.
    /// </summary>
    /// <param name="renderContext">The render context for this pass.</param>
    /// <param name="depthOnlyShader">Optional depth-only shader; <see langword="null"/> for a pass that replaces material shaders.</param>
    public void RenderOpaqueLayer(Scene.RenderContext renderContext, Shader? depthOnlyShader = null)
    {
        using var passScope = GraphicsContext.RenderState.Scope();

        var drawIndirect = Scene.DrawMeshletsIndirect;
        var depthPrepass = depthOnlyShader != null && Scene.EnableDepthPrepass;

        if (drawIndirect)
        {
            BindIndirectDrawBuffers();

            // CommandBarrierBit is defined over the indirect buffer only, not the compacted count buffer
            GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit
                | MemoryBarrierFlags.BufferUpdateBarrierBit);
        }

        if (depthPrepass)
        {
            using (new GLDebugGroup("Depth Prepass"))
            using (GraphicsContext.RenderState.Scope(colorWriteMask: RsColorWriteEnableBits.None))
            {
                PerfStats.Active.SuspendTriangleCounter();

                var passShader = renderContext.ReplacementShader;

                renderContext.RenderPass = RenderPass.DepthOnly;
                foreach (var (bucket, calls) in depthOnlyDraws)
                {
                    renderContext.ReplacementShader = bucket == DepthOnlyBucket.Specialized ? depthOnlyShader : null;
                    MeshBatchRenderer.Render(calls, renderContext);
                }

                renderContext.ReplacementShader = passShader;

                PerfStats.Active.ResumeTriangleCounter();
            }

            using (new GLDebugGroup("Opaque Prepassed"))
            using (GraphicsContext.RenderState.Scope(depthWrite: false, depthFunc: RsComparison.Equal))
            {
                renderContext.RenderPass = RenderPass.OpaqueAggregate;
                MeshBatchRenderer.Render(renderLists[renderContext.RenderPass], renderContext);
            }
        }
        else if (drawIndirect)
        {
            using var aggregateGroup = new GLDebugGroup("Aggregate Render");

            renderContext.RenderPass = RenderPass.OpaqueAggregate;
            MeshBatchRenderer.Render(renderLists[renderContext.RenderPass], renderContext);
        }

        using (new GLDebugGroup("Opaque Render"))
        {
            renderContext.RenderPass = RenderPass.Opaque;
            MeshBatchRenderer.Render(renderLists[renderContext.RenderPass], renderContext);
        }

        RenderAlphaTestGeometry(renderContext, depthOnlyShader);

        using (new GLDebugGroup("StaticOverlay Render"))
        {
            renderContext.RenderPass = RenderPass.StaticOverlay;
            MeshBatchRenderer.Render(renderLists[renderContext.RenderPass], renderContext);
        }
    }

    /// <summary>Prepasses the alpha tested geometry and then draws it, both after the opaque geometry.</summary>
    private void RenderAlphaTestGeometry(Scene.RenderContext renderContext, Shader? depthOnlyShader)
    {
        if (alphaTestAggregateDraws.Count == 0 && alphaTestOpaqueDraws.Count == 0)
        {
            return;
        }

        // Both lists are drawn twice, so they are ordered once here instead of per pass.
        alphaTestAggregateDraws.Sort(MeshBatchRenderer.CompareStageThenProgram);
        alphaTestOpaqueDraws.Sort(MeshBatchRenderer.CompareCustomPipeline);

        var prepassed = depthOnlyShader != null;

        if (prepassed)
        {
            using var prepassGroup = new GLDebugGroup("Alpha Test Depth Prepass");
            using var prepassState = GraphicsContext.RenderState.Scope(colorWriteMask: RsColorWriteEnableBits.None);

            PerfStats.Active.SuspendTriangleCounter();

            var passShader = renderContext.ReplacementShader;
            renderContext.ReplacementShader = null;

            renderContext.RenderPass = RenderPass.DepthOnly;
            renderContext.DepthOnlyShader = depthOnlyShader;
            MeshBatchRenderer.Render(alphaTestAggregateDraws, renderContext);
            MeshBatchRenderer.Render(alphaTestOpaqueDraws, renderContext);

            renderContext.DepthOnlyShader = null;
            renderContext.ReplacementShader = passShader;

            PerfStats.Active.ResumeTriangleCounter();
        }

        using var drawGroup = new GLDebugGroup("Alpha Test Render");

        using var drawState = prepassed
            ? GraphicsContext.RenderState.Scope(depthWrite: false, depthFunc: RsComparison.Equal)
            : default;

        renderContext.RenderPass = RenderPass.OpaqueAggregate;
        MeshBatchRenderer.Render(alphaTestAggregateDraws, renderContext);

        renderContext.RenderPass = RenderPass.Opaque;
        MeshBatchRenderer.Render(alphaTestOpaqueDraws, renderContext);
    }

    /// <summary>Renders all translucent draw calls collected during <see cref="CollectSceneDrawCalls"/>.</summary>
    /// <param name="renderContext">The render context for this pass.</param>
    public void RenderTranslucentLayer(Scene.RenderContext renderContext)
    {
        using (new GLDebugGroup("Translucent Render"))
        {
            renderContext.RenderPass = RenderPass.Translucent;
            MeshBatchRenderer.Render(renderLists[RenderPass.Translucent], renderContext);
        }
    }

    /// <summary>
    /// Renders the opaque first-person viewmodel layer collected during <see cref="CollectSceneDrawCalls"/>.
    /// Rendered before the main scene so its reserved near depth range can never be overtaken by world geometry.
    /// </summary>
    /// <param name="renderContext">The render context for this pass, expected to use the dedicated viewmodel camera and depth range.</param>
    public void RenderViewmodelOpaqueLayer(Scene.RenderContext renderContext)
    {
        using var _ = GraphicsContext.RenderState.Scope();

        renderContext.RenderPass = RenderPass.Opaque;
        MeshBatchRenderer.Render(viewmodelRenderLists[RenderPass.Opaque], renderContext);
    }

    /// <summary>
    /// Renders the translucent first-person viewmodel layer collected during <see cref="CollectSceneDrawCalls"/>.
    /// Rendered after the main scene (and 3D sky) translucent passes so it composites correctly on top of them.
    /// </summary>
    /// <param name="renderContext">The render context for this pass, expected to use the dedicated viewmodel camera and depth range.</param>
    public void RenderViewmodelTranslucentLayer(Scene.RenderContext renderContext)
    {
        using var _ = GraphicsContext.RenderState.Scope(depthWrite: false, blend: true);

        renderContext.RenderPass = RenderPass.Translucent;
        MeshBatchRenderer.Render(viewmodelRenderLists[RenderPass.Translucent], renderContext);
    }

    /// <summary>
    /// Renders depth-writing geometry that samples the scene color, collected during <see cref="CollectSceneDrawCalls"/>.
    /// Runs after the framebuffer grab but before water and translucents, so those still sort against its depth.
    /// </summary>
    /// <param name="renderContext">The render context for this pass.</param>
    public void RenderOpaqueRefractLayer(Scene.RenderContext renderContext)
    {
        var requests = renderLists[RenderPass.OpaqueRefract];

        if (requests.Count == 0)
        {
            return;
        }

        using (new GLDebugGroup("Opaque Refract Render"))
        using (GraphicsContext.RenderState.Scope())
        {
            renderContext.RenderPass = RenderPass.OpaqueRefract;
            MeshBatchRenderer.Render(requests, renderContext);
        }
    }

    /// <summary>Renders the draw calls that fill the water effects map; the caller owns the render target.</summary>
    /// <param name="renderContext">The render context for this pass.</param>
    public void RenderWaterEffectsLayer(Scene.RenderContext renderContext)
    {
        renderContext.RenderPass = RenderPass.Translucent;
        renderContext.Layer = RenderLayer.WaterEffects;
        MeshBatchRenderer.Render(waterEffectsRenderList, renderContext);
    }

    /// <summary>Renders water draw calls collected during <see cref="CollectSceneDrawCalls"/>.</summary>
    /// <param name="renderContext">The render context for this pass.</param>
    public void RenderWaterLayer(Scene.RenderContext renderContext)
    {
        var requests = renderLists[RenderPass.Water];

        if (requests.Count == 0)
        {
            return;
        }

        using (new GLDebugGroup("Fancy Water Render"))
        using (GraphicsContext.RenderState.Scope())
        {
            renderContext.RenderPass = RenderPass.Water;
            MeshBatchRenderer.Render(requests, renderContext);
        }
    }

    /// <summary>Renders all selected nodes using the outline shader to produce selection highlights.</summary>
    /// <param name="renderContext">The render context for this pass.</param>
    public void RenderOutlineLayer(Scene.RenderContext renderContext)
    {
        var passShader = renderContext.ReplacementShader;

        renderContext.RenderPass = RenderPass.Outline;
        renderContext.ReplacementShader = Scene.OutlineShader;

        MeshBatchRenderer.Render(renderLists[RenderPass.Outline], renderContext);

        renderContext.ReplacementShader = passShader;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Scene.ShadingLightBinner == LightBinner)
        {
            Scene.ShadingLightBinner = null;
        }

        LightBinner.Dispose();
        indirectDraws?.Delete();
        compactedDraws?.Delete();
        compactedCounts?.Delete();
        pvsHiddenGpu?.Delete();

        indirectDraws = null;
        compactedDraws = null;
        compactedCounts = null;
        pvsHiddenGpu = null;
    }
}
