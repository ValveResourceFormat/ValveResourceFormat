using System.Runtime.InteropServices;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Utils;

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

        // m_bEnableFadingAndClamping gates the size clamp and the distance fade together. The bounds
        // below are trail-specific and differ from the sprite renderer's.
        private readonly bool enableFadingAndClamping;
        private readonly float minSize;
        private readonly float maxSize = 2000f;
        private readonly INumberProvider startFadeSize = new LiteralNumberProvider(1000f);
        private readonly INumberProvider endFadeSize = new LiteralNumberProvider(2000f);

        private readonly ParticleOrientation orientationType;
        private readonly ParticleField prevPositionSource = ParticleField.PositionPrevious;

        /// <summary>Whether the previous point is the Verlet one, whose distance moved is converted to a velocity.</summary>
        private readonly bool usesVerletDelta;

        private readonly bool flipUvByPitchYaw;

        /// <summary>The fields that crop the card across and along the ribbon; any field that is not a scalar crops nothing.</summary>
        private readonly ParticleField horizontalCropField = ParticleField.NoneDisabled;

        /// <inheritdoc cref="horizontalCropField"/>
        private readonly ParticleField verticalCropField = ParticleField.NoneDisabled;

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

        /// <summary>Whether the visibility inputs scale the trail, which they do from behavior version 11.</summary>
        private readonly bool appliesVisibility;

        // Evaluated with the particle inputs and uploaded with the draw, since they are the same for the whole system
        private Vector3 headColor = Vector3.One;
        private Vector3 tailColor = Vector3.One;

        public RenderTrails(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene)
            : base(parse, rendererContext, scene, DefaultTextureName, instancing: 2)
        {
            CreateVertexArray(InstanceLayout<TrailInstance>(), instanceDivisor: 1);

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

            usesVerletDelta = prevPositionSource == ParticleField.PositionPrevious;

            // No source still reads the Verlet previous position, but as a plain point
            if (prevPositionSource < 0)
            {
                prevPositionSource = ParticleField.PositionPrevious;
            }

            flipUvByPitchYaw = parse.Boolean("m_bFlipUVBasedOnPitchYaw", flipUvByPitchYaw);
            horizontalCropField = parse.ParticleField("m_nHorizCropField", horizontalCropField);
            verticalCropField = parse.ParticleField("m_nVertCropField", verticalCropField);
            headRadiusTaper = parse.NumberProvider("m_flRadiusHeadTaper", headRadiusTaper);
            tailRadiusTaper = parse.NumberProvider("m_flRadiusTaper", tailRadiusTaper);
            headAlphaScale = parse.NumberProvider("m_flHeadAlphaScale", headAlphaScale);
            tailAlphaScale = parse.NumberProvider("m_flTailAlphaScale", tailAlphaScale);
            headColorScale = parse.VectorProvider("m_vecHeadColorScale", headColorScale);
            tailColorScale = parse.VectorProvider("m_vecTailColorScale", tailColorScale);
            appliesVisibility = parse.BehaviorVersion >= 11;
        }

        /// <summary>One particle's attributes, which the shader expands into the four corners of its ribbon.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct TrailInstance
        {
            /// <summary>The centre of the ribbon in xyz, the tail's alpha before any fade in w.</summary>
            [VertexAttribute(VertexSlot.Position)] public Vector4 CenterTailAlpha;

            /// <summary>The colour before either end's colour scale in rgb, the head's alpha before any fade in a.</summary>
            [VertexAttribute(VertexSlot.Color)] public Vector4 ColorHead;

            /// <summary>The flip and crop scale of the card coordinate.</summary>
            [VertexAttribute("vTrailUvCrop")] public Vector2 UvCrop;

            /// <summary>Across the ribbon in xyz, the head's half width before the size clamp in w.</summary>
            [VertexAttribute("vAxisU")] public Vector4 AxisU;

            /// <summary>Along the ribbon and already halved in xyz, the tail's half width before the size clamp in w.</summary>
            [VertexAttribute("vAxisV")] public Vector4 AxisV;

            /// <inheritdoc cref="SpritecardVertex.SheetInputs"/>
            [VertexAttribute(VertexSlot.TexCoord)] public Vector4 SheetInputs;

            /// <summary>The sequence the first layer plays, then the one later layers play.</summary>
            [VertexAttribute(VertexSlot.TexCoord1)] public Vector2 Sequences;

            /// <inheritdoc cref="SpritecardVertex.FacingAxis"/>
            [VertexAttribute(VertexSlot.Normal)] public Vector3 FacingAxis;
        }

        /// <inheritdoc/>
        /// <remarks>The crop fields are read between two steps like the attributes every renderer blends.</remarks>
        public override ulong BlendedFields => base.BlendedFields | ScalarMask(horizontalCropField) | ScalarMask(verticalCropField);

        private static ulong ScalarMask(ParticleField field) => ParticleBuffer.IsScalar(field) ? field.Mask() : 0;

        /// <summary>How much of the card coordinate a crop field leaves, which travels to the shader as a byte.</summary>
        private static float CropScale(ReadOnlySpan<float> crops, int index)
        {
            if (crops.IsEmpty)
            {
                return 1f;
            }

            var crop = (int)(MathUtils.Saturate(crops[index] * 0.5f) * 255f) / 255f;

            return 1f - crop;
        }

        /// <inheritdoc/>
        protected override int UpdateVertices(ParticleCollection particleBag, ParticleSystemState systemState, Camera camera)
        {
            var scales = appliesVisibility ? EvaluateScales(systemState, camera) : EvaluateScales(systemState);

            if (scales.Radius == 0f)
            {
                return 0;
            }

            var radiusScale = scales.Radius < 0f ? 1f : scales.Radius;

            // Converts the moved distance to a velocity (distance / dt) before scaling by the trail-length
            // attribute. The division only applies when the previous point is the Verlet one, and the
            // operator can opt out of it.
            var oneOverDt = ignoreDeltaTime || !usesVerletDelta || systemState.CurrentStep == 0f
                ? 1f
                : 1f / systemState.CurrentStep;

            var quadCount = 0;
            var sorted = particleBag.Count > 1;

            using (var vertexBuffer = new RentedBuffer<TrailInstance>(particleBag.Count))
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
                tailColor = tailColorScale.NextVector(systemState);
                var tailAlphaScales = tailAlphaScale.Fill(current, systemState);
                headColor = headColorScale.NextVector(systemState);
                var headAlphaScales = headAlphaScale.Fill(current, systemState);

                var positions = current.ReadVectors(ParticleField.Position);
                var previousPositions = current.ReadVectors(prevPositionSource);
                var normals = current.ReadVectors(ParticleField.Normal);
                var colors = current.ReadVectors(ParticleField.Color);
                var trailLengths = current.ReadFloats(ParticleField.TrailLength);
                var radii = current.ReadFloats(ParticleField.Radius);
                var alphas = current.ReadFloats(ParticleField.Alpha);
                var alpha2s = ReadAlpha2(current);
                var ages = current.ReadAges();
                var extraData1 = current.ReadFloats(ParticleField.ShaderExtraData1);
                var extraData2 = current.ReadFloats(ParticleField.ShaderExtraData2);
                var sequenceNumbers = current.ReadFloats(ParticleField.SequenceNumber);
                var sheetAges = ReadSheetAges(current);
                var randoms = ReadParticleRandoms(current, systemState);
                var yaws = flipUvByPitchYaw ? current.ReadFloats(ParticleField.Yaw) : default;
                var pitches = flipUvByPitchYaw ? current.ReadFloats(ParticleField.Pitch) : default;
                var horizontalCrops = ParticleBuffer.IsScalar(horizontalCropField) ? current.ReadFloats(horizontalCropField) : default;
                var verticalCrops = ParticleBuffer.IsScalar(verticalCropField) ? current.ReadFloats(verticalCropField) : default;

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
                    length *= MathF.Min(1f, ages[index] / MathF.Max(lengthFadeInTime, 1e-9f));
                    length = MathF.Max(minLength, MathF.Min(maxLength, length));

                    // A short trail is narrowed so it cannot render wider than it is long
                    var radius = MathF.Min(radii[index] * radiusScale, constrainRadiusToLengthRatio * length);

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

                    // Only the normal-aligned mode has a normal to fade by view angle with; the others
                    // substitute the direction the ribbon runs in
                    var facingAxis = orientationType == ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL
                        ? normals[index]
                        : direction;

                    // The trail slides along the motion axis by m_flForwardShift lengths;
                    // direction runs backwards along travel here, so the shift subtracts
                    var center = position + (direction * (length * (0.5f - forwardShift)));

                    // Flipping and cropping scale the card coordinate about its centre; the geometry stays
                    var uvScale = new Vector2(CropScale(horizontalCrops, index), CropScale(verticalCrops, index));

                    if (flipUvByPitchYaw)
                    {
                        if (yaws[index] >= MathF.PI)
                        {
                            uvScale.X = -uvScale.X;
                        }

                        if (pitches[index] >= MathF.PI)
                        {
                            uvScale.Y = -uvScale.Y;
                        }
                    }

                    // Each tint channel is packed from the low byte of round(tint * 255) without a clamp, so a tint
                    // past 1 or below 0 wraps; it is saturated here
                    var color = BlendColor(colors[index], alphas[index], alpha2s[index], scales);

                    // Only the first layer plays the particle's sequence; the later ones play sequence 0
                    var sequence = SequenceLane(sequenceNumbers[index], rawIndices: false);

                    // The shader scales each end's colour and saturates it, then fades both ends by
                    // their own half width and distance. Folding the half length into the along axis
                    // leaves the shader a corner offset of exactly +-1 along it. The half widths stay
                    // separate: the ends taper independently, which is what makes the quad a trapezoid
                    // rather than a parallelogram. The per particle modes read the tail's alpha and half width.
                    instances[quadCount++] = new TrailInstance
                    {
                        CenterTailAlpha = new Vector4(center, MathUtils.Saturate(color.W * tailAlphaScales[index])),
                        ColorHead = new Vector4(color.AsVector3(), MathUtils.Saturate(color.W * headAlphaScales[index])),
                        UvCrop = uvScale,
                        AxisU = new Vector4(widthAxis, radius * headRadiusTapers[index]),
                        AxisV = new Vector4(direction * (length * 0.5f), radius * tailRadiusTapers[index]),
                        SheetInputs = new Vector4(randoms[index], extraData1[index], sheetAges[index], extraData2[index]),
                        Sequences = new Vector2(sequence, 0f),
                        FacingAxis = facingAxis,
                    };
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
            // Both fade bounds are a radius per unit of camera distance, as the two size bounds are.
            // Each end is faded and clamped by its own constrained and tapered half width.
            SetSizeClampAndFadeUniforms(enableFadingAndClamping,
                minSize,
                startFadeSize.NextNumber(systemState),
                endFadeSize.NextNumber(systemState),
                maxSize);

            // The view angle fade runs in every mode and outside the fade-and-clamp gate. Each end fades
            // by the angle at its own end of the ribbon.
            SetViewAngleFadeUniforms(FadesByViewAngle);

            // A trail does not zoom between sheet frames
            Shader.SetUniform2("uFrameZoom", Vector2.One);

            Shader.SetUniform3("uTrailHeadColorScale", headColor);
            Shader.SetUniform3("uTrailTailColorScale", tailColor);

            base.SetDrawUniforms(systemState);
        }
    }
}
