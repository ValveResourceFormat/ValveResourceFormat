using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Lights particles from up to four point or spot lights placed on control points, writing the
    /// lit result into the particle color. It colors the particles only; it puts no light into the
    /// scene.
    /// </summary>
    /// <remarks>
    /// The particle's initial color, scaled by <c>m_flScale</c> (its "initial color bias"), is kept as
    /// a base and the lights add on top of it, so a dark particle can be lit well past its own color.
    /// Each light falls off as an inverse square that reaches half intensity at its 50% distance, and
    /// is then ramped linearly to nothing between its 50% and 0% distances. With <c>m_bUseNormal</c>
    /// each particle takes the direction from control point 0 as its normal, lit as half-Lambert by
    /// default; a spot light also fades by how far the particle lies off its control point's forward
    /// axis. The operator's run strength blends from the incoming color, so an oscillating operator
    /// fade flashes the lights.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ControlpointLight">C_OP_ControlpointLight</seealso>
    class ControlpointLight : ParticleFunctionOperator
    {
        private const int LightCount = 4;

        private readonly float initialColorBias;
        private readonly bool useNormal;
        private readonly bool halfLambert = true;
        private readonly bool clampLowerRange;
        private readonly bool clampUpperRange;
        private readonly Light[] lights;

        /// <summary>One authored light, with the falloff distances it was given.</summary>
        private readonly record struct Light(int ControlPoint, Vector3 Offset, Vector3 Color, float FiftyDistance, float ZeroDistance, bool Spot);

        public ControlpointLight(ParticleDefinitionParser parse) : base(parse)
        {
            initialColorBias = parse.Float("m_flScale", initialColorBias);
            useNormal = parse.Boolean("m_bUseNormal", useNormal);
            halfLambert = parse.Boolean("m_bUseHLambert", halfLambert);
            clampLowerRange = parse.Boolean("m_bClampLowerRange", clampLowerRange);
            clampUpperRange = parse.Boolean("m_bClampUpperRange", clampUpperRange);

            var authored = new List<Light>(LightCount);

            for (var i = 1; i <= LightCount; i++)
            {
                var color = parse.Color24($"m_LightColor{i}", Vector3.Zero);

                // A black light contributes nothing, which is what every unused slot defaults to
                if (color == Vector3.Zero)
                {
                    continue;
                }

                authored.Add(new Light(
                    parse.Int32($"m_nControlPoint{i}", 0),
                    parse.Vector3($"m_vecCPOffset{i}", Vector3.Zero),
                    color,
                    parse.Float($"m_LightFiftyDist{i}", 100f),
                    parse.Float($"m_LightZeroDist{i}", 200f),
                    parse.Boolean($"m_bLightType{i}", false)));
            }

            lights = [.. authored];
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (lights.Length == 0)
            {
                return;
            }

            Span<Vector3> positions = stackalloc Vector3[lights.Length];
            Span<Vector3> forwards = stackalloc Vector3[lights.Length];

            for (var i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                positions[i] = ControlPointTransformProvider.TransformPosition(particleSystemState, light.ControlPoint, light.Offset);
                forwards[i] = light.Spot
                    ? Vector3.Normalize(ControlPointTransformProvider.TransformDirection(particleSystemState, light.ControlPoint, Vector3.UnitX))
                    : Vector3.Zero;
            }

            var center = particleSystemState.GetControlPoint(0).Position;

            foreach (ref var particle in particles.Current)
            {
                var initialColor = particle.GetInitialVector(particles, ParticleField.Color);
                var normal = useNormal
                    ? MathUtils.SafeNormalize(particle.Position - center, Vector3.UnitZ, ParticleMath.MinimumLengthSquared)
                    : Vector3.Zero;

                var lit = initialColor * initialColorBias;

                for (var i = 0; i < lights.Length; i++)
                {
                    lit += lights[i].Color * Illuminance(lights[i], positions[i], forwards[i], particle.Position, normal);
                }

                if (clampLowerRange)
                {
                    lit = Vector3.Max(lit, initialColor);
                }

                if (clampUpperRange)
                {
                    lit = Vector3.Min(lit, initialColor);
                }

                particle.Color = Vector3.Lerp(particle.Color, lit, strength);
            }
        }

        /// <summary>How strongly one light reaches a particle, before its color.</summary>
        private float Illuminance(in Light light, Vector3 lightPosition, Vector3 spotForward, Vector3 particlePosition, Vector3 normal)
        {
            var toLight = lightPosition - particlePosition;
            var distance = toLight.Length();
            var intensity = Attenuation(distance, light.FiftyDistance, light.ZeroDistance);

            if (intensity <= 0f || distance <= float.Epsilon)
            {
                return intensity;
            }

            var direction = toLight / distance;

            if (useNormal)
            {
                var cosine = Vector3.Dot(normal, direction);

                if (halfLambert)
                {
                    var wrapped = (cosine * 0.5f) + 0.5f;
                    intensity *= wrapped * wrapped;
                }
                else
                {
                    intensity *= MathF.Max(0f, cosine);
                }
            }

            if (light.Spot)
            {
                intensity *= MathF.Max(0f, Vector3.Dot(spotForward, -direction));
            }

            return intensity;
        }

        /// <summary>
        /// Inverse square falloff reaching half intensity at <paramref name="fiftyDistance"/>, ramped
        /// down to nothing between there and <paramref name="zeroDistance"/>. Without a usable 50%
        /// distance the light only ramps down to its 0% distance.
        /// </summary>
        internal static float Attenuation(float distance, float fiftyDistance, float zeroDistance)
        {
            if (zeroDistance > 0f && distance >= zeroDistance)
            {
                return 0f;
            }

            if (fiftyDistance <= 0f)
            {
                return zeroDistance > 0f ? 1f - (distance / zeroDistance) : 1f;
            }

            var ratio = distance / fiftyDistance;
            var intensity = 1f / (1f + (ratio * ratio));

            if (zeroDistance > fiftyDistance && distance > fiftyDistance)
            {
                intensity *= 1f - ((distance - fiftyDistance) / (zeroDistance - fiftyDistance));
            }

            return intensity;
        }
    }
}
