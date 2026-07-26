using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.Materials;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Fullscreen overlay for the LightTiles and EnvmapTiles render modes, drawing the tile masks the cull
/// passes produced directly rather than the per fragment heat map.
/// </summary>
/// <remarks>
/// The in shader heat map can only colour fragments that were shaded, so it shows the grid folded onto
/// geometry, and under subgroup ops it reports the subgroup's union rather than the tile's own list. This
/// covers the whole screen and reads the masks with no depth bin, which makes it ground truth for what
/// the producer actually marked.
/// </remarks>
public class LightTilesOverlay(RendererContext rendererContext)
{
    /// <summary>Which batch of the cull bits buffer the overlay draws.</summary>
    public enum Batch
    {
        /// <summary>Not a tile debug mode; draw nothing.</summary>
        None,
        /// <summary>Barn light tile masks.</summary>
        BarnLights,
        /// <summary>Environment map probe tile masks.</summary>
        EnvMaps,
    }

    /// <summary>Blend weight the overlay is drawn with, low enough to read the scene underneath.</summary>
    public const float Alpha = 0.2f;

    private Shader? shader;

    /// <summary>
    /// Returns which batch <paramref name="renderMode"/> asks for. Looked up rather than cached, because
    /// render mode ids are only assigned while shaders are parsed, which can happen after this type loads.
    /// </summary>
    /// <param name="renderMode">The active render mode's shader id.</param>
    public static Batch BatchFor(int renderMode)
    {
        if (renderMode == 0)
        {
            return Batch.None;
        }

        if (renderMode == RenderModes.GetShaderId("LightTiles"))
        {
            return Batch.BarnLights;
        }

        if (renderMode == RenderModes.GetShaderId("EnvmapTiles"))
        {
            return Batch.EnvMaps;
        }

        return Batch.None;
    }

    /// <summary>
    /// Draws the overlay over whatever is already in the bound framebuffer.
    /// </summary>
    /// <param name="cullBits">The cull bits buffer both compute passes wrote, or null when culling is off.</param>
    /// <param name="tileBase">First word of the batch's tile region.</param>
    /// <param name="words">Mask words per tile for the batch. Zero draws nothing.</param>
    public void Render(StorageBuffer? cullBits, uint tileBase, uint words)
    {
        if (cullBits == null || words == 0u)
        {
            return;
        }

        shader ??= rendererContext.ShaderLoader.LoadShader("vrf.light_tiles_overlay");

        Debug.Assert(shader != null);

        using var _ = new GLDebugGroup("Cull Tiles Overlay");

        shader.Use();
        shader.SetUniform1("g_flOverlayAlpha", Alpha);
        shader.SetUniform1("g_nOverlayTileBase", tileBase);
        shader.SetUniform1("g_nOverlayWords", words);

        cullBits.BindBufferBase();

        GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(false);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        GL.BindVertexArray(rendererContext.MeshBufferCache.EmptyVAO);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);
        GL.Enable(EnableCap.DepthTest);
    }
}
