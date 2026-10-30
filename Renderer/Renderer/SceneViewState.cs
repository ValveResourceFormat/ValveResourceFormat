using System.Diagnostics;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer;

/// <summary>Culling results and draw lists for drawing a scene through one view.</summary>
public sealed class SceneViewState
{
    /// <summary>Gets the scene this state draws.</summary>
    public Scene Scene { get; }

    /// <summary>Gets the tile and depth bin cull passes for this view of the scene.</summary>
    public LightBinner LightBinner => lightBinner ?? throw new InvalidOperationException("A shadow caster view has no light binner.");

    /// <summary>Gets or sets the PVS bitfield this view culls with, empty to cull nothing.</summary>
    public ReadOnlyMemory<byte> Pvs { get; set; }

    /// <summary>Gets the <c>info_visibility_box</c> volumes this view culls with, split for its eye.</summary>
    public VisibilityBoxCuller VisibilityBoxes { get; } = new();

    /// <summary>Gets whether anything drawn after the framebuffer grab samples the scene color.</summary>
    public bool WantsSceneColor { get; private set; }

    /// <summary>Gets whether anything drawn after the framebuffer grab samples the scene depth.</summary>
    public bool WantsSceneDepth { get; private set; }

    /// <summary>Gets whether anything is queued to draw into the water effects map this frame.</summary>
    public bool HasWaterEffects => waterEffectsRenderList.Count > 0;

    /// <summary>Gets whether anything is queued to draw into the effects bloom map this frame.</summary>
    public bool HasEffectsBloom => effectsBloomRenderList.Count > 0;

    /// <summary>Gets whether anything is queued to draw in the screen-space layer this frame.</summary>
    public bool HasScreenSpaceEffects => screenSpaceRenderList.Count > 0;

    /// <summary>Gets whether any water surface is queued to draw this frame.</summary>
    public bool HasWater => renderLists[RenderPass.Water].Count > 0;

    /// <summary>Gets whether the translucent layer has shapes for <see cref="RenderOrderIndependentFills"/> this frame.</summary>
    internal bool HasOrderIndependentFills => orderIndependentFills.Count > 0;

    internal RenderTexture? DepthPyramid { get; set; }

    internal Matrix4x4 DepthPyramidViewProjection { get; set; }

    internal bool DepthPyramidValid { get; set; }

    private readonly LightBinner? lightBinner;

    private readonly bool collectsShadowCasters;

    private StorageBuffer? indirectDraws;
    private StorageBuffer? compactedDraws;
    private StorageBuffer? compactedCounts;
    private int indirectLayoutVersion = -1;

    private StorageBuffer? cpuVisibleGpu;
    private bool cpuVisibleActive;
    private uint[] cpuVisibleBits = [];

    // A caster view draws the aggregates of the fragments it marked whole, culled on the GPU against this
    private readonly Frustum casterCullFrustum = new();
    private bool hasGpuCulledCasters;

    private readonly List<SceneNode> cullResults = [];
    private int staticCullCount;
    private readonly Plane[] lastFrustumPlanes = new Plane[6];
    private bool hasLastFrustum;
    private bool lastIncludedStatic;
    private int lastOctreeVersion = -1;
    private readonly List<(VisibilityBox Box, int Placement, bool Inside)> lastBoxSplit = [];
    private ReadOnlyMemory<byte> lastPvs;

    private readonly Dictionary<RenderPass, List<MeshBatchRenderer.Request>> renderLists = new()
    {
        [RenderPass.OpaqueAggregate] = [],
        [RenderPass.OpaqueFragments] = [],
        [RenderPass.Opaque] = [],
        [RenderPass.StaticOverlay] = [],
        [RenderPass.OpaqueRefract] = [],
        [RenderPass.Water] = [],
        [RenderPass.Translucent] = [],
    };

    private readonly Dictionary<RenderPass, List<MeshBatchRenderer.Request>> viewmodelRenderLists = new()
    {
        [RenderPass.Opaque] = [],
        [RenderPass.Translucent] = [],
    };

    /// <summary>Translucent draws that go to the water effects map instead of the scene.</summary>
    private readonly List<MeshBatchRenderer.Request> waterEffectsRenderList = [];
    private readonly List<MeshBatchRenderer.Request> effectsBloomRenderList = [];
    private readonly List<MeshBatchRenderer.Request> toolsRenderList = [];

    /// <summary>
    /// Gets the nodes the last culling found that draw in the tools pass, which the editor draws
    /// over the final image.
    /// </summary>
    public IReadOnlyList<MeshBatchRenderer.Request> ToolsRenderList => toolsRenderList;

    /// <summary>Draws that go over the finished scene through the screen-space view.</summary>
    private readonly List<MeshBatchRenderer.Request> screenSpaceRenderList = [];

    /// <summary>Translucent shapes whose fills skip the sorted translucent layer when it draws them order independently.</summary>
    private readonly List<ShapeSceneNode> orderIndependentFills = [];

    /// <summary>Visible nodes that draw themselves, listed once each however many passes they draw in.</summary>
    private readonly List<SceneNode> customBufferNodes = [];

    private readonly HashSet<SceneAggregate> aggregatesWithVisibleFragments = [];

    private readonly Predicate<MeshBatchRenderer.Request> hasNoVisibleFragments;

    private readonly Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>> depthOnlyDraws = CreateDepthOnlyBuckets();

    // A bit per node ID instead of draw lists, as there can be hundreds of barn light faces
    private uint[] visibleBits = [];
    private int visibleWords;

    private VisibleNodeEnumerator EnumerateVisibleNodes() => new(Scene, visibleBits.AsSpan(0, visibleWords));

    private ref struct VisibleNodeEnumerator(Scene scene, ReadOnlySpan<uint> bits)
    {
        private readonly ReadOnlySpan<uint> bits = bits;
        private int word = -1;
        private uint remaining;

        public SceneNode Current { get; private set; } = null!;

        public readonly VisibleNodeEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            while (remaining == 0)
            {
                if (++word >= bits.Length)
                {
                    return false;
                }

                remaining = bits[word];
            }

            var id = word * MathUtils.BitsPerWord + BitOperations.TrailingZeroCount(remaining);
            remaining &= remaining - 1;

            Current = scene.GetNodeById((uint)id);
            return true;
        }
    }

    private readonly List<RenderableMesh> singleMesh = [null!];

    /// <summary>Alpha tested draws, held out of the opaque passes for <see cref="RenderAlphaTestGeometry"/>.</summary>
    private readonly List<MeshBatchRenderer.Request> alphaTestAggregateDraws = [];

    /// <inheritdoc cref="alphaTestAggregateDraws"/>
    private readonly List<MeshBatchRenderer.Request> alphaTestOpaqueDraws = [];

    /// <summary>Opaque draws that cannot be primed, drawn after the primed ones while the depth prepass is on.</summary>
    private readonly List<MeshBatchRenderer.Request> unprimedOpaqueDraws = [];

    /// <inheritdoc cref="unprimedOpaqueDraws"/>
    private readonly List<MeshBatchRenderer.Request> viewmodelUnprimedOpaqueDraws = [];

    /// <summary>The viewmodel draws of the depth prepass.</summary>
    private readonly Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>> viewmodelDepthOnlyDraws = CreateDepthOnlyBuckets();

    /// <summary>Surfaces drawn over the resolved opaque scene, for <see cref="RenderTranslucentDepthLayer"/>.</summary>
    private readonly List<MeshBatchRenderer.Request> translucentDepthDraws = [];

    /// <summary>The draws of the nodes given to <see cref="RenderNodes"/>.</summary>
    private readonly List<MeshBatchRenderer.Request> nodeDraws = [];

    /// <summary>Gets whether any surface is queued for <see cref="RenderTranslucentDepthLayer"/> this frame.</summary>
    public bool HasTranslucentDepthDraws => translucentDepthDraws.Count > 0;

    // Mesh surfaces only: particles draw themselves and never reach this list, and additive glows hide nothing behind them
    private static bool WritesTranslucentDepth(RenderMaterial material)
        => material is { IsCs2Water: false, CanPrimeDepth: true, IsAdditive: false };

    /// <summary>Initializes a camera view of an already initialized <paramref name="scene"/>.</summary>
    public SceneViewState(Scene scene) : this(scene, shadowCasters: false)
    {
    }

    internal SceneViewState(Scene scene, bool shadowCasters)
    {
        ArgumentNullException.ThrowIfNull(scene);

        Scene = scene;
        hasNoVisibleFragments = request => request.Node is SceneAggregate aggregate && !aggregatesWithVisibleFragments.Contains(aggregate);
        collectsShadowCasters = shadowCasters;

        if (shadowCasters)
        {
            return;
        }

        lightBinner = new LightBinner(this);
        lightBinner.LoadShaders();

        // Presizes the lists so the first frames do not grow them
        CollectSceneDrawCalls(new Camera(), Frustum.CreateEmpty());
    }

    /// <summary>
    /// Returns all scene nodes whose bounding boxes intersect the given frustum, caching static results across frames when the frustum is unchanged.
    /// </summary>
    /// <param name="frustum">The view frustum to test against.</param>
    /// <param name="includeStatic">Whether to include static nodes.</param>
    /// <param name="boxes">Visibility boxes that leave out whole cells of static nodes, or <see langword="null"/>. <see cref="Pvs"/> leaves out cells too.</param>
    /// <returns>A list of visible scene nodes (valid until the next call to this method).</returns>
    public List<SceneNode> GetFrustumCullResults(Frustum frustum, bool includeStatic = true, VisibilityBoxCuller? boxes = null)
    {
        // Optimization: Do not clear static culled results from last frame if the frustum did not change
        if (!hasLastFrustum || lastIncludedStatic != includeStatic || !frustum.Planes.AsSpan().SequenceEqual(lastFrustumPlanes)
            || lastOctreeVersion != Scene.OctreeVersion || !(boxes?.MatchesSplit(lastBoxSplit) ?? lastBoxSplit.Count == 0)
            || !lastPvs.Equals(Pvs))
        {
            frustum.Planes.CopyTo(lastFrustumPlanes, 0);
            hasLastFrustum = true;
            lastIncludedStatic = includeStatic;
            lastOctreeVersion = Scene.OctreeVersion;
            lastPvs = Pvs;

            if (boxes != null)
            {
                boxes.CopySplitTo(lastBoxSplit);
            }
            else
            {
                lastBoxSplit.Clear();
            }

            cullResults.Clear();

            // Only camera views see enough of the scene to size for all of it
            if (!collectsShadowCasters)
            {
                cullResults.Capacity = Math.Max(cullResults.Capacity, Scene.NodeCount + 100);
            }

            if (includeStatic)
            {
                Scene.StaticOctree.Query(frustum, boxes, Pvs.Span, cullResults);
            }

            staticCullCount = cullResults.Count;
        }
        else
        {
            cullResults.RemoveRange(staticCullCount, cullResults.Count - staticCullCount);
        }

        Scene.DynamicOctree.Query(frustum, cullResults);
        return cullResults;
    }

    private enum NodeCull
    {
        None,
        Hidden,
        Pvs,
        VisibilityBox,
    }

    private NodeCull TestNodeCull(SceneNode node, ReadOnlySpan<byte> pvs, VisibilityBoxCuller? boxes)
    {
        if (!node.Visible)
        {
            return NodeCull.Hidden;
        }

        if (!Scene.IsNodeInPvs(node, pvs))
        {
            return NodeCull.Pvs;
        }

        if (boxes != null && boxes.IsCulled(node.BoundingBox))
        {
            return NodeCull.VisibilityBox;
        }

        return NodeCull.None;
    }

    // While active, the GPU only draws the fragments marked here. The query leaves out whole cells that PVS
    // or visibility boxes hide, so a fragment it never reaches has to stay hidden rather than be drawn.
    private void ResetCpuVisibleBits(bool active)
    {
        var objectEntryCount = Scene.ObjectEntryCount;

        cpuVisibleActive = active && objectEntryCount > 0;

        if (!cpuVisibleActive)
        {
            return;
        }

        var bitWords = MathUtils.DivideRoundUp(objectEntryCount, 32);

        if (cpuVisibleBits.Length < bitWords)
        {
            cpuVisibleBits = new uint[bitWords];
        }

        Array.Clear(cpuVisibleBits, 0, bitWords);
    }

    private void MarkFragmentVisibleGpu(SceneNode node)
    {
        if (cpuVisibleActive && node is SceneAggregate.Fragment && node.Id < (uint)Scene.ObjectEntryCount)
        {
            MathUtils.SetBit(cpuVisibleBits, (int)node.Id);
        }
    }

    private void Add(in MeshBatchRenderer.Request request, RenderPass renderPass)
    {
        Debug.Assert(request.Call is not null);

        if (!Scene.IsDrawn(request.Call.Material, request.Node))
        {
            return;
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
                else if (Scene.Settings.EnableDepthPrepass && material.CanPrimeDepth)
                {
                    depthOnlyDraws[GetDepthOnlyBucket(request.Call)].Add(request);
                }
            }
        }

        if (renderPass == RenderPass.OpaqueFragments)
        {
            // Skip individual fragment draws if aggregate can be drawn with indirect draw
            if (IsDrawnByAggregate(request.Node))
            {
                return;
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
        if (renderPass == RenderPass.Opaque && !isLatePass)
        {
            var material = request.Call.Material;

            if (!isViewmodelLayer && material is { IsAlphaTest: true, CanPrimeDepth: true })
            {
                queueList = alphaTestOpaqueDraws;
            }
            else if (Scene.Settings.EnableDepthPrepass)
            {
                if (!material.CanPrimeDepth)
                {
                    queueList = isViewmodelLayer ? viewmodelUnprimedOpaqueDraws : unprimedOpaqueDraws;
                }
                else
                {
                    var depthDraws = isViewmodelLayer ? viewmodelDepthOnlyDraws : depthOnlyDraws;
                    depthDraws[GetDepthOnlyBucket(request.Call)].Add(request);
                }
            }
        }

        if (isLatePass && !isViewmodelLayer && WritesTranslucentDepth(request.Call.Material))
        {
            translucentDepthDraws.Add(request);
        }

        // Only draws that happen after the grab can make use of the resolved copies.
        if (isLatePass)
        {
            WantsSceneColor |= readsSceneColor;
        }

        // A draw before the grab reads the depth copy of the previous frame
        WantsSceneDepth |= request.Call.Material.Shader.ReservedTexturesUsed.Contains("g_tSceneDepth");

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
        effectsBloomRenderList.Clear();
        screenSpaceRenderList.Clear();
        orderIndependentFills.Clear();
        toolsRenderList.Clear();
        customBufferNodes.Clear();
        alphaTestAggregateDraws.Clear();
        alphaTestOpaqueDraws.Clear();
        unprimedOpaqueDraws.Clear();
        viewmodelUnprimedOpaqueDraws.Clear();
        translucentDepthDraws.Clear();

        foreach (var bucket in depthOnlyDraws.Values)
        {
            bucket.Clear();
        }

        foreach (var bucket in viewmodelDepthOnlyDraws.Values)
        {
            bucket.Clear();
        }

        WantsSceneColor = false;
        WantsSceneDepth = Scene.ProjectedDecals.Count > 0;

        ResetCpuVisibleBits((!Pvs.IsEmpty && Scene.VoxelVisibility != null) || VisibilityBoxes.IsActive);

        var frustum = cullFrustum ?? camera.ViewFrustum;
        var visibleNodes = GetFrustumCullResults(frustum, boxes: VisibilityBoxes);
        var pvs = Pvs.Span;

        PerfStats.Active.Count(Counter.SceneObjectInView, visibleNodes.Count);

        aggregatesWithVisibleFragments.Clear();

        // Collect mesh calls
        foreach (var node in visibleNodes)
        {
            // The viewmodel is drawn at the eye, not where it is placed
            var boxes = (node.RenderPasses & CustomRenderPasses.Viewmodel) != 0 ? null : VisibilityBoxes;

            switch (TestNodeCull(node, pvs, boxes))
            {
                case NodeCull.Hidden:
                    continue;

                case NodeCull.Pvs:
                    PerfStats.Active.Count(Counter.SceneObjectCulledByPvs, 1);
                    continue;

                case NodeCull.VisibilityBox:
                    PerfStats.Active.Count(Counter.SceneObjectCulledByVisibilityBox, 1);
                    continue;
            }

            MarkFragmentVisibleGpu(node);

            if (node is MeshCollectionNode meshCollection)
            {
                if ((node.RenderPasses & CustomRenderPasses.Default) == 0)
                {
                    continue;
                }

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
            else if (node is ShapeSceneNode { ToolsMaterial: { } toolsMaterial } && !Scene.IsDrawn(toolsMaterial, node))
            {
                // A shape standing for a tools material is shown as the material would be
                continue;
            }
            else
            {
                if (node is SceneLight light)
                {
                    PerfStats.Active.CountLightInView(light);
                }

                var customRender = new MeshBatchRenderer.Request
                {
                    // Volumes that span the map, which no sort draws correctly, keep the order they are given
                    DistanceFromCamera = node is ShapeSceneNode { HasOrderIndependentFill: true }
                        ? 100000f - node.OverlayRenderOrder * 10f
                        : node.GetCameraDistance(camera),
                    Node = node,
                };

                WantsSceneDepth |= node is ParticleSceneNode { WantsSceneDepth: true };
                WantsSceneColor |= node is ParticleSceneNode { WantsSceneColor: true };

                var customPasses = node.RenderPasses;

                if (customPasses != CustomRenderPasses.None)
                {
                    customBufferNodes.Add(node);
                }

                var isViewmodel = (customPasses & CustomRenderPasses.Viewmodel) != 0;
                var customLists = isViewmodel ? viewmodelRenderLists : renderLists;

                if ((customPasses & CustomRenderPasses.Opaque) != 0)
                {
                    var opaqueList = (Scene.Settings.EnableDepthPrepass, isViewmodel) switch
                    {
                        (false, _) => customLists[RenderPass.Opaque],
                        (true, false) => unprimedOpaqueDraws,
                        (true, true) => viewmodelUnprimedOpaqueDraws,
                    };

                    opaqueList.Add(customRender);
                }

                if ((customPasses & CustomRenderPasses.Translucent) != 0)
                {
                    customLists[RenderPass.Translucent].Add(customRender);

                    if (node is ShapeSceneNode { HasOrderIndependentFill: true } shape)
                    {
                        orderIndependentFills.Add(shape);
                    }
                }

                if ((customPasses & CustomRenderPasses.WaterEffects) != 0)
                {
                    waterEffectsRenderList.Add(customRender);
                }

                if ((customPasses & CustomRenderPasses.EffectsBloom) != 0)
                {
                    effectsBloomRenderList.Add(customRender);
                }

                if ((customPasses & CustomRenderPasses.ScreenSpace) != 0)
                {
                    screenSpaceRenderList.Add(customRender);
                }

                if ((customPasses & CustomRenderPasses.Tools) != 0)
                {
                    toolsRenderList.Add(customRender);
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

    internal static Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>> CreateDepthOnlyBuckets()
        => Enum.GetValues<DepthOnlyBucket>().ToDictionary(static bucket => bucket, static _ => new List<MeshBatchRenderer.Request>());

    internal (float Min, float Max) CollectShadowCasters(Frustum frustum, bool includeStatic, VisibilityBoxCuller boxes, Vector3 depthFitAxis,
        bool cullAggregatesOnGpu)
    {
        var depthMin = float.MaxValue;
        var depthMax = float.MinValue;

        void AccumulateDepthFit(SceneNode casterNode)
        {
            if (depthFitAxis == Vector3.Zero)
            {
                return;
            }

            var bounds = casterNode.BoundingBox;
            var center = Vector3.Dot(bounds.Center, depthFitAxis);
            var extent = Vector3.Dot(bounds.Size, Vector3.Abs(depthFitAxis)) * 0.5f;

            depthMin = Math.Min(depthMin, center - extent);
            depthMax = Math.Max(depthMax, center + extent);
        }

        // IDs start at 1
        visibleWords = MathUtils.DivideRoundUp(Scene.NodeCount + 1, 32);

        if (visibleBits.Length < visibleWords)
        {
            visibleBits = new uint[visibleWords];
        }
        else
        {
            Array.Clear(visibleBits, 0, visibleWords);
        }

        // Every fragment the GPU may draw is marked, as the flags and the material decide too
        var cullOnGpu = cullAggregatesOnGpu && Scene.DrawMeshletsIndirect;
        hasGpuCulledCasters = false;
        ResetCpuVisibleBits(cullOnGpu);

        if (cullOnGpu)
        {
            frustum.Planes.CopyTo(casterCullFrustum.Planes, 0);
        }

        var pvs = Pvs.Span;

        foreach (var node in GetFrustumCullResults(frustum, includeStatic, boxes))
        {
            switch (TestNodeCull(node, pvs, boxes))
            {
                case NodeCull.Hidden:
                    continue;

                case NodeCull.Pvs:
                    PerfStats.Active.Count(Counter.ShadowCasterCulledByPvs, 1);
                    continue;

                case NodeCull.VisibilityBox:
                    PerfStats.Active.Count(Counter.ShadowCasterCulledByVisibilityBox, 1);
                    continue;
            }

            // NoShadows keeps an object out of every shadow view; games ignore NoSunShadows
            var casts = node switch
            {
                MeshCollectionNode => (node.Flags & ObjectTypeFlags.NoShadows) == 0,
                SceneAggregate.Fragment fragment => (fragment.Flags & ObjectTypeFlags.NoShadows) == 0 && fragment.Parent.IsFragmentInActiveLod(fragment),
                SceneAggregate aggregate => (aggregate.AllFlags & ObjectTypeFlags.NoShadows) == 0 && aggregate.InstanceTransforms.Count > 0,
                _ => (node.Flags & ObjectTypeFlags.NoShadows) == 0 && (node.RenderPasses & CustomRenderPasses.DepthOnly) != 0,
            };

            if (!casts)
            {
                continue;
            }

            AccumulateDepthFit(node);

            if (cullOnGpu && node is SceneAggregate.Fragment { Parent.CanDrawIndirect: true } indirectFragment)
            {
                if (Scene.CastsShadows(indirectFragment.DrawCall.Material, indirectFragment))
                {
                    MarkFragmentVisibleGpu(indirectFragment);
                    MathUtils.SetBit(visibleBits, (int)indirectFragment.Parent.Id);
                    hasGpuCulledCasters = true;
                }

                continue;
            }

            MathUtils.SetBit(visibleBits, (int)node.Id);
        }

        return (depthMin, depthMax);
    }

    private void AddShadowCasterDraws(SceneNode node, Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>> draws)
    {
        List<RenderableMesh> meshes;

        switch (node)
        {
            case MeshCollectionNode meshCollection:
                meshes = meshCollection.RenderableMeshes;
                break;

            case SceneAggregate.Fragment fragment:
                AddShadowCasterDraw(fragment.RenderMesh, fragment.DrawCall, node, draws);
                return;

            // Drawn indirectly, after its fragments passed the material test
            case SceneAggregate { InstanceTransforms.Count: 0 } aggregate:
                var call = aggregate.RenderMesh.DrawCallsOpaque[0];
                draws[GetDepthOnlyBucket(call)].Add(new MeshBatchRenderer.Request
                {
                    Mesh = aggregate.RenderMesh,
                    Call = call,
                    Node = node,
                });
                return;

            case SceneAggregate aggregate:
                singleMesh[0] = aggregate.RenderMesh;
                meshes = singleMesh;
                break;

            default:
                draws[DepthOnlyBucket.MaterialDepthMode].Add(new MeshBatchRenderer.Request { Node = node });
                return;
        }

        foreach (var mesh in meshes)
        {
            foreach (var opaqueCall in mesh.DrawCallsOpaque)
            {
                AddShadowCasterDraw(mesh, opaqueCall, node, draws);
            }
        }
    }

    private void AddShadowCasterDraw(RenderableMesh mesh, DrawCall opaqueCall, SceneNode node, Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>> draws)
    {
        if (!Scene.CastsShadows(opaqueCall.Material, node))
        {
            return;
        }

        draws[GetDepthOnlyBucket(opaqueCall)].Add(new MeshBatchRenderer.Request
        {
            Mesh = mesh,
            Call = opaqueCall,
            Node = node,
        });
    }

    private static DepthOnlyBucket GetDepthOnlyBucket(DrawCall opaqueCall)
        => opaqueCall.Material switch
        {
            { VertexAnimation: true } => DepthOnlyBucket.MaterialDepthMode,
            { IsAlphaTest: true } material => material.Shader.DepthMode != null
                ? DepthOnlyBucket.MaterialDepthMode
                : DepthOnlyBucket.AlphaTest,
            _ => DepthOnlyBucket.Specialized,
        };

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

        var visibleWords = cpuVisibleActive ? MathUtils.DivideRoundUp(Math.Max(scene.ObjectEntryCount, 1), 32) : 1;

        if (cpuVisibleBits.Length < visibleWords)
        {
            cpuVisibleBits = new uint[visibleWords];
        }

        cpuVisibleGpu = Scene.Upload<uint>(cpuVisibleGpu, cpuVisibleBits.AsSpan(0, visibleWords),
            ReservedBufferSlots.BufferSlot10, "CpuVisible");
        cpuVisibleGpu.BindBufferBase();

        cullShader.SetUniform("g_bCpuVisibleEnabled", cpuVisibleActive);
        cullShader.SetUniform("g_bBackfaceCulling", !collectsShadowCasters);

        var occlusionDebugEnabled = !collectsShadowCasters && scene.Settings.OcclusionDebugEnabled && scene.OcclusionDebug != null;

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
        var depthPrepass = depthOnlyShader != null && Scene.Settings.EnableDepthPrepass;

        if (drawIndirect)
        {
            BindIndirectDrawBuffers();

            // CommandBarrierBit is defined over the indirect buffer only, not the compacted count buffer
            GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit
                | MemoryBarrierFlags.BufferUpdateBarrierBit);
        }

        alphaTestAggregateDraws.Sort(MeshBatchRenderer.CompareStageThenProgram);
        alphaTestOpaqueDraws.Sort(MeshBatchRenderer.CompareCustomPipeline);

        if (depthPrepass)
        {
            RenderDepthPrepass(renderContext, depthOnlyDraws, depthOnlyShader!);
            RenderAlphaTestDepthPrepass(renderContext, depthOnlyShader!);
        }

        if (drawIndirect)
        {
            using var aggregateGroup = new GLDebugGroup("Aggregate Render");
            using var aggregateState = depthPrepass
                ? GraphicsContext.RenderState.Scope(depthWrite: false, depthFunc: RsComparison.Equal)
                : default;

            renderContext.RenderPass = RenderPass.OpaqueAggregate;
            MeshBatchRenderer.Render(renderLists[renderContext.RenderPass], renderContext);
        }

        if (renderLists[RenderPass.Opaque].Count > 0 || unprimedOpaqueDraws.Count > 0)
        {
            using var opaqueGroup = new GLDebugGroup("Opaque Render");

            renderContext.RenderPass = RenderPass.Opaque;

            using (depthPrepass
                ? GraphicsContext.RenderState.Scope(depthWrite: false, depthFunc: RsComparison.Equal)
                : default)
            {
                MeshBatchRenderer.Render(renderLists[renderContext.RenderPass], renderContext);
            }

            MeshBatchRenderer.Render(unprimedOpaqueDraws, renderContext);
        }

        RenderAlphaTestGeometry(renderContext, depthPrepass);

        if (renderLists[RenderPass.StaticOverlay].Count == 0)
        {
            return;
        }

        using var overlayGroup = new GLDebugGroup("StaticOverlay Render");

        renderContext.RenderPass = RenderPass.StaticOverlay;
        MeshBatchRenderer.Render(renderLists[renderContext.RenderPass], renderContext);
    }

    /// <summary>Writes the depth of the primable opaque draws, so the color passes after it can draw with an equal depth test.</summary>
    private static void RenderDepthPrepass(Scene.RenderContext renderContext, Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>> draws, Shader depthOnlyShader)
    {
        using var prepassGroup = new GLDebugGroup("Depth Prepass");
        using var prepassState = GraphicsContext.RenderState.Scope(colorWriteMask: RsColorWriteEnableBits.None);

        PerfStats.Active.SuspendTriangleCounter();

        var passShader = renderContext.ReplacementShader;

        renderContext.RenderPass = RenderPass.DepthOnly;
        renderContext.DepthOnlyShader = depthOnlyShader;

        foreach (var (bucket, calls) in draws)
        {
            renderContext.ReplacementShader = bucket == DepthOnlyBucket.Specialized ? depthOnlyShader : null;
            MeshBatchRenderer.Render(calls, renderContext);
        }

        renderContext.DepthOnlyShader = null;
        renderContext.ReplacementShader = passShader;

        PerfStats.Active.ResumeTriangleCounter();
    }

    /// <summary>Writes the depth of the alpha tested geometry, so the color passes after it can draw with an equal depth test.</summary>
    private void RenderAlphaTestDepthPrepass(Scene.RenderContext renderContext, Shader depthOnlyShader)
    {
        if (alphaTestAggregateDraws.Count == 0 && alphaTestOpaqueDraws.Count == 0)
        {
            return;
        }

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

    /// <summary>Draws the alpha tested geometry after the opaque geometry.</summary>
    private void RenderAlphaTestGeometry(Scene.RenderContext renderContext, bool prepassed)
    {
        if (alphaTestAggregateDraws.Count == 0 && alphaTestOpaqueDraws.Count == 0)
        {
            return;
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
        using (GraphicsContext.RenderState.Scope(depthFunc: RsComparison.CloserEqual))
        {
            renderContext.RenderPass = RenderPass.Translucent;
            MeshBatchRenderer.Render(renderLists[RenderPass.Translucent], renderContext);
        }
    }

    /// <summary>
    /// Draws the fills of the translucent layer's shapes with <see cref="ShapeSceneNode.HasOrderIndependentFill"/>
    /// into the order independent layers, in any order.
    /// </summary>
    internal void RenderOrderIndependentFills()
    {
        foreach (var shape in orderIndependentFills)
        {
            shape.RenderOrderIndependentFill();
        }
    }

    internal void RenderOrderIndependentLines()
    {
        foreach (var shape in orderIndependentFills)
        {
            shape.RenderOrderIndependentLines();
        }
    }

    /// <summary>
    /// Lays down the depth of the surfaces the refract, water and translucent layers draw over the opaque scene,
    /// collected during <see cref="CollectSceneDrawCalls"/>, each through its material's own depth mode where it has one.
    /// </summary>
    /// <param name="renderContext">The render context for this pass, bound to a depth buffer that already holds the opaque depth.</param>
    /// <param name="depthOnlyShader">The shader for materials without a depth mode of their own.</param>
    public void RenderTranslucentDepthLayer(Scene.RenderContext renderContext, Shader depthOnlyShader)
    {
        using var _ = new GLDebugGroup("Translucent Depth");

        PerfStats.Active.SuspendTriangleCounter();

        translucentDepthDraws.Sort(MeshBatchRenderer.CompareCustomPipeline);

        renderContext.RenderPass = RenderPass.DepthOnly;
        renderContext.DepthOnlyShader = depthOnlyShader;
        MeshBatchRenderer.Render(translucentDepthDraws, renderContext);
        renderContext.DepthOnlyShader = null;

        PerfStats.Active.ResumeTriangleCounter();
    }

    internal void RenderShadowCasters(Scene.RenderContext renderContext, Shader depthOnlyShader, Dictionary<DepthOnlyBucket, List<MeshBatchRenderer.Request>> draws)
    {
        if (hasGpuCulledCasters)
        {
            using (new GLDebugGroup("Cull Caster Meshlets"))
            {
                MeshletCullGpu(casterCullFrustum);

                if (Scene.CompactMeshletDraws)
                {
                    CompactIndirectDraws();
                }
            }

            BindIndirectDrawBuffers();

            // CommandBarrierBit is defined over the indirect buffer only, not the compacted count buffer
            GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit
                | MemoryBarrierFlags.BufferUpdateBarrierBit);
        }

        foreach (var node in EnumerateVisibleNodes())
        {
            AddShadowCasterDraws(node, draws);
        }

        renderContext.RenderPass = RenderPass.DepthOnly;
        renderContext.DepthOnlyShader = depthOnlyShader;

        PerfStats.Active.SuspendTriangleCounter();

        foreach (var (bucket, calls) in draws)
        {
            if (calls.Count == 0)
            {
                continue;
            }

            renderContext.ReplacementShader = bucket == DepthOnlyBucket.Specialized ? depthOnlyShader : null;

            MeshBatchRenderer.Render(calls, renderContext);
            calls.Clear();
        }

        PerfStats.Active.ResumeTriangleCounter();
    }

    /// <summary>
    /// Renders the opaque first-person viewmodel layer collected during <see cref="CollectSceneDrawCalls"/>.
    /// Rendered before the main scene so its reserved near depth range can never be overtaken by world geometry.
    /// </summary>
    /// <param name="renderContext">The render context for this pass, expected to use the dedicated viewmodel camera and depth range.</param>
    /// <param name="depthOnlyShader">Optional depth-only shader; <see langword="null"/> for a pass that replaces material shaders.</param>
    public void RenderViewmodelOpaqueLayer(Scene.RenderContext renderContext, Shader? depthOnlyShader = null)
    {
        using var _ = GraphicsContext.RenderState.Scope();

        var depthPrepass = depthOnlyShader != null && Scene.Settings.EnableDepthPrepass;

        if (depthPrepass)
        {
            RenderDepthPrepass(renderContext, viewmodelDepthOnlyDraws, depthOnlyShader!);
        }

        renderContext.RenderPass = RenderPass.Opaque;

        using (depthPrepass
            ? GraphicsContext.RenderState.Scope(depthWrite: false, depthFunc: RsComparison.Equal)
            : default)
        {
            MeshBatchRenderer.Render(viewmodelRenderLists[RenderPass.Opaque], renderContext);
        }

        MeshBatchRenderer.Render(viewmodelUnprimedOpaqueDraws, renderContext);
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

    /// <summary>Renders the draw calls that fill the effects bloom map; the caller owns the render target.</summary>
    /// <param name="renderContext">The render context for this pass.</param>
    public void RenderEffectsBloomLayer(Scene.RenderContext renderContext)
    {
        renderContext.RenderPass = RenderPass.Translucent;
        renderContext.Layer = RenderLayer.Bloom;
        MeshBatchRenderer.Render(effectsBloomRenderList, renderContext);
    }

    /// <summary>Renders the draw calls of the screen-space layer; the caller binds the screen-space view.</summary>
    /// <param name="renderContext">The render context for this pass.</param>
    public void RenderScreenSpaceLayer(Scene.RenderContext renderContext)
    {
        renderContext.RenderPass = RenderPass.Translucent;
        renderContext.Layer = RenderLayer.ScreenSpace;
        MeshBatchRenderer.Render(screenSpaceRenderList, renderContext);
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

    /// <summary>
    /// Draws what the last collection queued for some nodes again, each node all at once, with the replacement
    /// shader of the context. Nodes the collection left out, such as those out of view, are not drawn.
    /// </summary>
    /// <param name="renderContext">The render context, with the shader to draw with.</param>
    /// <param name="nodes">The nodes to draw.</param>
    /// <returns>Whether anything was drawn.</returns>
    public bool RenderNodes(Scene.RenderContext renderContext, IReadOnlySet<SceneNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        Debug.Assert(renderContext.ReplacementShader != null);

        nodeDraws.Clear();

        foreach (var requests in renderLists.Values)
        {
            AddNodeDraws(requests, nodes);
        }

        foreach (var requests in viewmodelRenderLists.Values)
        {
            AddNodeDraws(requests, nodes);
        }

        AddNodeDraws(alphaTestAggregateDraws, nodes);
        AddNodeDraws(alphaTestOpaqueDraws, nodes);
        AddNodeDraws(unprimedOpaqueDraws, nodes);
        AddNodeDraws(viewmodelUnprimedOpaqueDraws, nodes);

        // Listed once each, unlike their requests, which are queued in every pass they draw in
        foreach (var node in customBufferNodes)
        {
            if (nodes.Contains(node))
            {
                nodeDraws.Add(new MeshBatchRenderer.Request { Node = node });
            }
        }

        // Fragments drawn by their aggregate have no draws of their own queued
        foreach (var node in nodes)
        {
            if (IsDrawnByAggregate(node) && node is SceneAggregate.Fragment fragment
                && aggregatesWithVisibleFragments.Contains(fragment.Parent) && fragment.Parent.IsFragmentInActiveLod(fragment)
                && Scene.IsDrawn(fragment.DrawCall.Material, fragment))
            {
                nodeDraws.Add(new MeshBatchRenderer.Request { Mesh = fragment.RenderMesh, Call = fragment.DrawCall, Node = fragment });
            }
        }

        if (nodeDraws.Count == 0)
        {
            return false;
        }

        renderContext.RenderPass = RenderPass.All;
        MeshBatchRenderer.Render(nodeDraws, renderContext);

        return true;
    }

    private void AddNodeDraws(List<MeshBatchRenderer.Request> requests, IReadOnlySet<SceneNode> nodes)
    {
        foreach (var request in requests)
        {
            if (request.Call != null && nodes.Contains(request.Node))
            {
                nodeDraws.Add(request);
            }
        }
    }

    private bool IsDrawnByAggregate(SceneNode node)
        => Scene.DrawMeshletsIndirect && node is SceneAggregate.Fragment { Parent.CanDrawIndirect: true };

    /// <summary>Deletes the GPU buffers of this view.</summary>
    public void Delete()
    {
        if (lightBinner != null && Scene.ShadingLightBinner == lightBinner)
        {
            Scene.ShadingLightBinner = null;
        }

        lightBinner?.Delete();
        indirectDraws?.Delete();
        compactedDraws?.Delete();
        compactedCounts?.Delete();
        cpuVisibleGpu?.Delete();

        indirectDraws = null;
        compactedDraws = null;
        compactedCounts = null;
        cpuVisibleGpu = null;
    }
}
