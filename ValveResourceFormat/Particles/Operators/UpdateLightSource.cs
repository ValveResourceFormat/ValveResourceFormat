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
        private static readonly Vector3 Luminance = new(0.2126f, 0.7152f, 0.0722f);

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

        public override void Restart(ParticleSystemState particleSystemState)
        {
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (particles.Count == 0)
            {
                return;
            }

            var weightedPosition = Vector3.Zero;
            var weightSum = 0f;
            var tintedVolume = Vector3.Zero;

            foreach (ref var particle in particles.Current)
            {
                var volume = particle.Radius * particle.Radius * particle.Radius;
                var tint = particle.Color * particle.Alpha;
                var weight = tint.Length() * volume;

                weightedPosition += particle.Position * weight;
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
            var spread = 2f / MathF.Max(0.1f, SphereVolume * range * range * range);
            var color = colorTint * tintedVolume * SphereVolume * (brightnessScale * 10000f) * spread;

            if (range <= 1f || color.Length() <= 1f / 256f)
            {
                PutOut(observer);
                return;
            }

            var position = weightedPosition / (ParticleMath.FloatEpsilon + weightSum);
            range = MathF.Min(range, MathF.Sqrt(Vector3.Dot(color, Luminance) / MinimumIntensity));

            if (light.IsLit)
            {
                var damping = Math.Clamp(MathF.Pow(positionDampingConstant, frameTime), 0f, 0.99f);

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
