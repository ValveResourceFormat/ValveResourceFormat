using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Lights the scene with one point light standing for the whole collection. It sits at the
    /// particles' centre weighted by their brightness and volume, takes its colour from their summed
    /// tinted volume spread over a sphere of its range, and reaches a multiple of the size of the
    /// system's bounds. It eases towards each new placement, and goes out while the bounds are empty
    /// or it would be too small or too dim.
    /// </summary>
    /// <remarks>
    /// The light is published through <see cref="IParticleSystemObserver.OnLightSourceUpdated"/>.
    /// A restart keeps it; a replay puts it out.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_UpdateLightSource">C_OP_UpdateLightSource</seealso>
    class UpdateLightSource : ParticleFunctionOperator
    {
        private const float SphereVolume = 4f / 3f * MathF.PI;
        private const float MinimumIntensity = 0.001f;

        /// <summary>Floor of the lit sphere volume the brightness is spread over.</summary>
        private const float MinimumSpreadVolume = 0.1f;

        /// <summary>Scale from the particles' tinted volume to light brightness.</summary>
        private const float BrightnessUnit = 10000f;

        /// <summary>Colour length below which the light is too dark to show, one 8-bit step.</summary>
        private const float MinimumColorLength = 1f / 256f;

        /// <summary>Largest share of the previous light kept per step, so the light always follows.</summary>
        private const float MaximumDamping = 0.99f;

        private readonly Vector3 colorTint = Vector3.One;
        private readonly float brightnessScale = 1f;
        private readonly float radiusScale = 4f;
        private readonly float minimumLightingRadius;
        private readonly float maximumLightingRadius = 100000f;
        private readonly float positionDampingConstant = 0.1f;

        private readonly ParticleLightSource light = new();

        public UpdateLightSource(ParticleDefinitionParser parse) : base(parse)
        {
            colorTint = ColorSpace.SrgbGammaToLinear(parse.Color24("m_vColorTint", colorTint));
            brightnessScale = parse.Float("m_flBrightnessScale", brightnessScale);
            radiusScale = parse.Float("m_flRadiusScale", radiusScale);
            minimumLightingRadius = parse.Float("m_flMinimumLightingRadius", minimumLightingRadius);
            maximumLightingRadius = parse.Float("m_flMaximumLightingRadius", maximumLightingRadius);
            positionDampingConstant = parse.Float("m_flPositionDampingConstant", positionDampingConstant);
        }

        public override void Initialize(ParticleSystemState particleSystemState)
        {
            if (light.IsLit)
            {
                light.IsLit = false;
                particleSystemState.Data?.Observer?.OnLightSourceUpdated(light);
            }
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var weightedPosition = Vector3.Zero;
            var weightSum = 0f;
            var tintedVolume = Vector3.Zero;

            var current = particles.Current;
            var radii = current.ReadFloats(ParticleField.Radius);
            var colors = current.ReadVectors(ParticleField.Color);
            var alphas = current.ReadFloats(ParticleField.Alpha);
            var positions = current.ReadVectors(ParticleField.Position);

            for (var i = 0; i < radii.Length; i++)
            {
                var radius = radii[i];
                var volume = radius * radius * radius;
                var tint = colors[i] * alphas[i];
                var weight = tint.Length() * volume;

                weightedPosition += positions[i] * weight;
                weightSum += weight;
                tintedVolume += tint * volume;
            }

            var observer = particleSystemState.Data?.Observer;

            if (particleSystemState.Data?.WorldBoundingBox is not { } bounds || bounds.Size.MinComponent() <= 0f)
            {
                PutOut(observer);
                return;
            }

            var extent = Vector3.Distance(bounds.Min, bounds.Center);
            var range = MathF.Max(minimumLightingRadius, MathF.Min(maximumLightingRadius, extent * radiusScale));
            var spread = 2f / MathF.Max(MinimumSpreadVolume, SphereVolume * range * range * range);
            var color = colorTint * tintedVolume * SphereVolume * (brightnessScale * BrightnessUnit) * spread;

            if (range <= 1f || color.Length() <= MinimumColorLength)
            {
                PutOut(observer);
                return;
            }

            var position = weightedPosition / (ParticleMath.FloatEpsilon + weightSum);
            range = MathF.Min(range, MathF.Sqrt(Vector3.Dot(color, ParticleMath.LuminanceWeights) / MinimumIntensity));

            if (light.IsLit)
            {
                var damping = Math.Clamp(MathF.Pow(positionDampingConstant, frameTime), 0f, MaximumDamping);

                position = MathUtils.Lerp(position, light.Position, damping);
                color = MathUtils.Lerp(color, light.Color, damping);
                range = MathUtils.Lerp(range, light.Range, damping);
            }

            light.IsLit = true;
            light.Position = position;
            light.Color = color;
            light.Range = range;

            observer?.OnLightSourceUpdated(light);
        }

        private void PutOut(IParticleSystemObserver? observer)
        {
            if (!light.IsLit)
            {
                return;
            }

            light.IsLit = false;
            observer?.OnLightSourceUpdated(light);
        }
    }
}
