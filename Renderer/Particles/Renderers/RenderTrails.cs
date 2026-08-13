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
        private const string ShaderName = "particle_spritecard";

        private const string DefaultTextureName = "materials/particle/base_trail.vtex";

        // The shared quad index buffer covers 65532 indices, six per quad
        private const int MaxQuads = 65532 / 6;

        // Quad corners in ring order, matching the winding of the shared quad index buffer
        private static readonly Vector2[] QuadCorners = [new(-1f, -1f), new(-1f, 1f), new(1f, 1f), new(1f, -1f)];

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

        private (int Vao, int Buffer) SetupQuadBuffer(string label)
        {
            var buffer = GraphicsDevice.CreateBuffer(label);
            var vao = SpritecardVertex.InputLayout.CreateVertexArray(label, buffer, rendererContext.MeshBufferCache.QuadIndices.GLHandle);

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

            // Rented from the shared float pool so the memory is reused across renderers.
            Span<ParticleTextureLayer.UvTransform> uvTransforms = stackalloc ParticleTextureLayer.UvTransform[ParticleTextureLayer.MaxLayers];
            Span<(Vector2 Min, Vector2 Max, Vector2 NextMin, Vector2 NextMax)> layerRects
                = stackalloc (Vector2, Vector2, Vector2, Vector2)[ParticleTextureLayer.MaxLayers];

            ParticleTextureLayer.ResolveUvTransforms(layers, systemState, uvTransforms);

            using (var vertexBuffer = new RentedFloatBuffer<SpritecardVertex>(particleBag.Count * 4))
            {
                var vertices = vertexBuffer.Span;

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

                    // Corners in index buffer winding order, with the local quad's [-1, 1] axes mapping to [0, 1] uvs
                    var quadStart = quadCount * 4;
                    var alpha = particle.Alpha * particle.AlphaAlternate * colorFade * alphaFade
                        * AlphaScale.NextNumber(ref particle, systemState);
                    var tint = particle.Color * colorFade;

                    var head = Vector4.Clamp(
                        new Vector4(tint * headColor, alpha * headAlphaScale.NextNumber(ref particle, systemState)),
                        Vector4.Zero, Vector4.One);
                    var tail = Vector4.Clamp(
                        new Vector4(tint * tailColor, alpha * tailAlphaScale.NextNumber(ref particle, systemState)),
                        Vector4.Zero, Vector4.One);

                    for (var j = 0; j < 4; ++j)
                    {
                        var corner = QuadCorners[j];
                        var isHead = corner.Y < 0f;
                        var worldPosition = center
                            + (widthAxis * (corner.X * (isHead ? headHalfWidth : tailHalfWidth)))
                            + (lengthAxis * (corner.Y * halfLength));
                        var color = isHead ? head : tail;
                        var cornerUv = (corner * 0.5f) + new Vector2(0.5f);

                        ref var vertex = ref vertices[quadStart + j];
                        vertex = default;
                        vertex.Position = worldPosition;
                        vertex.Color = color;
                        vertex.FrameBlend = frameBlend;
                        vertex.UV = ParticleTextureLayer.PlaceCorner(cornerUv, uvTransforms[0], layerRects[0].Min, layerRects[0].Max);
                        vertex.UVNextFrame = ParticleTextureLayer.PlaceCorner(cornerUv, uvTransforms[0], layerRects[0].NextMin, layerRects[0].NextMax);

                        for (var layer = 1; layer < layers.Length; layer++)
                        {
                            var (min, max, nextMin, nextMax) = layerRects[layer];
                            var layerUv = ParticleTextureLayer.PlaceCorner(cornerUv, uvTransforms[layer], min, max);
                            var layerUvNext = ParticleTextureLayer.PlaceCorner(cornerUv, uvTransforms[layer], nextMin, nextMax);
                            vertex.SetLayerUv(layer - 1, new Vector4(layerUv.X, layerUv.Y, layerUvNext.X, layerUvNext.Y));
                        }
                    }

                    quadCount++;

                    if (quadCount == MaxQuads)
                    {
                        break;
                    }
                }

                if (quadCount > 0)
                {
                    GL.NamedBufferData(vertexBufferHandle, quadCount * 4 * SpritecardVertex.InputLayout.Stride, vertexBuffer.FloatArray, BufferUsageHint.DynamicDraw);
                }
            }

            return quadCount;
        }

        public override void Render(ParticleCollection particleBag, ParticleSystemState systemState, Camera camera)
        {
            if (particleBag.Count == 0)
            {
                return;
            }

            var quadCount = UpdateVertices(particleBag, systemState, camera);

            if (quadCount == 0)
            {
                return;
            }

            using var _ = SpritecardStateScope(GraphicsContext.RenderState, blendMode);

            shader.Use();
            VertexArray.Bind(vaoHandle, shader);

            ParticleTextureLayer.Bind(shader, layers, systemState);

            // TODO: This formula is a guess but still seems too bright compared to valve particles
            SetSharedUniforms(shader, systemState);

            shader.SetUniform1("uBlendFrames", blendFrames);

            // Set every draw: the program is shared with every other spritecard renderer, whatever their mode.
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
