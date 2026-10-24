using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Editor.ToolRender;

/// <summary>
/// The variants of the tools shader each set of mode flags is drawn with, and what each kind of handle is
/// drawn with.
/// </summary>
internal sealed class ToolShaders(RendererContext rendererContext)
{
    private const string ShaderName = "tools_wireframe";
    private const string HandleShaderName = "tools_handle";

    /// <summary>A handle material: the sprite shader, the shape it is cut out of, and how much of it is kept.</summary>
    public sealed record HandleMaterial(Shader Shader, RenderTexture Texture, float AlphaTestReference)
    {
        public void Use()
        {
            Shader.Use();
            Shader.SetTexture(0, "g_tColor", Texture);
            Shader.SetUniform("g_flAlphaTestReference", AlphaTestReference);
        }
    }

    private readonly Dictionary<(bool Thick, bool Bias, bool Shaded), Shader> shaders = [];
    private readonly Dictionary<(ToolHandleType Type, bool Border, bool Translucent), HandleMaterial?> handles = [];

    public Shader Get(ToolRenderModeFlags flags, bool thickLines)
    {
        var key = (
            Thick: thickLines,
            Bias: (flags & ToolRenderModeFlags.DepthBias) != 0,
            Shaded: !thickLines && (flags & (ToolRenderModeFlags.Shaded | ToolRenderModeFlags.Wireframe)) == ToolRenderModeFlags.Shaded);

        if (!shaders.TryGetValue(key, out var shader))
        {
            shader = rendererContext.ShaderLoader.LoadShader(ShaderName,
                ("D_THICK_LINES", (byte)(key.Thick ? 1 : 0)),
                ("D_DEPTH_BIAS", (byte)(key.Bias ? 1 : 0)),
                ("D_SHADED", (byte)(key.Shaded ? 1 : 0)));

            shaders[key] = shader;
        }

        return shader;
    }

    /// <summary>Gets what a kind of handle is drawn with.</summary>
    /// <returns>Whether the handle is drawn at all, which a generic one is not.</returns>
    public bool TryGetHandle(ToolHandleState state, [NotNullWhen(true)] out HandleMaterial? material)
    {
        var key = (state.Type, state.Border, state.Translucent);

        if (!handles.TryGetValue(key, out material))
        {
            handles[key] = material = LoadHandle(key.Type, key.Border, key.Translucent);
        }

        return material != null;
    }

    // Each type has a material that cuts the handle out of a texture, the translucent one keeping more of it
    private HandleMaterial? LoadHandle(ToolHandleType type, bool border, bool translucent)
    {
        if (type == ToolHandleType.Generic)
        {
            return null;
        }

        var shader = rendererContext.ShaderLoader.LoadShader(HandleShaderName, ("D_BORDER", (byte)(border ? 1 : 0)));
        var name = string.Concat("materials/tools/handle_", type switch
        {
            ToolHandleType.Square => "square",
            ToolHandleType.Circle => "circle",
            ToolHandleType.Cross => "cross",
            ToolHandleType.Ring => "ring",
            ToolHandleType.SquareRing => "square_ring",
            ToolHandleType.EdgedCircle => "edged_circle",
            ToolHandleType.Diamond => "diamond",
            _ => "edged_diamond",
        }, translucent ? "_translucent.vmat" : ".vmat");

        var materialLoader = rendererContext.MaterialLoader;

        // Without the material, the handle is the whole square
        if (rendererContext.FileLoader.LoadFileCompiled(name)?.DataBlock is not Material material)
        {
            return new HandleMaterial(shader, materialLoader.GetDefaultColor(), -1f);
        }

        var texture = material.TextureParams.Values.FirstOrDefault() is { } texturePath
            ? materialLoader.GetTexture(texturePath)
            : materialLoader.GetDefaultColor();

        var alphaTested = material.IntParams.GetValueOrDefault("F_ALPHA_TEST") != 0;
        var alphaTestReference = alphaTested ? material.FloatParams.GetValueOrDefault("g_flAlphaTestReference", 0.3f) : -1f;

        return new HandleMaterial(shader, texture, alphaTestReference);
    }
}
