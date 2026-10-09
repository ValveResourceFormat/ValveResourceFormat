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
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var forward1 = ControlPointTransformProvider.TransformDirection(particleSystemState, inputControlPoint1, Vector3.UnitX);
            var forward2 = ControlPointTransformProvider.TransformDirection(particleSystemState, inputControlPoint2, Vector3.UnitX);
            forward1 = MathUtils.SafeNormalize(forward1, Vector3.UnitX, ParticleMath.MinimumLengthSquared);
            forward2 = MathUtils.SafeNormalize(forward2, Vector3.UnitX, ParticleMath.MinimumLengthSquared);

            var (rangeMin, rangeMax) = fieldOutput is ParticleField.Alpha or ParticleField.AlphaAlternate
                ? (MathUtils.Saturate(outputMin), MathUtils.Saturate(outputMax))
                : (outputMin, outputMax);

            foreach (ref var particle in particles.Current)
            {
                var first = (useParticleVelocity, useParticleNormal) switch
                {
                    (true, _) => MathUtils.SafeNormalize(particle.Position - particle.PositionPrevious, Vector3.Zero, ParticleMath.MinimumLengthSquared),
                    (_, true) => particle.Normal,
                    _ => forward1,
                };

                var dot = Vector3.Dot(first, forward2);

                if (activeRange && (dot < inputMin || dot > inputMax))
                {
                    continue;
                }

                var remapped = MathUtils.RemapValClamped(dot, inputMin, inputMax, rangeMin, rangeMax);

                particle.SetScalar(fieldOutput, particle.ModifyScalarBySetMethod(particles, fieldOutput, remapped, setMethod));
            }
        }
    }
}
