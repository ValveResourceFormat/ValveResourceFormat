using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Lights particles from up to four point lights placed on control points, writing the lit result
    /// into the particle color. It colors the particles only; it puts no light into the scene.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The particle's current color scaled by <c>m_flScale</c> is the base and every light adds its
    /// color times its falloff. The sum is clamped to [0, 1], or with <c>m_bClampLowerRange</c> and
    /// <c>m_bClampUpperRange</c> to no less and no more than the current color. A light sits at its
    /// control point plus <c>m_vecCPOffsetN</c> in world space, unrotated. Its falloff is
    /// <c>1 / (c + l*d + q*d^2)</c>, fitted through 1 at the light, 1/2 at the 50% distance and 1/256
    /// at the 0% distance, with <c>d</c> at least 1. A dynamic light takes its color from the scene
    /// lighting at its position. With <c>m_bUseNormal</c> each light is weighted by <c>max(0, N.L)</c>,
    /// where N points from that light's control point to the particle.
    /// </para>
    /// <para>
    /// The following is intentional and must not be corrected. With <c>m_bUseNormal</c> and
    /// <c>m_bUseHLambert</c> (the default) the lights read a color slot that stays black, so they add
    /// nothing. Spot lights (<c>m_bLightTypeN</c>) have no direction and a zero cone, so they add nothing
    /// either. A 50% distance of 0 divides by zero in the fit, and that light's NaN falloff poisons the
    /// sum even when the light itself is black. The lower clamp keeps its second operand where the sum is
    /// NaN, so the particle turns black, or keeps its current color under <c>m_bClampLowerRange</c>.
    /// </para>
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
        private readonly record struct Light(int ControlPoint, Vector3 Offset, Vector3 Color, bool Dynamic, Falloff Falloff);

        public ControlpointLight(ParticleDefinitionParser parse) : base(parse)
        {
            initialColorBias = parse.Float("m_flScale", initialColorBias);
            useNormal = parse.Boolean("m_bUseNormal", useNormal);
            halfLambert = parse.Boolean("m_bUseHLambert", halfLambert);
            clampLowerRange = parse.Boolean("m_bClampLowerRange", clampLowerRange);
            clampUpperRange = parse.Boolean("m_bClampUpperRange", clampUpperRange);

            // Intentional: half-Lambert lights add no light
            var blackColorSlot = useNormal && halfLambert;
            var authored = new List<Light>(LightCount);

            for (var i = 1; i <= LightCount; i++)
            {
                // Intentional: spot lights add no light
                if (parse.Boolean($"m_bLightType{i}", false))
                {
                    continue;
                }

                var light = new Light(
                    parse.Int32($"m_nControlPoint{i}", 0),
                    parse.Vector3($"m_vecCPOffset{i}", Vector3.Zero),
                    blackColorSlot ? Vector3.Zero : parse.Color24($"m_LightColor{i}", Vector3.Zero),
                    !blackColorSlot && parse.Boolean($"m_bLightDynamic{i}", false),
                    Falloff.Solve(parse.Float($"m_LightFiftyDist{i}", 100f), parse.Float($"m_LightZeroDist{i}", 200f)));

                // Intentional: a black light with a NaN falloff still blacks out the particle
                if (light.Color == Vector3.Zero && !light.Dynamic && light.Falloff.IsFinite)
                {
                    continue;
                }

                authored.Add(light);
            }

            lights = [.. authored];
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            Span<Vector3> sources = stackalloc Vector3[lights.Length];
            Span<Vector3> positions = stackalloc Vector3[lights.Length];
            Span<Vector3> colors = stackalloc Vector3[lights.Length];
            Span<Falloff.Cutoff> cutoffs = stackalloc Falloff.Cutoff[lights.Length];

            for (var i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                sources[i] = particleSystemState.GetControlPoint(light.ControlPoint).Position;
                positions[i] = sources[i] + light.Offset;
                colors[i] = light.Dynamic
                    ? Vector3.Clamp(particleSystemState.Lighting.SampleAmbientLight(positions[i]), Vector3.Zero, Vector3.One)
                    : light.Color;
                cutoffs[i] = light.Falloff.CutoffFor(colors[i]);
            }

            foreach (ref var particle in particles.Current)
            {
                var color = particle.Color;
                var lit = color * initialColorBias;

                for (var i = 0; i < lights.Length; i++)
                {
                    var toLight = positions[i] - particle.Position;
                    var intensity = lights[i].Falloff.Evaluate(toLight.LengthSquared(), cutoffs[i]);

                    if (useNormal)
                    {
                        var fromSource = particle.Position - sources[i];
                        var normal = fromSource / MathF.Sqrt(MathF.Max(fromSource.LengthSquared(), ParticleMath.FloatEpsilon));
                        intensity *= MathF.Max(0f, Vector3.Dot(normal, MathUtils.SafeNormalize(toLight)));
                    }

                    lit += colors[i] * intensity;
                }

                // Intentional: a NaN sum takes the lower bound, blacking out the particle
                lit = MaxOrSecond(lit, clampLowerRange ? color : Vector3.Zero);
                particle.Color = Vector3.Min(lit, clampUpperRange ? color : Vector3.One);
            }
        }

        /// <summary>Per component <c>a &gt; b ? a : b</c>, which gives <paramref name="b"/> where <paramref name="a"/> is NaN.</summary>
        private static Vector3 MaxOrSecond(Vector3 a, Vector3 b)
            => new(a.X > b.X ? a.X : b.X, a.Y > b.Y ? a.Y : b.Y, a.Z > b.Z ? a.Z : b.Z);

        /// <summary>A light's distance falloff, <c>1 / (constant + linear*d + quadratic*d^2)</c>.</summary>
        private readonly record struct Falloff(float Constant, float Linear, float Quadratic)
        {
            private static readonly Vector3 LuminanceWeights = new(0.21259999f, 0.71520001f, 0.0722f);

            /// <summary>
            /// A distance past which the light is cut off and the intensity subtracted so it reaches
            /// zero there, both zero when the light has none.
            /// </summary>
            public readonly record struct Cutoff(float Range, float Offset);

            public bool IsFinite => float.IsFinite(Constant) && float.IsFinite(Linear) && float.IsFinite(Quadratic);

            /// <summary>
            /// Fits the falloff to 1 at the light, 1/2 at <paramref name="fiftyDistance"/> and 1/256 at
            /// <paramref name="zeroDistance"/>, then scales it to exactly 1/2 at the 50% distance. A 0%
            /// distance below the 50% one is replaced by twice the 50% distance.
            /// </summary>
            public static Falloff Solve(float fiftyDistance, float zeroDistance)
            {
                if (fiftyDistance > zeroDistance)
                {
                    zeroDistance = fiftyDistance + fiftyDistance;
                }

                var (quadratic, linear, constant) = FitQuadratic(0f, 1f, fiftyDistance, 2f, zeroDistance, 256f) ?? (0f, 1f, 0f);
                var scale = 2f / ((((quadratic * fiftyDistance) + linear) * fiftyDistance) + constant);

                return new Falloff(constant * scale, linear * scale, quadratic * scale);
            }

            /// <summary>
            /// The cutoff of a falloff without a constant term: where the light falls to a thousandth
            /// of its luminance.
            /// </summary>
            public Cutoff CutoffFor(Vector3 color)
            {
                if (Constant != 0f)
                {
                    return default;
                }

                var range = 0f;
                var luminance = Vector3.Dot(color, LuminanceWeights);

                if (luminance > 0f)
                {
                    var scale = 0.001f / luminance;

                    if (SolveQuadratic(Quadratic * scale, Linear * scale, (Constant * scale) - 1f) is var (root1, root2))
                    {
                        range = MathF.Max(0f, MathF.Max(root1, root2));
                    }
                }

                return range > 0f
                    ? new Cutoff(range, 1f / ((range * range * Quadratic) + ((range * Linear) + ParticleMath.FloatEpsilon)))
                    : default;
            }

            public float Evaluate(float distanceSquared, Cutoff cutoff)
            {
                distanceSquared = MathF.Max(1f, distanceSquared);

                var constant = Constant != 0f ? Constant : ParticleMath.FloatEpsilon;
                var intensity = 1f / (constant + (Linear * MathF.Sqrt(distanceSquared)) + (Quadratic * distanceSquared));

                if (cutoff.Offset != 0f)
                {
                    intensity = MathF.Max(0f, intensity - cutoff.Offset);
                }

                if (cutoff.Range != 0f && !(distanceSquared < cutoff.Range * cutoff.Range))
                {
                    return 0f;
                }

                return intensity;
            }

            /// <summary>
            /// The quadratic through three points, as (x^2, x, 1) coefficients, or null when two points
            /// share an x. When the middle point would make a monotonic curve turn back at x = 1, it is
            /// moved toward the straight line between the outer points in steps of 5%.
            /// </summary>
            private static (float A, float B, float C)? FitQuadratic(float x0, float y0, float x1, float y1, float x2, float y2)
            {
                if (x0 > x1)
                {
                    (x0, y0, x1, y1) = (x1, y1, x0, y0);
                }

                if (x1 > x2)
                {
                    (x1, y1, x2, y2) = (x2, y2, x1, y1);
                }

                if (x0 > x1)
                {
                    (x0, y0, x1, y1) = (x1, y1, x0, y0);
                }

                var determinant = (x0 - x1) * (x0 - x2) * (x1 - x2);

                if (determinant == 0f)
                {
                    return null;
                }

                var linearY1 = ((x1 - x0) * (y2 - y0) / (x2 - x0)) + y0;
                var blend = 0f;

                while (true)
                {
                    var middle = (linearY1 * blend) + ((1f - blend) * y1);

                    var a = (((middle - y0) * x2) + ((y0 - y2) * x1) + ((y2 - middle) * x0)) / determinant;
                    var b = (((y0 - middle) * (x2 * x2)) + ((middle - y2) * (x0 * x0)) + ((x1 * x1) * (y2 - y0))) / determinant;
                    var c = (((((y0 * x2) - (x0 * y2)) * (x1 * x1)) + ((x0 * x2 * (x2 - x0)) * middle)) + (((x0 * x0 * y2) - (x2 * x2 * y0)) * x1)) / determinant;

                    var slope = a + a + b;
                    var turnsBack = y0 < y1 && y1 < y2
                        ? slope < 0f
                        : y0 > y1 && y1 > y2 && slope > 0f;

                    if (!turnsBack)
                    {
                        return (a, b, c);
                    }

                    blend += 0.05f;

                    if (blend > 1f)
                    {
                        return (a, b, c);
                    }
                }
            }

            /// <summary>The roots of <c>a*x^2 + b*x + c</c>, or null when it has none.</summary>
            private static (float, float)? SolveQuadratic(float a, float b, float c)
            {
                if (a == 0f)
                {
                    if (b != 0f)
                    {
                        return (-c / b, -c / b);
                    }

                    return c == 0f ? (0f, 0f) : null;
                }

                var discriminant = (b * b) - (a * 4f * c);

                if (discriminant < 0f)
                {
                    return null;
                }

                var root = MathF.Sqrt(discriminant);
                return ((root - b) / (a + a), (-b - root) / (a + a));
            }
        }
    }
}
