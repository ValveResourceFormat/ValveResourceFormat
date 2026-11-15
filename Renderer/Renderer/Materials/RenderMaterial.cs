using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Utils;
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
        /// <summary>Baked directional irradiance, whole, red split, or a luminance direction.</summary>
        [SamplerName("g_tDirectionalIrradiance", "g_tDirectionalIrradianceR", "g_tDirectionalIrradianceLum")]
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
        [SamplerName("g_tLightmapDebugCharts")]
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
        /// <summary>Nearest depth of the resolved scene and the translucent surfaces in front of it.</summary>
        [SamplerName("g_tTranslucentSceneDepth")]
        TranslucentSceneDepth,
        /// <summary>Screen space water effects map written by the water effects layer.</summary>
        [SamplerName("g_tWaterEffectsMap")]
        WaterEffectsMap,
        /// <summary>The scene's morph composite atlas, see <see cref="MorphCompositeAtlas"/>.</summary>
        [SamplerName("g_tCompositeMorphTextureAtlas")]
        MorphCompositeTexture,
        /// <summary>The volumetric fog volume of the view being drawn.</summary>
        [SamplerName("g_tFogVolume")]
        FogVolume,
        /// <summary>The gradient fog texture of the view being drawn.</summary>
        [SamplerName("g_tGradientFogTexture")]
        GradientFogTexture,
        /// <summary>The volume texture whose slices are the cookies of per-pixel spot lights.</summary>
        [SamplerName("g_tLightCookieVolume")]
        LightCookieVolume,
        /// <summary>Screen space ambient occlusion of the opaque scene, 1 where nothing occludes.</summary>
        [SamplerName("g_tSsao")]
        Ssao,
        /// <summary>Last reserved slot.</summary>
        Last = Ssao,
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
        public MaterialInputs<long> IntParams { get; } = [];

        /// <summary>Gets the float parameters, seeded from <see cref="Material"/> and writable per instance.</summary>
        public MaterialInputs<float> FloatParams { get; } = [];

        /// <summary>Gets the vector parameters, seeded from <see cref="Material"/> and writable per instance.</summary>
        public MaterialInputs<Vector4> VectorParams { get; } = [];

        /// <summary>Gets the map of matrix uniform names to their current values for this material.</summary>
        public MaterialInputs<Matrix4x4> Matrices { get; } = [];

        /// <summary>Gets the map of texture uniform names to the bound <see cref="RenderTexture"/> objects for this material.</summary>
        public MaterialInputs<RenderTexture> Textures { get; } = [];

        /// <summary>Tells the streamed textures of this material that a mesh draws them.</summary>
        internal void MarkTexturesDrawnByMesh()
        {
            foreach (var texture in Textures.Values)
            {
                if (texture.Stream is { } stream)
                {
                    stream.DrawnByMesh = true;
                }
            }
        }

        /// <summary>Gets or sets a value indicating whether this material is rendered as a screen-space or world-space overlay (polygon-offset, no depth write).</summary>
        public bool IsOverlay { get; set; }

        /// <summary>Gets a value indicating whether this is a tools-only material that should not appear in normal rendering.</summary>
        public bool IsToolsMaterial { get; private set; }

        /// <summary>
        /// Gets a value indicating whether geometry using this material blocks light: it casts shadows even when it
        /// is not drawn, such as a tools material that is hidden.
        /// </summary>
        public bool BlocksLight { get; private set; }

        /// <summary>Gets a value indicating whether this material uses the CS2 water rendering path.</summary>
        public bool IsCs2Water { get; private set; }

        /// <summary>
        /// Gets a value indicating whether geometry using this material shows the sky in place of the world behind it,
        /// see <see cref="SceneViewState.RenderSkyOccluderMask"/>.
        /// </summary>
        public bool IsSkyOccluder { get; private set; }

        /// <summary>
        /// Whether this material's shader samples the scene color texture, so it must be drawn after the framebuffer
        /// grab in <see cref="RenderPass.OpaqueRefract"/> or <see cref="RenderPass.Water"/>. Queried live: shaders compile
        /// without blocking, so this only becomes true once the program is linked and reflected, one frame after the
        /// material is first collected.
        /// </summary>
        public bool ReadsSceneColor => Shader.ReadsSceneColor;

        /// <summary>Gets a value indicating whether this material drives vertex animation (foliage or morph-based).</summary>
        public bool VertexAnimation { get; private set; }

        /// <summary>Gets a value indicating whether geometry using this material should be excluded from shadow passes.</summary>
        public bool DoNotCastShadows { get; private set; }

        /// <summary>Gets a value indicating whether this material uses any blending mode that requires back-to-front ordering.</summary>
        public bool IsTranslucent { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this translucent material also draws its back faces opaque, before the
        /// translucent surface, so that the inside of the mesh shows through.
        /// </summary>
        public bool HasInterior { get; private set; }

        /// <summary>Gets a value indicating whether this material only adds light over the destination, hiding nothing behind it.</summary>
        public bool IsAdditive => IsTranslucent && destBlend == RsBlendMode.One;

        /// <summary>
        /// Gets a value indicating whether this translucent material first writes depth over its whole silhouette, so
        /// that only the nearest surface of each mesh blends.
        /// </summary>
        public bool WritesDepthBeforeBlending { get; private set; }

        /// <summary>Gets a value indicating whether this material uses alpha-to-coverage alpha testing.</summary>
        public bool IsAlphaTest { get; private set; }

        /// <summary>
        /// Gets whether this material can perform early depth priming and then shade with Equal comparison.
        /// An alpha-tested material is primed even when it opts out of the depth prepass.
        /// </summary>
        public bool CanPrimeDepth => !IsOverlay && !disableDepthTest && !HasInterior
            && (IsAlphaTest || (IntParams.GetValueOrDefault("F_NO_Z_PREPASS") == 0 && IntParams.GetValueOrDefault("F_DISABLE_Z_PREPASS") == 0));

        private readonly RendererContext? Context;

        private GlobalsState? globals;

        private uint InputsVersion => unchecked(
            IntParams.Version + FloatParams.Version + VectorParams.Version + Matrices.Version + Textures.Version);

        private readonly IShaderDataProvider shaderDataProvider;
        private ExpressionContext expressionContext;
        private RsBlendMode srcBlend;
        private RsBlendMode destBlend;
        private bool isRenderBackfaces;
        private bool hasDepthBias;
        private bool disableDepthTest;
        private bool translucentDepthWrite;
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
            : this(material, rendererContext.ShaderDataProvider)
        {
            Context = rendererContext;

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
                    foreach (var feature in shader.Features.StaticComboArray)
                    {
                        if (feature.Name.StartsWith("F_TEXTURE_FORMAT", StringComparison.Ordinal))
                        {
                            for (byte i = 0; i < feature.StateNames.Length; i++)
                            {
                                var checkbox = feature.StateNames[i];

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
        public RenderMaterial(Shader shader) : this(new Material { Resource = null!, ShaderName = shader.Name }, BasicShaderData, normalizeShaderName: false)
        {
            Shader = shader;
            SortId = GetSortId();
        }

        /// <summary>Sort ID range per shader program: groups draws by shader, arbitrary order within a group.</summary>
        public const int PerShaderSortIdRange = 10_000;

        // Arbitrary within a shader but the same on every load, so that coplanar and blended draws resolve the same way each time
        private int GetSortId() => Shader.Program * PerShaderSortIdRange + 1 + (int)(StringToken.Get(Material.Name) % (PerShaderSortIdRange - 2));

        private static readonly BasicShaderDataProvider BasicShaderData = new();

        // Renderer shaders are wrapped by their own name, only material shader names are normalized
        RenderMaterial(Material material, IShaderDataProvider shaderDataProvider, bool normalizeShaderName = true)
        {
            Material = material;
            this.shaderDataProvider = shaderDataProvider;
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
        [MemberNotNull(nameof(expressionContext))]
        public void LoadRenderState()
        {
            var material = Material;
            var attributes = shaderDataProvider.GetIntAttributes(material, IntParams);
            expressionContext = new ExpressionContext(this, shaderDataProvider.GetExpressions(material, IntParams));
            IsToolsMaterial = attributes.ContainsKey("tools.toolsmaterial");
            BlocksLight = attributes.GetValueOrDefault("mapbuilder.blocklight") != 0;
            DoNotCastShadows = attributes.GetValueOrDefault("DoNotCastShadows") == 1;
            IsCs2Water = attributes.GetValueOrDefault("bCsgoWater") == 1;
            IsSkyOccluder = ShaderName == "sky_occluder.vfx";
            isRenderBackfaces = shaderDataProvider.GetRenderBackfaces(material, IntParams);
            disableDepthTest = IntParams.GetValueOrDefault("F_DISABLE_Z_BUFFERING") == 1;

            var blendState = shaderDataProvider.GetBlendState(material, IntParams);
            HasInterior = attributes.GetValueOrDefault("crystal") == 1;
            IsTranslucent = HasInterior || attributes.GetValueOrDefault("translucent") == 1;
            IsAlphaTest = !IsTranslucent && blendState.AlphaToCoverageEnable;

            // Our shaders output color without alpha premultiplied, unlike some game shaders, so only factors
            // that multiply the background are used as compiled. Translucency that is not baked blends normally.
            (srcBlend, destBlend) = (blendState.SrcBlend[0], blendState.DestBlend[0]) switch
            {
                _ when !blendState.BlendEnable[0] => (RsBlendMode.SrcAlpha, RsBlendMode.InvSrcAlpha),
                (_, RsBlendMode.One) => (RsBlendMode.SrcAlpha, RsBlendMode.One),
                (RsBlendMode.DestColor or RsBlendMode.Zero, _) and var modulate => modulate,
                _ => (RsBlendMode.SrcAlpha, RsBlendMode.InvSrcAlpha),
            };

            if (IsCs2Water)
            {
                return;
            }

            VertexAnimation = shaderDataProvider.GetVertexAnimation(material, IntParams);

            hasDepthBias = IntParams.GetValueOrDefault("F_DEPTHBIAS") == 1 || IntParams.GetValueOrDefault("F_DEPTH_BIAS") == 1;
            IsOverlay = attributes.GetValueOrDefault("overlay") == 1;
            DoNotCastShadows |= IsOverlay;

            var canWriteTranslucentDepth = IsTranslucent && !IsOverlay && !disableDepthTest;

            translucentDepthWrite = canWriteTranslucentDepth && shaderDataProvider.GetTranslucentDepthWrite(material, IntParams);

            WritesDepthBeforeBlending = canWriteTranslucentDepth
                && IntParams.GetValueOrDefault("F_WRITE_DEPTH_BEFORE_ALPHA_BLENDING") == 1
                && IntParams.GetValueOrDefault("F_DISABLE_DEPTH_WRITE") == 0;
        }

        /// <summary>
        /// Fills this material's constant buffer if it is stale and binds it to
        /// <see cref="ReservedBufferSlots.Globals"/>.
        /// </summary>
        /// <param name="shader">
        /// The shader being drawn with, whose layout the buffer is filled for. Usually <see cref="Shader"/>,
        /// but a few renderers draw a material through a shader of their own.
        /// </param>
        /// <param name="attributes">Node render attributes to bake in, if any.</param>
        internal void BindGlobals(Shader shader, RenderAttributes? attributes = null)
        {
            if (shader.GlobalsLayout.Size == 0)
            {
                return;
            }

            if (attributes?.Overrides(this, shader.GlobalsLayout) != true)
            {
                attributes = null;
            }

            EnsureGlobals(shader, attributes).Bind();
        }

        /// <summary>A constant buffer and what it was last filled from.</summary>
        internal sealed class GlobalsState(Globals buffer)
        {
            public Globals Buffer { get; } = buffer;
            public GlobalsLayout? Layout { get; set; }
            public uint InputsVersion { get; set; }
            public uint AttributesVersion { get; set; }
            public float EvaluatedTime { get; set; }
            public bool ExpressionsConstant { get; set; }
        }

        // The material's own buffer, or the one baked with node attributes
        private Globals EnsureGlobals(Shader shader, RenderAttributes? attributes = null)
        {
            var state = attributes == null
                ? globals ??= new GlobalsState(new Globals(GetGlobalsName(shader)))
                : CollectionsMarshal.GetValueRefOrAddDefault(attributes.Baked, this, out _) ??= new GlobalsState(new Globals(GetGlobalsName(shader)));

            var layout = shader.GlobalsLayout;
            var attributesVersion = attributes?.Version ?? 0;
            var buffer = state.Buffer;

            if (state.Layout != layout || state.InputsVersion != InputsVersion || state.AttributesVersion != attributesVersion)
            {
                state.Layout = layout;
                state.InputsVersion = InputsVersion;
                state.AttributesVersion = attributesVersion;

                buffer.BeginFill(layout);
                SetParams(shader, buffer);
            }
            else if (state.ExpressionsConstant || state.EvaluatedTime == expressionContext.Time)
            {
                return buffer;
            }
            else
            {
                // Expressions that read time or attributes are evaluated again once per frame
                buffer.BeginUpdate();
            }

            state.EvaluatedTime = expressionContext.Time;
            state.ExpressionsConstant = EvaluateExpressions(shader, buffer, attributes);

            // Attributes go last so they beat derived constants
            if (attributes != null)
            {
                foreach (var (name, value) in attributes.Values)
                {
                    if (layout.Members.TryGetValue(name, out var constant))
                    {
                        SetEvaluated(shader, buffer, constant, name, value.ToVector4());
                    }
                }
            }

            buffer.EndFill();
            return buffer;
        }

        private string GetGlobalsName(Shader shader) => Material.Name.Length > 0 ? Material.Name : shader.Name;

        private void SetParams(Shader shader, Globals buffer)
        {
            var members = shader.GlobalsLayout.Members;

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
                    SetVectorParam(shader, buffer, constant, name, value);
                }
            }

            foreach (var (name, value) in Matrices)
            {
                if (members.TryGetValue(name, out var constant))
                {
                    buffer.SetMatrix(constant, value);
                }
            }
        }

        private bool EvaluateExpressions(Shader shader, Globals buffer, RenderAttributes? attributes)
        {
            var members = shader.GlobalsLayout.Members;
            var variables = expressionContext.Variables;

            expressionContext.IsConstant = true;
            expressionContext.Attributes = attributes;

            // Game shader files compile the same expressions, with constants folded per static combo
            foreach (var (name, expression) in shader.Expressions)
            {
                Evaluate(shader, buffer, members, name, variables.TryGetValue(name, out var variable) ? variable.CompiledExpression : expression);
            }

            // Other game expressions mostly convert a raw parameter our shaders already convert, matrices are final
            foreach (var (name, variable) in variables)
            {
                if (variable.VfxType == VfxVariableType.Float4x4 && !shader.Expressions.ContainsKey(name))
                {
                    Evaluate(shader, buffer, members, name, variable.CompiledExpression);
                }
            }

            foreach (var (name, expression) in Material.DynamicParams)
            {
                if (!IsOverridden(name) && members.TryGetValue(name, out var constant) && expressionContext.TryEvaluate(expression, out var value))
                {
                    SetEvaluated(shader, buffer, constant, name, value);
                }
            }

            return expressionContext.IsConstant;
        }

        private void Evaluate(Shader shader, Globals buffer, IReadOnlyDictionary<string, GlobalsMember> members, string name, byte[] expression)
        {
            if (!members.TryGetValue(name, out var constant))
            {
                return;
            }

            if (constant.Type == GlobalsType.Mat4)
            {
                if (!Matrices.ContainsKey(name) && expressionContext.TryEvaluateMatrix(expression, out var matrix))
                {
                    buffer.SetMatrix(constant, matrix);
                }
            }
            else if (expressionContext.TryEvaluate(expression, out var value))
            {
                SetEvaluated(shader, buffer, constant, name, value);
            }
        }

        private static void SetEvaluated(Shader shader, Globals buffer, in GlobalsMember constant, string name, Vector4 value)
        {
            if (constant.ComponentCount == 1)
            {
                buffer.SetScalar(constant, value.X);
                return;
            }

            SetVectorParam(shader, buffer, constant, name, value);
        }

        private static void SetVectorParam(Shader shader, Globals buffer, in GlobalsMember constant, string name, Vector4 value)
        {
            buffer.SetVector(constant, shader.SrgbUniforms.Contains(name)
                ? new Vector4(ColorSpace.SrgbGammaToLinear(value.AsVector3()), value.W)
                : value);
        }

        private bool TryGetParam(string name, out Vector4 value)
        {
            if (VectorParams.TryGetValue(name, out value))
            {
                return true;
            }

            if (FloatParams.TryGetValue(name, out var scalar))
            {
                value = new Vector4(scalar);
                return true;
            }

            if (IntParams.TryGetValue(name, out var integer))
            {
                value = new Vector4(integer);
                return true;
            }

            return false;
        }

        private bool IsOverridden(string name)
        {
            if (VectorParams.TryGetValue(name, out var vector))
            {
                return vector != Material.VectorParams.GetValueOrDefault(name);
            }

            if (FloatParams.TryGetValue(name, out var scalar))
            {
                return scalar != Material.FloatParams.GetValueOrDefault(name);
            }

            return IntParams.TryGetValue(name, out var integer) && integer != Material.IntParams.GetValueOrDefault(name);
        }

        [Conditional("DEBUG")]
        private void AssertGlobalsAreForThisShader()
        {
            Debug.Assert(globals?.Layout is null || globals.Layout == Shader.GlobalsLayout,
                $"'{Material.Name}' was last filled for a different shader's layout.");
        }

        /// <summary>Sets a packed global uniform in this material's constant buffer.</summary>
        /// <remarks>
        /// The value survives until the buffer is refilled. A refill happens only when an input of the buffer
        /// changes or a shader reload replaces the layout.
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
            globals?.Buffer.Delete();
            globals = null;

            depthMaterial?.Delete();
            depthMaterial = null;
        }

        private RenderMaterial? depthMaterial;
        private RenderTexture? depthColorTexture;
        private uint depthMaterialVersion;

        /// <summary>Gets the texture holding this material's color and opacity, under whichever name its shader gives it.</summary>
        /// <returns>The bound color texture, or <see langword="null"/> when the material binds none.</returns>
        internal RenderTexture? GetColorTexture()
        {
            var name = Shader.RepresentativeTexture;

            if (name == null)
            {
                return null;
            }

            return Textures.GetValueOrDefault(name);
        }

        /// <summary>Material for a shared depth-only shader, holding the color texture and alpha reference of this material.</summary>
        private RenderMaterial GetDepthMaterial(Shader depthShader)
        {
            if (depthMaterial != null && depthMaterialVersion == InputsVersion)
            {
                return depthMaterial;
            }

            var depth = depthMaterial ??= new RenderMaterial(depthShader);
            depthMaterialVersion = InputsVersion;

            depth.FloatParams["g_flAlphaTestReference"] = FloatParams.GetValueOrDefault("g_flAlphaTestReference", 0.5f);

            // Without a color texture the alpha test would sample whatever the unit was left holding
            depthColorTexture = GetColorTexture() ?? Context?.MaterialLoader.GetDefaultColor();

            return depth;
        }

        /// <summary>Binds textures, sets material uniforms, and applies blend/depth render state for this material.</summary>
        /// <param name="shader">The shader to use for this draw call, or <see langword="null"/> to use <see cref="Shader"/>.</param>
        /// <param name="depthPass">Whether the draw only lays down depth. A shared depth shader still gets the render state of this material.</param>
        /// <param name="attributes">Node render attributes. Ignored by shaders without material data.</param>
        public void Render(Shader? shader = default, bool depthPass = false, RenderAttributes? attributes = null)
        {
            textureUnit = TextureUnitStart;

            shader ??= Shader;

            if (shader.IgnoreMaterialData)
            {
                if (shader.IsAlphaTestVariant)
                {
                    GetDepthMaterial(shader).BindGlobals(shader);

                    if (depthColorTexture != null)
                    {
                        shader.SetTexture(textureUnit, "g_tColor", depthColorTexture);
                    }
                }

                if (depthPass)
                {
                    var depthState = GraphicsContext.RenderState;
                    depthState.Apply(ComposeRenderState(depthState.CurrentPass));
                }

                return;
            }

            BindGlobals(shader, attributes);

            boundSamplerUnits.Clear();

            var userConfigSampler = 0;
            if (shader.SamplerUserConfigUniforms.Count > 0 && Context?.MaterialLoader is { } loader)
            {
                var addressModeU = ToAddressMode(IntParams.GetValueOrDefault("g_nTextureAddressModeU"));
                var addressModeV = ToAddressMode(IntParams.GetValueOrDefault("g_nTextureAddressModeV"));
                userConfigSampler = loader.GetOrCreateSampler(addressModeU, addressModeV);

                static RsTextureAddressMode ToAddressMode(long mode)
                    => mode is >= 0 and <= (long)RsTextureAddressMode.MirrorOnce
                        ? (RsTextureAddressMode)mode
                        : RsTextureAddressMode.Wrap;
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

            var renderState = GraphicsContext.RenderState;
            renderState.Apply(ComposeRenderState(renderState.CurrentPass, shader.IsInteriorVariant));
        }

        private sealed class ExpressionContext(RenderMaterial material, ShaderExpressions? expressions) : IVfxEvalContext
        {
            // Parameters reading each other in a loop would otherwise overflow the stack
            private const int MaxDepth = 8;
            private int depth;

            public IReadOnlyDictionary<string, VfxVariableDescription> Variables { get; } = expressions?.Variables ?? ReadOnlyDictionary<string, VfxVariableDescription>.Empty;

            private bool TryGetVariable(StringToken name, [NotNullWhen(true)] out VfxVariableDescription? variable)
            {
                variable = null;
                return expressions?.Shader.VariablesByToken.TryGetValue(name, out variable) == true;
            }

            /// <summary>Gets or sets whether everything evaluated since this was last set only depends on the material.</summary>
            public bool IsConstant { get; set; } = true;

            /// <summary>Gets or sets the node render attributes of the buffer being filled, null for the material's own.</summary>
            public RenderAttributes? Attributes { get; set; }

            public float Time => material.Context?.Uptime ?? 0f;

            public bool TryGetAttribute(StringToken name, out Vector4 value)
            {
                value = default;
                return Attributes?.TryGetValue(name, out value) == true
                    || material.Context?.GlobalRenderAttributes.TryGetValue(name, out value) == true;
            }

            public bool TryEvaluate(byte[] expression, out Vector4 value)
            {
                var success = VfxEvalRuntime.TryEvaluate(expression, this, out value, out var isConstant);
                IsConstant &= isConstant;
                return success;
            }

            public bool TryEvaluateMatrix(byte[] expression, out Matrix4x4 value)
            {
                var success = VfxEvalRuntime.TryEvaluateMatrix(expression, this, out value, out var isConstant);
                IsConstant &= isConstant;
                return success;
            }

            public bool TryGetMaterialParam(StringToken name, out Vector4 value)
            {
                TryGetVariable(name, out var variable);
                var paramName = variable?.Name ?? name.Name;

                // Expressions in our shaders read features by name, there is no features file to index
                if (paramName.StartsWith("F_", StringComparison.Ordinal))
                {
                    value = new Vector4(material.IntParams.GetValueOrDefault(paramName));
                    return true;
                }

                // A parameter with an expression reads as its result, or its baked value when that fails
                if (depth < MaxDepth && !material.IsOverridden(paramName) && material.Material.DynamicParams.TryGetValue(paramName, out var expression))
                {
                    depth++;
                    var success = TryEvaluate(expression, out value);
                    depth--;

                    if (success)
                    {
                        return true;
                    }
                }

                if (material.TryGetParam(paramName, out value) || material.Shader.Default.TryGetParam(paramName, out value))
                {
                    return true;
                }

                if (variable == null)
                {
                    return false;
                }

                var floats = variable.FloatDefs;
                var ints = variable.IntDefs;

                Vector4? fallback = variable.VfxType switch
                {
                    VfxVariableType.Float => new Vector4(floats[0]),
                    VfxVariableType.Float2 or VfxVariableType.Float3 or VfxVariableType.Float4 => new Vector4(floats[0], floats[1], floats[2], floats[3]),
                    VfxVariableType.Int or VfxVariableType.Bool => new Vector4(ints[0]),
                    VfxVariableType.Int2 or VfxVariableType.Int3 or VfxVariableType.Int4 => new Vector4(ints[0], ints[1], ints[2], ints[3]),
                    _ => null,
                };

                value = fallback.GetValueOrDefault();
                return fallback.HasValue;
            }

            public bool IsTextureParam(StringToken name)
                => TryGetVariable(name, out var variable)
                    ? variable.RegisterType is VfxRegisterType.Texture or VfxRegisterType.TextureIndex
                    : name.Name.StartsWith("g_t", StringComparison.Ordinal);

            public int GetFeature(int index)
                => expressions?.Shader.Features is { } features ? (int)material.IntParams.GetValueOrDefault(features.StaticComboArray[index].Name) : 0;

            public Vector4 GetTextureSize(StringToken textureName)
                => TryGetTexture(textureName, out var texture) ? new Vector4(texture.Width, texture.Height, texture.Depth, 0f) : Vector4.Zero;

            public Vector4 GetTextureAverageColor(StringToken textureName)
                => TryGetTexture(textureName, out var texture) ? new Vector4(texture.Reflectivity.AsVector3(), 1f) : Vector4.One;

            private bool TryGetTexture(StringToken textureName, [NotNullWhen(true)] out RenderTexture? texture)
            {
                TryGetVariable(textureName, out var variable);
                return material.Textures.TryGetValue(variable?.Name ?? textureName.Name, out texture);
            }
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
        /// <param name="interior">Whether the draw is the opaque back face pass of <see cref="HasInterior"/>, which does not blend.</param>
        /// <returns>The state this material draws with.</returns>
        public RenderState ComposeRenderState(in RenderState passState, bool interior = false)
        {
            var state = passState;

            if (IsOverlay)
            {
                state.DepthStencil.DepthWriteEnable = false;
            }

            if (IsAlphaTest)
            {
                // Coverage is a single bit without multisampling, which turns this into a second,
                // harder alpha cutout on top of the one the shader already does.
                // A pass that tests against prepass depth takes its coverage from that depth. A second coverage
                // mask would leave samples where the masks disagree with depth and no color.
                var coverageFromPrepass = state.DepthStencil.DepthFunc == RsComparison.Equal;
                state.Blend.AlphaToCoverageEnable = state.Rasterizer.MultisampleEnable && !coverageFromPrepass;
            }
            else if (interior)
            {
                state.BlendEnable = false;
                state.DepthStencil.DepthWriteEnable = true;
                state.Rasterizer.CullMode = RsCullMode.Front;
            }
            else if (IsTranslucent)
            {
                if (IsOverlay)
                {
                    state.BlendEnable = true;
                }

                state.SetBlend(srcBlend, destBlend);

                if (WritesDepthBeforeBlending)
                {
                    // The blended draw lands on the depth its own first draw wrote
                    state.DepthStencil.DepthWriteEnable = true;
                    state.DepthStencil.DepthFunc = RsComparison.CloserEqual;
                }
                else if (translucentDepthWrite)
                {
                    state.DepthStencil.DepthWriteEnable = true;
                }
            }

            if (hasDepthBias || IsOverlay)
            {
                state.Rasterizer.DepthBias = 64;
                state.Rasterizer.DepthBiasClamp = 0.0005f;
            }

            if (isRenderBackfaces && !interior)
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
