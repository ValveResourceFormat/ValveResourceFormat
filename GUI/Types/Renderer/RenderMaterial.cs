using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    enum ReservedTextureSlots
    {
        BRDFLookup = 0,
        BlueNoise,
        FogCubeTexture,
        Lightmap1,
        Lightmap2,
        Lightmap3,
        Lightmap4,
        Lightmap5,
        Lightmap6,
        EnvironmentMap,
        Probe1,
        Probe2,
        Probe3,
        ShadowDepthBufferDepth,
        SceneColor,
        SceneDepth,
        SceneStencil,
        MorphCompositeTexture,
        Last = MorphCompositeTexture,
    }

    enum BlendMode
    {
        Opaque,
        AlphaTest,
        Translucent,
        Additive,
        Multiply,
        Mod2x,
        ModThenAdd,
    }

    [DebuggerDisplay("{Material.Name} ({Shader.Name})")]
    class RenderMaterial
    {
        private const int TextureUnitStart = (int)ReservedTextureSlots.Last + 1;

        public int SortId { get; }
        public required Shader Shader { get; init; }
        public Material Material { get; }
        public Dictionary<string, RenderTexture> Textures { get; } = [];
        public bool IsOverlay { get; }
        public bool IsToolsMaterial { get; }
        public bool IsCs2Water { get; }
        public bool DoNotCastShadows { get; }

        public bool IsTranslucent => blendMode >= BlendMode.Translucent;
        public bool IsAlphaTest => blendMode == BlendMode.AlphaTest;

        private readonly BlendMode blendMode;
        private readonly bool isRenderBackfaces;
        private readonly bool hasDepthBias;
        private int textureUnit;

        [SetsRequiredMembers]
        public RenderMaterial(Material material, VrfGuiContext guiContext, Dictionary<string, byte>? shaderArguments)
            : this(material)
        {
            var materialArguments = material.GetShaderArguments();
            var combinedShaderParameters = shaderArguments ?? materialArguments;

            if (shaderArguments != null)
            {
                foreach (var kvp in materialArguments)
                {
                    combinedShaderParameters[kvp.Key] = kvp.Value;
                }
            }

            if (material.ShaderName == "sky.vfx")
            {
                ShaderCollection? shader = null;

                try
                {
                    shader = guiContext.FileLoader.LoadShader(material.ShaderName);
                }
                catch (UnexpectedMagicException e)
                {
                    Log.Error(nameof(RenderMaterial), $"Failed to load the sky shader: {e.Message}");
                }

                if (shader?.Features != null)
                {
                    foreach (var block in shader.Features.StaticComboArray)
                    {
                        if (block.Name.StartsWith("F_TEXTURE_FORMAT", StringComparison.Ordinal))
                        {
                            for (byte i = 0; i < block.Strings.Length; i++)
                            {
                                var checkbox = block.Strings[i];

                                switch (checkbox)
                                {
                                    case "YCoCg (dxt compressed)": combinedShaderParameters.Add("VRF_TEXTURE_FORMAT_YCOCG", i); break;
                                    case "RGBM (dxt compressed)": combinedShaderParameters.Add("VRF_TEXTURE_FORMAT_RGBM_DXT", i); break;
                                    case "RGBM (8-bit uncompressed)": combinedShaderParameters.Add("VRF_TEXTURE_FORMAT_RGBM", i); break;
                                }
                            }

                            break;
                        }
                    }
                }
            }

            SetRenderState();
            Shader = guiContext.ShaderLoader.LoadShader(material.ShaderName, combinedShaderParameters, blocking: false);
            ResetRenderState();

            SortId = GetSortId();
        }

        [SetsRequiredMembers]
        public RenderMaterial(Shader shader) : this(new Material { ShaderName = shader.Name })
        {
            Shader = shader;
            SortId = GetSortId();
        }

        private int GetSortId() => Shader.Program * 10000 + Random.Shared.Next(1, 9999);

        static readonly string[] TranslucentShaders =
        [
            "vr_glass.vfx",
            "vr_glass_markable.vfx",
            "vr_energy_field.vfx",
            "csgo_glass.vfx",
            "csgo_effects.vfx",
            "tools_sprite.vfx",
        ];

        RenderMaterial(Material material)
        {
            Material = material;

            IsToolsMaterial = material.IntAttributes.ContainsKey("tools.toolsmaterial");
            DoNotCastShadows = material.IntAttributes.GetValueOrDefault("F_DO_NOT_CAST_SHADOWS") == 1;
            isRenderBackfaces = material.IntParams.GetValueOrDefault("F_RENDER_BACKFACES") == 1;

            if (material.ShaderName == "csgo_water_fancy.vfx")
            {
                blendMode = BlendMode.Translucent;
                DoNotCastShadows = true;
                IsCs2Water = true;
                return;
            }

            // :MaterialIsOverlay
            hasDepthBias = material.IntParams.GetValueOrDefault("F_DEPTHBIAS") == 1 || material.IntParams.GetValueOrDefault("F_DEPTH_BIAS") == 1;
            IsOverlay = material.IntParams.GetValueOrDefault("F_OVERLAY") == 1;

            if (material.ShaderName == "csgo_decalmodulate.vfx")
            {
                blendMode = BlendMode.Mod2x;
                return;
            }

            if (material.IntParams.GetValueOrDefault("F_ALPHA_TEST") == 1)
            {
                blendMode = BlendMode.AlphaTest;
            }

            if (material.IntParams.GetValueOrDefault("F_TRANSLUCENT") == 1
            || TranslucentShaders.AsSpan().Contains(material.ShaderName)
            || material.IntAttributes.ContainsKey("mapbuilder.water"))
            {
                blendMode = BlendMode.Translucent;
            }

            if (material.IntParams.GetValueOrDefault("F_ADDITIVE_BLEND") == 1)
            {
                blendMode = BlendMode.Additive;
            }

            // :MaterialIsOverlay
            if (IsTranslucent && hasDepthBias && material.ShaderName is "csgo_vertexlitgeneric.vfx" or "csgo_complex.vfx")
            {
                IsOverlay = true;
            }

            var blendModeParam = 0;
            if (material.ShaderName.EndsWith("static_overlay.vfx", StringComparison.Ordinal) || material.ShaderName is "citadel_overlay.vfx")
            {
                IsOverlay = true;
                blendModeParam = (int)material.IntParams.GetValueOrDefault("F_BLEND_MODE");
            }

            if (material.ShaderName == "csgo_unlitgeneric.vfx")
            {
                blendModeParam = (int)material.IntParams.GetValueOrDefault("F_BLEND_MODE");
            }

            blendMode = blendModeParam switch
            {
                1 => BlendMode.Translucent,
                2 => BlendMode.AlphaTest,
                3 => BlendMode.Mod2x,
                4 => BlendMode.Additive,
                5 => BlendMode.Multiply,
                6 => BlendMode.ModThenAdd,
                _ => blendMode,
            };
        }

        public void Render(Shader? shader = default)
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
                shader.SetMaterialVector4Uniform(param.Key, value);
            }

            SetRenderState();
        }

        public void PostRender()
        {
            ResetRenderState();

            for (var i = TextureUnitStart; i <= textureUnit; i++)
            {
                GL.BindTextureUnit(i, 0);
            }
        }

        private void SetRenderState()
        {
            if (IsOverlay)
            {
                GL.DepthMask(false);
            }

            if (blendMode >= BlendMode.Translucent)
            {
                if (IsOverlay)
                {
                    GL.Enable(EnableCap.Blend);
                }

                if (blendMode >= BlendMode.Mod2x)
                {
                    GL.BlendFunc(BlendingFactor.DstColor, BlendingFactor.SrcColor);
                }
                else if (blendMode >= BlendMode.Additive)
                {
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                }
                else
                {
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
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

        private void ResetRenderState()
        {
            if (IsOverlay)
            {
                GL.DepthMask(true);

                if (blendMode >= BlendMode.Translucent)
                {
                    GL.Disable(EnableCap.Blend);
                }
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
        }
    }
}
