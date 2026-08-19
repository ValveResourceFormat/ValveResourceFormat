using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.VfxEval;

namespace ValveResourceFormat.Renderer.Materials
{
    /// <summary>Names the sampler uniforms bound to a <see cref="ReservedTextureSlots"/> member. Names may share a slot when their texture targets differ, since a unit holds one binding per target, or when no shader variant declares both.</summary>
    /// <param name="names">The sampler uniform names bound to this slot.</param>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SamplerNameAttribute(params string[] names) : Attribute
    {
        /// <summary>Gets the sampler uniform names bound to this slot.</summary>
        public IReadOnlyList<string> Names { get; } = names;
    }

    /// <summary>
    /// Reserved GPU texture unit slots for global textures.
    /// </summary>
    public enum ReservedTextureSlots
    {
        /// <summary>BRDF lookup texture for PBR shading.</summary>
        [SamplerName("g_tBRDFLookup")]
        BRDFLookup = 0,
        /// <summary>Blue noise texture for dithering and randomization.</summary>
        [SamplerName("g_tBlueNoise")]
        BlueNoise,
        /// <summary>Fog cube texture for atmospheric fog rendering.</summary>
        [SamplerName("g_tFogCubeTexture")]
        FogCubeTexture,
        /// <summary>Baked irradiance, from the lightmap or the light probe volume.</summary>
        [SamplerName("g_tIrradiance", "g_tLPV_Irradiance")]
        Lightmap1,
        /// <summary>Baked directional irradiance, whole or red split.</summary>
        [SamplerName("g_tDirectionalIrradiance", "g_tDirectionalIrradianceR")]
        Lightmap2,
        /// <summary>Baked direct light indices, or green split directional irradiance.</summary>
        [SamplerName("g_tDirectLightIndices", "g_tLPV_Indices", "g_tDirectionalIrradianceG")]
        Lightmap3,
        /// <summary>Baked direct light strengths, or blue split directional irradiance.</summary>
        [SamplerName("g_tDirectLightStrengths", "g_tLPV_Scalars", "g_tDirectionalIrradianceB")]
        Lightmap4,
        /// <summary>Baked direct light shadows.</summary>
        [SamplerName("g_tDirectLightShadows", "g_tLPV_Shadows")]
        Lightmap5,
        /// <summary>Lightmap irradiance debug chart.</summary>
        [SamplerName("g_tIrradianceDebugChart")]
        Lightmap6,
        /// <summary>Environment cubemap for reflections.</summary>
        [SamplerName("g_tEnvironmentMap")]
        EnvironmentMap,
        /// <summary>Shadow depth buffer for primary shadow pass.</summary>
        [SamplerName("g_tShadowDepthBufferDepth")]
        ShadowDepthBufferDepth,
        /// <summary>Shadow depth buffer for barn lights.</summary>
        [SamplerName("g_tBarnLightShadowDepth")]
        BarnLightShadowDepth,
        /// <summary>Light cookie texture (clamped wrap mode).</summary>
        [SamplerName("g_tLightCookieTexture")]
        LightCookieTexture,
        /// <summary>Light cookie texture (repeat wrap mode).</summary>
        [SamplerName("g_tLightCookieTextureWrap")]
        LightCookieTextureWrap,
        /// <summary>Scrolling wave normals and blotch mask.</summary>
        [SamplerName("g_tWetnessWaves")]
        WetnessWaves,
        /// <summary>Resolved opaque scene color for refraction.</summary>
        [SamplerName("g_tSceneColor")]
        SceneColor,
        /// <summary>Resolved scene depth buffer.</summary>
        [SamplerName("g_tSceneDepth")]
        SceneDepth,
        /// <summary>Morph composite texture for vertex animation.</summary>
        [SamplerName("morphCompositeTexture")]
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
        /// <summary>
        /// First non-reserved texture unit. Material (per-draw) textures bind here and above so the
        /// globally bound <see cref="ReservedTextureSlots"/> textures stay intact.
        /// </summary>
        public const int TextureUnitStart = (int)ReservedTextureSlots.Last + 1;

        /// <summary>Gets a value used to bucket this material into draw-call sort bins; derived from the shader program handle and a random offset.</summary>
        public int SortId { get; }

        /// <summary>Gets the <see cref="Shaders.Shader"/> used to render this material.</summary>
        public required Shader Shader { get; init; }

        /// <summary>Gets the underlying parsed <see cref="ValveResourceFormat.ResourceTypes.Material"/> data, as read from the file.</summary>
        public Material Material { get; }

        /// <summary>Gets the integer parameters, seeded from <see cref="Material"/> and writable per instance.</summary>
        public MaterialInputs<long> IntParams { get; } = new();

        /// <summary>Gets the float parameters, seeded from <see cref="Material"/> and writable per instance.</summary>
        public MaterialInputs<float> FloatParams { get; } = new();

        /// <summary>Gets the vector parameters, seeded from <see cref="Material"/> and writable per instance.</summary>
        public MaterialInputs<Vector4> VectorParams { get; } = new();

        /// <summary>Gets the map of matrix uniform names to their current values for this material.</summary>
        public MaterialInputs<Matrix4x4> Matrices { get; } = new();

        /// <summary>Gets the map of texture uniform names to the bound <see cref="RenderTexture"/> objects for this material.</summary>
        public MaterialInputs<RenderTexture> Textures { get; } = new();

        /// <summary>Gets or sets a value indicating whether this material is rendered as a screen-space or world-space overlay (polygon-offset, no depth write).</summary>
        public bool IsOverlay { get; set; }

        /// <summary>Gets a value indicating whether this is a tools-only material that should not appear in normal rendering.</summary>
        public bool IsToolsMaterial { get; private set; }

        /// <summary>Gets a value indicating whether this material uses the CS2 water rendering path.</summary>
        public bool IsCs2Water { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this material's shader samples the scene color texture, and therefore
        /// has to be drawn after the framebuffer grab in <see cref="RenderPass.OpaqueRefract"/> or <see cref="RenderPass.Water"/>.
        /// Queried live rather than cached: shaders are compiled without blocking, so this only becomes true once
        /// the program has been linked and reflected, which is one frame after the material is first collected.
        /// </summary>
        public bool ReadsSceneColor => Shader.ReadsSceneColor;

        /// <summary>Gets a value indicating whether this material drives vertex animation (foliage or morph-based).</summary>
        public bool VertexAnimation { get; private set; }

        /// <summary>Gets a value indicating whether geometry using this material should be excluded from shadow passes.</summary>
        public bool DoNotCastShadows { get; private set; }

        /// <summary>Gets a value indicating whether this material uses any blending mode that requires back-to-front ordering.</summary>
        public bool IsTranslucent => blendMode >= BlendMode.Translucent;

        /// <summary>Gets a value indicating whether this material uses alpha-to-coverage alpha testing.</summary>
        public bool IsAlphaTest => blendMode == BlendMode.AlphaTest;

        private readonly MaterialLoader? Loader;

        private Globals? globals;
        private GlobalsLayout? filledLayout;
        private uint filledVersion;

        private uint InputsVersion => unchecked(
            IntParams.Version + FloatParams.Version + VectorParams.Version + Matrices.Version + Textures.Version);

        private BlendMode blendMode;
        private bool isRenderBackfaces;
        private bool hasDepthBias;
        private bool disableDepthTest;
        private int textureUnit;
        private readonly List<int> boundSamplerUnits = [];

#if DEBUG
        private static int maxCombinedTextureImageUnits;
#endif

        /// <summary>Initializes a new instance of the <see cref="RenderMaterial"/> class from a parsed material resource, loading its shader and applying render state.</summary>
        /// <param name="material">The parsed Source 2 material data.</param>
        /// <param name="rendererContext">The renderer context used to load shaders and textures.</param>
        /// <param name="shaderArguments">Optional caller-supplied static combo values used as a base; the material's own shader arguments are merged in afterward and take precedence on conflicting keys.</param>
        [SetsRequiredMembers]
        public RenderMaterial(Material material, RendererContext rendererContext, Dictionary<string, byte>? shaderArguments)
            : this(material)
        {
            Loader = rendererContext.MaterialLoader;

            var materialArguments = material.GetShaderArguments();
            var combinedShaderParameters = shaderArguments ?? materialArguments;

            if (shaderArguments != null)
            {
                foreach (var kvp in materialArguments)
                {
                    combinedShaderParameters[kvp.Key] = kvp.Value;
                }
            }

            if (ShaderName == "sky.vfx")
            {
                ShaderCollection? shader = null;

                try
                {
                    shader = rendererContext.FileLoader.LoadShader(ShaderName);
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

            Shader = rendererContext.ShaderLoader.LoadShader(ShaderName, combinedShaderParameters, blocking: false);

            SortId = GetSortId();
        }

        /// <summary>Initializes a new instance of the <see cref="RenderMaterial"/> class wrapping an existing shader with an empty material.</summary>
        /// <param name="shader">The shader to use for rendering.</param>
        [SetsRequiredMembers]
        public RenderMaterial(Shader shader) : this(new Material { Resource = null!, ShaderName = shader.Name }, normalizeShaderName: false)
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

        // Renderer shaders are wrapped by their own name, only material shader names are normalized
        RenderMaterial(Material material, bool normalizeShaderName = true)
        {
            Material = material;
            ShaderName = normalizeShaderName ? NormalizeShaderName(material.ShaderName) : material.ShaderName;

            IntParams.EnsureCapacity(material.IntParams.Count);
            FloatParams.EnsureCapacity(material.FloatParams.Count);
            VectorParams.EnsureCapacity(material.VectorParams.Count);

            foreach (var (name, value) in material.IntParams)
            {
                IntParams[name] = value;
            }

            foreach (var (name, value) in material.FloatParams)
            {
                FloatParams[name] = value;
            }

            foreach (var (name, value) in material.VectorParams)
            {
                VectorParams[name] = value;
            }

            LoadRenderState();
        }

        /// <summary>Gets the shader name of the material, normalized to the <c>.vfx</c> extension the renderer resolves shaders by.</summary>
        public string ShaderName { get; }

        // Some games name their shaders .shader or .shader_c instead of .vfx
        private static string NormalizeShaderName(string shaderName)
        {
            return string.Concat(Path.GetFileNameWithoutExtension(shaderName.AsSpan()), ShaderLoader.VfxExtension);
        }

        /// <summary>
        /// Load or reload render state from material data.
        /// </summary>
        public void LoadRenderState()
        {
            var material = Material;
            IsToolsMaterial = material.IntAttributes.ContainsKey("tools.toolsmaterial");
            DoNotCastShadows = material.IntAttributes.GetValueOrDefault("F_DO_NOT_CAST_SHADOWS") == 1;
            isRenderBackfaces = IntParams.GetValueOrDefault("F_RENDER_BACKFACES") == 1;
            disableDepthTest = IntParams.GetValueOrDefault("F_DISABLE_Z_BUFFERING") == 1;

            if (ShaderName == "csgo_water_fancy.vfx")
            {
                blendMode = BlendMode.Translucent;
                DoNotCastShadows = true;
                IsCs2Water = true;
                return;
            }

            VertexAnimation = IntParams.GetValueOrDefault("F_VERTEX_ANIMATION") > 0
                || IntParams.GetValueOrDefault("F_FOLIAGE_ANIMATION") > 0;

            // :MaterialIsOverlay
            hasDepthBias = IntParams.GetValueOrDefault("F_DEPTHBIAS") == 1 || IntParams.GetValueOrDefault("F_DEPTH_BIAS") == 1;
            IsOverlay = IntParams.GetValueOrDefault("F_OVERLAY") == 1;

            if (ShaderName == "csgo_decalmodulate.vfx")
            {
                blendMode = BlendMode.Mod2x;
                return;
            }

            if (IntParams.GetValueOrDefault("F_ALPHA_TEST") > 0)
            {
                blendMode = BlendMode.AlphaTest;
            }

            if (IntParams.GetValueOrDefault("F_TRANSLUCENT") == 1
            || TranslucentShaders.AsSpan().Contains(ShaderName)
            || material.IntAttributes.ContainsKey("mapbuilder.water"))
            {
                blendMode = BlendMode.Translucent;
            }

            if (IntParams.GetValueOrDefault("F_ADDITIVE_BLEND") == 1)
            {
                blendMode = BlendMode.Additive;
            }

            // :MaterialIsOverlay
            if (IsTranslucent && hasDepthBias && ShaderName is "csgo_vertexlitgeneric.vfx" or "csgo_complex.vfx")
            {
                IsOverlay = true;
            }

            var blendModeParam = 0;
            if (ShaderName.EndsWith("static_overlay.vfx", StringComparison.Ordinal) || ShaderName is "citadel_overlay.vfx")
            {
                IsOverlay = true;
                blendModeParam = (int)IntParams.GetValueOrDefault("F_BLEND_MODE");
            }

            if (ShaderName == "csgo_unlitgeneric.vfx")
            {
                blendModeParam = (int)IntParams.GetValueOrDefault("F_BLEND_MODE");
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

        /// <summary>
        /// Fills this material's constant buffer if it is stale and binds it to
        /// <see cref="ReservedBufferSlots.Globals"/>.
        /// </summary>
        /// <param name="shader">
        /// The shader being drawn with, whose layout the buffer is filled for. Usually <see cref="Shader"/>,
        /// but a few renderers draw a material through a shader of their own.
        /// </param>
        internal void BindGlobals(Shader shader)
        {
            if (shader.GlobalsLayout.Size == 0)
            {
                return;
            }

            EnsureGlobals(shader).Bind();
        }

        private Globals EnsureGlobals(Shader shader)
        {
            var layout = shader.GlobalsLayout;

            globals ??= new Globals(Material.Name.Length > 0 ? Material.Name : shader.Name);

            if (filledLayout != layout || filledVersion != InputsVersion)
            {
                filledLayout = layout;
                filledVersion = InputsVersion;

                FillGlobals(shader, layout, globals);
            }

            return globals;
        }

        private void FillGlobals(Shader shader, GlobalsLayout layout, Globals buffer)
        {
            buffer.BeginFill(layout);

            var members = layout.Members;

            foreach (var (name, value) in IntParams)
            {
                if (members.TryGetValue(name, out var constant))
                {
                    buffer.SetScalar(constant, value);
                }
            }

            foreach (var (name, value) in FloatParams)
            {
                if (members.TryGetValue(name, out var constant))
                {
                    buffer.SetScalar(constant, value);
                }
            }

            foreach (var (name, value) in VectorParams)
            {
                if (members.TryGetValue(name, out var constant))
                {
                    buffer.SetVector(constant, shader.SrgbUniforms.Contains(name)
                        ? new Vector4(ColorSpace.SrgbGammaToLinear(value.AsVector3()), value.W)
                        : value);
                }
            }

            foreach (var (name, value) in Matrices)
            {
                if (members.TryGetValue(name, out var constant))
                {
                    buffer.SetMatrix(constant, value);
                }
            }

            // todo: dynamic eval

            if (shader.Name.StartsWith("csgo_environment", StringComparison.Ordinal))
            {
                EvalCsgoEnvironmentColorMatrices(shader, buffer);
            }
            else if (shader.Name is "environment_blend.vfx" or "pbr.vfx")
            {
                EvalDeadlockColorMatrices(shader, buffer);
            }
            else if (ShaderName.EndsWith("static_overlay.vfx", StringComparison.Ordinal))
            {
                EvalStaticOverlayColorAdjust(shader, buffer);
            }

            buffer.EndFill();
        }

        [Conditional("DEBUG")]
        private void AssertGlobalsAreForThisShader()
        {
            Debug.Assert(filledLayout is null || filledLayout == Shader.GlobalsLayout,
                $"'{Material.Name}' was last filled for a different shader's layout.");
        }

        /// <summary>Sets a packed global uniform in this material's constant buffer.</summary>
        /// <remarks>
        /// The value survives until the buffer is refilled, which only happens when one of the inputs it is
        /// built from changes, or a shader reload replaces the layout.
        /// </remarks>
        public void SetUniform(string name, float value)
        {
            AssertGlobalsAreForThisShader();

            if (Shader.GlobalsLayout.Members.TryGetValue(name, out var constant))
            {
                EnsureGlobals(Shader).SetScalar(constant, value);
            }
        }

        /// <inheritdoc cref="SetUniform(string, float)"/>
        public void SetUniform(string name, long value)
        {
            AssertGlobalsAreForThisShader();

            if (Shader.GlobalsLayout.Members.TryGetValue(name, out var constant))
            {
                EnsureGlobals(Shader).SetScalar(constant, value);
            }
        }

        /// <inheritdoc cref="SetUniform(string, float)"/>
        public void SetUniform(string name, bool value) => SetUniform(name, value ? 1L : 0L);

        /// <inheritdoc cref="SetUniform(string, float)"/>
        public void SetUniform(string name, Vector4 value)
        {
            AssertGlobalsAreForThisShader();

            if (Shader.GlobalsLayout.Members.TryGetValue(name, out var constant))
            {
                EnsureGlobals(Shader).SetVector(constant, value);
            }
        }

        /// <inheritdoc cref="SetUniform(string, float)"/>
        public void SetUniform(string name, Vector3 value) => SetUniform(name, new Vector4(value, 0f));

        /// <inheritdoc cref="SetUniform(string, float)"/>
        public void SetUniform(string name, Vector2 value) => SetUniform(name, new Vector4(value, 0f, 0f));

        /// <inheritdoc cref="SetUniform(string, float)"/>
        public void SetUniform(string name, Matrix4x4 value)
        {
            AssertGlobalsAreForThisShader();

            if (Shader.GlobalsLayout.Members.TryGetValue(name, out var constant))
            {
                EnsureGlobals(Shader).SetMatrix(constant, value);
            }
        }

        /// <summary>Releases the GPU resources this material owns. Safe to call more than once.</summary>
        public void Delete()
        {
            globals?.Delete();
            globals = null;
            filledLayout = null;
        }

        /// <summary>Binds textures, sets material uniforms, and applies blend/depth render state for this material.</summary>
        /// <param name="shader">The shader to use for this draw call, or <see langword="null"/> to use <see cref="Shader"/>.</param>
        public void Render(Shader? shader = default)
        {
            textureUnit = TextureUnitStart;

            shader ??= Shader;

            if (shader.IgnoreMaterialData)
            {
                if (shader.IsDepthOnlyAlphaTest)
                {
                    shader.Default.BindGlobals(shader);

                    var colorTexture = Textures.GetValueOrDefault("g_tColor");
                    if (colorTexture != null)
                    {
                        shader.SetTexture(textureUnit, "g_tColor", colorTexture);
                    }
                }

                return;
            }

            BindGlobals(shader);

            boundSamplerUnits.Clear();

            var userConfigSampler = 0;
            if (shader.SamplerUserConfigUniforms.Count > 0 && Loader != null)
            {
                var addressModeU = (int)IntParams.GetValueOrDefault("g_nTextureAddressModeU");
                var addressModeV = (int)IntParams.GetValueOrDefault("g_nTextureAddressModeV");
                userConfigSampler = Loader.GetOrCreateSampler(addressModeU, addressModeV);
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

#if DEBUG
            if (maxCombinedTextureImageUnits == 0)
            {
                GL.GetInteger(GetPName.MaxCombinedTextureImageUnits, out maxCombinedTextureImageUnits);
            }

            Debug.Assert(textureUnit <= maxCombinedTextureImageUnits,
                $"'{shader.Name}' needs {textureUnit} texture units ({textureUnit - TextureUnitStart} of its own on top of {TextureUnitStart} reserved) but the driver only has {maxCombinedTextureImageUnits}.");
#endif

            var renderState = Shader.RendererContext.RenderState;
            renderState.Apply(ComposeRenderState(renderState.CurrentPass));
        }

        private static void SetMatrix(Shader shader, Globals buffer, string name, Matrix4x4 value)
        {
            if (shader.GlobalsLayout.Members.TryGetValue(name, out var constant))
            {
                buffer.SetMatrix(constant, value);
            }
        }

        private void EvalCsgoEnvironmentColorMatrices(Shader shader, Globals buffer)
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

            var floatValueLookup = FloatParams.GetAlternateLookup<ReadOnlySpan<char>>();
            var vectorValueLookup = VectorParams.GetAlternateLookup<ReadOnlySpan<char>>();
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
                SetMatrix(shader, buffer, param.Key, ccMatrix);
            }
        }

        // environment_blend suffixes both the matrices and their inputs by layer; pbr has one
        // unsuffixed pair fed by layer 1's params.
        private void EvalDeadlockColorMatrices(Shader shader, Globals buffer)
        {
            const float TintStrength = 0.85f;

            const string albedoCorrectPrefix = "g_mAlbedoColorCorrect";
            const string colorTintPrefix = "g_mTextureColorTint";

            const string csbBaseKeyString = "g_vAlbedoContrastSaturationBrightness1";
            const string tintBaseKeyString = "g_vColorTint1";
            const string tintModeBaseKeyString = "g_nTextureColorTintMode1";
            const string colorTextureBaseKeyString = "g_tColor1";

            Span<char> csbKey = stackalloc char[csbBaseKeyString.Length]; csbBaseKeyString.AsSpan().CopyTo(csbKey);
            Span<char> tintKey = stackalloc char[tintBaseKeyString.Length]; tintBaseKeyString.AsSpan().CopyTo(tintKey);
            Span<char> tintModeKey = stackalloc char[tintModeBaseKeyString.Length]; tintModeBaseKeyString.AsSpan().CopyTo(tintModeKey);
            Span<char> colorTextureKey = stackalloc char[colorTextureBaseKeyString.Length]; colorTextureBaseKeyString.AsSpan().CopyTo(colorTextureKey);

            var vectorValueLookup = VectorParams.GetAlternateLookup<ReadOnlySpan<char>>();
            var intValueLookup = IntParams.GetAlternateLookup<ReadOnlySpan<char>>();
            var textureLookup = Textures.GetAlternateLookup<ReadOnlySpan<char>>();

            foreach (var param in shader.Default.Matrices)
            {
                // Sheen is tinted the same way but has no colour correct matrix, and reads
                // its own params rather than the albedo's.
                if (param.Key == "g_mSheenTextureColorTint")
                {
                    var sheenTint = VectorParams.GetValueOrDefault("g_vSheenColorTint1", Vector4.One).AsVector3();
                    var sheenMode = (int)IntParams.GetValueOrDefault("g_nSheenTextureColorTintMode1", 0L);

                    SetMatrix(shader, buffer, param.Key, VfxEvalFunctions.MatrixColorTint3(ColorSpace.SrgbGammaToLinear(sheenTint), TintStrength, sheenMode));
                    continue;
                }

                var isAlbedoCorrect = param.Key.StartsWith(albedoCorrectPrefix, StringComparison.Ordinal);

                if (!isAlbedoCorrect && !param.Key.StartsWith(colorTintPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                // pbr's unsuffixed pair reads the params layer 1 would have used.
                var isLayered = char.IsAsciiDigit(param.Key[^1]);
                var layerCharacter = isLayered ? param.Key[^1] : '1';

                if (isAlbedoCorrect)
                {
                    csbKey[^1] = layerCharacter;
                    colorTextureKey[^1] = layerCharacter;

                    var csb = Vector3.One;
                    if (vectorValueLookup.TryGetValue(csbKey, out var csbValue))
                    {
                        csb = csbValue.AsVector3();
                    }

                    var textureAverageColor = Vector3.One;
                    if (textureLookup.TryGetValue(isLayered ? colorTextureKey : colorTextureKey[..^1], out var colorTexture))
                    {
                        textureAverageColor = colorTexture.Reflectivity.AsVector3();
                    }

                    SetMatrix(shader, buffer, param.Key, VfxEvalFunctions.MatrixColorCorrect2(csb, textureAverageColor));
                }
                else
                {
                    tintKey[^1] = layerCharacter;
                    tintModeKey[^1] = layerCharacter;

                    var tint = Vector3.One;
                    if (vectorValueLookup.TryGetValue(tintKey, out var tintValue))
                    {
                        tint = tintValue.AsVector3();
                    }

                    intValueLookup.TryGetValue(tintModeKey, out var mode);

                    SetMatrix(shader, buffer, param.Key, VfxEvalFunctions.MatrixColorTint3(ColorSpace.SrgbGammaToLinear(tint), TintStrength, (int)mode));
                }
            }
        }

        private void EvalStaticOverlayColorAdjust(Shader shader, Globals buffer)
        {
            if (!shader.Default.Matrices.ContainsKey("g_mTextureColorAdjust"))
            {
                return;
            }

            var csb = new Vector3(
                FloatParams.GetValueOrDefault("g_fTextureColorContrast", 1f),
                FloatParams.GetValueOrDefault("g_fTextureColorSaturation", 1f),
                FloatParams.GetValueOrDefault("g_fTextureColorBrightness", 1f));

            var tint = VectorParams.GetValueOrDefault("g_vTextureColorCorrectionTint", Vector4.One);

            var textureAverageColor = Vector3.One;
            if (Textures.TryGetValue("g_tColor", out var colorTexture))
            {
                textureAverageColor = colorTexture.Reflectivity.AsVector3();
            }

            var ccMatrix = VfxEvalFunctions.MatrixColorCorrect2(csb, textureAverageColor);
            var tintMatrix = VfxEvalFunctions.MatrixColorTint2(tint.AsVector3(), 1f);

            SetMatrix(shader, buffer, "g_mTextureColorAdjust", Matrix4x4.Multiply(tintMatrix, ccMatrix));
        }

        /// <summary>Unbinds the samplers that were bound for this material's draw call.</summary>
        public void PostRender()
        {
            foreach (var unit in boundSamplerUnits)
            {
                GL.BindSampler(unit, 0);
            }
        }

        /// <summary>Composes this material's render state over the given pass baseline.</summary>
        /// <param name="passState">The baseline state of the enclosing pass.</param>
        /// <returns>The state this material draws with.</returns>
        public RenderState ComposeRenderState(in RenderState passState)
        {
            var state = passState;

            if (IsOverlay)
            {
                state.DepthStencil.DepthWriteEnable = false;
            }

            if (blendMode == BlendMode.AlphaTest)
            {
                // Coverage is a single bit without multisampling, which turns this into a second,
                // harder alpha cutout on top of the one the shader already does.
                state.Blend.AlphaToCoverageEnable = state.Rasterizer.MultisampleEnable;
            }
            else if (blendMode >= BlendMode.Translucent)
            {
                if (IsOverlay)
                {
                    state.BlendEnable = true;
                }

                var (srcBlend, dstBlend) = blendMode switch
                {
                    BlendMode.Additive => (RsBlendMode.SrcAlpha, RsBlendMode.One),
                    BlendMode.Multiply => (RsBlendMode.Zero, RsBlendMode.SrcColor),
                    BlendMode.Mod2x => (RsBlendMode.DestColor, RsBlendMode.SrcColor),
                    BlendMode.ModThenAdd => (RsBlendMode.DestColor, RsBlendMode.InvSrcAlpha),
                    _ => (RsBlendMode.SrcAlpha, RsBlendMode.InvSrcAlpha),
                };
                state.SetBlend(srcBlend, dstBlend);
            }

            if (hasDepthBias || IsOverlay)
            {
                state.Rasterizer.DepthBias = 64;
                state.Rasterizer.DepthBiasClamp = 0.0005f;
            }

            if (isRenderBackfaces)
            {
                state.Rasterizer.CullMode = RsCullMode.None;
            }

            if (disableDepthTest)
            {
                state.DepthStencil.DepthTestEnable = false;
            }

            return state;
        }
    }
}
