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

    /// <summary>Number of logarithmic depth slices items are binned into.</summary>
    private const int DepthSliceCount = 32;

    /// <summary>Far distance the logarithmic slice distribution is fitted to, in world units.</summary>
    private const float DepthSliceFar = 32768f;

    /// <summary>Camera near plane, matching <see cref="Camera.CreateProjectionMatrix"/>.</summary>
    private const float DepthSliceNear = 1f;

    private readonly TiledCullFeeder Feeder = new();

    private Shader? TileCullBitsShader;
    private Shader? DepthBinCullBitsShader;

    private StorageBuffer? CullItemsGpu;
    private StorageBuffer? CullPlanesGpu;
    private UniformBuffer<CullParams>? CullParamsGpu;

    private int TileCols;
    private int TileRows;
    private int CullBitsWords;
    private bool CullBitsAllVisible;
    private bool Active;

    /// <summary>Gets the buffer holding this scene's per tile and per depth bin masks.</summary>
    public StorageBuffer? CullBits { get; private set; }

    /// <summary>Gets or sets whether items are binned to screen tiles at all.</summary>
    public bool Enabled { get; set; } = true;

    private bool CanCull => Enabled
        && (scene.LightingInfo.LightingData.NumBarnLights > 0 || scene.LightingInfo.EnvMaps.Count > 0)
        && TileCullBitsShader != null
        && DepthBinCullBitsShader != null;

    /// <summary>Loads the two compute shaders. Call once the GL context exists.</summary>
    public void LoadShaders()
    {
        TileCullBitsShader = scene.RendererContext.ShaderLoader.LoadShader("vrf.compute_tile_cullbits");
        DepthBinCullBitsShader = scene.RendererContext.ShaderLoader.LoadShader("vrf.compute_depthbin_cullbits");
    }

    /// <summary>Binds the mask buffer to its reserved slot for the shading pass.</summary>
    public void BindCullBits() => CullBits?.BindBufferBase();

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
    public void SetViewConstants(ViewConstants viewConstants, int viewportWidth, int viewportHeight, bool enabled)
    {
        Active = enabled && CanCull && viewportWidth > 0 && viewportHeight > 0;

        const int tileSize = 1 << TileShift;

        var width = Math.Max(viewportWidth, 1);
        var height = Math.Max(viewportHeight, 1);

        TileCols = (width + tileSize - 1) >> TileShift;
        TileRows = (height + tileSize - 1) >> TileShift;

        var depthKeyRange = MathF.Log2(DepthSliceFar / DepthSliceNear);

        Feeder.Begin(
            TileCols, TileRows, tileSize,
            DepthSliceCount, depthKeyRange,
            new Vector2(width, height),
            viewConstants.WorldToProjection,
            viewConstants.CameraPosition, viewConstants.CameraDirWs,
            Camera.NearPlane);

        Feeder.AddBarnLights(scene.LightingInfo.BinnedBarnLightVolumes);
        Feeder.AddEnvMaps(scene.LightingInfo.EnvMaps);
        Feeder.End();

        EnsureBuffers();

        viewConstants.LightTileBase = Feeder.TileBase(TiledCullFeeder.BatchBarnLights);
        viewConstants.LightSliceBase = Feeder.BinBase(TiledCullFeeder.BatchBarnLights);
        viewConstants.LightCullWords = Feeder.Stride(TiledCullFeeder.BatchBarnLights);
        viewConstants.LightTileShift = TileShift;
        viewConstants.LightTileCols = (uint)TileCols;
        viewConstants.LightTileRows = (uint)TileRows;
        viewConstants.LightSliceCount = DepthSliceCount;
        viewConstants.LightDepthSliceParams = new Vector4(
            DepthSliceCount / depthKeyRange,
            -DepthSliceCount * MathF.Log2(DepthSliceNear) / depthKeyRange,
            0f, 0f);

        viewConstants.EnvMapTileBase = Feeder.TileBase(TiledCullFeeder.BatchEnvMaps);
        viewConstants.EnvMapBinBase = Feeder.BinBase(TiledCullFeeder.BatchEnvMaps);
        viewConstants.EnvMapCullWords = Feeder.Stride(TiledCullFeeder.BatchEnvMaps);

        viewConstants.LightCullWorldToProjection = viewConstants.WorldToProjection;
        viewConstants.LightCullCameraPosition = viewConstants.CameraPosition;
        viewConstants.LightCullCameraDir = viewConstants.CameraDirWs;

        viewConstants.LightCullPixelRemap = ViewConstants.PixelRemapIdentity;
    }

    /// <summary>
    /// Builds the tile and depth bin masks so the shading pass can iterate only the items that reach a
    /// given fragment. The items were already projected and frustum rejected on the CPU by
    /// <see cref="SetViewConstants"/>; these two passes only rasterize them into bits.
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

        using var _ = new GLDebugGroup("Cull Tiles and Depth Bins");

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
            CullBitsAllVisible = false;
        }
    }

    /// <summary>
    /// Feeds the depth pyramid to the optional occlusion test in <c>compute_tile_cullbits</c>. That test is
    /// behind a shader constant, because occluding a cull item is only sound when every receiver that reads
    /// it is opaque; these are set regardless so enabling it needs no CPU change.
    /// </summary>
    private void SetOcclusionUniforms(Shader shader)
    {
        var occlusionEnabled = scene.EnableOcclusionCulling && scene.DepthPyramidValid && scene.DepthPyramid != null;

        shader.SetUniform1("g_bOcclusionCullEnabled", occlusionEnabled ? 1 : 0);

        if (!occlusionEnabled)
        {
            return;
        }

        Debug.Assert(scene.DepthPyramid != null);

        shader.SetUniform1("g_nDepthPyramidMaxMip", scene.DepthPyramid.NumMipLevels - 1);
        shader.SetUniform1("g_nDepthPyramidWidth", scene.DepthPyramid.Width);
        shader.SetUniform1("g_nDepthPyramidHeight", scene.DepthPyramid.Height);
        shader.SetUniform1("g_flDepthRangeMin", Renderer.DepthRange.Scene.Near);
        shader.SetUniform1("g_flDepthRangeMax", Renderer.DepthRange.Scene.Far);

        shader.SetUniform2("g_vCullToPyramidScale", new Vector2(
            scene.DepthPyramid.Width / MathF.Max(Feeder.ViewportSize.X, 1f),
            scene.DepthPyramid.Height / MathF.Max(Feeder.ViewportSize.Y, 1f)));

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(scene.DepthPyramid.Target, scene.DepthPyramid.Handle);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        CullBits?.Delete();
        CullItemsGpu?.Delete();
        CullPlanesGpu?.Delete();
        CullParamsGpu?.Dispose();

        CullBits = null;
        CullItemsGpu = null;
        CullPlanesGpu = null;
        CullParamsGpu = null;
    }
}
