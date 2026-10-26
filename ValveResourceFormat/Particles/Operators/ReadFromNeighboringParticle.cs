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

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var live = particles.Current;
            var outputIsVector = ParticleBuffer.IsVector(fieldOutput);
            var inputIsVector = ParticleBuffer.IsVector(fieldInput);

            if (!outputIsVector && !ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var maxDistances = distanceCheck.Fill(live, particleSystemState);
            var amounts = interpolation.Fill(live, particleSystemState);

            var positions = live.ReadVectors(ParticleField.Position);
            var inputVectors = inputIsVector ? live.ReadVectors(fieldInput) : default;
            var inputScalars = inputIsVector ? default : live.ReadFloats(fieldInput);
            var outputVectors = outputIsVector ? live.Vectors(fieldOutput) : default;
            var outputScalars = outputIsVector ? default : live.Floats(fieldOutput);

            for (var i = 0; i < live.Length; i++)
            {
                var neighbor = Math.Max(Math.Min(i + increment, live.Length - 1), 0);

                var maxDistance = maxDistances[i];
                var amount = amounts[i];
                var maxDistanceSquared = maxDistance * maxDistance;

                if (maxDistanceSquared > 0f && Vector3.DistanceSquared(positions[i], positions[neighbor]) > maxDistanceSquared)
                {
                    continue;
                }

                if (outputIsVector)
                {
                    // A scalar input is read as if it were a vector, taking Y and Z from the particles 4 and 8 places
                    // past the neighbor; the scalar is broadcast instead
                    var value = inputIsVector ? inputVectors[neighbor] : new Vector3(inputScalars[neighbor]);

                    if (fieldOutput.IsNormalizedField())
                    {
                        value = Vector3.Clamp(value, Vector3.Zero, Vector3.One);
                    }

                    outputVectors[i] = MathUtils.Lerp(outputVectors[i], value, amount);
                    continue;
                }

                var scalar = inputIsVector ? inputVectors[neighbor].X : inputScalars[neighbor];

                if (fieldOutput.IsIntegerAttribute())
                {
                    outputScalars[i] = scalar;
                    continue;
                }

                if (fieldOutput.IsAlpha())
                {
                    scalar = MathUtils.Saturate(scalar);
                }

                outputScalars[i] = MathUtils.Lerp(outputScalars[i], scalar, MathUtils.Saturate(amount));
            }
        }
    }
}
