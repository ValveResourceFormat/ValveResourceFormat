using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.VfxEval;

namespace ValveResourceFormat.Renderer.Materials
{
    /// <summary>
    /// Reserved GPU texture unit slots for global textures.
    /// </summary>
    public enum ReservedTextureSlots
    {
        /// <summary>BRDF lookup texture for PBR shading.</summary>
        BRDFLookup = 0,
        /// <summary>Blue noise texture for dithering and randomization.</summary>
        BlueNoise,
        /// <summary>Fog cube texture for atmospheric fog rendering.</summary>
        FogCubeTexture,
        /// <summary>Lightmap texture channel 1.</summary>
        Lightmap1,
        /// <summary>Lightmap texture channel 2.</summary>
        Lightmap2,
        /// <summary>Lightmap texture channel 3.</summary>
        Lightmap3,
        /// <summary>Lightmap texture channel 4.</summary>
        Lightmap4,
        /// <summary>Lightmap texture channel 5.</summary>
        Lightmap5,
        /// <summary>Lightmap texture channel 6.</summary>
        Lightmap6,
        /// <summary>Environment cubemap for reflections.</summary>
        EnvironmentMap,
        /// <summary>Light probe irradiance slot 1.</summary>
        Probe1,
        /// <summary>Light probe irradiance slot 2.</summary>
        Probe2,
        /// <summary>Light probe irradiance slot 3.</summary>
        Probe3,
        /// <summary>Shadow depth buffer for primary shadow pass.</summary>
        ShadowDepthBufferDepth,
        /// <summary>Shadow depth buffer for barn lights.</summary>
        BarnLightShadowDepth,
        /// <summary>Light cookie texture (clamped wrap mode).</summary>
        LightCookieTexture,
        /// <summary>Light cookie texture (repeat wrap mode).</summary>
        LightCookieTextureWrap,
        /// <summary>Resolved opaque scene color for refraction.</summary>
        SceneColor,
        /// <summary>Resolved scene depth buffer.</summary>
        SceneDepth,
        /// <summary>Resolved scene stencil buffer.</summary>
        SceneStencil,
        /// <summary>Hierarchical depth pyramid for occlusion culling.</summary>
        DepthPyramid,
        /// <summary>Morph composite texture for vertex animation.</summary>
        MorphCompositeTexture,
        /// <summary>Last reserved slot; equal to <see cref="MorphCompositeTexture"/>.</summary>
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

    /// <summary>
    /// Material with shader, textures, and render state for GPU rendering.
    /// </summary>
    [DebuggerDisplay("{Material.Name} ({Shader.Name})")]
    public class RenderMaterial
    {
        private const int TextureUnitStart = (int)ReservedTextureSlots.Last + 1;

        /// <summary>Gets a value used to bucket this material into draw-call sort bins; derived from the shader program handle and a random offset.</summary>
        public int SortId { get; }

        /// <summary>Gets the <see cref="Shaders.Shader"/> used to render this material.</summary>
        public required Shader Shader { get; init; }

        /// <summary>Gets the underlying parsed <see cref="ValveResourceFormat.ResourceTypes.Material"/> data.</summary>
        public Material Material { get; }

        /// <summary>Gets the map of matrix uniform names to their current values for this material.</summary>
        public Dictionary<string, Matrix4x4> Matrices { get; } = [];

        /// <summary>Gets the map of texture uniform names to the bound <see cref="RenderTexture"/> objects for this material.</summary>
        public Dictionary<string, RenderTexture> Textures { get; } = [];

        /// <summary>Gets or sets a value indicating whether this material is rendered as a screen-space or world-space overlay (polygon-offset, no depth write).</summary>
        public bool IsOverlay { get; set; }

        /// <summary>Gets a value indicating whether this is a tools-only material that should not appear in normal rendering.</summary>
        public bool IsToolsMaterial { get; private set; }

        /// <summary>Gets a value indicating whether this material uses the CS2 water rendering path.</summary>
        public bool IsCs2Water { get; private set; }

        /// <summary>Gets a value indicating whether this material drives vertex animation (foliage or morph-based).</summary>
        public bool VertexAnimation { get; private set; }

        /// <summary>Gets a value indicating whether geometry using this material should be excluded from shadow passes.</summary>
        public bool DoNotCastShadows { get; private set; }

        /// <summary>Gets a value indicating whether this material uses any blending mode that requires back-to-front ordering.</summary>
        public bool IsTranslucent => blendMode >= BlendMode.Translucent;

        /// <summary>Gets a value indicating whether this material uses alpha-to-coverage alpha testing.</summary>
        public bool IsAlphaTest => blendMode == BlendMode.AlphaTest;

        private static readonly Dictionary<(int, int), int> SamplerCache = [];

        private BlendMode blendMode;
        private bool isRenderBackfaces;
        private bool hasDepthBias;
        private int textureUnit;
        private readonly List<int> boundSamplerUnits = [];

        /// <summary>Initializes a new instance of the <see cref="RenderMaterial"/> class from a parsed material resource, loading its shader and applying render state.</summary>
        /// <param name="material">The parsed Source 2 material data.</param>
        /// <param name="rendererContext">The renderer context used to load shaders and textures.</param>
        /// <param name="shaderArguments">Optional caller-supplied static combo overrides that take precedence over the material's own arguments.</param>
        [SetsRequiredMembers]
        public RenderMaterial(Material material, RendererContext rendererContext, Dictionary<string, byte>? shaderArguments)
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
                    shader = rendererContext.FileLoader.LoadShader(material.ShaderName);
                }
                catch (UnexpectedMagicException e)
                {
                    rendererContext.Logger.LogError(e, "Failed to load the sky shader");
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
            Shader = rendererContext.ShaderLoader.LoadShader(material.ShaderName, combinedShaderParameters, blocking: false);
            ResetRenderState();

            SortId = GetSortId();
        }

        /// <summary>Initializes a new instance of the <see cref="RenderMaterial"/> class wrapping an existing shader with an empty material.</summary>
        /// <param name="shader">The shader to use for rendering.</param>
        [SetsRequiredMembers]
        public RenderMaterial(Shader shader) : this(new Material { Resource = null!, ShaderName = shader.Name })
        {
            Shader = shader;
            SortId = GetSortId();
        }

        /// <summary>The sort ID range allocated per unique shader program, used to group draw calls by shader while preserving random ordering within a group.</summary>
        public const int PerShaderSortIdRange = 10_000;
        private int GetSortId() => Shader.Program * PerShaderSortIdRange + Random.Shared.Next(1, 9999);

        static readonly string[] TranslucentShaders =
        [
            "vr_glass.vfx",
            "vr_glass_markable.vfx",
            "vr_energy_field.vfx",
            "csgo_glass.vfx",
            "csgo_effects.vfx",
        ];

        RenderMaterial(Material material)
        {
            Material = material;
            LoadRenderState();
        }

        /// <summary>
        /// Load or reload render state from material data.
        /// </summary>
        public void LoadRenderState()
        {
            var material = Material;
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

            VertexAnimation = material.IntParams.GetValueOrDefault("F_VERTEX_ANIMATION") > 0
                || material.IntParams.GetValueOrDefault("F_FOLIAGE_ANIMATION") > 0;

            // :MaterialIsOverlay
            hasDepthBias = material.IntParams.GetValueOrDefault("F_DEPTHBIAS") == 1 || material.IntParams.GetValueOrDefault("F_DEPTH_BIAS") == 1;
            IsOverlay = material.IntParams.GetValueOrDefault("F_OVERLAY") == 1;

            if (material.ShaderName == "csgo_decalmodulate.vfx")
            {
                blendMode = BlendMode.Mod2x;
                return;
            }

            if (material.IntParams.GetValueOrDefault("F_ALPHA_TEST") > 0)
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

        /// <summary>Binds textures, sets material uniforms, and applies blend/depth render state for this material.</summary>
        /// <param name="shader">The shader to use for this draw call, or <see langword="null"/> to use <see cref="Shader"/>.</param>
        public void Render(Shader? shader = default)
        {
            textureUnit = TextureUnitStart;

            shader ??= Shader;

            if (shader.IgnoreMaterialData)
            {
                return;
            }

            boundSamplerUnits.Clear();

            var userConfigSampler = 0;
            if (shader.SamplerUserConfigUniforms.Count > 0)
            {
                var addressModeU = (int)Material.IntParams.GetValueOrDefault("g_nTextureAddressModeU");
                var addressModeV = (int)Material.IntParams.GetValueOrDefault("g_nTextureAddressModeV");
                userConfigSampler = GetOrCreateUserConfigSampler(addressModeU, addressModeV);
            }

            foreach (var (name, defaultTexture) in shader.Default.Textures)
            {
                var texture = Textures.GetValueOrDefault(name, defaultTexture);

                if (!shader.SetTexture(textureUnit, name, texture))
                {
                    continue;
                }

                if (userConfigSampler != 0 && shader.SamplerUserConfigUniforms.Contains(name))
                {
                    GL.BindSampler(textureUnit, userConfigSampler);
                    boundSamplerUnits.Add(textureUnit);
                }

                textureUnit++;
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

            if (shader.Name.StartsWith("csgo_environment", StringComparison.Ordinal))
            {
                EvalCsgoEnvironmentColorMatrices(shader);
            }

            SetRenderState();
        }

        private void EvalCsgoEnvironmentColorMatrices(Shader shader)
        {
            const string contrastBaseKeyString = "g_fTextureColorContrast1";
            const string saturationBaseKeyString = "g_fTextureColorSaturation1";
            const string brightnessBaseKeyString = "g_fTextureColorBrightness1";
            const string tintBaseKeyString = "g_vTextureColorTint1";
            const string colorTextureBaseKeyString = "g_tColor1";

            Span<char> contrastKey = stackalloc char[contrastBaseKeyString.Length]; contrastBaseKeyString.AsSpan().CopyTo(contrastKey);
            Span<char> saturationKey = stackalloc char[saturationBaseKeyString.Length]; saturationBaseKeyString.AsSpan().CopyTo(saturationKey);
            Span<char> brightnessKey = stackalloc char[brightnessBaseKeyString.Length]; brightnessBaseKeyString.AsSpan().CopyTo(brightnessKey);
            Span<char> tintKey = stackalloc char[tintBaseKeyString.Length]; tintBaseKeyString.AsSpan().CopyTo(tintKey);
            Span<char> colorTextureKey = stackalloc char[colorTextureBaseKeyString.Length]; colorTextureBaseKeyString.AsSpan().CopyTo(colorTextureKey);

            var floatValueLookup = Material.FloatParams.GetAlternateLookup<ReadOnlySpan<char>>();
            var vectorValueLookup = Material.VectorParams.GetAlternateLookup<ReadOnlySpan<char>>();
            var textureLookup = Textures.GetAlternateLookup<ReadOnlySpan<char>>();

            static void TryGetValueNoUpdate<T>(Dictionary<string, T>.AlternateLookup<ReadOnlySpan<char>> lookup, ReadOnlySpan<char> key, ref T outValue)
            {
                if (lookup.TryGetValue(key, out var value))
                {
                    outValue = value;
                }
            }

            foreach (var param in shader.Default.Matrices)
            {
                if (!param.Key.StartsWith("g_mTexture", StringComparison.Ordinal))
                {
                    continue;
                }

                var layerCharacter = param.Key[^1];
                var layerIndex = layerCharacter - '0';

                if (layerIndex < 1 || layerIndex > 3)
                {
                    continue;
                }

                var csb = Vector3.One;
                var tint = Vector4.One;
                var textureAverageColor = Vector4.One;

                contrastKey[^1] = layerCharacter;
                saturationKey[^1] = layerCharacter;
                brightnessKey[^1] = layerCharacter;
                tintKey[^1] = layerCharacter;
                colorTextureKey[^1] = layerCharacter;

                TryGetValueNoUpdate(floatValueLookup, contrastKey, ref csb.X);
                TryGetValueNoUpdate(floatValueLookup, saturationKey, ref csb.Y);
                TryGetValueNoUpdate(floatValueLookup, brightnessKey, ref csb.Z);
                TryGetValueNoUpdate(vectorValueLookup, tintKey, ref tint);

                if (textureLookup.TryGetValue(colorTextureKey, out var colorTexture))
                {
                    textureAverageColor = colorTexture.Reflectivity;
                }

                var ccMatrix = VfxEvalFunctions.MatrixColorCorrect2(csb, textureAverageColor.AsVector3());

                if (param.Key.StartsWith("g_mTextureAdjust", StringComparison.Ordinal))
                {
                    tint = Vector4.One;
                }

                var tintMatrix = VfxEvalFunctions.MatrixColorTint2(tint.AsVector3(), 1f);

                ccMatrix = Matrix4x4.Multiply(tintMatrix, ccMatrix);
                shader.SetUniform4x4(param.Key, ccMatrix);
            }
        }

        /// <summary>Restores render state and unbinds textures after the draw call for this material has completed.</summary>
        public void PostRender()
        {
            ResetRenderState();

            for (var i = TextureUnitStart; i <= textureUnit; i++)
            {
                GL.BindTextureUnit(i, 0);
            }

            foreach (var unit in boundSamplerUnits)
            {
                GL.BindSampler(unit, 0);
            }
        }

        private static int GetOrCreateUserConfigSampler(int addressModeU, int addressModeV)
        {
            var key = (addressModeU, addressModeV);
            if (SamplerCache.TryGetValue(key, out var sampler))
            {
                return sampler;
            }

            GL.CreateSamplers(1, out sampler);
            GL.SamplerParameter(sampler, SamplerParameterName.TextureWrapS, (int)MapAddressMode(addressModeU));
            GL.SamplerParameter(sampler, SamplerParameterName.TextureWrapT, (int)MapAddressMode(addressModeV));
            GL.SamplerParameter(sampler, SamplerParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.SamplerParameter(sampler, SamplerParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            if (MaterialLoader.MaxTextureMaxAnisotropy >= 4)
            {
                GL.SamplerParameter(sampler, (SamplerParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, MaterialLoader.MaxTextureMaxAnisotropy);
            }

            SamplerCache[key] = sampler;
            return sampler;
        }

        private static TextureWrapMode MapAddressMode(int mode) => mode switch
        {
            0 => TextureWrapMode.Repeat,
            1 => TextureWrapMode.MirroredRepeat,
            2 => TextureWrapMode.ClampToEdge,
            3 => TextureWrapMode.ClampToBorder,
            // 4 => ...
            _ => TextureWrapMode.Repeat,
            // _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown texture address mode"),
        };

        private void SetRenderState()
        {
            if (IsOverlay)
            {
                GL.DepthMask(false);
            }

            if (blendMode == BlendMode.AlphaTest)
            {
                GL.Enable(EnableCap.SampleAlphaToCoverage); // todo: only if msaa samples > 1
            }
            else if (blendMode >= BlendMode.Translucent)
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

            if (blendMode == BlendMode.AlphaTest)
            {
                GL.Disable(EnableCap.SampleAlphaToCoverage);
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
