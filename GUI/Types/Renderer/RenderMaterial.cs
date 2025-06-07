using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
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
        AnimationTexture,
        MorphCompositeTexture,
        Last = MorphCompositeTexture,
    }

    [DebuggerDisplay("{Material.Name} ({Shader.Name})")]
    class RenderMaterial
    {
        private const int TextureUnitStart = (int)ReservedTextureSlots.Last + 1;

        public int SortId { get; }
        public required Shader Shader { get; init; }
        public Material Material { get; }
        public Dictionary<string, RenderTexture> Textures { get; } = [];
        public bool IsTranslucent { get; }
        public bool IsOverlay { get; }
        public bool IsAlphaTest { get; }
        public bool IsToolsMaterial { get; }

        private readonly bool isAdditiveBlend;
        private readonly bool isMod2x;
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
                var shader = guiContext.FileLoader.LoadShader(material.ShaderName);

                if (shader.Features != null)
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
            Shader = guiContext.ShaderLoader.LoadShader(material.ShaderName, combinedShaderParameters);
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
                || material.ShaderName == "csgo_decalmodulate.vfx"
                || material.ShaderName == "tools_sprite.vfx";
            IsAlphaTest = material.IntParams.GetValueOrDefault("F_ALPHA_TEST") == 1;
            isAdditiveBlend = material.IntParams.GetValueOrDefault("F_ADDITIVE_BLEND") == 1;
            isRenderBackfaces = material.IntParams.GetValueOrDefault("F_RENDER_BACKFACES") == 1;

            // :MaterialIsOverlay
            hasDepthBias = material.IntParams.GetValueOrDefault("F_DEPTHBIAS") == 1 || material.IntParams.GetValueOrDefault("F_DEPTH_BIAS") == 1;
            IsOverlay = (material.IntParams.GetValueOrDefault("F_OVERLAY") == 1)
                || (IsTranslucent && hasDepthBias && material.ShaderName is "csgo_vertexlitgeneric.vfx" or "csgo_complex.vfx");

            var blendMode = 0;

            if (material.ShaderName.EndsWith("static_overlay.vfx", StringComparison.Ordinal)
                || material.ShaderName is "citadel_overlay.vfx")
            {
                IsOverlay = true;
                blendMode = (int)material.IntParams.GetValueOrDefault("F_BLEND_MODE");
            }

            if (material.ShaderName == "csgo_unlitgeneric.vfx")
            {
                blendMode = (int)material.IntParams.GetValueOrDefault("F_BLEND_MODE");
            }
            else if (material.ShaderName == "csgo_decalmodulate.vfx")
            {
                blendMode = 3; // mod2x
            }

            if (blendMode > 0)
            {
                IsTranslucent = blendMode > 0 && blendMode != 2;
                IsAlphaTest = blendMode == 2;
                isMod2x = blendMode == 3;
                isAdditiveBlend = blendMode == 4;
                // 5 = multiply
                // 6 = modthenadd
            }
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

            if (IsTranslucent)
            {
                if (IsOverlay)
                {
                    GL.Enable(EnableCap.Blend);
                }

                if (isMod2x)
                {
                    GL.BlendFunc(BlendingFactor.DstColor, BlendingFactor.SrcColor);
                }
                else if (isAdditiveBlend)
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

                if (IsTranslucent)
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

        public static Vector3 SrgbGammaToLinear(Vector3 vSrgbGammaColor)
        {
            var vLinearSegment = vSrgbGammaColor / 12.92f;
            const float power = 2.4f;

            var vExpSegment = (vSrgbGammaColor / 1.055f) + new Vector3(0.055f / 1.055f);
            vExpSegment = new Vector3(
                MathF.Pow(vExpSegment.X, power),
                MathF.Pow(vExpSegment.Y, power),
                MathF.Pow(vExpSegment.Z, power)
            );

            var vLinearColor = new Vector3(
                (vSrgbGammaColor.X <= 0.04045f) ? vLinearSegment.X : vExpSegment.X,
                (vSrgbGammaColor.Y <= 0.04045f) ? vLinearSegment.Y : vExpSegment.Y,
                (vSrgbGammaColor.Z <= 0.04045f) ? vLinearSegment.Z : vExpSegment.Z
            );

            return vLinearColor;
        }
    }
}
