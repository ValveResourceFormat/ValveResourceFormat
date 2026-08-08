using System.Buffers;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders particles as camera-facing or orientation-aligned textured quads (sprites),
    /// with support for sprite sheet animation, blend modes, and per-particle color and alpha.
    /// </summary>
    /// <remarks>
    /// The workhorse renderer used by most effects. Multi-frame sequences can be animated or
    /// used to provide visual variation.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderSprites">C_OP_RenderSprites</seealso>
    internal class RenderSprites : ParticleFunctionRenderer
    {
        private const string ShaderName = "particle_sprite";
        // position 3, colour 4, uv 2, next-frame uv 2, frame blend 1
        private const int VertexSize = 12 + ((MaxTextureLayers - 1) * 4);

        // The shader keeps one sampler per layer, so this is a hard ceiling rather than a preference.
        private const int MaxTextureLayers = 5;

        private const string DefaultTextureName = "materials/particle/base_sprite.vtex";

        private static readonly INumberProvider OneNumberProvider = new LiteralNumberProvider(1f);

        // Interpolated names would allocate on every draw, and this is per-frame renderer code.
        private static readonly string[] LayerTextureUniforms = ["uTexture", "uTextureLayer1", "uTextureLayer2", "uTextureLayer3", "uTextureLayer4"];
        private static readonly string[] LayerChannelsUniforms = ["uLayerChannels[0]", "uLayerChannels[1]", "uLayerChannels[2]", "uLayerChannels[3]", "uLayerChannels[4]"];
        private static readonly string[] LayerBlendModeUniforms = ["uLayerBlendMode[0]", "uLayerBlendMode[1]", "uLayerBlendMode[2]", "uLayerBlendMode[3]", "uLayerBlendMode[4]"];
        private static readonly string[] LayerBlendUniforms = ["uLayerBlend[0]", "uLayerBlend[1]", "uLayerBlend[2]", "uLayerBlend[3]", "uLayerBlend[4]"];
        private static readonly string[] LayerEffectModeUniforms = ["uLayerEffectMode[0]", "uLayerEffectMode[1]", "uLayerEffectMode[2]", "uLayerEffectMode[3]", "uLayerEffectMode[4]"];

        /// <summary>One entry of m_vecTexturesInput: a texture plus how it folds into the layers below it.</summary>
        private sealed class TextureLayer(RenderTexture texture)
        {
            public RenderTexture Texture { get; set; } = texture;
            public SpriteCardTextureChannel Channels { get; init; } = SpriteCardTextureChannel.SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA;
            public SpriteCardTextureType EffectMode { get; init; } = SpriteCardTextureType.SPRITECARD_TEXTURE_DIFFUSE;
            public ParticleTextureLayerBlendType BlendMode { get; init; } = ParticleTextureLayerBlendType.SPRITECARD_TEXTURE_BLEND_MULTIPLY;
            public INumberProvider Blend { get; init; } = OneNumberProvider;
        }

        private readonly Shader shader;
        private readonly RendererContext RendererContext;
        private readonly int vaoHandle;
        private readonly TextureLayer[] layers;

        private readonly float animationRate = 0.1f;
        private readonly ParticleAnimationType animationType = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;
        private readonly INumberProvider minSize = new LiteralNumberProvider(0f);
        private readonly INumberProvider maxSize = new LiteralNumberProvider(5000f);
        private readonly INumberProvider startFadeSize = new LiteralNumberProvider(100000000f);
        private readonly INumberProvider endFadeSize = new LiteralNumberProvider(200000000f);
        private readonly bool distanceAlpha;

        // m_flStartFadeDot/m_flEndFadeDot: the normal-aligned modes fade out as the card turns edge-on to
        // the camera. The defaults span 1..2 against a value that never exceeds 1, so no fade by default.
        private readonly float startFadeDot = 1f;
        private readonly float endFadeDot = 2f;

        // m_flCenterXOffset/m_flCenterYOffset shift the quad within its own corner space, before the
        // radius scale, so the card pivots about a point other than its middle.
        private readonly INumberProvider centerXOffset = new LiteralNumberProvider(0f);
        private readonly INumberProvider centerYOffset = new LiteralNumberProvider(0f);
        // Both default on: shipped content only ever writes them as false, which is how the compiled KV
        // reveals a default it omits.
        private readonly bool gammaCorrectVertexColors = true;
        private readonly bool saturateColorPreAlphaBlend = true;

        // m_bBlendFramesSeq0 cross-fades consecutive sheet frames instead of stepping between them.
        // m_bMaxLuminanceBlendingSequence0 swaps the plain lerp for a luminance-weighted one, which keeps
        // the brighter of the two frames dominant through the cross-fade.
        private readonly bool blendFrames = true;
        private readonly bool maxLuminanceFrameBlend;

        private readonly INumberProvider radiusScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider alphaScale = new LiteralNumberProvider(1f);
        private readonly IVectorProvider colorScale = new LiteralVectorProvider(Vector3.One);

        private readonly bool animateInFps;
        private readonly ParticleBlendMode blendMode = ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA;
        private readonly INumberProvider overbrightFactor = new LiteralNumberProvider(1);
        private readonly ParticleOrientation orientationType;
        private readonly INumberProvider diffuseAmount = new LiteralNumberProvider(1);
        private readonly INumberProvider selfIllumAmount = new LiteralNumberProvider(0);
        private readonly INumberProvider alphaMapToZero = new LiteralNumberProvider(0);
        private readonly INumberProvider alphaMapToOne = new LiteralNumberProvider(1);

        private readonly INumberProvider desaturation = new LiteralNumberProvider(0);
        // -1 means no control point, so no shift.
        private readonly int hsvShiftControlPoint = -1;

        private readonly bool outline;
        private readonly Vector4 outlineColor = Vector4.One;
        // Start0, End0, Start1, End1 -- the order the shader's two-sided ramp wants them in.
        private readonly Vector4 outlineRanges = new(0.5f, 0.7f, 0.6f, 0.8f);
        private int vertexBufferHandle;


        public RenderSprites(ParticleDefinitionParser parse, RendererContext rendererContext) : base(parse)
        {
            RendererContext = rendererContext;

            blendMode = parse.Enum<ParticleBlendMode>("m_nOutputBlendMode", blendMode);

            shader = RendererContext.ShaderLoader.LoadShader(ShaderName);

            // The same quad is reused for all particles
            vaoHandle = SetupQuadBuffer();

            string? textureName = null;

            if (parse.Data.ContainsKey("m_hTexture"))
            {
                // Legacy single-texture form; equivalent to one layer with every control at its default.
                textureName = parse.Data.GetStringProperty("m_hTexture");
                layers = [new TextureLayer(rendererContext.MaterialLoader.GetTexture(textureName, srgbRead: true))];
            }
            else
            {
                var parsed = new List<TextureLayer>();

                foreach (var textureInput in parse.Array("m_vecTexturesInput"))
                {
                    if (!textureInput.Boolean("m_bEnabled", true))
                    {
                        continue;
                    }

                    // Normal maps and motion vector sheets are not colour: compositing them into the chain
                    // would tint the card with a tangent-space basis or a flow field. They are the majority
                    // of the extra layers in shipped content, so this matters more than it sounds.
                    var textureType = textureInput.Enum("m_nTextureType", SpriteCardTextureType.SPRITECARD_TEXTURE_DIFFUSE);

                    if (textureType is SpriteCardTextureType.SPRITECARD_TEXTURE_NORMALMAP
                        or SpriteCardTextureType.SPRITECARD_TEXTURE_ANIMMOTIONVEC)
                    {
                        continue;
                    }

                    // A gradient layer synthesizes its ramp from m_Gradient rather than loading a texture.
                    var replaceWithGradient = textureInput.Boolean("m_bReplaceTextureWithGradient", false);

                    if (parsed.Count == MaxTextureLayers)
                    {
                        break;
                    }

                    RenderTexture layerTexture;

                    if (replaceWithGradient)
                    {
                        layerTexture = MaterialLoader.GenerateGradientTexture(ParseGradientStops(textureInput));
                    }
                    else
                    {
                        var layerTextureName = textureInput.Data.ContainsKey("m_hTexture")
                            ? textureInput.Data.GetStringProperty("m_hTexture")
                            : null;

                        if (string.IsNullOrEmpty(layerTextureName))
                        {
                            layerTextureName = DefaultTextureName;
                        }

                        textureName ??= layerTextureName;
                        layerTexture = rendererContext.MaterialLoader.GetTexture(layerTextureName, srgbRead: true);
                    }

                    parsed.Add(new TextureLayer(layerTexture)
                    {
                        Channels = textureInput.Enum("m_nTextureChannels", SpriteCardTextureChannel.SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA),
                        BlendMode = textureInput.Enum("m_nTextureBlendMode", ParticleTextureLayerBlendType.SPRITECARD_TEXTURE_BLEND_MULTIPLY),
                        Blend = textureInput.NumberProvider("m_flTextureBlend", OneNumberProvider),
                        EffectMode = textureType,
                    });
                }

                layers = parsed.Count > 0
                    ? [.. parsed]
                    : [new TextureLayer(rendererContext.MaterialLoader.GetTexture(DefaultTextureName, srgbRead: true))];
            }

#if DEBUG
            var vaoLabel = $"{nameof(RenderSprites)}: {System.IO.Path.GetFileName(textureName)}";
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, Math.Min(GLEnvironment.MaxLabelLength, vaoLabel.Length), vaoLabel);
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, vertexBufferHandle, Math.Min(GLEnvironment.MaxLabelLength, vaoLabel.Length), vaoLabel);
#endif

            animateInFps = parse.Boolean("m_bAnimateInFPS", animateInFps);
            overbrightFactor = parse.NumberProvider("m_flOverbrightFactor", overbrightFactor);
            orientationType = parse.Enum("m_nOrientationType", orientationType);
            animationRate = parse.Float("m_flAnimationRate", animationRate);
            minSize = parse.NumberProvider("m_flMinSize", minSize);
            maxSize = parse.NumberProvider("m_flMaxSize", maxSize);
            startFadeSize = parse.NumberProvider("m_flStartFadeSize", startFadeSize);
            endFadeSize = parse.NumberProvider("m_flEndFadeSize", endFadeSize);
            distanceAlpha = parse.Boolean("m_bDistanceAlpha", distanceAlpha);
            startFadeDot = parse.Float("m_flStartFadeDot", startFadeDot);
            endFadeDot = parse.Float("m_flEndFadeDot", endFadeDot);
            animationType = parse.Enum<ParticleAnimationType>("m_nAnimationType", animationType);
            radiusScale = parse.NumberProvider("m_flRadiusScale", radiusScale);
            alphaScale = parse.NumberProvider("m_flAlphaScale", alphaScale);
            colorScale = parse.VectorProvider("m_vecColorScale", colorScale);
            diffuseAmount = parse.NumberProvider("m_flDiffuseAmount", diffuseAmount);
            selfIllumAmount = parse.NumberProvider("m_flSelfIllumAmount", selfIllumAmount);
            alphaMapToZero = parse.NumberProvider("m_flSourceAlphaValueToMapToZero", alphaMapToZero);
            alphaMapToOne = parse.NumberProvider("m_flSourceAlphaValueToMapToOne", alphaMapToOne);
            centerXOffset = parse.NumberProvider("m_flCenterXOffset", centerXOffset);
            centerYOffset = parse.NumberProvider("m_flCenterYOffset", centerYOffset);
            gammaCorrectVertexColors = parse.Boolean("m_bGammaCorrectVertexColors", gammaCorrectVertexColors);
            saturateColorPreAlphaBlend = parse.Boolean("m_bSaturateColorPreAlphaBlend", saturateColorPreAlphaBlend);
            blendFrames = parse.Boolean("m_bBlendFramesSeq0", blendFrames);
            maxLuminanceFrameBlend = parse.Boolean("m_bMaxLuminanceBlendingSequence0", maxLuminanceFrameBlend);
            desaturation = parse.NumberProvider("m_flDesaturation", desaturation);
            hsvShiftControlPoint = parse.Int32("m_nHSVShiftControlPoint", hsvShiftControlPoint);

            outline = parse.Boolean("m_bOutline", outline);

            if (outline)
            {
                var color = parse.Color24("m_OutlineColor", new Vector3(1f));
                outlineColor = new Vector4(color, parse.Int32("m_nOutlineAlpha", 255) / 255f);
                outlineRanges = new Vector4(
                    parse.Float("m_flOutlineStart0", outlineRanges.X),
                    parse.Float("m_flOutlineEnd0", outlineRanges.Y),
                    parse.Float("m_flOutlineStart1", outlineRanges.Z),
                    parse.Float("m_flOutlineEnd1", outlineRanges.W));
            }
        }

        public override void SetWireframe(bool isWireframe)
        {
            // Solid color
            shader.SetUniform1("isWireframe", isWireframe ? 1 : 0);
        }

        /// <inheritdoc/>
        // The override stands in for the card's base texture; the layers composited over it keep their
        // own textures along with the channels and blend settings that fold them together.
        public override void SetTextureOverride(RenderTexture texture)
        {
            layers[0].Texture = texture;
        }

        private int SetupQuadBuffer()
        {
            const int stride = sizeof(float) * VertexSize;

            GL.CreateVertexArrays(1, out int vao);
            GL.CreateBuffers(1, out vertexBufferHandle);
            GL.VertexArrayVertexBuffer(vao, 0, vertexBufferHandle, 0, stride);
            GL.VertexArrayElementBuffer(vao, RendererContext.MeshBufferCache.QuadIndices.GLHandle);

            // A driver is free to drop an attribute whose only use sits behind a uniform branch, in which
            // case GetAttribLocation reports -1 and binding it would raise a GL error.
            void SetupAttribute(string name, int components, int offsetInFloats)
            {
                var location = GL.GetAttribLocation(shader.Program, name);

                if (location < 0)
                {
                    return;
                }

                GL.EnableVertexArrayAttrib(vao, location);
                GL.VertexArrayAttribFormat(vao, location, components, VertexAttribType.Float, false, sizeof(float) * offsetInFloats);
                GL.VertexArrayAttribBinding(vao, location, 0);
            }

            SetupAttribute("aVertexPosition", 3, 0);
            SetupAttribute("aVertexColor", 4, 3);
            SetupAttribute("aTexCoords", 2, 7);
            SetupAttribute("aTexCoordsNextFrame", 2, 9);
            SetupAttribute("aFrameBlend", 1, 11);
            SetupAttribute("aLayerUv0", 4, 12);
            SetupAttribute("aLayerUv1", 4, 16);
            SetupAttribute("aLayerUv2", 4, 20);
            SetupAttribute("aLayerUv3", 4, 24);

            return vao;
        }

        // m_Gradient's stops, as the ramp generator wants them. A stop's colour is authored as a byte
        // triple and its position defaults to the start of the ramp.
        private static (float Position, Color32 Color)[] ParseGradientStops(ParticleDefinitionParser textureInput)
        {
            var gradient = textureInput.Data.GetSubCollection("m_Gradient");

            if (gradient == null)
            {
                return [];
            }

            var stops = new ParticleDefinitionParser(gradient, textureInput.Logger).Array("m_Stops");
            var parsed = new (float Position, Color32 Color)[stops.Length];

            for (var i = 0; i < stops.Length; i++)
            {
                var color = stops[i].Color24("m_Color", Vector3.One) * 255f;
                parsed[i] = (stops[i].Float("m_flPosition", 0f), new Color32((byte)color.X, (byte)color.Y, (byte)color.Z));
            }

            return parsed;
        }

        /// <summary>
        /// The current and next frame rectangles of one layer's own sprite sheet, for this particle's
        /// sequence at the base layer's animation time. A one-frame sequence yields the same rect twice
        /// and a sheetless texture spans the full [0, 1] range, so cross-fading is a no-op for both.
        /// </summary>
        private (Vector2 UvMin, Vector2 UvMax, Vector2 NextMin, Vector2 NextMax) GetLayerSheetUvs(int layer, ref Particle particle, out float frameBlend)
        {
            frameBlend = 0f;

            var spriteSheetData = layers[layer].Texture.SpriteSheetData;
            if (spriteSheetData == null || spriteSheetData.Sequences.Length == 0 || spriteSheetData.Sequences[0].Frames.Length == 0)
            {
                return (Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.One);
            }

            var sequence = spriteSheetData.Sequences[particle.Sequence % spriteSheetData.Sequences.Length];

            var frame = sequence.Frames.Length > 1
                ? GetSheetFrame(ref particle, sequence.FramesPerSecond, animationRate, animationType, animateInFps)
                : 0f;

            var frameId = (int)MathF.Floor(frame);
            frameBlend = frame - frameId;

            // TODO: Support more than one image per frame?
            var currentImage = sequence.Frames[ResolveSheetFrame(frameId, sequence.Frames.Length, sequence.Clamp)].Images[0];
            var nextImage = sequence.Frames[ResolveSheetFrame(frameId + 1, sequence.Frames.Length, sequence.Clamp)].Images[0];

            return (currentImage.UncroppedMin, currentImage.UncroppedMax, nextImage.UncroppedMin, nextImage.UncroppedMax);
        }

        // Writes a layer's current+next uv rectangle pair as one vec4 per corner.
        private static void WriteQuadUvPair(float[] vertices, int offset, (Vector2 UvMin, Vector2 UvMax, Vector2 NextMin, Vector2 NextMax) uvs)
        {
            WriteQuadUv(vertices, offset, uvs.UvMin, uvs.UvMax);
            WriteQuadUv(vertices, offset + 2, uvs.NextMin, uvs.NextMax);
        }

        // Writes one uv rectangle across the quad's four corners, at the given offset within each vertex.
        // The quad winds top-left, bottom-left, bottom-right, top-right with v increasing downward.
        private static void WriteQuadUv(float[] vertices, int offset, Vector2 min, Vector2 max)
        {
            vertices[offset + (VertexSize * 0) + 0] = min.X;
            vertices[offset + (VertexSize * 0) + 1] = max.Y;
            vertices[offset + (VertexSize * 1) + 0] = min.X;
            vertices[offset + (VertexSize * 1) + 1] = min.Y;
            vertices[offset + (VertexSize * 2) + 0] = max.X;
            vertices[offset + (VertexSize * 2) + 1] = min.Y;
            vertices[offset + (VertexSize * 3) + 0] = max.X;
            vertices[offset + (VertexSize * 3) + 1] = max.Y;
        }

        // A quad orientation matrix from a base (right, up) pair with the particle roll folded in, matching the
        // spritecard vertex shader. The axes are intentionally not re-normalized (some modes rely on that, e.g.
        // SCREEN_Z foreshortens as the camera tilts). The face row is only the normal and does not affect corners.
        private static Matrix4x4 QuadBasis(Vector3 baseRight, Vector3 baseUp, float roll)
        {
            var c = MathF.Cos(roll);
            var s = MathF.Sin(roll);
            var right = (baseRight * c) + (baseUp * s);
            var up = (baseUp * c) - (baseRight * s);
            var face = Vector3.Cross(right, up);
            face = face.LengthSquared() > 1e-12f ? Vector3.Normalize(face) : Vector3.UnitZ;
            return new Matrix4x4(
                right.X, right.Y, right.Z, 0f,
                up.X, up.Y, up.Z, 0f,
                face.X, face.Y, face.Z, 0f,
                0f, 0f, 0f, 1f);
        }

        // World-space camera forward (into the scene): the billboard maps local +Z to the toward-camera axis.
        private static Vector3 CameraForward(Matrix4x4 billboard)
            => -new Vector3(billboard.M31, billboard.M32, billboard.M33);

        // SCREEN_Z_ALIGNED: up locked to world +Z, right = cross(worldZ, forward) left un-normalized, so the
        // sprite yaws about vertical to face the camera and foreshortens as the view tilts off-horizontal.
        private static Matrix4x4 ScreenZAlignedBasis(Matrix4x4 billboard, float roll)
            => QuadBasis(Vector3.Cross(Vector3.UnitZ, CameraForward(billboard)), Vector3.UnitZ, roll);

        // WORLD_Z_ALIGNED: the quad lies flat in the world XY plane (normal = +Z), rolling about vertical,
        // independent of the camera.
        private static Matrix4x4 WorldZAlignedBasis(float roll)
            => QuadBasis(new Vector3(0f, -1f, 0f), new Vector3(1f, 0f, 0f), roll);

        // ALIGN_TO_PARTICLE_NORMAL: quad plane perpendicular to the particle normal, with the shader's canonical
        // tangent frame. The reference axis is world -Y once the normal tilts at all off horizontal, and world
        // +Z only while it is nearly horizontal; either choice stays clear of the normal.
        private static Matrix4x4 ParticleNormalBasis(Vector3 normal, float roll)
        {
            var reference = MathF.Abs(normal.Z) > 0.1f ? new Vector3(0f, -1f, 0f) : new Vector3(0f, 0f, 1f);
            var up = Vector3.Normalize(Vector3.Cross(normal, reference));
            var right = Vector3.Cross(up, normal);
            return QuadBasis(right, up, roll);
        }

        // SCREENALIGN_TO_PARTICLE_NORMAL: the quad's right edge follows the particle normal while it turns toward
        // the camera about that normal. Falls back to a billboard when the normal points at the camera.
        private static Matrix4x4 ScreenAlignToNormalBasis(Matrix4x4 billboard, Vector3 normal, float roll)
        {
            var n = Vector3.Normalize(normal);
            var w = Vector3.Cross(n, CameraForward(billboard));
            if (w.LengthSquared() < 1e-8f)
            {
                return billboard;
            }

            return QuadBasis(n, Vector3.Normalize(w), roll);
        }

        /// <summary>Fills and uploads the quad buffer, returning the number of quads actually emitted.</summary>
        private int UpdateVertices(ParticleCollection particles, ParticleSystemRenderState systemRenderState, Camera camera)
        {
            var modelViewMatrix = camera.CameraViewMatrix;

            // Create billboarding rotation (always facing camera)
            if (!Matrix4x4.Decompose(modelViewMatrix, out _, out var modelViewRotation, out _))
            {
                throw new InvalidOperationException("Matrix decompose failed");
            }

            modelViewRotation = Quaternion.Inverse(modelViewRotation);
            var billboardMatrix = Matrix4x4.CreateFromQuaternion(modelViewRotation);

            // All four bounds are a radius per unit of camera distance, and the whole group is gated
            // on m_bDistanceAlpha.
            var minSizeSlope = minSize.NextNumber(systemRenderState);
            var maxSizeSlope = maxSize.NextNumber(systemRenderState);
            var startFadeSlope = startFadeSize.NextNumber(systemRenderState);
            var endFadeSlope = endFadeSize.NextNumber(systemRenderState);

            var centerOffset = new Vector2(
                centerXOffset.NextNumber(systemRenderState),
                centerYOffset.NextNumber(systemRenderState));


            // Distance from the quad centre to its furthest corner, in half-widths, so a particle can be
            // bounded by a sphere without building its basis first.
            var cornerDistance = new Vector2(1f + MathF.Abs(centerOffset.X), 1f + MathF.Abs(centerOffset.Y)).Length();
            var cullFrustum = camera.ViewFrustum;
            const bool PerParticleFrustumCull = false;

            // Only the two normal-aligned modes fade by view angle, and only when the range can actually
            // be entered: the value it tests is a dot product magnitude, so it never exceeds 1.
            var viewAngleFadeActive = startFadeDot < 1f
                && endFadeDot > startFadeDot
                && orientationType is ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL
                    or ParticleOrientation.PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL;

            // Update vertex buffer
            var rawVertices = ArrayPool<float>.Shared.Rent(particles.Count * VertexSize * 4);

            try
            {
                var i = 0;
                foreach (ref var particle in particles.Current)
                {
                    var radiusScale = this.radiusScale.NextNumber(ref particle, systemRenderState);

                    // Scales rgb and alpha alike, matching the shader's fade of the whole vertex colour.
                    var colorFade = 1f;

                    // The view-angle fade touches alpha only, unlike the size fade below.
                    var alphaFade = 1f;

                    if (viewAngleFadeActive)
                    {
                        var toCamera = camera.Location - particle.Position;

                        if (toCamera.LengthSquared() > 1e-12f)
                        {
                            var facing = MathF.Abs(Vector3.Dot(Vector3.Normalize(particle.Normal), Vector3.Normalize(toCamera)));
                            alphaFade = 1f - MathUtils.Smoothstep(startFadeDot, endFadeDot, facing);
                        }
                    }

                    if (distanceAlpha)
                    {
                        var cameraDistance = Vector3.Distance(camera.Location, particle.Position);
                        var radius = particle.Radius * radiusScale;
                        var fadeStart = startFadeSlope * cameraDistance;
                        var fadeEnd = endFadeSlope * cameraDistance;

                        if (radius > fadeStart)
                        {
                            if (radius >= fadeEnd)
                            {
                                // Faded out entirely; emitting the quad would only cost overdraw.
                                continue;
                            }

                            colorFade = 1f - ((radius - fadeStart) / (fadeEnd - fadeStart));
                        }

                        if (particle.Radius > 0f)
                        {
                            // Expressed back as a scale, because the corner transform takes one. Nested
                            // min/max rather than a clamp: an inverted range has to resolve to the maximum
                            // the way the shader's does, not throw.
                            radiusScale = MathF.Min(MathF.Max(radius, minSizeSlope * cameraDistance), maxSizeSlope * cameraDistance) / particle.Radius;
                        }
                    }

                    var alphaScale = this.alphaScale.NextNumber(ref particle, systemRenderState);
                    var alpha = particle.Alpha * alphaScale * colorFade * alphaFade;
                    var halfWidth = particle.Radius * radiusScale;

                    // The spritecard vertex shader scales the corner offset to zero below 1/255 alpha, so
                    // a quad with no extent or no alpha rasterizes nothing either way.
                    if (halfWidth <= 0f || alpha < 1f / 255f)
                    {
                        continue;
                    }

                    // Off-screen particles still cost their vertices and a scan-out of the whole quad.
                    // The bound is the corner furthest from the centre: the axes of every orientation
                    // basis are unit length or shorter, so the offset corner distance covers all of them.
                    if (PerParticleFrustumCull && !cullFrustum.IsEmpty)
                    {
                        var cornerReach = halfWidth * cornerDistance;

                        if (orientationType == ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL)
                        {
                            // Its right axis is cross(up, normal) left un-normalized, so a normal that is
                            // not unit length widens the quad past what the corner distance alone covers.
                            cornerReach *= MathF.Max(1f, particle.Normal.Length());
                        }

                        if (!cullFrustum.Intersects(particle.Position, cornerReach))
                        {
                            continue;
                        }
                    }

                    // Per-mode quad orientation, ported from the spritecard vertex shader (roll = Rotation.Z).
                    // SCREEN_ALIGNED is the plain camera billboard; FULL_3AXIS_ROTATION has no shader variant and
                    // uses the particle's full rotation basis.
                    var roll = particle.Rotation.Z;
                    var modelMatrix = orientationType switch
                    {
                        ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED => particle.GetRotationMatrix() * billboardMatrix * particle.GetTransformationMatrix(radiusScale),
                        ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_Z_ALIGNED => ScreenZAlignedBasis(billboardMatrix, roll) * particle.GetTransformationMatrix(radiusScale),
                        ParticleOrientation.PARTICLE_ORIENTATION_WORLD_Z_ALIGNED => WorldZAlignedBasis(roll) * particle.GetTransformationMatrix(radiusScale),
                        ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL => ParticleNormalBasis(particle.Normal, roll) * particle.GetTransformationMatrix(radiusScale),
                        ParticleOrientation.PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL => ScreenAlignToNormalBasis(billboardMatrix, particle.Normal, roll) * particle.GetTransformationMatrix(radiusScale),
                        _ => particle.GetRotationMatrix() * particle.GetTransformationMatrix(radiusScale),
                    };

                    // The centre offset shifts the corners before the model matrix scales them, so it is
                    // measured in half-widths rather than world units.
                    var tl = Vector4.Transform(new Vector4(centerOffset.X - 1, centerOffset.Y - 1, 0, 1), modelMatrix);
                    var bl = Vector4.Transform(new Vector4(centerOffset.X - 1, centerOffset.Y + 1, 0, 1), modelMatrix);
                    var br = Vector4.Transform(new Vector4(centerOffset.X + 1, centerOffset.Y + 1, 0, 1), modelMatrix);
                    var tr = Vector4.Transform(new Vector4(centerOffset.X + 1, centerOffset.Y - 1, 0, 1), modelMatrix);

                    var quadStart = i * VertexSize * 4;
                    rawVertices[quadStart + 0] = tl.X;
                    rawVertices[quadStart + 1] = tl.Y;
                    rawVertices[quadStart + 2] = tl.Z;
                    rawVertices[quadStart + (VertexSize * 1) + 0] = bl.X;
                    rawVertices[quadStart + (VertexSize * 1) + 1] = bl.Y;
                    rawVertices[quadStart + (VertexSize * 1) + 2] = bl.Z;
                    rawVertices[quadStart + (VertexSize * 2) + 0] = br.X;
                    rawVertices[quadStart + (VertexSize * 2) + 1] = br.Y;
                    rawVertices[quadStart + (VertexSize * 2) + 2] = br.Z;
                    rawVertices[quadStart + (VertexSize * 3) + 0] = tr.X;
                    rawVertices[quadStart + (VertexSize * 3) + 1] = tr.Y;
                    rawVertices[quadStart + (VertexSize * 3) + 2] = tr.Z;

                    // Colors
                    for (var j = 0; j < 4; ++j)
                    {
                        rawVertices[quadStart + (VertexSize * j) + 3] = particle.Color.X * colorFade;
                        rawVertices[quadStart + (VertexSize * j) + 4] = particle.Color.Y * colorFade;
                        rawVertices[quadStart + (VertexSize * j) + 5] = particle.Color.Z * colorFade;
                        rawVertices[quadStart + (VertexSize * j) + 6] = alpha;
                    }

                    // Each layer resolves frame rects against its own sheet, timed by the base sequence:
                    // companion sheets match the base rects, one-frame sequences pin an atlas region.
                    var (uvMin, uvMax, uvNextMin, uvNextMax) = GetLayerSheetUvs(0, ref particle, out var frameBlend);

                    WriteQuadUv(rawVertices, quadStart + 7, uvMin, uvMax);
                    WriteQuadUv(rawVertices, quadStart + 9, uvNextMin, uvNextMax);

                    for (var layer = 1; layer < layers.Length; layer++)
                    {
                        var layerUvs = GetLayerSheetUvs(layer, ref particle, out _);
                        WriteQuadUvPair(rawVertices, quadStart + 12 + ((layer - 1) * 4), layerUvs);
                    }

                    for (var j = 0; j < 4; ++j)
                    {
                        rawVertices[quadStart + (VertexSize * j) + 11] = frameBlend;
                    }

                    i++;
                }

                GL.NamedBufferData(vertexBufferHandle, i * VertexSize * 4 * sizeof(float), rawVertices, BufferUsageHint.DynamicDraw);

                return i;
            }
            finally
            {
                ArrayPool<float>.Shared.Return(rawVertices);
            }
        }

        public override void Render(ParticleCollection particleBag, ParticleSystemRenderState systemRenderState, Camera camera)
        {
            if (particleBag.Count == 0)
            {
                return;
            }

            // Update vertex buffer. Fully faded particles are skipped, so this can be fewer than the
            // live particle count.
            var quadCount = UpdateVertices(particleBag, systemRenderState, camera);

            if (quadCount == 0)
            {
                return;
            }

            // Draw it. The translucent pass leaves blend/depth state to each custom draw, so enable blending and
            // stop depth writes here; otherwise sprites are opaque. The cable renderer instead draws opaque with depth writes.
            GL.Enable(EnableCap.Blend);
            GL.DepthMask(false);
            GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);

            GL.Disable(EnableCap.CullFace);

            shader.Use();
            GL.BindVertexArray(vaoHandle);

            // Layer 0 keeps the plain uTexture name; the rest take a sampler each. Units past the layer
            // count are never sampled, but they get layer 0's texture so no sampler is left unbound.
            for (var layer = 0; layer < MaxTextureLayers; layer++)
            {
                var source = layer < layers.Length ? layers[layer] : layers[0];
                shader.SetTexture(RenderMaterial.TextureUnitStart + layer, LayerTextureUniforms[layer], source.Texture);
            }

            shader.SetUniform1("uLayerCount", layers.Length);

            for (var layer = 0; layer < layers.Length; layer++)
            {
                shader.SetUniform1(LayerChannelsUniforms[layer], (int)layers[layer].Channels);
                shader.SetUniform1(LayerBlendModeUniforms[layer], (int)layers[layer].BlendMode);
                shader.SetUniform1(LayerBlendUniforms[layer], layers[layer].Blend.NextNumber(systemRenderState));
                shader.SetUniform1(LayerEffectModeUniforms[layer], (int)layers[layer].EffectMode);
            }

            shader.SetUniform1("uOverbrightFactor", overbrightFactor.NextNumber(systemRenderState));
            shader.SetUniform1("uColorFactor", diffuseAmount.NextNumber(systemRenderState) + selfIllumAmount.NextNumber(systemRenderState));
            shader.SetUniform1("uDesaturation", desaturation.NextNumber(systemRenderState));

            // The control point carries (hue offset, saturation scale, value scale). Identity when absent.
            shader.SetUniform3("uHsvShift", hsvShiftControlPoint >= 0
                ? systemRenderState.GetControlPoint(hsvShiftControlPoint).Position
                : new Vector3(0f, 1f, 1f));

            // A smoothstep over the source alpha, sent unconditionally: the engine's own defaults are
            // (0, 1), which its x < y guard accepts, so the remap is live unless an effect inverts it.
            shader.SetUniform2("uAlphaRemapRange", new Vector2(
                alphaMapToZero.NextNumber(systemRenderState),
                alphaMapToOne.NextNumber(systemRenderState)));

            shader.SetUniform3("uColorScale", colorScale.NextVector(systemRenderState));
            shader.SetUniform1("uGammaCorrectVertexColors", gammaCorrectVertexColors);
            shader.SetUniform1("uSaturateColorPreAlphaBlend", saturateColorPreAlphaBlend);
            shader.SetUniform1("uBlendFrames", blendFrames);
            shader.SetUniform1("uMaxLuminanceFrameBlend", maxLuminanceFrameBlend);
            shader.SetUniform1("uOutline", outline);
            shader.SetUniform4("uOutlineColor", outlineColor);
            shader.SetUniform4("uOutlineRanges", outlineRanges);

            // Set every draw: the program is shared with every other sprite renderer, whatever their mode.
            shader.SetUniform1("uBlendMode", (int)blendMode);

            // DRAW
            PerfStats.Active.Count(Counter.ParticleDraw);
            GL.DrawElements(PrimitiveType.Triangles, quadCount * 6, DrawElementsType.UnsignedShort, 0);

            GL.Enable(EnableCap.CullFace);
        }

        public override IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;

        public override void SetRenderMode(string renderMode)
        {
        }

        public override void Delete()
        {
            GL.DeleteVertexArray(vaoHandle);
            GL.DeleteBuffer(vertexBufferHandle);
        }
    }
}
