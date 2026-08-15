using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.Materials;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Fullscreen overlay for the LightTiles and EnvmapTiles render modes, reading the tile masks directly
/// rather than through the per fragment heat map.
/// </summary>
/// <remarks>
/// The heat map only colours shaded fragments, so it shows the grid folded onto geometry, and under
/// subgroup ops it reports the subgroup's union rather than the tile's own list. This covers the whole
/// screen and ignores the depth bin, which makes it ground truth for what the producer marked.
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
    /// Returns which batch <paramref name="renderMode"/> asks for. Looked up rather than cached: render
    /// mode ids are assigned while shaders are parsed, which can happen after this type loads.
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

    /// <summary>Draws the overlay over whatever is already in the bound framebuffer.</summary>
    /// <param name="cullBits">The cull bits buffer, or null when culling is off.</param>
    /// <param name="tileBase">First word of the batch's tile region.</param>
    /// <param name="words">Mask words per tile. Zero draws nothing.</param>
    public void Render(StorageBuffer? cullBits, uint tileBase, uint words)
    {
        if (cullBits == null || words == 0u)
        {
            return;
        }

        shader ??= rendererContext.ShaderLoader.LoadShader("light_tiles_overlay");

        Debug.Assert(shader != null);

        using var _ = new GLDebugGroup("Cull Tiles Overlay");

        shader.Use();
        shader.SetUniform("g_flOverlayAlpha", Alpha);
        shader.SetUniform("g_nOverlayTileBase", tileBase);
        shader.SetUniform("g_nOverlayWords", words);

        cullBits.BindBufferBase();

        using var overlayState = rendererContext.RenderState.Scope(depthTest: false, depthWrite: false, blend: true);

        GL.BindVertexArray(rendererContext.MeshBufferCache.EmptyVAO);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }
}
