using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    enum ReservedTextureSlots
    {
        AnimationTexture = 0,
        CubemapFog = 1,
    }

    class RenderMaterial
    {
        private const int TextureUnitStart = 2; // Reserve texture slots. Must always be the size of ReservedTextureSlots.

        public Shader Shader => shader;
        public Material Material { get; }
        public IKeyValueCollection VsInputSignature { get; }
        public Dictionary<string, RenderTexture> Textures { get; } = new();
        public bool IsBlended { get; }
        public bool IsToolsMaterial { get; }

        private readonly Shader shader;
        private readonly bool isAdditiveBlend;
        private readonly bool isMod2x;
        private readonly bool isRenderBackfaces;
        private readonly bool isOverlay;
        private int textureUnit;

        public RenderMaterial(Material material, IKeyValueCollection insg, ShaderLoader shaderLoader)
            : this(material)
        {
            VsInputSignature = insg;
            shader = shaderLoader.LoadShader(material.ShaderName, material.GetShaderArguments());
        }

        public RenderMaterial(Shader shader) : this(new Material { ShaderName = shader.Name })
        {
            this.shader = shader;
        }

        RenderMaterial(Material material)
        {
            Material = material;
            IsToolsMaterial = material.IntAttributes.ContainsKey("tools.toolsmaterial");
            IsBlended = (material.IntParams.ContainsKey("F_TRANSLUCENT") && material.IntParams["F_TRANSLUCENT"] == 1)
                || material.IntAttributes.ContainsKey("mapbuilder.water")
                || material.ShaderName == "vr_glass.vfx"
                || material.ShaderName == "vr_glass_markable.vfx"
                || material.ShaderName == "csgo_glass.vfx"
                || material.ShaderName == "csgo_effects.vfx"
                || material.ShaderName == "tools_sprite.vfx";
            isAdditiveBlend = material.IntParams.ContainsKey("F_ADDITIVE_BLEND") && material.IntParams["F_ADDITIVE_BLEND"] == 1;
            isRenderBackfaces = material.IntParams.ContainsKey("F_RENDER_BACKFACES") && material.IntParams["F_RENDER_BACKFACES"] == 1;
            isOverlay = (material.IntParams.ContainsKey("F_OVERLAY") && material.IntParams["F_OVERLAY"] == 1)
                || material.IntParams.ContainsKey("F_DEPTH_BIAS") && material.IntParams["F_DEPTH_BIAS"] == 1;

            if (material.ShaderName.EndsWith("static_overlay.vfx", System.StringComparison.Ordinal))
            {
                isOverlay = true;
                var blendMode = material.IntParams.GetValueOrDefault("F_BLEND_MODE");
                IsBlended = blendMode > 0;
                isMod2x = blendMode == 3;
                isAdditiveBlend = blendMode == 4;
            }
        }

        public void Render(Shader shader = default, WorldLightingInfo lightingInfo = default)
        {
            textureUnit = TextureUnitStart;

            shader ??= this.shader;

            if (shader.Name == "vrf.picking")
            {
                // Discard material data for picking shader, (blend modes, etc.)
                return;
            }

            foreach (var (name, texture) in Textures)
            {
                if (shader.SetTexture(textureUnit, name, texture))
                {
                    textureUnit++;
                }
            }

            if (lightingInfo != default)
            {
                foreach (var (name, texture) in lightingInfo.Lightmaps)
                {
                    if (shader.SetTexture(textureUnit, name, texture))
                    {
                        textureUnit++;
                    }
                }
            }

            foreach (var param in Material.IntParams)
            {
                if (param.Key.Length < 1 || param.Key[0] == 'F')
                {
                    continue;
                }

                shader.SetUniform1(param.Key, (int)param.Value);
            }

            foreach (var param in Material.FloatParams)
            {
                shader.SetUniform1(param.Key, param.Value);
            }

            foreach (var param in Material.VectorParams)
            {
                shader.SetUniform4(param.Key, param.Value);
            }

            if (IsBlended)
            {
                GL.DepthMask(false);
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, isAdditiveBlend ? BlendingFactor.One : BlendingFactor.OneMinusSrcAlpha);
                if (isMod2x)
                {
                    GL.BlendFunc(BlendingFactor.DstColor, BlendingFactor.SrcColor);
                }
            }

            if (isOverlay)
            {
                GL.Enable(EnableCap.PolygonOffsetFill);
                GL.PolygonOffset(-0.05f, -64);
            }

            if (isRenderBackfaces)
            {
                GL.Disable(EnableCap.CullFace);
            }
        }

        public void PostRender()
        {
            if (IsBlended)
            {
                GL.DepthMask(true);
                GL.Disable(EnableCap.Blend);
            }

            if (isOverlay)
            {
                GL.Disable(EnableCap.PolygonOffsetFill);
                GL.PolygonOffset(0, 0);
            }

            if (isRenderBackfaces)
            {
                GL.Enable(EnableCap.CullFace);
            }

            for (var i = TextureUnitStart; i <= textureUnit; i++)
            {
                GL.BindTextureUnit(i, 0);
            }
        }
    }
}
