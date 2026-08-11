using System.Numerics;

namespace ValveResourceFormat.Renderer.Particles.Utils
{
    /// <summary>
    /// The evaluation pipeline behind noise-typed particle inputs: an unweighted octave sum of the
    /// selected primitive over a scaled coordinate, a turbulence resample, a shaping modifier, and
    /// the output range. Turbulence overrides worley with fixed per-mode jitter and seed values, and
    /// its alternate mode swaps a worley input to a simplex resample and every other input to a
    /// worley resample, exactly as the engine computes it. The octave sum is unweighted against
    /// amplitude-falloff divisors, also engine behavior.
    /// </summary>
    sealed class NoiseEvaluator
    {
        private static readonly float[] OctaveDivisors = [1f, 1.5f, 1.75f, 1.875f];

        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly float noiseScale = 0.1f;
        private readonly Vector3 offsetRate;
        private readonly float noiseOffset;
        private readonly int octaves = 1;
        private readonly ParticleNoiseType noiseType;
        private readonly ParticleNoiseTurbulence turbulence;
        private readonly ParticleNoiseModifier modifier;
        private readonly float turbulenceScale = 1.25f;
        private readonly float turbulenceMix = 0.5f;
        private readonly float worleyJitter;

        public NoiseEvaluator(ParticleDefinitionParser parse)
        {
            outputMin = parse.Float("m_flNoiseOutputMin", outputMin);
            outputMax = parse.Float("m_flNoiseOutputMax", outputMax);
            noiseScale = parse.Float("m_flNoiseScale", noiseScale);
            offsetRate = parse.Vector3("m_vecNoiseOffsetRate", offsetRate);
            noiseOffset = parse.Float("m_flNoiseOffset", noiseOffset);
            octaves = Math.Clamp(parse.Int32("m_nNoiseOctaves", octaves), 1, 4);
            noiseType = parse.Enum("m_nNoiseType", noiseType);
            turbulence = parse.Enum("m_nNoiseTurbulence", turbulence);
            modifier = parse.Enum("m_nNoiseModifier", modifier);
            turbulenceScale = parse.Float("m_flNoiseTurbulenceScale", turbulenceScale);
            turbulenceMix = parse.Float("m_flNoiseTurbulenceMix", turbulenceMix);
            worleyJitter = Noise.WorleyJitter(noiseOffset);
        }

        /// <summary>Evaluates the pipeline at a sample vector, returning a value in the output range.</summary>
        public float Evaluate(Vector3 input, float age)
        {
            var coord = ((input + Vector3.One) * (noiseScale * 0.1f))
                + new Vector3(noiseOffset) + (age * 0.1f * offsetRate);

            var sum = 0f;
            var frequency = 1f;

            for (var i = 0; i < octaves; i++)
            {
                sum += SampleNoise(coord * frequency, worleyJitter, noiseOffset);
                frequency *= 2f;
            }

            var value = ApplyTurbulence(sum / OctaveDivisors[octaves - 1], coord);

            return (Math.Clamp(ApplyModifier(value), 0f, 1f) * (outputMax - outputMin)) + outputMin;
        }

        private float SampleNoise(Vector3 coord, float jitter, float seed) => noiseType switch
        {
            ParticleNoiseType.PF_NOISE_TYPE_SIMPLEX => Noise.Simplex3D(coord),
            ParticleNoiseType.PF_NOISE_TYPE_WORLEY => Noise.Worley3D(coord, jitter, seed),
            ParticleNoiseType.PF_NOISE_TYPE_CURL => Noise.Curl3D(coord).X,
            _ => Noise.Value3D(coord),
        };

        private float ApplyTurbulence(float value, Vector3 coord)
        {
            if (turbulence == ParticleNoiseTurbulence.PF_NOISE_TURB_NONE)
            {
                return value;
            }

            var scaled = coord * turbulenceScale;
            float target;

            switch (turbulence)
            {
                case ParticleNoiseTurbulence.PF_NOISE_TURB_HIGHLIGHT:
                    target = 1f - (2f * MathF.Abs(SampleNoise(scaled + new Vector3(2.804f, 1.98f, 2.43f), 0.9f, 132f)));
                    break;
                case ParticleNoiseTurbulence.PF_NOISE_TURB_FEEDBACK:
                    target = SampleNoise(scaled + (value * new Vector3(1.804f, 2.98f, 1.43f)), 0.9f, 324f);
                    break;
                case ParticleNoiseTurbulence.PF_NOISE_TURB_LOOPY:
                {
                    var magnitude = MathF.Abs(value);
                    var fraction = magnitude - MathF.Truncate(magnitude);
                    var triangle = (4f - (4f * fraction)) * fraction;
                    var wave = MathF.CopySign(triangle, value);

                    if (((int)magnitude & 1) == 1)
                    {
                        wave = -wave;
                    }

                    target = SampleNoise(scaled + (wave * new Vector3(1.244f, -1.15f, 2.37f)), 0.9f, 132f);
                    break;
                }
                case ParticleNoiseTurbulence.PF_NOISE_TURB_CONTRAST:
                    target = Math.Clamp(2f * SampleNoise(scaled + ((2f - value) * new Vector3(2.93f, 2.734f, 2.13f)), 0.9f, 132f), -1f, 1f);
                    break;
                case ParticleNoiseTurbulence.PF_NOISE_TURB_ALTERNATE:
                {
                    var warped = scaled + (value * new Vector3(3.393f, 3.734f, 3.313f));
                    var resampled = noiseType == ParticleNoiseType.PF_NOISE_TYPE_WORLEY
                        ? Noise.Simplex3D(warped)
                        : Noise.Worley3D(warped, 0.97f, 22f);

                    target = value * resampled;
                    break;
                }
                default:
                    return value;
            }

            return float.Lerp(value, target, turbulenceMix);
        }

        private float ApplyModifier(float value) => modifier switch
        {
            ParticleNoiseModifier.PF_NOISE_MODIFIER_NONE => (value * 0.5f) + 0.5f,
            ParticleNoiseModifier.PF_NOISE_MODIFIER_LINES => MathF.Pow(MathF.Sin(value * (3f * MathF.PI)), 2f),
            ParticleNoiseModifier.PF_NOISE_MODIFIER_CLUMPS => (2.5f * MathF.Abs(value)) - 0.5f,
            ParticleNoiseModifier.PF_NOISE_MODIFIER_RINGS => Math.Clamp((MathF.Abs(MathF.Sin(value * MathF.PI)) + 0.25f) / 1.25f, 0f, 1f),
            _ => 1f,
        };
    }
}
