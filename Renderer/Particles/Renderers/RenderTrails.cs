using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders particles as trail segments stretched between the particle's current and previous
    /// positions, with configurable length, fade-in, texture scaling, and blend modes.
    /// </summary>
    /// <remarks>
    /// Trails are sprites that stretch based on their speed over time. Traditional use cases
    /// include bullet tracers and sparks; they are also useful when particles need to be oriented
    /// in 3D space, which regular sprites handle poorly.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderTrails">C_OP_RenderTrails</seealso>
    internal class RenderTrails : ParticleFunctionRenderer
    {
        private const string ShaderName = "particle_spritecard_trail";

        private const string DefaultTextureName = "materials/particle/base_trail.vtex";

        // The shared quad index buffer covers 65532 indices, six per quad
        private const int MaxQuads = 65532 / 6;

        // Quad corners in ring order, matching the winding of the shared quad index buffer
        private readonly Shader shader;
        private readonly RendererContext rendererContext;
        private readonly int vaoHandle;
        private readonly int vertexBufferHandle;

        private readonly float animationRate = 0.1f;
        private readonly ParticleAnimationType animationType = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;
        private readonly bool animateInFps;
        private readonly bool blendFrames = true;

        // m_bEnableFadingAndClamping gates the size clamp, the distance fade and the view angle fade
        // together. The bounds below are trail-specific and differ from the sprite renderer's.
        private readonly bool enableFadingAndClamping;
        private readonly float minSize;
        private readonly float maxSize = 2000f;
        private readonly INumberProvider startFadeSize = new LiteralNumberProvider(1000f);
        private readonly INumberProvider endFadeSize = new LiteralNumberProvider(2000f);
        private readonly float startFadeDot = 1f;
        private readonly float endFadeDot = 2f;

        private readonly ParticleBlendMode blendMode = ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA;
        private readonly ParticleTextureLayer[] layers;
        private readonly ParticleOrientation orientationType;
        private readonly ParticleField prevPositionSource = ParticleField.PositionPrevious; // this is a real thing

        private readonly float maxLength = 2000f;
        private readonly float minLength;
        private readonly float lengthScale = 1f;
        private readonly float lengthFadeInTime;
        private readonly bool ignoreDeltaTime;
        private readonly float constrainRadiusToLengthRatio = 1f;
        private readonly float forwardShift;

        private readonly INumberProvider headRadiusTaper = new LiteralNumberProvider(1f);
        private readonly INumberProvider tailRadiusTaper = new LiteralNumberProvider(1f);
        private readonly INumberProvider headAlphaScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider tailAlphaScale = new LiteralNumberProvider(1f);
        private readonly IVectorProvider headColorScale = new LiteralVectorProvider(Vector3.One);
        private readonly IVectorProvider tailColorScale = new LiteralVectorProvider(Vector3.One);

        public RenderTrails(ParticleDefinitionParser parse, RendererContext rendererContext) : base(parse)
        {
            this.rendererContext = rendererContext;

            blendMode = parse.Enum<ParticleBlendMode>("m_nOutputBlendMode", blendMode);

            (layers, var textureName) = ParticleTextureLayer.Build(parse, rendererContext, DefaultTextureName, srgbRead: OutputIsColor);

            shader = rendererContext.ShaderLoader.LoadShader(ShaderName, ("S_TEXTURE_LAYERS", (byte)(layers.Length - 1)));

            instanceLayout = BuildInstanceLayout(layers.Length);

            // All trails of this renderer are batched into a single dynamic vertex buffer
            (vaoHandle, vertexBufferHandle) = SetupQuadBuffer($"{nameof(RenderTrails)}: {System.IO.Path.GetFileName(textureName)}");

            orientationType = parse.Enum("m_nOrientationType", orientationType);
            animationRate = parse.Float("m_flAnimationRate", animationRate);
            maxLength = parse.Float("m_flMaxLength", maxLength);
            minLength = parse.Float("m_flMinLength", minLength);
            lengthScale = parse.Float("m_flLengthScale", lengthScale);
            lengthFadeInTime = parse.Float("m_flLengthFadeInTime", lengthFadeInTime);
            ignoreDeltaTime = parse.Boolean("m_bIgnoreDT", ignoreDeltaTime);
            constrainRadiusToLengthRatio = parse.Float("m_flConstrainRadiusToLengthRatio", constrainRadiusToLengthRatio);
            forwardShift = parse.Float("m_flForwardShift", forwardShift);
            animationType = parse.Enum<ParticleAnimationType>("m_nAnimationType", animationType);
            animateInFps = parse.Boolean("m_bAnimateInFPS", animateInFps);
            blendFrames = parse.Boolean("m_bBlendFramesSeq0", blendFrames);
            enableFadingAndClamping = parse.Boolean("m_bEnableFadingAndClamping", enableFadingAndClamping);
            minSize = parse.Float("m_flMinSize", minSize);
            maxSize = parse.Float("m_flMaxSize", maxSize);
            startFadeSize = parse.NumberProvider("m_flStartFadeSize", startFadeSize);
            endFadeSize = parse.NumberProvider("m_flEndFadeSize", endFadeSize);
            startFadeDot = parse.Float("m_flStartFadeDot", startFadeDot);
            endFadeDot = parse.Float("m_flEndFadeDot", endFadeDot);
            prevPositionSource = parse.ParticleField("m_nPrevPntSource", prevPositionSource);
            headRadiusTaper = parse.NumberProvider("m_flRadiusHeadTaper", headRadiusTaper);
            tailRadiusTaper = parse.NumberProvider("m_flRadiusTaper", tailRadiusTaper);
            headAlphaScale = parse.NumberProvider("m_flHeadAlphaScale", headAlphaScale);
            tailAlphaScale = parse.NumberProvider("m_flTailAlphaScale", tailAlphaScale);
            headColorScale = parse.VectorProvider("m_vecHeadColorScale", headColorScale);
            tailColorScale = parse.VectorProvider("m_vecTailColorScale", tailColorScale);

            if (minLength > maxLength)
            {
                // Some particles may have length range set up incorrectly
                maxLength = minLength;
            }
        }

        public override void SetWireframe(bool isWireframe)
        {
            shader.SetUniform1("isWireframe", isWireframe ? 1 : 0);
        }

        /// <inheritdoc/>
        public override void SetTextureOverride(RenderTexture texture)
        {
            // The override stands in for the base texture; layers composited over it keep their own.
            layers[0].Texture = texture;
        }

        // Floats per particle before the extra layers: centre 3, head colour 4, tail colour 4, the across
        // axis and the head half width 4, the along axis and the tail half width 4, this frame's uv rect
        // 4, the next frame's 4, frame blend 1.
        private const int BaseInstanceFloats = 28;

        // Each layer past the first resolves its own sheet frame, so it carries its own pair of rects.
        private const int LayerInstanceFloats = 8;

        private readonly VertexInputLayout instanceLayout;

        private static readonly string[] LayerRectNames = ["vLayerRect0", "vLayerRect1", "vLayerRect2", "vLayerRect3"];
        private static readonly string[] LayerRectNextNames = ["vLayerRectNext0", "vLayerRectNext1", "vLayerRectNext2", "vLayerRectNext3"];

        /// <summary>
        /// One set of attributes per particle rather than per corner: the four corners come from
        /// <c>gl_VertexID</c>, so nothing is duplicated four times. The buffer carries only the layers
        /// this trail has, so a one layer trail pays nothing for the four it does not.
        /// </summary>
        /// <remarks>
        /// The locations are still allocated against every name the shader file declares, layers this
        /// trail does not have included, because that is what the shader allocates against: its
        /// <c>#if</c> blocks are still in the source the locations are stamped over. Allocating over the
        /// shorter list would place the axes somewhere the shader never reads.
        /// </remarks>
        private static VertexInputLayout BuildInstanceLayout(int layerCount)
        {
            // In shader declaration order, so taking a prefix of it is a valid buffer layout
            var declared = new List<VertexAttribute>
            {
                new(VertexSlot.Position, DXGI_FORMAT.R32G32B32_FLOAT),
                new(VertexSlot.Color, DXGI_FORMAT.R32G32B32A32_FLOAT),
                new("vColorTail", DXGI_FORMAT.R32G32B32A32_FLOAT),
                new("vWidthAxis", DXGI_FORMAT.R32G32B32A32_FLOAT),
                new("vLengthAxis", DXGI_FORMAT.R32G32B32A32_FLOAT),
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

            var inBuffer = baseAttributes + (2 * (layerCount - 1));
            var floats = BaseInstanceFloats + (LayerInstanceFloats * (layerCount - 1));

            var declaredNames = declared.ConvertAll(attribute => attribute.Name).ToArray();
            var elements = declared.GetRange(0, inBuffer).ToArray();

            return new VertexInputLayout(floats * sizeof(float), declaredNames, elements);
        }

        private (int Vao, int Buffer) SetupQuadBuffer(string label)
        {
            var buffer = GraphicsDevice.CreateBuffer(label);
            // No index buffer: the corners are generated, and every attribute advances once per particle
            var vao = instanceLayout.CreateVertexArray(label, buffer, indexBuffer: 0, instanceDivisor: 1);

            return (vao, buffer);
        }

        /// <summary>
        /// Builds one quad per visible trail into the shared vertex buffer, returning how many were written.
        /// </summary>
        private int UpdateVertices(ParticleCollection particleBag, ParticleSystemState systemState, Camera camera)
        {
            var headColor = headColorScale.NextVector(systemState);
            var tailColor = tailColorScale.NextVector(systemState);

            // The moved distance is converted back to a velocity (distance / dt) before scaling by
            // the trail-length attribute. The division only applies when the previous point comes
            // from the Verlet pair, and the operator can opt out of it entirely.
            var usesVerletDelta = prevPositionSource == ParticleField.PositionPrevious;
            var oneOverDt = ignoreDeltaTime || !usesVerletDelta || particleBag.CurrentFrameTime == 0f
                ? 1f
                : 1f / particleBag.CurrentFrameTime;

            // Both fade bounds are a radius per unit of camera distance, as the two size bounds are.
            var startFadeSlope = startFadeSize.NextNumber(systemState);
            var endFadeSlope = endFadeSize.NextNumber(systemState);

            // The shader fades by view angle in every mode and outside the fade-and-clamp gate; the
            // defaults of (1, 2) put the smoothstep past its own range, which is what makes it inert.
            var viewAngleFadeActive = startFadeDot < 1f && endFadeDot > startFadeDot;

            var quadCount = 0;

            Span<(Vector2 Min, Vector2 Max, Vector2 NextMin, Vector2 NextMax)> layerRects
                = stackalloc (Vector2, Vector2, Vector2, Vector2)[ParticleTextureLayer.MaxLayers];

            // One set of attributes per particle, rented from the shared float pool so the memory is
            // reused across renderers.
            var instanceFloats = instanceLayout.Stride / sizeof(float);

            using (var vertexBuffer = new RentedFloatBuffer<float>(particleBag.Count * instanceFloats))
            {
                var instances = vertexBuffer.Span;

                foreach (ref var particle in particleBag.Current)
                {
                    var position = particle.Position;
                    var previousPosition = particle.GetVector(prevPositionSource);
                    // A particle that has not moved has no direction to run in, and the engine collapses
                    // its four control points onto the position rather than streaking along a fixed axis
                    var difference = previousPosition - position;

                    if (difference == Vector3.Zero)
                    {
                        continue;
                    }

                    var direction = Vector3.Normalize(difference);

                    var length = lengthScale * particle.TrailLength * difference.Length() * oneOverDt;

                    length *= MathF.Min(1f, particle.Age / MathF.Max(lengthFadeInTime, 1e-9f));

                    // The engine clamps the full extent of the trail, and it clamps unconditionally: an
                    // effect that authors m_flLengthScale 0 alongside a minimum length is asking for a
                    // fixed streak that does not track speed, so a zero raw length still draws.
                    length = Math.Clamp(length, minLength, maxLength);

                    if (length == 0f)
                    {
                        continue;
                    }

                    var particleRadius = particle.Radius * RadiusScale.NextNumber(ref particle, systemState);

                    // Scales rgb and alpha alike, as the view angle fade below touches alpha only
                    var colorFade = 1f;
                    var alphaFade = 1f;

                    if (enableFadingAndClamping)
                    {
                        var cameraDistance = Vector3.Distance(camera.Location, particle.Position);
                        var fadeStart = startFadeSlope * cameraDistance;
                        var fadeEnd = endFadeSlope * cameraDistance;

                        if (particleRadius > fadeStart)
                        {
                            if (particleRadius >= fadeEnd)
                            {
                                continue;
                            }

                            colorFade = 1f - ((particleRadius - fadeStart) / (fadeEnd - fadeStart));
                        }
                    }

                    if (viewAngleFadeActive)
                    {
                        var toCamera = camera.Location - particle.Position;

                        if (toCamera.LengthSquared() > ParticleMath.MinimumLengthSquared)
                        {
                            // Only the normal-aligned mode has a normal to face with; the others
                            // substitute the direction the ribbon runs in.
                            var facingAxis = orientationType == ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL
                                ? particle.Normal
                                : direction;

                            var facing = MathF.Abs(Vector3.Dot(Vector3.Normalize(facingAxis), Vector3.Normalize(toCamera)));
                            alphaFade = 1f - MathUtils.Smoothstep(startFadeDot, endFadeDot, facing);
                        }
                    }

                    // A short trail is narrowed so it cannot render wider than it is long
                    var radius = MathF.Min(particleRadius, constrainRadiusToLengthRatio * length);

                    // The ribbon always runs along the motion; only the plane it is flattened into
                    // changes with the orientation. The spritecard vertex shader takes the up vector
                    // for that plane from three buckets, and only ALIGN_TO_PARTICLE_NORMAL reads the
                    // particle's normal: it is the sole mode whose instance record carries one.
                    var planeNormal = orientationType switch
                    {
                        ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL => particle.Normal,
                        ParticleOrientation.PARTICLE_ORIENTATION_WORLD_Z_ALIGNED => Vector3.UnitZ,
                        _ => camera.Location - position,
                    };

                    var widthAxis = Vector3.Cross(direction, planeNormal);
                    widthAxis = widthAxis.LengthSquared() > ParticleMath.MinimumLengthSquared
                        ? Vector3.Normalize(widthAxis)
                        : Vector3.Normalize(Vector3.Cross(direction, MathF.Abs(direction.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitX));

                    var lengthAxis = direction;
                    // The radius is the half extent across the ribbon, while the length spans it end to end
                    var halfWidth = radius;
                    var halfLength = length * 0.5f;

                    // The engine slides the trail along the motion axis by m_flForwardShift lengths;
                    // direction runs backwards along travel here, so the shift subtracts
                    var center = position + (direction * (length * (0.5f - forwardShift)));

                    var headHalfWidth = halfWidth * headRadiusTaper.NextNumber(ref particle, systemState);
                    var tailHalfWidth = halfWidth * tailRadiusTaper.NextNumber(ref particle, systemState);

                    // The shader clamps per vertex, on the radius the CPU has already constrained and
                    // tapered, so each end is bounded against its own distance rather than the centre's
                    if (enableFadingAndClamping)
                    {
                        var headDistance = Vector3.Distance(camera.Location, center - (lengthAxis * halfLength));
                        var tailDistance = Vector3.Distance(camera.Location, center + (lengthAxis * halfLength));

                        headHalfWidth = MathF.Min(MathF.Max(headHalfWidth, minSize * headDistance), maxSize * headDistance);
                        tailHalfWidth = MathF.Min(MathF.Max(tailHalfWidth, minSize * tailDistance), maxSize * tailDistance);
                    }

                    var frameBlend = 0f;

                    for (var layer = 0; layer < layers.Length; layer++)
                    {
                        var min = Vector2.Zero;
                        var max = Vector2.One;
                        var nextMin = min;
                        var nextMax = max;

                        var spriteSheetData = layers[layer].Texture.SpriteSheetData;
                        if (spriteSheetData != null && spriteSheetData.Sequences.Length > 0 && spriteSheetData.Sequences[0].Frames.Length > 0)
                        {
                            var sequence = spriteSheetData.Sequences[particle.SequenceNumber % spriteSheetData.Sequences.Length];
                            var (frame, nextFrame, blend) = GetSheetFrame(ref particle, sequence, animationRate, animationType, animateInFps);

                            // TODO: Support more than one image per frame?
                            var currentImage = sequence.Frames[frame].Images[0];
                            var nextImage = sequence.Frames[nextFrame].Images[0];

                            min = currentImage.UncroppedMin;
                            max = currentImage.UncroppedMax;
                            nextMin = nextImage.UncroppedMin;
                            nextMax = nextImage.UncroppedMax;

                            if (layer == 0)
                            {
                                frameBlend = blend;
                            }
                        }

                        layerRects[layer] = (min, max, nextMin, nextMax);
                    }

                    var start = quadCount * instanceFloats;
                    var alpha = particle.Alpha * particle.AlphaAlternate * colorFade * alphaFade
                        * AlphaScale.NextNumber(ref particle, systemState);
                    var tint = particle.Color * colorFade;

                    var head = Vector4.Clamp(
                        new Vector4(tint * headColor, alpha * headAlphaScale.NextNumber(ref particle, systemState)),
                        Vector4.Zero, Vector4.One);
                    var tail = Vector4.Clamp(
                        new Vector4(tint * tailColor, alpha * tailAlphaScale.NextNumber(ref particle, systemState)),
                        Vector4.Zero, Vector4.One);

                    // The ribbon spans centre +- lengthAxis * halfLength, so folding the half length into
                    // the axis leaves the shader a corner offset of exactly +-1 along it. The half widths
                    // stay separate: the two ends taper independently, which is what makes the quad a
                    // trapezoid rather than a parallelogram.
                    var lengthVector = lengthAxis * halfLength;

                    instances[start + 0] = center.X;
                    instances[start + 1] = center.Y;
                    instances[start + 2] = center.Z;
                    instances[start + 3] = head.X;
                    instances[start + 4] = head.Y;
                    instances[start + 5] = head.Z;
                    instances[start + 6] = head.W;
                    instances[start + 7] = tail.X;
                    instances[start + 8] = tail.Y;
                    instances[start + 9] = tail.Z;
                    instances[start + 10] = tail.W;
                    instances[start + 11] = widthAxis.X;
                    instances[start + 12] = widthAxis.Y;
                    instances[start + 13] = widthAxis.Z;
                    instances[start + 14] = headHalfWidth;
                    instances[start + 15] = lengthVector.X;
                    instances[start + 16] = lengthVector.Y;
                    instances[start + 17] = lengthVector.Z;
                    instances[start + 18] = tailHalfWidth;
                    instances[start + 19] = layerRects[0].Min.X;
                    instances[start + 20] = layerRects[0].Min.Y;
                    instances[start + 21] = layerRects[0].Max.X;
                    instances[start + 22] = layerRects[0].Max.Y;
                    instances[start + 23] = layerRects[0].NextMin.X;
                    instances[start + 24] = layerRects[0].NextMin.Y;
                    instances[start + 25] = layerRects[0].NextMax.X;
                    instances[start + 26] = layerRects[0].NextMax.Y;
                    instances[start + 27] = frameBlend;

                    for (var layer = 1; layer < layers.Length; layer++)
                    {
                        var (min, max, nextMin, nextMax) = layerRects[layer];
                        var layerStart = start + BaseInstanceFloats + ((layer - 1) * LayerInstanceFloats);

                        instances[layerStart + 0] = min.X;
                        instances[layerStart + 1] = min.Y;
                        instances[layerStart + 2] = max.X;
                        instances[layerStart + 3] = max.Y;
                        instances[layerStart + 4] = nextMin.X;
                        instances[layerStart + 5] = nextMin.Y;
                        instances[layerStart + 6] = nextMax.X;
                        instances[layerStart + 7] = nextMax.Y;
                    }

                    quadCount++;

                    if (quadCount == MaxQuads)
                    {
                        break;
                    }
                }

                if (quadCount > 0)
                {
                    GL.NamedBufferData(vertexBufferHandle, quadCount * instanceLayout.Stride, vertexBuffer.FloatArray, BufferUsageHint.DynamicDraw);
                }
            }

            return quadCount;
        }

        private int quadCount;

        /// <inheritdoc/>
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

            // TODO: This formula is a guess but still seems too bright compared to valve particles
            SetSharedUniforms(shader, systemState);

            shader.SetUniform1("uBlendFrames", blendFrames);

            // Set every draw: the program is shared with every other spritecard renderer, whatever their mode.
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
