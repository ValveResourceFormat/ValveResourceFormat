using System.Collections.Generic;
using System.Linq;
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

        public Shader Shader { get; set; }
        public Material Material { get; }
        public IKeyValueCollection VsInputSignature { get; }
        public Dictionary<string, RenderTexture> Textures { get; } = new();
        public bool IsTranslucent { get; }
        public bool IsAlphaTest { get; }
        public bool IsOverlay { get; }
        public bool IsToolsMaterial { get; }

        private readonly bool isAdditiveBlend;
        private readonly bool isMod2x;
        private readonly bool isRenderBackfaces;
        private readonly bool hasDepthBias;
        private int textureUnit;

        public RenderMaterial(Material material, IKeyValueCollection insg, ShaderLoader shaderLoader, Dictionary<string, byte> shaderArguments)
            : this(material)
        {
            VsInputSignature = insg;

            var combinedShaderParameters = shaderArguments == null ? material.GetShaderArguments() : shaderArguments
                .Concat(material.GetShaderArguments())
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Shader = shaderLoader.LoadShader(material.ShaderName, combinedShaderParameters);
        }

        public RenderMaterial(Shader shader) : this(new Material { ShaderName = shader.Name })
        {
            Shader = shader;
        }

        RenderMaterial(Material material)
        {
            Material = material;
            IsToolsMaterial = material.IntAttributes.ContainsKey("tools.toolsmaterial");
            IsTranslucent = (material.IntParams.GetValueOrDefault("F_TRANSLUCENT") == 1)
                || material.IntAttributes.ContainsKey("mapbuilder.water")
                || material.ShaderName == "vr_glass.vfx"
                || material.ShaderName == "vr_glass_markable.vfx"
                || material.ShaderName == "csgo_glass.vfx"
                || material.ShaderName == "csgo_effects.vfx";
            IsAlphaTest = !IsTranslucent && material.IntParams.GetValueOrDefault("F_ALPHA_TEST") == 1;
            isAdditiveBlend = material.IntParams.GetValueOrDefault("F_ADDITIVE_BLEND") == 1;
            isRenderBackfaces = material.IntParams.GetValueOrDefault("F_RENDER_BACKFACES") == 1;
            hasDepthBias = material.IntParams.GetValueOrDefault("F_DEPTHBIAS") == 1 || material.IntParams.GetValueOrDefault("F_DEPTH_BIAS") == 1;
            IsOverlay = (material.IntParams.GetValueOrDefault("F_OVERLAY") == 1)
                || (IsTranslucent && hasDepthBias && material.ShaderName is "csgo_vertexlitgeneric.vfx" or "csgo_complex.vfx");

            if (material.ShaderName.EndsWith("static_overlay.vfx", System.StringComparison.Ordinal))
            {
                IsOverlay = true;
                var blendMode = material.IntParams.GetValueOrDefault("F_BLEND_MODE");
                IsTranslucent = blendMode > 0 && blendMode != 2;
                isMod2x = blendMode == 3;
                isAdditiveBlend = blendMode == 4;
            }
        }

        public void Render(Shader shader = default, WorldLightingInfo lightingInfo = default)
        {
            textureUnit = TextureUnitStart;

            shader ??= Shader;

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

            if (IsTranslucent)
            {
                GL.DepthMask(false);
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, isAdditiveBlend ? BlendingFactor.One : BlendingFactor.OneMinusSrcAlpha);
                if (isMod2x)
                {
                    GL.BlendFunc(BlendingFactor.DstColor, BlendingFactor.SrcColor);
                }
            }

            if (hasDepthBias || IsOverlay)
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
            if (IsTranslucent)
            {
                GL.DepthMask(true);
                GL.Disable(EnableCap.Blend);
            }

            if (hasDepthBias || IsOverlay)
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
