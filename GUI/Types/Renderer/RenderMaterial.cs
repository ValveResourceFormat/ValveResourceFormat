using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Renderer
{
    enum ReservedTextureSlots
    {
        BRDFLookup = 0,
        FogCubeTexture,
        Lightmap1,
        Lightmap2,
        Lightmap3,
        Lightmap4,
        EnvironmentMap,
        Probe1,
        Probe2,
        Probe3,
        AnimationTexture,
        MorphCompositeTexture,
        Last = MorphCompositeTexture,
    }

    class RenderMaterial
    {
        private const int TextureUnitStart = (int)ReservedTextureSlots.Last + 1;

        public int SortId { get; }
        public Shader Shader { get; set; }
        public Material Material { get; }
        public KVObject VsInputSignature { get; }
        public Dictionary<string, RenderTexture> Textures { get; } = [];
        public bool IsTranslucent { get; }
        public bool IsOverlay { get; }
        public bool IsToolsMaterial { get; }

        private readonly bool isAdditiveBlend;
        private readonly bool isMod2x;
        private readonly bool isRenderBackfaces;
        private readonly bool hasDepthBias;
        private int textureUnit;

        public RenderMaterial(Material material, KVObject insg, ShaderLoader shaderLoader, Dictionary<string, byte> shaderArguments)
            : this(material)
        {
            VsInputSignature = insg;

            var materialArguments = material.GetShaderArguments();
            var combinedShaderParameters = shaderArguments ?? materialArguments;

            if (shaderArguments != null)
            {
                foreach (var kvp in materialArguments)
                {
                    combinedShaderParameters[kvp.Key] = kvp.Value;
                }
            }

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
                || material.ShaderName == "csgo_effects.vfx"
                || material.ShaderName == "tools_sprite.vfx";
            isAdditiveBlend = material.IntParams.GetValueOrDefault("F_ADDITIVE_BLEND") == 1;
            isRenderBackfaces = material.IntParams.GetValueOrDefault("F_RENDER_BACKFACES") == 1;
            hasDepthBias = material.IntParams.GetValueOrDefault("F_DEPTHBIAS") == 1 || material.IntParams.GetValueOrDefault("F_DEPTH_BIAS") == 1;
            IsOverlay = (material.IntParams.GetValueOrDefault("F_OVERLAY") == 1)
                || (IsTranslucent && hasDepthBias && material.ShaderName is "csgo_vertexlitgeneric.vfx" or "csgo_complex.vfx");

            var blendMode = 0;

            if (material.ShaderName.EndsWith("static_overlay.vfx", System.StringComparison.Ordinal))
            {
                IsOverlay = true;
                blendMode = (int)material.IntParams.GetValueOrDefault("F_BLEND_MODE");
            }

            if (material.ShaderName == "csgo_unlitgeneric.vfx")
            {
                blendMode = (int)material.IntParams.GetValueOrDefault("F_BLEND_MODE");
            }

            if (blendMode > 0)
            {
                IsTranslucent = blendMode > 0 && blendMode != 2;
                isMod2x = blendMode == 3;
                isAdditiveBlend = blendMode == 4;
                // 5 = multiply
                // 6 = modthenadd
            }

            SortId = GetHashCode(); // It doesn't really matter what we use, it could be a random value
        }

        public void Render(Shader shader = default)
        {
            textureUnit = TextureUnitStart;

            shader ??= Shader;

            if (shader.Name == "vrf.picking")
            {
                // Discard material data for picking shader, (blend modes, etc.)
                return;
            }

            foreach (var (name, defaultTexture) in shader.Default.Textures)
            {
                var texture = Textures.GetValueOrDefault(name, defaultTexture);

                if (shader.SetTexture(textureUnit, name, texture))
                {
                    textureUnit++;
                }
            }

            foreach (var param in shader.Default.Material.IntParams)
            {
                var value = (int)Material.IntParams.GetValueOrDefault(param.Key, param.Value);
                shader.SetUniform1(param.Key, value);
            }

            foreach (var param in shader.Default.Material.FloatParams)
            {
                var value = Material.FloatParams.GetValueOrDefault(param.Key, param.Value);
                shader.SetUniform1(param.Key, value);
            }

            foreach (var param in shader.Default.Material.VectorParams)
            {
                var value = Material.VectorParams.GetValueOrDefault(param.Key, param.Value);
                shader.SetUniform4(param.Key, value);
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
                GL.PolygonOffsetClamp(0, 64, 0.0005f);
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
                GL.PolygonOffsetClamp(0, 0, 0);
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
