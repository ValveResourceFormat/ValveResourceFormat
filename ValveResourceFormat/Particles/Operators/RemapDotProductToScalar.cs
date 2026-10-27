using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps how closely two directions line up into a scalar attribute. The first direction is a
    /// control point's forward axis, or the particle's own velocity or normal; the second is another
    /// control point's forward axis.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapDotProductToScalar">C_OP_RemapDotProductToScalar</seealso>
    class RemapDotProductToScalar : ParticleFunctionOperator
    {
        private readonly int inputControlPoint1;
        private readonly int inputControlPoint2;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly bool useParticleVelocity;
        private readonly bool useParticleNormal;
        private readonly bool activeRange;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public RemapDotProductToScalar(ParticleDefinitionParser parse) : base(parse)
        {
            inputControlPoint1 = parse.Int32("m_nInputCP1", inputControlPoint1);
            inputControlPoint2 = parse.Int32("m_nInputCP2", inputControlPoint2);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            useParticleVelocity = parse.Boolean("m_bUseParticleVelocity", useParticleVelocity);
            useParticleNormal = parse.Boolean("m_bUseParticleNormal", useParticleNormal);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            setMethod = parse.Enum("m_nSetMethod", setMethod);

            AddInitialReadOf(parse, setMethod, fieldOutput);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var forward1 = ControlPointTransformProvider.TransformDirection(particleSystemState, inputControlPoint1, Vector3.UnitX);
            var forward2 = ControlPointTransformProvider.TransformDirection(particleSystemState, inputControlPoint2, Vector3.UnitX);

            var (rangeMin, rangeMax) = fieldOutput.IsAlpha()
                ? (MathUtils.Saturate(outputMin), MathUtils.Saturate(outputMax))
                : (outputMin, outputMax);

            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var current = particles.Current;
            var positions = useParticleVelocity ? current.ReadVectors(ParticleField.Position) : default;
            var previousPositions = useParticleVelocity ? current.ReadVectors(ParticleField.PositionPrevious) : default;
            var normals = useParticleNormal && !useParticleVelocity ? current.ReadVectors(ParticleField.Normal) : default;
            var output = current.Floats(fieldOutput);
            var initial = particles.Initial.ReadFloats(fieldOutput);
            var isInteger = fieldOutput.IsIntegerAttribute();

            for (var i = 0; i < output.Length; i++)
            {
                Vector3 first;

                if (useParticleVelocity)
                {
                    first = ParticleMath.NormalizeWithEpsilon(positions[i] - previousPositions[i]);
                }
                else if (useParticleNormal)
                {
                    first = normals[i];
                }
                else
                {
                    first = forward1;
                }

                // Summed in this order to match the rounding at a degenerate threshold of -1 or 1
                var dot = (first.Y * forward2.Y) + (first.Z * forward2.Z) + (first.X * forward2.X);

                if (activeRange && (dot < inputMin || dot > inputMax))
                {
                    continue;
                }

                var remapped = MathUtils.RemapValClamped(dot, inputMin, inputMax, rangeMin, rangeMax);
                var value = ParticleFieldExtensions.ApplySetMethod(remapped, initial[i], output[i], particleSystemState.CurrentStep, setMethod);
                output[i] = isInteger ? BitConverter.Int32BitsToSingle((int)value) : value;
            }
        }
    }
}
