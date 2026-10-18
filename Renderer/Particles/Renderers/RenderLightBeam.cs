using ValveResourceFormat.Animation;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Render tube lights along spans of consecutive particles. The particles are split into
    /// <c>m_flNumberOfLightsToCreate</c> overlapping spans; each span gets one Omni2 tube light along the
    /// longest axis of the box around it, as long as that box, emitting <c>m_flBrightnessLumensPerMeter</c>
    /// for each meter of its length in the average colour of the span. The light of the n-th span follows
    /// the n-th particle by id.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderLightBeam">C_OP_RenderLightBeam</seealso>
    internal class RenderLightBeam : ParticleLightRenderer
    {
        private const float MetersPerUnit = 0.0254f;

        private readonly IVectorProvider colorBlend = new LiteralVectorProvider(Vector3.One);
        private readonly INumberProvider brightnessLumensPerMeter = new LiteralNumberProvider(1000f);
        private readonly INumberProvider numberOfLightsToCreate = new LiteralNumberProvider(1f);
        private readonly bool castShadows;
        private readonly INumberProvider skirt = new LiteralNumberProvider(0.1f);
        private readonly INumberProvider range = new LiteralNumberProvider(512f);
        private readonly INumberProvider thickness = new LiteralNumberProvider(1f);
        private readonly INumberProvider innerConeAngle = new LiteralNumberProvider(180f);
        private readonly INumberProvider outerConeAngle = new LiteralNumberProvider(180f);
        private readonly IVectorProvider coneRotationOffset = new LiteralVectorProvider(Vector3.Zero);
        private readonly INumberProvider renderFilter = new LiteralNumberProvider(1f);

        private Vector3[] spanPositions = [];

        public RenderLightBeam(ParticleDefinitionParser parse, Scene scene)
            : base(parse, scene)
        {
            colorBlend = parse.CollectionVectorProvider("m_vColorBlend", colorBlend);
            brightnessLumensPerMeter = parse.CollectionNumberProvider("m_flBrightnessLumensPerMeter", brightnessLumensPerMeter);
            numberOfLightsToCreate = parse.CollectionNumberProvider("m_flNumberOfLightsToCreate", numberOfLightsToCreate);
            castShadows = parse.Boolean("m_bCastShadows", castShadows);
            skirt = parse.CollectionNumberProvider("m_flSkirt", skirt);
            range = parse.CollectionNumberProvider("m_flRange", range);
            thickness = parse.CollectionNumberProvider("m_flThickness", thickness);
            innerConeAngle = parse.CollectionNumberProvider("m_flInnerConeAngle", innerConeAngle);
            outerConeAngle = parse.CollectionNumberProvider("m_flOuterConeAngle", outerConeAngle);
            coneRotationOffset = parse.CollectionVectorProvider("m_vecConeRotationOffset", coneRotationOffset);
            renderFilter = parse.NumberProvider("m_flRenderFilter", renderFilter);
        }

        /// <inheritdoc/>
        protected override bool UpdateLights(ParticleCollection particles, ParticleSystemState systemState)
        {
            if (particles.Count > 1)
            {
                UpdateBeams(particles, systemState);
            }

            return true;
        }

        /// <summary>Places a light on each span.</summary>
        private void UpdateBeams(ParticleCollection particles, ParticleSystemState systemState)
        {
            var current = particles.Current;
            var count = current.Length;
            var beamCount = Math.Min((int)numberOfLightsToCreate.NextNumber(systemState), count);

            if (beamCount <= 0)
            {
                return;
            }

            var span = MathF.Max(1f, count / (float)beamCount);
            var blend = new Vector4(ParticleColorBlend.ConvertScale(ColorBlendType, colorBlend.NextVector(systemState)), 1f);
            var lumensPerMeter = brightnessLumensPerMeter.NextNumber(systemState);
            var outerAngle = Math.Clamp(outerConeAngle.NextNumber(systemState), 0f, 180f);
            var innerAngle = Math.Clamp(innerConeAngle.NextNumber(systemState), 0f, outerAngle);
            var coneRotation = EntityTransformHelper.EulerAnglesToRotationMatrix(coneRotationOffset.NextVector(systemState));
            var light = new ParticleLightPool.LightState(
                Color: Vector3.One,
                LinearBrightness: 0f,
                BrightnessScale: 0f,
                Range: range.NextNumber(systemState),
                SpotInnerAngle: innerAngle,
                SpotOuterAngle: outerAngle,
                LuminaireSize: thickness.NextNumber(systemState),
                FallOff: skirt.NextNumber(systemState),
                Transform: Matrix4x4.Identity);

            if (spanPositions.Length < count)
            {
                spanPositions = new Vector3[count];
            }

            for (var beam = 0; beam < beamCount; beam++)
            {
                if (Lights.Claim(current[beam].ParticleId) is not { } slot)
                {
                    continue;
                }

                slot.Touched = true;

                if (lumensPerMeter <= 0f)
                {
                    continue;
                }

                var first = (int)MathF.Floor(beam * span);
                var length = Math.Clamp((int)MathF.Ceiling((beam + 1) * span) - first, 2, count);

                if (!SumSpan(current, systemState, first, length, blend, out var colorSum))
                {
                    continue;
                }

                var (center, axis, beamLength) = GetPrincipalBounds(spanPositions.AsSpan(0, length));

                if (beamLength <= 0f)
                {
                    continue;
                }

                var color = colorSum / length;
                var peak = color.MaxComponent();

                if (peak <= 0f)
                {
                    continue;
                }

                var lumens = MetersPerUnit * beamLength * lumensPerMeter;
                var solidAngle = SceneLight.ComputeConeSolidAngle(outerAngle, innerAngle);

                slot.State = light with
                {
                    Color = ColorSpace.SrgbLinearToGamma(color / peak),
                    LinearBrightness = lumens * 4f * MathF.PI * 10f / solidAngle / (SceneLight.LinearBrightnessDistance * SceneLight.LinearBrightnessDistance),
                    BrightnessScale = peak,
                    Transform = coneRotation * Matrix4x4.CreateFromQuaternion(TransformMath.FromRotationBetweenUnitVectors(Vector3.UnitY, axis)) * Matrix4x4.CreateTranslation(center),
                    TubeHalfLength = beamLength * 0.5f,
                };
            }
        }

        /// <summary>
        /// Gathers the positions of a span into <see cref="spanPositions"/> and sums its linear colour
        /// weighted by alpha. Indices past the last particle read the last particle. Returns false when a
        /// particle of the span filters the beam out.
        /// </summary>
        private bool SumSpan(Span<Particle> current, ParticleSystemState systemState, int first, int length, Vector4 blend, out Vector3 colorSum)
        {
            colorSum = Vector3.Zero;

            for (var i = 0; i < length; i++)
            {
                ref var particle = ref current[Math.Min(first + i, current.Length - 1)];

                if (renderFilter.NextNumber(ref particle, systemState) < 1f)
                {
                    return false;
                }

                var color = ParticleColorBlend.Apply(ColorBlendType, ParticleColorBlend.ToBytes(particle.Color, particle.Alpha), blend);

                colorSum += ColorSpace.SrgbGammaToLinear(color.AsVector3() / 255f) * particle.Alpha;
                spanPositions[i] = particle.Position;
            }

            return true;
        }

        /// <summary>
        /// The centre of the box around the points, aligned with their principal axis, that axis, and the
        /// length of the box along it.
        /// </summary>
        private static (Vector3 Center, Vector3 Axis, float Length) GetPrincipalBounds(ReadOnlySpan<Vector3> points)
        {
            var mean = Vector3.Zero;

            foreach (var point in points)
            {
                mean += point;
            }

            mean /= points.Length;

            float xx = 0f, xy = 0f, xz = 0f, yy = 0f, yz = 0f, zz = 0f;

            foreach (var point in points)
            {
                var d = point - mean;
                xx += d.X * d.X;
                xy += d.X * d.Y;
                xz += d.X * d.Z;
                yy += d.Y * d.Y;
                yz += d.Y * d.Z;
                zz += d.Z * d.Z;
            }

            var axis = MathUtils.SafeNormalize(points[^1] - points[0], Vector3.UnitX);

            for (var iteration = 0; iteration < 32; iteration++)
            {
                var next = new Vector3(
                    (xx * axis.X) + (xy * axis.Y) + (xz * axis.Z),
                    (xy * axis.X) + (yy * axis.Y) + (yz * axis.Z),
                    (xz * axis.X) + (yz * axis.Y) + (zz * axis.Z));

                if (next.LengthSquared() <= 0f)
                {
                    return (mean, axis, 0f);
                }

                axis = Vector3.Normalize(next);
            }

            var frame = EntityTransformHelper.ForwardDirectionToRotationMatrix(axis);
            var toFrame = Matrix4x4.Transpose(frame);
            var bounds = new AABB(new Vector3(float.MaxValue), new Vector3(float.MinValue));

            foreach (var point in points)
            {
                bounds = bounds.Encapsulate(Vector3.TransformNormal(point, toFrame));
            }

            return (Vector3.TransformNormal(bounds.Center, frame), axis, bounds.Size.X);
        }

        /// <inheritdoc/>
        protected override SceneLight CreateLight(Scene scene)
        {
            return new SceneLight(scene)
            {
                Type = SceneLight.LightType.Point,
                Entity = SceneLight.EntityType.Omni2,
                DirectLight = SceneLight.DirectLightType.Dynamic,
                CastShadows = castShadows ? 1 : 0,
                BrightnessScale = 0f,
                LuminaireShape = 1,
                MinRoughness = SceneLight.MinSpecularRoughness,
                Name = nameof(RenderLightBeam),
            };
        }
    }
}
