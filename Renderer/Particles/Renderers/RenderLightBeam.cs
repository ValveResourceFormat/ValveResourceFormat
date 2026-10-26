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
        private readonly CurveLightStyle? lightStyle;
        private readonly INumberProvider lightStyleTime = new ParticleAgeNumberProvider();

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
            lightStyle = FindLightStyle(scene, parse.String("m_strLightStyle"));
            lightStyleTime = parse.NumberProvider("m_flLightStyleTime", lightStyleTime);
        }

        /// <inheritdoc/>
        protected override bool UpdateLights(ParticleCollection particles, ParticleSystemState systemState)
        {
            return particles.Count <= 1 || UpdateBeams(particles, systemState);
        }

        /// <summary>
        /// Places a light on each span. Returns false without placing any when no light is asked for, which
        /// leaves every light as it was.
        /// </summary>
        private bool UpdateBeams(ParticleCollection particles, ParticleSystemState systemState)
        {
            var current = particles.Current;
            var count = current.Length;

            // The inputs are evaluated in this order, before the beam count can end the step
            var lightRange = range.NextNumber(systemState);
            var lightSkirt = skirt.NextNumber(systemState);
            var lightThickness = thickness.NextNumber(systemState);
            var lumensPerMeter = brightnessLumensPerMeter.NextNumber(systemState);
            var lightCount = numberOfLightsToCreate.NextNumber(systemState);
            var innerCone = innerConeAngle.NextNumber(systemState);
            var outerCone = outerConeAngle.NextNumber(systemState);
            var coneRotation = EntityTransformHelper.EulerAnglesToRotationMatrix(coneRotationOffset.NextVector(systemState));
            var blend = new Vector4(ParticleColorBlend.ConvertScale(ColorBlendType, colorBlend.NextVector(systemState)), 1f);

            var beamCount = Math.Min((int)lightCount, count);

            if (beamCount <= 0)
            {
                return false;
            }

            var span = MathF.Max(1f, count / (float)beamCount);
            var keepLights = lightRange <= 0f || lightThickness <= 0f;
            var outerAngle = Math.Clamp(outerCone, 1f, 180f);

            // An inner angle wider than the outer one inverts the fade ramp, which is not reproduced
            var innerAngle = Math.Clamp(innerCone, 0f, outerAngle);
            var light = new ParticleLightPool.LightState(
                Color: Vector3.One,
                LinearBrightness: 0f,
                BrightnessScale: 0f,
                Range: lightRange,
                SpotInnerAngle: innerAngle,
                SpotOuterAngle: outerAngle,
                LuminaireSize: lightThickness,
                FallOff: lightSkirt,
                Transform: Matrix4x4.Identity,
                NoAngleFade: innerCone >= 180f);

            var spanPositions = ParticleScratch.Vectors(count);
            var particleIds = current.ReadIntegers(ParticleField.ParticleId);
            var columns = new BeamColumns(current);

            for (var beam = 0; beam < beamCount; beam++)
            {
                if (Lights.Claim(particleIds[beam]) is not { } slot)
                {
                    continue;
                }

                slot.Touched = true;

                if (keepLights)
                {
                    continue;
                }

                var first = (int)MathF.Floor(beam * span);
                var length = Math.Clamp((int)MathF.Ceiling((beam + 1) * span) - first, 2, count);

                var positions = spanPositions[..length];

                if (!SumSpan(current, columns, systemState, first, blend, positions, out var colorSum))
                {
                    continue;
                }

                var (center, axis, beamLength) = GetPrincipalBounds(positions);
                var lumens = MetersPerUnit * beamLength * lumensPerMeter;

                if (beamLength <= 0f || lumens == 0f)
                {
                    continue;
                }

                var color = colorSum / length;
                var peak = color.MaxComponent();

                slot.State = light with
                {
                    Color = peak > 0f ? ColorSpace.SrgbLinearToGamma(color / peak) : Vector3.One,
                    LinearBrightness = SceneLight.LinearBrightnessFromCandelas(lumens / (4f * MathF.PI)),
                    BrightnessScale = MathF.Max(peak, 0f),
                    Transform = coneRotation * Matrix4x4.CreateFromQuaternion(TransformMath.FromRotationBetweenUnitVectors(Vector3.UnitY, axis)) * Matrix4x4.CreateTranslation(center),
                    TubeHalfLength = beamLength * 0.5f,
                };
            }

            return true;
        }

        /// <summary>The attributes the beams are built from, for every particle of a span.</summary>
        private readonly ref struct BeamColumns(ParticleSpan particles)
        {
            public ReadOnlySpan<Vector3> Positions { get; } = particles.ReadVectors(ParticleField.Position);

            public ReadOnlySpan<Vector3> Colors { get; } = particles.ReadVectors(ParticleField.Color);

            public ReadOnlySpan<float> Alphas { get; } = particles.ReadFloats(ParticleField.Alpha);
        }

        /// <summary>
        /// Gathers the positions of the span starting at <paramref name="first"/> into <paramref name="positions"/>,
        /// which is as long as the span, and sums its linear colour weighted by alpha. Indices past the last
        /// particle read the last particle. Returns false when a particle of the span filters the beam out.
        /// </summary>
        private bool SumSpan(ParticleSpan current, in BeamColumns columns, ParticleSystemState systemState, int first, Vector4 blend,
            Span<Vector3> positions, out Vector3 colorSum)
        {
            colorSum = Vector3.Zero;

            var length = positions.Length;
            var last = current.Length - 1;
            var spanParticles = current.Slice(first, Math.Min(length, current.Length - first));
            var filters = renderFilter.Fill(spanParticles, systemState);
            var styleTimes = lightStyleTime.Fill(spanParticles, systemState);

            for (var i = 0; i < length; i++)
            {
                var index = Math.Min(first + i, last);

                if (filters[index - first] < 1f)
                {
                    return false;
                }

                var color = ParticleColorBlend.ApplyRounded(ColorBlendType, ParticleColorBlend.ToWrappedBytes(columns.Colors[index], 255f), blend);

                var styleBrightness = lightStyle?.GetBrightness(styleTimes[index - first]) ?? 1f;

                colorSum += ColorSpace.SrgbGammaToLinear(color.AsVector3() / 255f) * styleBrightness * columns.Alphas[index];
                positions[i] = columns.Positions[index];
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

            mean *= 1f / points.Length;

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

            float count = points.Length;
            var covariance = new Matrix4x4(
                xx / count, xy / count, xz / count, 0f,
                xy / count, yy / count, yz / count, 0f,
                xz / count, yz / count, zz / count, 0f,
                0f, 0f, 0f, 1f);

            var frame = Matrix4x4.CreateFromQuaternion(MathUtils.DiagonalizeSymmetric(covariance, out var variances));
            var magnitude = Vector3.Abs(variances);
            int principal;

            if (magnitude.X > magnitude.Y)
            {
                principal = magnitude.X > magnitude.Z ? 0 : 2;
            }
            else
            {
                principal = magnitude.Y > magnitude.Z ? 1 : 2;
            }

            var axis = frame.GetRow(principal).AsVector3();
            var toFrame = Matrix4x4.Transpose(frame);
            var bounds = new AABB(new Vector3(float.MaxValue), new Vector3(float.MinValue));

            foreach (var point in points)
            {
                bounds = bounds.Encapsulate(Vector3.TransformNormal(point, toFrame));
            }

            return (Vector3.TransformNormal(bounds.Center, frame), axis, bounds.Size[principal]);
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
                LuminaireShape = 1,
                MinRoughness = SceneLight.MinSpecularRoughness,
                Name = nameof(RenderLightBeam),
            };
        }
    }
}
