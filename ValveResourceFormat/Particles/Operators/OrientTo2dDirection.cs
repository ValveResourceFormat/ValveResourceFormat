using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Turns a rotation attribute toward the direction of a vector input in the XY plane, by default
    /// the particle's velocity. A direction along +X gives half a turn, and the blend runs straight
    /// between the two angles in radians, so crossing the -X axis can spin the long way round.
    /// </summary>
    /// <remarks>
    /// Operator strength is not read. A vector with no XY component leaves the particle alone.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_OrientTo2dDirection">C_OP_OrientTo2dDirection</seealso>
    class OrientTo2dDirection : ParticleFunctionOperator
    {
        private readonly IVectorProvider input = new ParticleVelocityVectorProvider();
        private readonly float rotationOffset;
        private readonly float spinStrength = 1f;
        private readonly ParticleField fieldOutput = ParticleField.Roll;

        public OrientTo2dDirection(ParticleDefinitionParser parse) : base(parse)
        {
            input = parse.VectorProvider("m_vecInput", input);
            rotationOffset = float.DegreesToRadians(parse.Float("m_flRotOffset", rotationOffset));
            spinStrength = parse.Float("m_flSpinStrength", spinStrength);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var current = particles.Current;
            var values = current.Floats(fieldOutput);
            var directions = input.Fill(current, particleSystemState);

            for (var i = 0; i < values.Length; i++)
            {
                var direction = directions[i];

                if (direction.X == 0f && direction.Y == 0f)
                {
                    continue;
                }

                var target = ParticleMath.Heading(direction) + rotationOffset;
                values[i] = MathUtils.Lerp(values[i], target, spinStrength);
            }
        }
    }
}
