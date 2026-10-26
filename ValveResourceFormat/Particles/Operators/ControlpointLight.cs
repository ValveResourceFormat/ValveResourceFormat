using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Lights particles from up to four point lights placed on control points, writing the lit result
    /// into the particle color. It colors the particles only; it puts no light into the scene.
    /// </summary>
    /// <remarks>
    /// The particle's current color scaled by <c>m_flScale</c> is the base and every light adds its
    /// color times its falloff. The sum is clamped to [0, 1], or with <c>m_bClampLowerRange</c> and
    /// <c>m_bClampUpperRange</c> to no less and no more than the current color. A light sits at its
    /// control point plus <c>m_vecCPOffsetN</c> in world space, unrotated. Its falloff is
    /// <c>1 / (c + l*d + q*d^2)</c>, fitted through 1 at the light, 1/2 at the 50% distance and 1/256
    /// at the 0% distance, with <c>d</c> at least 1. A dynamic light takes its color from the scene
    /// lighting at its position. With <c>m_bUseNormal</c> each light is weighted by <c>max(0, N.L)</c>,
    /// where N points from the light's control point to the particle (without the offset) and L from
    /// the particle to the offset light, so only an offset light adds anything.
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

        /// <summary>One authored light that can change a particle's color.</summary>
        private readonly record struct Light(int ControlPoint, Vector3 Offset, Vector3 Color, bool Dynamic, LightFalloff Falloff);

        public ControlpointLight(ParticleDefinitionParser parse) : base(parse)
        {
            initialColorBias = parse.Float("m_flScale", initialColorBias);
            useNormal = parse.Boolean("m_bUseNormal", useNormal);
            halfLambert = parse.Boolean("m_bUseHLambert", halfLambert);
            clampLowerRange = parse.Boolean("m_bClampLowerRange", clampLowerRange);
            clampUpperRange = parse.Boolean("m_bClampUpperRange", clampUpperRange);

            // With m_bUseNormal and m_bUseHLambert every light is black.
            var blackColorSlot = useNormal && halfLambert;
            var authored = new List<Light>(LightCount);

            // The fourth light copies the third light's settings and keeps only its own position and dynamic color,
            // so it takes the third light's color, type and falloff there; every light keeps its own values here
            for (var i = 1; i <= LightCount; i++)
            {
                // Spot lights have no direction and a zero cone, so they add nothing.
                if (parse.Boolean($"m_bLightType{i}", false))
                {
                    continue;
                }

                var light = new Light(
                    parse.Int32($"m_nControlPoint{i}", 0),
                    parse.Vector3($"m_vecCPOffset{i}", Vector3.Zero),
                    blackColorSlot ? Vector3.Zero : parse.Color24($"m_LightColor{i}", Vector3.Zero),
                    !blackColorSlot && parse.Boolean($"m_bLightDynamic{i}", false),
                    LightFalloff.Solve(parse.Float($"m_LightFiftyDist{i}", 100f), parse.Float($"m_LightZeroDist{i}", 200f)));

                // A 50% distance of 0 gives a NaN falloff, which blacks out the particle even from a black light.
                if (light.Color == Vector3.Zero && !light.Dynamic && light.Falloff.IsFinite)
                {
                    continue;
                }

                authored.Add(light);
            }

            lights = [.. authored];
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Color.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            Span<Vector3> controlPoints = stackalloc Vector3[lights.Length];
            Span<Vector3> positions = stackalloc Vector3[lights.Length];
            Span<Vector3> colors = stackalloc Vector3[lights.Length];
            Span<LightFalloff.Cutoff> cutoffs = stackalloc LightFalloff.Cutoff[lights.Length];

            for (var i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                controlPoints[i] = particleSystemState.GetControlPoint(light.ControlPoint).Position;
                positions[i] = controlPoints[i] + light.Offset;
                colors[i] = light.Dynamic
                    ? Vector3.Clamp(particleSystemState.Lighting.SampleAmbientLight(positions[i]), Vector3.Zero, Vector3.One)
                    : light.Color;
                cutoffs[i] = light.Falloff.CutoffFor(colors[i]);
            }

            var current = particles.Current;
            var particleColors = current.Vectors(ParticleField.Color);
            var particlePositions = current.ReadVectors(ParticleField.Position);

            for (var p = 0; p < particleColors.Length; p++)
            {
                var color = particleColors[p];
                var particlePosition = particlePositions[p];
                var lit = color * initialColorBias;

                for (var i = 0; i < lights.Length; i++)
                {
                    var toLight = positions[i] - particlePosition;
                    var intensity = lights[i].Falloff.Evaluate(toLight.LengthSquared(), cutoffs[i]);

                    if (useNormal)
                    {
                        var fromControlPoint = particlePosition - controlPoints[i];
                        var normal = fromControlPoint / MathF.Sqrt(MathF.Max(fromControlPoint.LengthSquared(), ParticleMath.FloatEpsilon));
                        intensity *= MathF.Max(0f, Vector3.Dot(normal, MathUtils.SafeNormalize(toLight)));
                    }

                    lit += colors[i] * intensity;
                }

                // A NaN sum takes the lower bound, so the particle turns black or keeps its color under m_bClampLowerRange.
                lit = MaxOrSecond(lit, clampLowerRange ? color : Vector3.Zero);
                particleColors[p] = Vector3.Min(lit, clampUpperRange ? color : Vector3.One);
            }
        }

        /// <summary>Per component <c>a &gt; b ? a : b</c>, which gives <paramref name="b"/> where <paramref name="a"/> is NaN.</summary>
        private static Vector3 MaxOrSecond(Vector3 a, Vector3 b)
            => new(a.X > b.X ? a.X : b.X, a.Y > b.Y ? a.Y : b.Y, a.Z > b.Z ? a.Z : b.Z);
    }
}
