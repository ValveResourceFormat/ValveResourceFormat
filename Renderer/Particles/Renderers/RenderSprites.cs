using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Utils;
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
        private const string ShaderName = "particle_spritecard_instanced";

        private const string DefaultTextureName = "materials/particle/base_sprite.vtex";

        private readonly Shader shader;
        private readonly RendererContext rendererContext;
        private readonly int vaoHandle;
        private readonly ParticleTextureLayer[] layers;

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
        private readonly bool softEdges;
        private readonly float edgeSoftnessStart = 0.6f;
        private readonly float edgeSoftnessEnd = 0.5f;

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

            (layers, var textureName) = ParticleTextureLayer.Build(parse, rendererContext, DefaultTextureName, srgbRead: OutputIsColor);

            shader = rendererContext.ShaderLoader.LoadShader(ShaderName, ("S_TEXTURE_LAYERS", (byte)(layers.Length - 1)));

            instanceLayout = BuildInstanceLayout(layers.Length);

            // The same quad is reused for all particles
            vaoHandle = SetupQuadBuffer($"{nameof(RenderSprites)}: {System.IO.Path.GetFileName(textureName)}");

            animateInFps = parse.Boolean("m_bAnimateInFPS", animateInFps);
            orientationType = parse.Enum("m_nOrientationType", orientationType);
            animationRate = parse.Float("m_flAnimationRate", animationRate);
            minSize = parse.NumberProvider("m_flMinSize", minSize);
            maxSize = parse.NumberProvider("m_flMaxSize", maxSize);
            startFadeSize = parse.NumberProvider("m_flStartFadeSize", startFadeSize);
            endFadeSize = parse.NumberProvider("m_flEndFadeSize", endFadeSize);
            distanceAlpha = parse.Boolean("m_bDistanceAlpha", distanceAlpha);
            softEdges = parse.Boolean("m_bSoftEdges", softEdges);
            edgeSoftnessStart = parse.Float("m_flEdgeSoftnessStart", edgeSoftnessStart);
            edgeSoftnessEnd = parse.Float("m_flEdgeSoftnessEnd", edgeSoftnessEnd);
            startFadeDot = parse.Float("m_flStartFadeDot", startFadeDot);
            endFadeDot = parse.Float("m_flEndFadeDot", endFadeDot);
            animationType = parse.BehaviorVersion >= 10
                ? parse.Enum<ParticleAnimationType>("m_nAnimationType", animationType)
                : ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;
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

        // Floats per particle before the extra layers: origin 3, right 3, up 3, colour 4, this frame's
        // uv rect 4, the next frame's 4, frame blend 1.
        private const int BaseInstanceFloats = 22;

        // Each layer past the first resolves its own sheet frame, so it carries its own pair of rects.
        private const int LayerInstanceFloats = 8;

        private readonly VertexInputLayout instanceLayout;

        /// <summary>
        /// One set of attributes per particle rather than per corner: the four corners come from
        /// <c>gl_VertexID</c>, so nothing is duplicated four times. The buffer carries only the layers
        /// this card has, so a one layer card pays nothing for the four it does not.
        /// </summary>
        /// <remarks>
        /// The locations still have to be allocated against every name the shader file declares, layers
        /// this card does not have included. The shader allocates over its assembled source with the
        /// <c>#if</c> blocks still in it, so a switched off layer takes a slot there whether this card
        /// uses it or not; allocating over the shorter list would put <c>vRight</c> and <c>vUp</c>
        /// somewhere the shader never reads, and every card would collapse to a point.
        /// </remarks>
        private static VertexInputLayout BuildInstanceLayout(int layerCount)
        {
            // In shader declaration order, so taking a prefix of it is a valid buffer layout
            var declared = new List<VertexAttribute>
            {
                new(VertexSlot.Position, DXGI_FORMAT.R32G32B32_FLOAT),
                new("vRight", DXGI_FORMAT.R32G32B32_FLOAT),
                new("vUp", DXGI_FORMAT.R32G32B32_FLOAT),
                new(VertexSlot.Color, DXGI_FORMAT.R32G32B32A32_FLOAT),
                new("vUvRect", DXGI_FORMAT.R32G32B32A32_FLOAT),
                new("vUvRectNext", DXGI_FORMAT.R32G32B32A32_FLOAT),
                new("vFrameBlend", DXGI_FORMAT.R32_FLOAT),
            };

            var baseAttributes = declared.Count;

            for (var layer = 1; layer < ParticleTextureLayer.MaxLayers; layer++)
            {
                declared.Add(new VertexAttribute(LayerRectNames[layer - 1], DXGI_FORMAT.R32G32B32A32_FLOAT));
                declared.Add(new VertexAttribute(LayerRectNextNames[layer - 1], DXGI_FORMAT.R32G32B32A32_FLOAT));
            }

            // One rect pair per layer past the first, and the rest of the declared names are only here
            // so that both sides count the same slots
            var inBuffer = baseAttributes + (2 * (layerCount - 1));
            var floats = BaseInstanceFloats + (LayerInstanceFloats * (layerCount - 1));

            var declaredNames = declared.ConvertAll(attribute => attribute.Name).ToArray();
            var elements = declared.GetRange(0, inBuffer).ToArray();

            return new VertexInputLayout(floats * sizeof(float), declaredNames, elements);
        }

        private static readonly string[] LayerRectNames = ["vLayerRect0", "vLayerRect1", "vLayerRect2", "vLayerRect3"];
        private static readonly string[] LayerRectNextNames = ["vLayerRectNext0", "vLayerRectNext1", "vLayerRectNext2", "vLayerRectNext3"];

        private int SetupQuadBuffer(string label)
        {
            vertexBufferHandle = GraphicsDevice.CreateBuffer(label);

            // No index buffer: the corners are generated, and every attribute advances once per particle
            return instanceLayout.CreateVertexArray(label, vertexBufferHandle, indexBuffer: 0, instanceDivisor: 1);
        }

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
        /// <summary>The card-space coordinate of one quad corner, before any layer transform.</summary>
        internal static Vector2 CardUv(int corner) => corner switch
        {
            0 => new Vector2(0f, 1f),
            1 => new Vector2(0f, 0f),
            2 => new Vector2(1f, 0f),
            3 => new Vector2(1f, 1f),
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
            if (yaw != 0f && up.LengthSquared() > ParticleMath.MinimumLengthSquared)
            {
                right = Vector3.Transform(right, Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(up), yaw));
            }

            var face = Vector3.Cross(right, up);
            face = face.LengthSquared() > ParticleMath.MinimumLengthSquared ? Vector3.Normalize(face) : Vector3.UnitZ;
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
            if (w.LengthSquared() < ParticleMath.MinimumLengthSquared)
            {
                return billboard;
            }

            return QuadBasis(n, Vector3.Normalize(w), roll, yaw);
        }

        /// <summary>Fills and uploads the quad buffer, returning the number of quads actually emitted.</summary>
        private int UpdateVertices(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
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
            var minSizeSlope = minSize.NextNumber(systemState);
            var maxSizeSlope = maxSize.NextNumber(systemState);
            var startFadeSlope = startFadeSize.NextNumber(systemState);
            var endFadeSlope = endFadeSize.NextNumber(systemState);

            var centerOffset = new Vector2(
                CenterXOffset.NextNumber(systemState),
                CenterYOffset.NextNumber(systemState));

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

            // One set of attributes per particle, rented from the shared float pool so the memory is
            // reused across renderers.
            var instanceFloats = instanceLayout.Stride / sizeof(float);

            using (var vertexBuffer = new RentedFloatBuffer<float>(particles.Count * instanceFloats))
            {
                var instances = vertexBuffer.Span;
                var i = 0;
                foreach (ref var particle in particles.Current)
                {
                    var radiusScale = RadiusScale.NextNumber(ref particle, systemState);

                    // Scales rgb and alpha alike, matching the shader's fade of the whole vertex colour.
                    var colorFade = 1f;

                    // The view-angle fade touches alpha only, unlike the size fade below.
                    var alphaFade = 1f;

                    if (viewAngleFadeActive)
                    {
                        var toCamera = camera.Location - particle.Position;

                        if (toCamera.LengthSquared() > ParticleMath.MinimumLengthSquared)
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

                    var alphaScale = AlphaScale.NextNumber(ref particle, systemState);
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

                    // The corner map is corner.x * row0 + corner.y * row1 + translation, so the first two
                    // rows are already the card's axes with the radius folded in. Handing those over
                    // replaces four Vector4.Transform calls here and three duplicate vertices.
                    var right = new Vector3(modelMatrix.M11, modelMatrix.M12, modelMatrix.M13);
                    var up = new Vector3(modelMatrix.M21, modelMatrix.M22, modelMatrix.M23);

                    // The centre offset shifts the corners before the model matrix scales them, so it is
                    // measured in half-widths and folds into the origin along those same two axes.
                    var origin = new Vector3(modelMatrix.M41, modelMatrix.M42, modelMatrix.M43)
                        + (centerOffset.X * right)
                        + (centerOffset.Y * up);

                    // Each layer resolves frame rects against its own sheet, timed by the base sequence:
                    // companion sheets match the base rects, one-frame sequences pin an atlas region.
                    var (uvMin, uvMax, uvNextMin, uvNextMax) = GetLayerSheetUvs(0, ref particle, out var frameBlend);

                    var start = i * instanceFloats;

                    instances[start + 0] = origin.X;
                    instances[start + 1] = origin.Y;
                    instances[start + 2] = origin.Z;
                    instances[start + 3] = right.X;
                    instances[start + 4] = right.Y;
                    instances[start + 5] = right.Z;
                    instances[start + 6] = up.X;
                    instances[start + 7] = up.Y;
                    instances[start + 8] = up.Z;
                    instances[start + 9] = particle.Color.X * colorFade;
                    instances[start + 10] = particle.Color.Y * colorFade;
                    instances[start + 11] = particle.Color.Z * colorFade;
                    instances[start + 12] = alpha;
                    instances[start + 13] = uvMin.X;
                    instances[start + 14] = uvMin.Y;
                    instances[start + 15] = uvMax.X;
                    instances[start + 16] = uvMax.Y;
                    instances[start + 17] = uvNextMin.X;
                    instances[start + 18] = uvNextMin.Y;
                    instances[start + 19] = uvNextMax.X;
                    instances[start + 20] = uvNextMax.Y;
                    instances[start + 21] = frameBlend;

                    for (var layer = 1; layer < layers.Length; layer++)
                    {
                        var (layerMin, layerMax, layerNextMin, layerNextMax) = GetLayerSheetUvs(layer, ref particle, out _);
                        var layerStart = start + BaseInstanceFloats + ((layer - 1) * LayerInstanceFloats);

                        instances[layerStart + 0] = layerMin.X;
                        instances[layerStart + 1] = layerMin.Y;
                        instances[layerStart + 2] = layerMax.X;
                        instances[layerStart + 3] = layerMax.Y;
                        instances[layerStart + 4] = layerNextMin.X;
                        instances[layerStart + 5] = layerNextMin.Y;
                        instances[layerStart + 6] = layerNextMax.X;
                        instances[layerStart + 7] = layerNextMax.Y;
                    }

                    i++;
                }

                GL.NamedBufferData(vertexBufferHandle, i * instanceLayout.Stride, vertexBuffer.FloatArray, BufferUsageHint.DynamicDraw);

                return i;
            }
        }

        private int quadCount;

        /// <inheritdoc/>
        // Fully faded particles are skipped, so this can be fewer than the live particle count.
        public override void UpdateBuffers(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            quadCount = particles.Count == 0 ? 0 : UpdateVertices(particles, systemState, camera);
        }

        public override void Render(ParticleCollection particleBag, ParticleSystemState systemState, Camera camera)
        {
            if (particleBag.Count == 0)
            {
                return;
            }

            if (quadCount == 0)
            {
                return;
            }

            using var _ = SpritecardStateScope(GraphicsContext.RenderState, blendMode);

            shader.Use();
            VertexArray.Bind(vaoHandle, shader);

            ParticleTextureLayer.Bind(shader, layers, systemState);
            ParticleTextureLayer.BindUvTransforms(shader, layers, systemState);

            SetSharedUniforms(shader, systemState);
            shader.SetUniform1("uBlendFrames", blendFrames);
            shader.SetUniform1("uOutline", outline);
            shader.SetUniform4("uOutlineColor", outlineColor);
            shader.SetUniform4("uOutlineRanges", outlineRanges);

            // Distance alpha renders an SDF texture through the alpha remap smoothstep: the edge
            // softness pair replaces the remap range, or a hard threshold just under 0.5.
            if (distanceAlpha)
            {
                var remap = softEdges
                    ? new Vector2(edgeSoftnessEnd, edgeSoftnessStart)
                    : new Vector2(GetAlphaRemapRange(systemState).X, 0.499f);
                shader.SetUniform2("uAlphaRemapRange", remap);
            }

            // Set every draw: the program is shared with every other sprite renderer, whatever their mode.
            shader.SetUniform1("uBlendMode", (int)blendMode);

            PerfStats.Active.Count(Counter.ParticleDraw);

            // Four corners generated per particle, so the buffer holds no vertices at all
            GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, quadCount);
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
