using System.Runtime.InteropServices;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders particles as trail segments stretched between the particle's current and previous
    /// positions, with configurable length, fade-in, texture scaling, and blend modes.
    /// </summary>
    /// <remarks>
    /// Trails are sprites stretched by speed over time. Typical uses are bullet tracers and sparks,
    /// and particles that need 3D orientation, which regular sprites handle poorly.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderTrails">C_OP_RenderTrails</seealso>
    internal class RenderTrails : ParticleSpritecardRenderer
    {
        private const string DefaultTextureName = "materials/particle/base_trail.vtex";

        // m_bEnableFadingAndClamping gates the size clamp, the distance fade and the view angle fade
        // together. The bounds below are trail-specific and differ from the sprite renderer's.
        private readonly bool enableFadingAndClamping;
        private readonly float minSize;
        private readonly float maxSize = 2000f;
        private readonly INumberProvider startFadeSize = new LiteralNumberProvider(1000f);
        private readonly INumberProvider endFadeSize = new LiteralNumberProvider(2000f);

        private readonly ParticleOrientation orientationType;
        private readonly ParticleField prevPositionSource = ParticleField.PositionPrevious;

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

        public RenderTrails(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene)
            : base(parse, rendererContext, scene, DefaultTextureName, instancing: 2)
        {
            // In buffer order. Each axis carries one end's half width in w.
            CreateVertexArray(BuildInstanceLayout<TrailInstance>(
            [
                new(VertexSlot.Position, DXGI_FORMAT.R32G32B32_FLOAT),
                new(VertexSlot.Color, DXGI_FORMAT.R32G32B32A32_FLOAT),
                new("vColorTail", DXGI_FORMAT.R32G32B32A32_FLOAT),
                new("vAxisU", DXGI_FORMAT.R32G32B32A32_FLOAT),
                new("vAxisV", DXGI_FORMAT.R32G32B32A32_FLOAT),
                new(VertexSlot.TexCoord, DXGI_FORMAT.R32G32B32A32_FLOAT),
                new(VertexSlot.TexCoord1, DXGI_FORMAT.R32G32B32A32_FLOAT),
                new("vFrameBlendAlphaRef", DXGI_FORMAT.R32G32B32_FLOAT),
            ]), instanceDivisor: 1);

            orientationType = parse.Enum("m_nOrientationType", orientationType);
            maxLength = parse.Float("m_flMaxLength", maxLength);
            minLength = parse.Float("m_flMinLength", minLength);
            lengthScale = parse.Float("m_flLengthScale", lengthScale);
            lengthFadeInTime = parse.Float("m_flLengthFadeInTime", lengthFadeInTime);
            ignoreDeltaTime = parse.Boolean("m_bIgnoreDT", ignoreDeltaTime);
            constrainRadiusToLengthRatio = parse.Float("m_flConstrainRadiusToLengthRatio", constrainRadiusToLengthRatio);
            forwardShift = parse.Float("m_flForwardShift", forwardShift);
            enableFadingAndClamping = parse.Boolean("m_bEnableFadingAndClamping", enableFadingAndClamping);
            minSize = parse.Float("m_flMinSize", minSize);
            maxSize = parse.Float("m_flMaxSize", maxSize);
            startFadeSize = parse.NumberProvider("m_flStartFadeSize", startFadeSize);
            endFadeSize = parse.NumberProvider("m_flEndFadeSize", endFadeSize);
            prevPositionSource = parse.ParticleField("m_nPrevPntSource", prevPositionSource);
            headRadiusTaper = parse.NumberProvider("m_flRadiusHeadTaper", headRadiusTaper);
            tailRadiusTaper = parse.NumberProvider("m_flRadiusTaper", tailRadiusTaper);
            headAlphaScale = parse.NumberProvider("m_flHeadAlphaScale", headAlphaScale);
            tailAlphaScale = parse.NumberProvider("m_flTailAlphaScale", tailAlphaScale);
            headColorScale = parse.VectorProvider("m_vecHeadColorScale", headColorScale);
            tailColorScale = parse.VectorProvider("m_vecTailColorScale", tailColorScale);
        }

        /// <summary>One particle's attributes, in buffer order. Must match the layout built in the constructor.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct TrailInstance
        {
            public Vector3 Center;
            public Vector4 ColorHead;
            public Vector4 ColorTail;

            /// <summary>Across the ribbon in xyz, the head's half width in w.</summary>
            public Vector4 AxisU;

            /// <summary>Along the ribbon and already halved in xyz, the tail's half width in w.</summary>
            public Vector4 AxisV;

            public Vector4 UvRect;
            public Vector4 UvRectNext;
            public Vector3 FrameBlendAlphaReference;
        }

        /// <inheritdoc/>
        protected override int UpdateVertices(ParticleCollection particleBag, ParticleSystemState systemState, Camera camera)
        {
            var scales = EvaluateScales(systemState);

            if (scales.Radius == 0f)
            {
                return 0;
            }

            var radiusScale = scales.Radius < 0f ? 1f : scales.Radius;

            // Converts the moved distance to a velocity (distance / dt) before scaling by the trail-length
            // attribute. The division only applies when the previous point is the Verlet one, and the
            // operator can opt out of it.
            var usesVerletDelta = prevPositionSource == ParticleField.PositionPrevious;
            var oneOverDt = ignoreDeltaTime || !usesVerletDelta || systemState.CurrentStep == 0f
                ? 1f
                : 1f / systemState.CurrentStep;

            // Both fade bounds are a radius per unit of camera distance, as the two size bounds are.
            var startFadeSlope = startFadeSize.NextNumber(systemState);
            var endFadeSlope = endFadeSize.NextNumber(systemState);

            // The shader fades by view angle in every mode and outside the fade-and-clamp gate; the
            // defaults of (1, 2) put the smoothstep past its own range, which is what makes it inert.
            var viewAngleFadeActive = FadesByViewAngle;

            var quadCount = 0;

            Span<SheetRects> layerRects = stackalloc SheetRects[ParticleTextureLayer.MaxLayers];

            // One set of attributes per particle, rented from the shared byte pool so the memory is
            // reused across renderers.
            var instanceFloats = VertexFloats;

            var sorted = particleBag.Count > 1;

            using (var vertexBuffer = new RentedBuffer<float>(particleBag.Count * instanceFloats))
            using (var drawOrder = new RentedBuffer<ulong>(sorted ? particleBag.Count : 0))
            {
                var instances = vertexBuffer.Span;
                var current = GetBlendedParticles(particleBag, systemState);

                var order = drawOrder.Span;

                if (sorted)
                {
                    SortBackToFront(current, camera.Location, order);
                }

                // Every input is evaluated for every particle before the loop, culled or not, in this order
                var tailRadiusTapers = tailRadiusTaper.Fill(current, systemState);
                var headRadiusTapers = headRadiusTaper.Fill(current, systemState);
                var tailColor = tailColorScale.NextVector(systemState);
                var tailAlphaScales = tailAlphaScale.Fill(current, systemState);
                var headColor = headColorScale.NextVector(systemState);
                var headAlphaScales = headAlphaScale.Fill(current, systemState);

                var positions = current.ReadVectors(ParticleField.Position);
                var previousPositions = current.ReadVectors(prevPositionSource);
                var normals = current.ReadVectors(ParticleField.Normal);
                var colors = current.ReadVectors(ParticleField.Color);
                var trailLengths = current.ReadFloats(ParticleField.TrailLength);
                var radii = current.ReadFloats(ParticleField.Radius);
                var alphas = current.ReadFloats(ParticleField.Alpha);
                var alpha2s = ReadAlpha2(current);
                var sheetInputs = new SheetInputs(current);
                var alphaReferenceInputs = ReadAlphaReferenceInputs(current, sheetInputs.Ages);

                for (var n = 0; n < current.Length; n++)
                {
                    var index = sorted ? (int)(uint)order[n] : n;
                    var position = BiasTowardCamera(positions[index], camera.Location);
                    var previousPosition = previousPositions[index];

                    // A particle that has not moved has no direction to run in, so its control points
                    // collapse onto the position
                    var difference = previousPosition - position;
                    var distance = MathF.Max(difference.Length(), ParticleMath.FloatEpsilon);
                    var direction = difference / distance;

                    var length = lengthScale * trailLengths[index] * distance * oneOverDt;

                    // A particle drawn before its birth, with a negative age, gets the minimum length
                    length *= MathF.Min(1f, sheetInputs.Ages[index] / MathF.Max(lengthFadeInTime, 1e-9f));
                    length = MathF.Max(minLength, MathF.Min(maxLength, length));

                    var particleRadius = radii[index] * radiusScale;

                    // Scales rgb and alpha alike, as the view angle fade below touches alpha only
                    var colorFade = 1f;
                    var alphaFade = 1f;

                    if (enableFadingAndClamping)
                    {
                        var cameraDistance = Vector3.Distance(camera.Location, position);
                        var fadeStart = startFadeSlope * cameraDistance;
                        var fadeEnd = endFadeSlope * cameraDistance;

                        if (particleRadius > fadeStart)
                        {
                            if (particleRadius >= fadeEnd)
                            {
                                continue;
                            }

                            colorFade = 1f - MathUtils.Remap(particleRadius, fadeStart, fadeEnd);
                        }
                    }

                    if (viewAngleFadeActive)
                    {
                        // Only the normal-aligned mode has a normal to face with; the others
                        // substitute the direction the ribbon runs in.
                        var facingAxis = orientationType == ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL
                            ? normals[index]
                            : direction;

                        alphaFade = ViewAngleFade(facingAxis, position, camera.Location);
                    }

                    // A short trail is narrowed so it cannot render wider than it is long
                    var radius = MathF.Min(particleRadius, constrainRadiusToLengthRatio * length);

                    // The ribbon always runs along the motion; only the plane it is flattened into
                    // changes with the orientation. The spritecard vertex shader takes the up vector
                    // for that plane from three buckets, and only ALIGN_TO_PARTICLE_NORMAL reads the
                    // particle's normal: it is the sole mode whose instance record carries one.
                    var planeNormal = orientationType switch
                    {
                        ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL => normals[index],
                        ParticleOrientation.PARTICLE_ORIENTATION_WORLD_Z_ALIGNED => Vector3.UnitZ,
                        _ => camera.Location - position,
                    };

                    var widthAxis = RibbonWidthAxis(direction, planeNormal);

                    // The radius is the half extent across the ribbon, while the length spans it end to end
                    var halfLength = length * 0.5f;

                    // The trail slides along the motion axis by m_flForwardShift lengths;
                    // direction runs backwards along travel here, so the shift subtracts
                    var center = position + (direction * (length * (0.5f - forwardShift)));

                    var headHalfWidth = radius * headRadiusTapers[index];
                    var tailHalfWidth = radius * tailRadiusTapers[index];

                    // The shader clamps per vertex, on the radius the CPU has already constrained and
                    // tapered, so each end is bounded against its own distance rather than the centre's
                    if (enableFadingAndClamping)
                    {
                        var headDistance = Vector3.Distance(camera.Location, center - (direction * halfLength));
                        var tailDistance = Vector3.Distance(camera.Location, center + (direction * halfLength));

                        headHalfWidth = MathF.Min(MathF.Max(headHalfWidth, minSize * headDistance), maxSize * headDistance);
                        tailHalfWidth = MathF.Min(MathF.Max(tailHalfWidth, minSize * tailDistance), maxSize * tailDistance);
                    }

                    var frameBlend = 0f;

                    var sequenceNumber = (int)sheetInputs.SequenceNumbers[index];

                    for (var layer = 0; layer < Layers.Length; layer++)
                    {
                        layerRects[layer] = SheetRects.Whole;

                        // TODO: A frame with more than one image draws only its first
                        if (GetSheetFrames(layer, sequenceNumber, sheetInputs, index) is { } frames)
                        {
                            layerRects[layer] = UncroppedRects(frames);

                            if (layer == 0)
                            {
                                frameBlend = frames.Blend;
                            }
                        }
                    }

                    var color = BlendColor(colors[index], alphas[index], alpha2s[index], scales);
                    var alpha = color.W * colorFade * alphaFade;
                    var tint = color.AsVector3() * colorFade;
                    var alphaReference = GetAlphaReference(alphaReferenceInputs, index, radius, alpha);

                    var head = Vector4.Clamp(
                        new Vector4(tint * headColor, alpha * headAlphaScales[index]),
                        Vector4.Zero, Vector4.One);
                    var tail = Vector4.Clamp(
                        new Vector4(tint * tailColor, alpha * tailAlphaScales[index]),
                        Vector4.Zero, Vector4.One);

                    // Folding the half length into the along axis leaves the shader a corner offset of
                    // exactly +-1 along it. The half widths stay separate: the ends taper independently,
                    // which is what makes the quad a trapezoid rather than a parallelogram.
                    var layerInstanceRects = WriteInstance(instances, quadCount, new TrailInstance
                    {
                        Center = center,
                        ColorHead = head,
                        ColorTail = tail,
                        AxisU = new Vector4(widthAxis, headHalfWidth),
                        AxisV = new Vector4(direction * halfLength, tailHalfWidth),
                        UvRect = Rect(layerRects[0].Min, layerRects[0].Max),
                        UvRectNext = Rect(layerRects[0].NextMin, layerRects[0].NextMax),
                        FrameBlendAlphaReference = new Vector3(frameBlend, alphaReference.X, alphaReference.Y),
                    });

                    for (var layer = 1; layer < Layers.Length; layer++)
                    {
                        WriteLayerRects(layerInstanceRects, layer, layerRects[layer]);
                    }

                    quadCount++;
                }

                if (quadCount > 0)
                {
                    UploadVertices(vertexBuffer.ByteArray, quadCount);
                }
            }

            return quadCount;
        }

        /// <inheritdoc/>
        protected override void SetDrawUniforms(ParticleSystemState systemState)
        {
            ParticleTextureLayer.BindUvTransforms(Shader, Layers, systemState);
            base.SetDrawUniforms(systemState);
        }
    }
}
