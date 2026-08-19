using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.Renderer.Particles.Utils;
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

        // The shader keeps one sampler per layer, so this is a hard ceiling rather than a preference.
        private const int MaxTextureLayers = 5;

        [StructLayout(LayoutKind.Sequential)]
        private struct Vertex
        {
            [VertexAttribute(VertexSlot.Position)] public Vector3 Position;
            [VertexAttribute(VertexSlot.Color)] public Vector4 Color;
            [VertexAttribute(VertexSlot.TexCoord)] public Vector2 UV;
            [VertexAttribute(VertexSlot.TexCoord1)] public Vector2 UVNextFrame;
            [VertexAttribute("vFrameBlend")] public float FrameBlend;
            [VertexAttribute("vLayerUv0")] public Vector4 LayerUv0;
            [VertexAttribute("vLayerUv1")] public Vector4 LayerUv1;
            [VertexAttribute("vLayerUv2")] public Vector4 LayerUv2;
            [VertexAttribute("vLayerUv3")] public Vector4 LayerUv3;

            /// <summary>The layout of this vertex, for creating vertex array objects.</summary>
            public static readonly VertexInputLayout InputLayout = VertexInputLayout.FromStruct<Vertex>();

            public void SetLayerUv(int layer, Vector4 uvs)
            {
                switch (layer)
                {
                    case 0: LayerUv0 = uvs; break;
                    case 1: LayerUv1 = uvs; break;
                    case 2: LayerUv2 = uvs; break;
                    case 3: LayerUv3 = uvs; break;
                    default: throw new ArgumentOutOfRangeException(nameof(layer));
                }
            }
        }

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
        private readonly RendererContext rendererContext;
        private readonly int vaoHandle;
        private readonly TextureLayer[] layers;

        private readonly float animationRate = 0.1f;
        private readonly ParticleAnimationType animationType = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;
        private readonly INumberProvider minSize = new LiteralNumberProvider(0f);
        private readonly INumberProvider maxSize = new LiteralNumberProvider(5000f);
        private readonly INumberProvider startFadeSize = new LiteralNumberProvider(100000000f);
        private readonly INumberProvider endFadeSize = new LiteralNumberProvider(200000000f);
        /// <summary>
        /// Selects the signed distance field alpha treatment, which drives soft edges and outlines.
        /// It has no bearing on the size clamp or the distance fade.
        /// </summary>
        private readonly bool distanceAlpha;

        // m_flStartFadeDot/m_flEndFadeDot: the normal-aligned modes fade out as the card turns edge-on to
        // the camera. The defaults span 1..2 against a value that never exceeds 1, so no fade by default.
        private readonly float startFadeDot = 1f;
        private readonly float endFadeDot = 2f;

        // m_bBlendFramesSeq0 cross-fades consecutive sheet frames instead of stepping between them.
        // m_bMaxLuminanceBlendingSequence0 swaps the plain lerp for a luminance-weighted one, which keeps
        // the brighter of the two frames dominant through the cross-fade.
        private readonly bool blendFrames = true;


        private readonly bool animateInFps;
        private readonly ParticleBlendMode blendMode = ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA;
        private readonly ParticleOrientation orientationType;


        private readonly bool outline;
        private readonly Vector4 outlineColor = Vector4.One;
        // Start0, End0, Start1, End1 -- the order the shader's two-sided ramp wants them in.
        private readonly Vector4 outlineRanges = new(0.5f, 0.7f, 0.6f, 0.8f);
        private int vertexBufferHandle;


        public RenderSprites(ParticleDefinitionParser parse, RendererContext rendererContext) : base(parse)
        {
            this.rendererContext = rendererContext;

            blendMode = parse.Enum<ParticleBlendMode>("m_nOutputBlendMode", blendMode);

            shader = rendererContext.ShaderLoader.LoadShader(ShaderName);

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
            blendFrames = parse.Boolean("m_bBlendFramesSeq0", blendFrames);

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
            GL.CreateBuffers(1, out vertexBufferHandle);

            return Vertex.InputLayout.CreateVertexArray(nameof(RenderSprites), vertexBufferHandle, rendererContext.MeshBufferCache.QuadIndices.GLHandle);
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

            var stops = textureInput.Nested(gradient).Array("m_Stops");
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

            var sequence = spriteSheetData.Sequences[particle.SequenceNumber % spriteSheetData.Sequences.Length];

            var (frame, nextFrame, blend) = GetSheetFrame(ref particle, sequence, animationRate, animationType, animateInFps);
            frameBlend = blend;

            // TODO: Support more than one image per frame?
            var currentImage = sequence.Frames[frame].Images[0];
            var nextImage = sequence.Frames[nextFrame].Images[0];

            return (currentImage.UncroppedMin, currentImage.UncroppedMax, nextImage.UncroppedMin, nextImage.UncroppedMax);
        }

        // One corner of a uv rectangle, in the quad's winding order (top-left, bottom-left,
        // bottom-right, top-right) with v increasing downward.
        private static Vector2 CornerUv(int corner, Vector2 min, Vector2 max) => corner switch
        {
            0 => new Vector2(min.X, max.Y),
            1 => new Vector2(min.X, min.Y),
            2 => new Vector2(max.X, min.Y),
            3 => new Vector2(max.X, max.Y),
            _ => throw new ArgumentOutOfRangeException(nameof(corner)),
        };

        // A quad orientation matrix from a base (right, up) pair with the particle roll folded in, matching the
        // spritecard vertex shader. The axes are intentionally not re-normalized (some modes rely on that, e.g.
        // SCREEN_Z foreshortens as the camera tilts). The face row is only the normal and does not affect corners.
        private static Matrix4x4 QuadBasis(Vector3 baseRight, Vector3 baseUp, float roll, float yaw)
        {
            var c = MathF.Cos(roll);
            var s = MathF.Sin(roll);
            var right = (baseRight * c) + (baseUp * s);
            var up = (baseUp * c) - (baseRight * s);

            // Yaw turns the card about its own up axis, foreshortening it horizontally to nothing at 90
            // degrees. Only the right axis is turned, and the axis is normalized without touching up.
            if (yaw != 0f && up.LengthSquared() > Epsilon.LengthSquared)
            {
                right = Vector3.Transform(right, Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(up), yaw));
            }

            var face = Vector3.Cross(right, up);
            face = face.LengthSquared() > Epsilon.LengthSquared ? Vector3.Normalize(face) : Vector3.UnitZ;
            return new Matrix4x4(
                right.X, right.Y, right.Z, 0f,
                up.X, up.Y, up.Z, 0f,
                face.X, face.Y, face.Z, 0f,
                0f, 0f, 0f, 1f);
        }

        // World-space camera forward (into the scene): the billboard maps local +Z to the toward-camera axis.
        private static Vector3 CameraForward(Matrix4x4 billboard)
            => -new Vector3(billboard.M31, billboard.M32, billboard.M33);

        // SCREEN_ALIGNED: the plain camera billboard, built from the camera's own right and up axes. The
        // particle's pitch never enters, so a normal-setting operator cannot tilt the card out of plane.
        private static Matrix4x4 ScreenAlignedBasis(Matrix4x4 billboard, float roll, float yaw)
            => QuadBasis(
                new Vector3(billboard.M11, billboard.M12, billboard.M13),
                new Vector3(billboard.M21, billboard.M22, billboard.M23), roll, yaw);

        // SCREEN_Z_ALIGNED: up locked to world +Z, right = cross(worldZ, forward) left un-normalized, so the
        // sprite yaws about vertical to face the camera and foreshortens as the view tilts off-horizontal.
        private static Matrix4x4 ScreenZAlignedBasis(Matrix4x4 billboard, float roll, float yaw)
            => QuadBasis(Vector3.Cross(Vector3.UnitZ, CameraForward(billboard)), Vector3.UnitZ, roll, yaw);

        // WORLD_Z_ALIGNED: the quad lies flat in the world XY plane (normal = +Z), rolling about vertical,
        // independent of the camera.
        private static Matrix4x4 WorldZAlignedBasis(float roll, float yaw)
            => QuadBasis(new Vector3(0f, -1f, 0f), new Vector3(1f, 0f, 0f), roll, yaw);

        // ALIGN_TO_PARTICLE_NORMAL: quad plane perpendicular to the particle normal, with the shader's canonical
        // tangent frame. The reference axis is world -Y once the normal tilts at all off horizontal, and world
        // +Z only while it is nearly horizontal; either choice stays clear of the normal.
        private static Matrix4x4 ParticleNormalBasis(Vector3 normal, float roll, float yaw)
        {
            var reference = MathF.Abs(normal.Z) > 0.1f ? new Vector3(0f, -1f, 0f) : new Vector3(0f, 0f, 1f);
            var up = Vector3.Normalize(Vector3.Cross(normal, reference));
            var right = Vector3.Cross(up, normal);
            return QuadBasis(right, up, roll, yaw);
        }

        // SCREENALIGN_TO_PARTICLE_NORMAL: the quad's right edge follows the particle normal while it turns toward
        // the camera about that normal. Falls back to a billboard when the normal points at the camera.
        private static Matrix4x4 ScreenAlignToNormalBasis(Matrix4x4 billboard, Vector3 normal, float roll, float yaw)
        {
            var n = Vector3.Normalize(normal);
            var w = Vector3.Cross(n, CameraForward(billboard));
            if (w.LengthSquared() < Epsilon.LengthSquared)
            {
                return billboard;
            }

            return QuadBasis(n, Vector3.Normalize(w), roll, yaw);
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

            // All four bounds are a radius per unit of camera distance
            var minSizeSlope = minSize.NextNumber(systemRenderState);
            var maxSizeSlope = maxSize.NextNumber(systemRenderState);
            var startFadeSlope = startFadeSize.NextNumber(systemRenderState);
            var endFadeSlope = endFadeSize.NextNumber(systemRenderState);

            var centerOffset = new Vector2(
                CenterXOffset.NextNumber(systemRenderState),
                CenterYOffset.NextNumber(systemRenderState));


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
            // Rented from the shared float pool so the memory is reused across renderers.
            using (var vertexBuffer = new RentedFloatBuffer<Vertex>(particles.Count * 4))
            {
                var vertices = vertexBuffer.Span;
                var i = 0;
                foreach (ref var particle in particles.Current)
                {
                    var radiusScale = RadiusScale.NextNumber(ref particle, systemRenderState);

                    // Scales rgb and alpha alike, matching the shader's fade of the whole vertex colour.
                    var colorFade = 1f;

                    // The view-angle fade touches alpha only, unlike the size fade below.
                    var alphaFade = 1f;

                    if (viewAngleFadeActive)
                    {
                        var toCamera = camera.Location - particle.Position;

                        if (toCamera.LengthSquared() > Epsilon.LengthSquared)
                        {
                            var facing = MathF.Abs(Vector3.Dot(Vector3.Normalize(particle.Normal), Vector3.Normalize(toCamera)));
                            alphaFade = 1f - MathUtils.Smoothstep(startFadeDot, endFadeDot, facing);
                        }
                    }

                    var cameraDistance = Vector3.Distance(camera.Location, particle.Position);
                    var radius = particle.Radius * radiusScale;
                    var fadeStart = startFadeSlope * cameraDistance;
                    var fadeEnd = endFadeSlope * cameraDistance;

                    // The fade reads the raw radius, independently of the size clamp below
                    if (radius > fadeStart)
                    {
                        if (radius >= fadeEnd)
                        {
                            continue;
                        }

                        colorFade = 1f - ((radius - fadeStart) / (fadeEnd - fadeStart));
                    }

                    // Nested min/max rather than a clamp, so an inverted range resolves to the maximum
                    if (particle.Radius > 0f)
                    {
                        radiusScale = MathF.Min(MathF.Max(radius, minSizeSlope * cameraDistance), maxSizeSlope * cameraDistance) / particle.Radius;
                    }

                    var alphaScale = AlphaScale.NextNumber(ref particle, systemRenderState);
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

                    // Per-mode quad orientation, ported from the spritecard vertex shader. Every mode folds in
                    // roll and yaw and none of them read pitch; only FULL_3AXIS_ROTATION uses the full basis.
                    var roll = particle.Rotation.Z;
                    var yaw = particle.Rotation.X;
                    var modelMatrix = orientationType switch
                    {
                        ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED => ScreenAlignedBasis(billboardMatrix, roll, yaw) * particle.GetTransformationMatrix(radiusScale),
                        ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_Z_ALIGNED => ScreenZAlignedBasis(billboardMatrix, roll, yaw) * particle.GetTransformationMatrix(radiusScale),
                        ParticleOrientation.PARTICLE_ORIENTATION_WORLD_Z_ALIGNED => WorldZAlignedBasis(roll, yaw) * particle.GetTransformationMatrix(radiusScale),
                        ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL => ParticleNormalBasis(particle.Normal, roll, yaw) * particle.GetTransformationMatrix(radiusScale),
                        ParticleOrientation.PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL => ScreenAlignToNormalBasis(billboardMatrix, particle.Normal, roll, yaw) * particle.GetTransformationMatrix(radiusScale),
                        _ => particle.GetRotationMatrix() * particle.GetTransformationMatrix(radiusScale),
                    };

                    // The centre offset shifts the corners before the model matrix scales them, so it is
                    // measured in half-widths rather than world units.
                    var tl = Vector4.Transform(new Vector4(centerOffset.X - 1, centerOffset.Y - 1, 0, 1), modelMatrix);
                    var bl = Vector4.Transform(new Vector4(centerOffset.X - 1, centerOffset.Y + 1, 0, 1), modelMatrix);
                    var br = Vector4.Transform(new Vector4(centerOffset.X + 1, centerOffset.Y + 1, 0, 1), modelMatrix);
                    var tr = Vector4.Transform(new Vector4(centerOffset.X + 1, centerOffset.Y - 1, 0, 1), modelMatrix);

                    // Corners in index buffer winding order: top-left, bottom-left, bottom-right, top-right.
                    Span<Vector3> corners =
                    [
                        new(tl.X, tl.Y, tl.Z),
                        new(bl.X, bl.Y, bl.Z),
                        new(br.X, br.Y, br.Z),
                        new(tr.X, tr.Y, tr.Z),
                    ];

                    var color = new Vector4(particle.Color * colorFade, alpha);

                    // Each layer resolves frame rects against its own sheet, timed by the base sequence:
                    // companion sheets match the base rects, one-frame sequences pin an atlas region.
                    var (uvMin, uvMax, uvNextMin, uvNextMax) = GetLayerSheetUvs(0, ref particle, out var frameBlend);

                    var quadStart = i * 4;

                    for (var j = 0; j < 4; ++j)
                    {
                        vertices[quadStart + j] = new Vertex
                        {
                            Position = corners[j],
                            Color = color,
                            UV = CornerUv(j, uvMin, uvMax),
                            UVNextFrame = CornerUv(j, uvNextMin, uvNextMax),
                            FrameBlend = frameBlend,
                        };
                    }

                    for (var layer = 1; layer < layers.Length; layer++)
                    {
                        var (layerMin, layerMax, layerNextMin, layerNextMax) = GetLayerSheetUvs(layer, ref particle, out _);

                        for (var j = 0; j < 4; ++j)
                        {
                            var uv = CornerUv(j, layerMin, layerMax);
                            var uvNext = CornerUv(j, layerNextMin, layerNextMax);
                            vertices[quadStart + j].SetLayerUv(layer - 1, new Vector4(uv.X, uv.Y, uvNext.X, uvNext.Y));
                        }
                    }

                    i++;
                }

                GL.NamedBufferData(vertexBufferHandle, i * 4 * Vertex.InputLayout.Stride, vertexBuffer.FloatArray, BufferUsageHint.DynamicDraw);

                return i;
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

            // The translucent pass leaves blend/depth state to each draw. Enable blending and stop
            // depth writes, or sprites render opaque. Cables instead draw opaque with depth writes.
            // Modulate-2x scales what is behind it, so it needs its own factors. Everything else
            // composites premultiplied; the additive path zeroes its own alpha in the shader.
            var mod2x = blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_MOD2X;
            using var _ = rendererContext.RenderState.Scope(blend: true, depthWrite: false, cullMode: RsCullMode.None,
                srcBlend: mod2x ? RsBlendMode.DestColor : RsBlendMode.One,
                dstBlend: mod2x ? RsBlendMode.SrcColor : RsBlendMode.InvSrcAlpha);

            shader.Use();
            VertexArray.Bind(vaoHandle, shader);

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

            SetSharedUniforms(shader, systemRenderState);
            shader.SetUniform1("uBlendFrames", blendFrames);
            shader.SetUniform1("uOutline", outline);
            shader.SetUniform4("uOutlineColor", outlineColor);
            shader.SetUniform4("uOutlineRanges", outlineRanges);

            // Set every draw: the program is shared with every other sprite renderer, whatever their mode.
            shader.SetUniform1("uBlendMode", (int)blendMode);

            PerfStats.Active.Count(Counter.ParticleDraw);
            GL.DrawElements(PrimitiveType.Triangles, quadCount * 6, DrawElementsType.UnsignedShort, 0);
        }

        public override IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;

        public override void SetRenderMode(string renderMode)
        {
        }

        public override void Delete()
        {
            VertexArray.Delete(vaoHandle);
            GL.DeleteBuffer(vertexBufferHandle);
        }
    }
}
