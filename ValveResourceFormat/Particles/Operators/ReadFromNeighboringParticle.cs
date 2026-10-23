namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Lerps an attribute of each particle toward an attribute of the particle a fixed number of
    /// places further along, optionally only while the two are close enough. Particles are walked in
    /// order, so with a positive increment a value moves one place per step, and with a negative one
    /// it runs through the whole system in a single step.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ReadFromNeighboringParticle">C_OP_ReadFromNeighboringParticle</seealso>
    class ReadFromNeighboringParticle : ParticleFunctionOperator
    {
        private readonly ParticleField fieldInput = ParticleField.Radius;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly int increment = 1;
        private readonly INumberProvider distanceCheck = new LiteralNumberProvider(0f);
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1f);

        public ReadFromNeighboringParticle(ParticleDefinitionParser parse) : base(parse)
        {
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            increment = parse.Int32("m_nIncrement", increment);
            distanceCheck = parse.NumberProvider("m_DistanceCheck", distanceCheck);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var live = particles.Current;
            var outputIsVector = fieldOutput.FieldType() == "vector";
            var inputIsVector = fieldInput.FieldType() == "vector";

            for (var i = 0; i < live.Length; i++)
            {
                var particle = live[i];
                var neighbor = live[Math.Max(Math.Min(i + increment, live.Length - 1), 0)];

                var maxDistance = distanceCheck.NextNumber(particle, particleSystemState);
                var amount = interpolation.NextNumber(particle, particleSystemState);
                var maxDistanceSquared = maxDistance * maxDistance;

                if (maxDistanceSquared > 0f && Vector3.DistanceSquared(particle.Position, neighbor.Position) > maxDistanceSquared)
                {
                    continue;
                }

                if (outputIsVector)
                {
                    var value = inputIsVector ? neighbor.GetVector(fieldInput) : new Vector3(neighbor.GetScalar(fieldInput));

                    if (fieldOutput.IsNormalizedField())
                    {
                        value = Vector3.Clamp(value, Vector3.Zero, Vector3.One);
                    }

                    particle.SetVector(fieldOutput, MathUtils.Lerp(particle.GetVector(fieldOutput), value, amount));
                    continue;
                }

                var scalar = inputIsVector ? neighbor.GetVector(fieldInput).X : neighbor.GetScalar(fieldInput);

                if (fieldOutput.IsIntegerAttribute())
                {
                    particle.SetScalar(fieldOutput, scalar);
                    continue;
                }

                if (fieldOutput.IsAlpha())
                {
                    scalar = MathUtils.Saturate(scalar);
                }

                particle.SetScalar(fieldOutput, MathUtils.Lerp(particle.GetScalar(fieldOutput), scalar, MathUtils.Saturate(amount)));
            }
        }
    }
}
