using System.Diagnostics;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.Shaders;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Per scene owner of the tile and depth bin cull passes: the item layout, the GPU buffers they read and
/// write, and the view constants that tell the shading pass where to look.
/// </summary>
/// <remarks>
/// One of these belongs to each <see cref="Scene"/>, because the bit indices it produces are positions in
/// that scene's barn light and env map arrays. The 3D skybox is a separate scene with separate arrays, so
/// its bits are only meaningful against its own.
/// </remarks>
public sealed class LightBinner(Scene scene) : IDisposable
{
    /// <summary>Screen tile size as a power of two shift. 4 is 16x16 pixels.</summary>
    private const int TileShift = 4;

    /// <summary>
    /// Number of logarithmic depth slices items are binned into. Must be a multiple of the depth bin
    /// pass's group size, which dispatches exactly and does not bound check.
    /// </summary>
    private const int DepthSliceCount = 32; // todo: experiment with 64 and higher.

    /// <summary>
    /// Floor for the fitted slice far, in world units. A scene with nothing binned, or with everything
    /// bunched up against the camera, still needs a range wide enough to be worth distributing.
    /// </summary>
    private const float MinSliceFar = 256f;

    /// <summary>Ceiling for the fitted slice far, in world units.</summary>
    private const float MaxSliceFar = 32768f;

    private readonly TiledCullFeeder Feeder = new();

    private Shader? TileCullBitsShader;
    private Shader? DepthBinCullBitsShader;

    private StorageBuffer? CullItemsGpu;
    private StorageBuffer? CullPlanesGpu;
    private UniformBuffer<CullParams>? CullParamsGpu;
    private UniformBuffer<LightCullConstants>? ConstantsGpu;

    private readonly LightCullConstants Constants = new();

    private int TileCols;
    private int TileRows;
    private int CullBitsWords;
    private bool CullBitsAllVisible;
    private bool Active;

    /// <summary>Gets the buffer holding this scene's per tile and per depth bin masks.</summary>
    public StorageBuffer? CullBits { get; private set; }

    private bool CanCull => (scene.LightingInfo.LightingData.NumBarnLights > 0 || scene.LightingInfo.EnvMaps.Count > 0)
        && TileCullBitsShader != null
        && DepthBinCullBitsShader != null;

    /// <summary>Loads the two compute shaders. Call once the GL context exists.</summary>
    public void LoadShaders()
    {
        TileCullBitsShader = scene.RendererContext.ShaderLoader.LoadShader("vrf.compute_tile_cullbits");
        DepthBinCullBitsShader = scene.RendererContext.ShaderLoader.LoadShader("vrf.compute_depthbin_cullbits");
    }

    /// <summary>
    /// Points this pass's pixels at the ones the masks were built for. A pass drawn into the same target
    /// through a different projection, the viewmodel, cannot take its tile from gl_FragCoord directly.
    /// </summary>
    /// <param name="remap">Pixel remap: xy scale, zw bias.</param>
    public void SetPixelRemap(Vector4 remap)
    {
        Constants.LightCullPixelRemap = remap;

        if (ConstantsGpu != null)
        {
            ConstantsGpu.Data = Constants;
        }
    }

    /// <summary>What this scene's binner produced for the frame, for the render stats display.</summary>
    /// <param name="Active">Whether items were binned at all, as opposed to every mask being filled with ones.</param>
    /// <param name="Faces">Barn light faces that reached a tile, out of the faces the shading pass iterates.</param>
    /// <param name="FaceSlots">Barn light faces the shading pass iterates.</param>
    /// <param name="Probes">Env map probes that reached a tile, out of the probes the shading pass iterates.</param>
    /// <param name="ProbeSlots">Env map probes the shading pass iterates.</param>
    /// <param name="HullVertices">Silhouette vertices the tile pass walks across every item.</param>
    /// <param name="MaskBytes">Size of the tile and depth bin mask buffer for this layout.</param>
    /// <param name="SliceFar">Far distance the depth slice distribution was fitted to.</param>
    /// <param name="SliceRatio">How much deeper each slice is than the one before it.</param>
    internal readonly record struct BinnerStats(
        bool Active, int Faces, int FaceSlots, int Probes, int ProbeSlots, int HullVertices, int MaskBytes,
        float SliceFar, float SliceRatio);

    /// <summary>Gets what this binner produced for the frame.</summary>
    internal BinnerStats Stats => new(
        Active,
        Feeder.BinnedCount(TiledCullFeeder.BatchBarnLights), Feeder.SlotCount(TiledCullFeeder.BatchBarnLights),
        Feeder.BinnedCount(TiledCullFeeder.BatchEnvMaps), Feeder.SlotCount(TiledCullFeeder.BatchEnvMaps),
        Feeder.PlaneCount,
        Feeder.TotalWords * sizeof(uint),
        Feeder.SliceFar,
        Feeder.SliceRatio);

    /// <summary>Gets the first word and stride of a batch's tile region, for the debug overlay.</summary>
    /// <param name="envMaps">Whether to describe the env map batch rather than the barn light one.</param>
    public (uint TileBase, uint Words) GetOverlayRegion(bool envMaps) => envMaps
        ? (Constants.EnvMapTileBase, Constants.EnvMapCullWords)
        : (Constants.LightTileBase, Constants.LightCullWords);

    /// <summary>Binds this scene's masks and their layout for the shading pass.</summary>
    public void Bind()
    {
        CullBits?.BindBufferBase();

        if (ConstantsGpu != null)
        {
            ConstantsGpu.BindBufferBase();
            ConstantsGpu.Update();
        }
    }

    /// <summary>
    /// Projects every cull item for this frame and writes the resulting tile grid layout into
    /// <paramref name="viewConstants"/>. Must run before the view buffer upload that precedes
    /// <see cref="Dispatch"/>: the fragment shader reads the layout from the view constants, so it has to
    /// be on the GPU before the first dispatch.
    /// </summary>
    /// <param name="viewConstants">View constants to publish the layout into.</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <param name="enabled">Whether the caller wants binning this frame.</param>
    public void Update(ViewConstants viewConstants, int viewportWidth, int viewportHeight, bool enabled)
    {
        Active = enabled && CanCull && viewportWidth > 0 && viewportHeight > 0;

        const int tileSize = 1 << TileShift;

        var width = Math.Max(viewportWidth, 1);
        var height = Math.Max(viewportHeight, 1);

        TileCols = (width + tileSize - 1) >> TileShift;
        TileRows = (height + tileSize - 1) >> TileShift;

        Feeder.Begin(
            TileCols, TileRows, tileSize,
            DepthSliceCount, MinSliceFar, MaxSliceFar,
            new Vector2(width, height),
            viewConstants.WorldToProjection,
            viewConstants.CameraPosition, viewConstants.CameraDirWs,
            viewConstants.NearPlane);

        if (Active)
        {
            Feeder.AddBarnLights(scene.LightingInfo.BinnedBarnLightVolumes);
            Feeder.AddEnvMaps(scene.LightingInfo.EnvMaps);
        }
        else
        {
            // Nothing will read a projected hull this frame, so only claim the slots. The layout still has
            // to be right: the shading pass indexes an all ones buffer through these same bases and strides.
            Feeder.AddCounts(scene.LightingInfo.BinnedBarnLightVolumes.Length, scene.LightingInfo.EnvMaps.Count);
        }

        Feeder.End();

        EnsureBuffers();

        Constants.LightTileBase = Feeder.TileBase(TiledCullFeeder.BatchBarnLights);
        Constants.LightSliceBase = Feeder.BinBase(TiledCullFeeder.BatchBarnLights);
        Constants.LightCullWords = Feeder.Stride(TiledCullFeeder.BatchBarnLights);
        Constants.LightTileShift = TileShift;
        Constants.LightTileCols = (uint)TileCols;
        Constants.LightTileRows = (uint)TileRows;
        Constants.LightSliceCount = DepthSliceCount;

        // Read after End, which is where the feeder fits the range to the items it just projected.
        var depthKeyRange = Feeder.DepthKeyRange;

        Constants.LightDepthSliceParams = new Vector4(
            DepthSliceCount / depthKeyRange,
            -DepthSliceCount * MathF.Log2(viewConstants.NearPlane) / depthKeyRange,
            0f, 0f);

        Constants.EnvMapTileBase = Feeder.TileBase(TiledCullFeeder.BatchEnvMaps);
        Constants.EnvMapBinBase = Feeder.BinBase(TiledCullFeeder.BatchEnvMaps);
        Constants.EnvMapCullWords = Feeder.Stride(TiledCullFeeder.BatchEnvMaps);
        Constants.EnvMapCount = (uint)Feeder.SlotCount(TiledCullFeeder.BatchEnvMaps);

        Constants.LightCullCameraPosition = viewConstants.CameraPosition;
        Constants.LightCullCameraDir = viewConstants.CameraDirWs;

        Constants.LightCullPixelRemap = ViewConstants.PixelRemapIdentity;

        Debug.Assert(ConstantsGpu is not null);
        ConstantsGpu.Data = Constants;
    }

    /// <summary>
    /// Builds the tile and depth bin masks so the shading pass can iterate only the items that reach a
    /// given fragment. The items were already projected and frustum rejected on the CPU by
    /// <see cref="Update"/>; these two passes only rasterize them into bits.
    /// </summary>
    public void Dispatch()
    {
        if (CullBits == null)
        {
            return;
        }

        if (!Active || Feeder.MaskCount == 0)
        {
            if (!CullBitsAllVisible)
            {
                CullBits.Fill(uint.MaxValue);
                CullBitsAllVisible = true;
            }

            return;
        }

        CullBitsAllVisible = false;

        Debug.Assert(TileCullBitsShader is not null && DepthBinCullBitsShader is not null);
        Debug.Assert(CullItemsGpu is not null && CullPlanesGpu is not null && CullParamsGpu is not null);

        CullItemsGpu.Update(Feeder.ItemArray, 0, Feeder.ItemCount * Unsafe.SizeOf<CullItem>());
        CullPlanesGpu.Update(Feeder.PlaneArray, 0, Feeder.PlaneCount * Unsafe.SizeOf<Vector2>());
        CullParamsGpu.Data = Feeder.Params;

        CullBits.BindBufferBase();
        CullItemsGpu.BindBufferBase();
        CullPlanesGpu.BindBufferBase();
        CullParamsGpu.BindBufferBase();

        var (tileX, tileY, tileZ) = Feeder.TileDispatch;
        TileCullBitsShader.Use();
        SetOcclusionUniforms(TileCullBitsShader);
        GL.DispatchCompute(tileX, tileY, tileZ);

        var (binX, binY, binZ) = Feeder.BinDispatch;
        DepthBinCullBitsShader.Use();
        GL.DispatchCompute(binX, binY, binZ);

        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
    }

    /// <summary>
    /// Grows the GPU buffers to fit the layout the feeder just produced. The bits buffer is sized from the
    /// batch layout rather than a worst case, so a scene with few items pays for few words.
    /// </summary>
    private void EnsureBuffers()
    {
        CullParamsGpu ??= new UniformBuffer<CullParams>(ReservedBufferSlots.CullParams);
        ConstantsGpu ??= new UniformBuffer<LightCullConstants>(ReservedBufferSlots.LightCull);

        CullItemsGpu ??= StorageBuffer.Allocate<CullItem>(
            ReservedBufferSlots.CullItems, Feeder.ItemArray.Length, BufferUsageHint.DynamicDraw);

        CullPlanesGpu ??= StorageBuffer.Allocate<Vector2>(
            ReservedBufferSlots.CullPlanes, Feeder.PlaneArray.Length, BufferUsageHint.DynamicDraw);

        if (CullBits == null || CullBitsWords < Feeder.TotalWords)
        {
            CullBits?.Delete();
            CullBitsWords = Feeder.TotalWords;
            CullBits = StorageBuffer.Allocate<uint>(
                ReservedBufferSlots.LightCullBits, CullBitsWords, BufferUsageHint.DynamicDraw);

            // A fresh allocation holds nothing in particular, and every zero bit reads as an item culled.
            // Start visible instead: a pass that never reaches Dispatch - a viewer holding a locked cull
            // frustum for the whole session - then shades against every light and probe rather than none.
            CullBits.Fill(uint.MaxValue);
            CullBitsAllVisible = true;
        }
    }

    /// <summary>
    /// Feeds the depth pyramid to the optional occlusion test in <c>compute_tile_cullbits</c>. That test is
    /// behind a shader constant, because occluding a cull item is only sound when every receiver that reads
    /// it is opaque; these are set regardless so enabling it needs no CPU change.
    /// </summary>
    private void SetOcclusionUniforms(Shader shader)
    {
        if (!scene.SetOcclusionUniforms(shader))
        {
            return;
        }

        Debug.Assert(scene.DepthPyramid != null);

        // Cull space is pixels, so this is just the pyramid's size over the viewport's. Only this pass
        // needs it, because only this pass starts from a screen rect rather than a world space box.
        shader.SetUniform2("g_vCullToPyramidScale", new Vector2(
            scene.DepthPyramid.Width / MathF.Max(Feeder.ViewportSize.X, 1f),
            scene.DepthPyramid.Height / MathF.Max(Feeder.ViewportSize.Y, 1f)));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        CullBits?.Delete();
        CullItemsGpu?.Delete();
        CullPlanesGpu?.Delete();
        CullParamsGpu?.Dispose();
        ConstantsGpu?.Dispose();

        CullBits = null;
        CullItemsGpu = null;
        CullPlanesGpu = null;
        CullParamsGpu = null;
        ConstantsGpu = null;
    }
}
