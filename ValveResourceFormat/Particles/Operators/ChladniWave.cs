using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Writes the value of a Chladni plate standing wave at each particle's position, remapped to a
    /// scalar attribute, optionally in a control point's local space. The 2D pattern is
    /// cos(n pi x) cos(m pi y) - cos(m pi x) cos(n pi y) with the harmonics (m, n) as the X and Y
    /// of <c>m_vecHarmonics</c>; the 3D one sums a cosine per axis.
    /// </summary>
    /// <remarks>
    /// In 2D the first product divides both coordinates by the X wavelength and the second by the Y
    /// wavelength. A zero wavelength leaves the particle unchanged. Angle outputs are converted from
    /// degrees unless the set method scales. Operator strength is not read.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ChladniWave">C_OP_ChladniWave</seealso>
    class ChladniWave : ParticleFunctionOperator
    {
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly INumberProvider inputMin = new LiteralNumberProvider(-2f);
        private readonly INumberProvider inputMax = new LiteralNumberProvider(2f);
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1f);
        private readonly IVectorProvider waveLength = new LiteralVectorProvider(new Vector3(64f));
        private readonly IVectorProvider harmonics = new LiteralVectorProvider(new Vector3(2f, 3f, 0f));
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly int localSpaceControlPoint = -1;
        private readonly bool is3D;

        private readonly float outputScale = 1f;

        public ChladniWave(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputMin = parse.NumberProvider("m_flInputMin", inputMin);
            inputMax = parse.NumberProvider("m_flInputMax", inputMax);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            waveLength = parse.VectorProvider("m_vecWaveLength", waveLength);
            harmonics = parse.VectorProvider("m_vecHarmonics", harmonics);
            setMethod = parse.Enum("m_nSetMethod", setMethod);
            localSpaceControlPoint = parse.Int32("m_nLocalSpaceControlPoint", localSpaceControlPoint);
            is3D = parse.Boolean("m_b3D", is3D);

            if (fieldOutput.IsOrientationField()
                && setMethod is not ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE and not ParticleSetMethod.PARTICLE_SET_SCALE_CURRENT_VALUE)
            {
                outputScale = float.DegreesToRadians(1f);
            }
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var toLocal = Matrix4x4.Identity;

            if (localSpaceControlPoint > -1)
            {
                var point = particleSystemState.GetControlPoint(localSpaceControlPoint);
                var toWorld = point.GetTransform();

                if (!Matrix4x4.Invert(toWorld, out toLocal))
                {
                    toLocal = Matrix4x4.Identity;
                }
            }

            foreach (ref var particle in particles.Current)
            {
                var position = Vector3.Transform(particle.Position, toLocal);
                var harmonic = harmonics.NextVector(ref particle, particleSystemState);
                var wave = waveLength.NextVector(ref particle, particleSystemState);

                var inputMin = this.inputMin.NextNumber(ref particle, particleSystemState);
                var inputMax = this.inputMax.NextNumber(ref particle, particleSystemState);
                var outputMin = this.outputMin.NextNumber(ref particle, particleSystemState);
                var outputMax = this.outputMax.NextNumber(ref particle, particleSystemState);

                if (wave.X == 0f || wave.Y == 0f || (is3D && wave.Z == 0f))
                {
                    continue;
                }

                var pattern = is3D
                    ? MathF.Cos(harmonic.X * position.X / wave.X) + MathF.Cos(harmonic.Y * position.Y / wave.Y) + MathF.Cos(harmonic.Z * position.Z / wave.Z)
                    : (MathF.Cos(harmonic.Y * MathF.PI * position.X / wave.X) * MathF.Cos(harmonic.X * MathF.PI * position.Y / wave.X))
                        - (MathF.Cos(harmonic.X * MathF.PI * position.X / wave.Y) * MathF.Cos(harmonic.Y * MathF.PI * position.Y / wave.Y));

                var value = MathUtils.Lerp(outputMin, outputMax, ParticleMath.ClampedFraction(pattern, inputMin, inputMax)) * outputScale;

                particle.SetScalar(fieldOutput, particle.ModifyScalarBySetMethod(particles, fieldOutput, value, setMethod));
            }
        }
    }
}
