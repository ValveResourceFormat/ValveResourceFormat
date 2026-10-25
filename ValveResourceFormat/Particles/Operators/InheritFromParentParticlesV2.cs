using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Binds each particle to a parent particle once, then copies one attribute from that parent every
    /// step, scaled and blended toward the copy by <c>m_flInterpolation</c>. Particles not yet bound
    /// take the parent the walk over the parent list has reached, which advances by
    /// <c>m_nIncrement</c> per particle, or by one parent each time the increments add up to a whole
    /// one with sub-sampling, where the copy also blends toward the next parent by the fraction
    /// carried. <c>m_nMissingParentBehavior</c> decides what a particle whose parent died does.
    /// </summary>
    /// <remarks>
    /// The bind test runs before the dead parent test, so a particle a parent kill marked before it
    /// was ever bound binds on its next step like any other.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_InheritFromParentParticlesV2">C_OP_InheritFromParentParticlesV2</seealso>
    class InheritFromParentParticlesV2 : ParticleFunctionOperator
    {
        private readonly INumberProvider scale = new LiteralNumberProvider(1f);
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly INumberProvider increment = new LiteralNumberProvider(1f);
        private readonly bool subSample;
        private readonly bool randomDistribution;
        private readonly bool reverse;
        private readonly ParticleMissingParentBehavior missingParentBehavior = ParticleMissingParentBehavior.MISSING_PARENT_DO_NOTHING;
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1f);

        public InheritFromParentParticlesV2(ParticleDefinitionParser parse) : base(parse)
        {
            scale = parse.NumberProvider("m_flScale", scale);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            increment = parse.NumberProvider("m_nIncrement", increment);
            subSample = parse.Boolean("m_bSubSample", subSample);
            randomDistribution = parse.Boolean("m_bRandomDistribution", randomDistribution);
            reverse = parse.Boolean("m_bReverse", reverse);
            missingParentBehavior = parse.Enum("m_nMissingParentBehavior", missingParentBehavior);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var parentData = particleSystemState.ParentSystem?.Data;

            if (parentData == null)
            {
                return;
            }

            var parentParticles = parentData.CurrentParticles;
            var count = parentParticles.Length;

            if (count == 0)
            {
                if (missingParentBehavior == ParticleMissingParentBehavior.MISSING_PARENT_KILL)
                {
                    foreach (ref var particle in particles.Current)
                    {
                        particle.Kill();
                    }
                }

                return;
            }

            var walk = reverse ? count - 1 : 0;
            var carried = 0f;

            foreach (ref var particle in particles.Current)
            {
                if (randomDistribution)
                {
                    walk = particleSystemState.Random.NextIndex(count);
                }
                else if (walk < 0 || walk > count - 1)
                {
                    walk = reverse ? count - 1 : 0;
                }

                var step = increment.NextNumber(ref particle, particleSystemState);
                var fractionalStep = subSample && step < 1f;

                if (particle.ParentParticleId < 0 && particle.ParentParticleIndex < 0)
                {
                    particle.ParentParticleId = parentParticles[walk].ParticleId;
                    particle.ParentParticleIndex = walk;
                }

                var copy = true;

                if (particle.ParentParticleId == int.MinValue)
                {
                    switch (missingParentBehavior)
                    {
                        case ParticleMissingParentBehavior.MISSING_PARENT_KILL:
                            particle.Kill();
                            copy = false;
                            break;
                        case ParticleMissingParentBehavior.MISSING_PARENT_FIND_NEW:
                            Rebind(ref particle, parentParticles, particleSystemState.Random.NextIndex(count));
                            break;
                        case ParticleMissingParentBehavior.MISSING_PARENT_SAME_INDEX:
                            Rebind(ref particle, parentParticles, Math.Clamp(particle.ParentParticleIndex, 0, count - 1));
                            break;
                        default:
                            copy = false;
                            break;
                    }
                }

                if (copy && (uint)particle.ParentParticleIndex < (uint)count)
                {
                    CopyParentAttribute(ref particle, parentParticles, particle.ParentParticleIndex,
                        scale.NextNumber(ref particle, particleSystemState), interpolation.NextNumber(ref particle, particleSystemState), carried);
                }

                if (fractionalStep)
                {
                    carried += step;

                    if (carried >= 1f)
                    {
                        carried -= MathF.Floor(carried);
                        walk += reverse ? -1 : 1;
                    }
                }
                else
                {
                    walk += (int)(reverse ? -step : step);
                }
            }
        }

        private static void Rebind(ref Particle particle, Span<Particle> parentParticles, int index)
        {
            particle.ParentParticleIndex = index;
            particle.ParentParticleId = parentParticles[index].ParticleId;
        }

        /// <summary>
        /// Blends the output attribute toward the scaled value of parent <paramref name="index"/> by
        /// <paramref name="blend"/>. A positive <paramref name="toNext"/> first moves that value that far
        /// toward the scaled value of the parent after it in the walk direction. Integer attributes and
        /// bone indices are copied as is.
        /// </summary>
        private void CopyParentAttribute(ref Particle particle, Span<Particle> parentParticles, int index, float scaleValue, float blend, float toNext)
        {
            ref var source = ref parentParticles[index];
            ref var next = ref parentParticles[reverse ? Math.Max(index - 1, 0) : Math.Min(index + 1, parentParticles.Length - 1)];

            if (fieldOutput == ParticleField.BoneIndices)
            {
                particle.BoneIndices = source.BoneIndices;
            }
            else if (fieldOutput.FieldType() == "vector")
            {
                var value = source.GetVector(fieldOutput) * scaleValue;

                if (fieldOutput.IsNormalizedField())
                {
                    value = Vector3.Clamp(value, Vector3.Zero, Vector3.One);
                }

                if (toNext > 0f)
                {
                    var nextValue = next.GetVector(fieldOutput) * scaleValue;

                    if (fieldOutput.IsNormalizedField())
                    {
                        nextValue = Vector3.Clamp(nextValue, Vector3.Zero, Vector3.One);
                    }

                    value = MathUtils.Lerp(value, nextValue, toNext);
                }

                particle.SetVector(fieldOutput, MathUtils.Lerp(particle.GetVector(fieldOutput), value, blend));
            }
            else if (fieldOutput.IsIntegerAttribute())
            {
                particle.SetScalar(fieldOutput, source.GetScalar(fieldOutput));
            }
            else
            {
                var value = source.GetScalar(fieldOutput) * scaleValue;

                if (toNext > 0f)
                {
                    value = MathUtils.Lerp(value, next.GetScalar(fieldOutput) * scaleValue, MathUtils.Saturate(toNext));
                }

                value = MathUtils.Lerp(particle.GetScalar(fieldOutput), value, MathUtils.Saturate(blend));

                if (fieldOutput.IsAlpha())
                {
                    value = MathUtils.Saturate(value);
                }

                particle.SetScalar(fieldOutput, value);
            }
        }
    }
}
