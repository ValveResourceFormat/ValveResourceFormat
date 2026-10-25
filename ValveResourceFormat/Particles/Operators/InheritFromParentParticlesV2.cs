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
        private readonly ParticleFieldStorage storage;

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
            storage = ParticleBuffer.StorageOf(fieldOutput);
        }

        /// <inheritdoc/>
        public override ulong StoredFields
            => fieldOutput.Mask() | ParticleField.ParentParticleIndex.Mask() | ParticleField.ParentParticleId.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var parentData = particleSystemState.ParentSystem?.Data;

            if (parentData == null)
            {
                return;
            }

            var current = particles.Current;
            var parentParticles = parentData.CurrentParticles;
            var count = parentParticles.Length;

            if (count == 0)
            {
                if (missingParentBehavior == ParticleMissingParentBehavior.MISSING_PARENT_KILL)
                {
                    for (var i = 0; i < current.Length; i++)
                    {
                        particles.Kill(i);
                    }
                }

                return;
            }

            var boneIndices = storage == ParticleFieldStorage.BoneIndices ? current.BoneIndices : default;
            var vectorOutput = storage == ParticleFieldStorage.Vector ? current.Vectors(fieldOutput) : default;
            var scalarOutput = (storage is ParticleFieldStorage.Integer or ParticleFieldStorage.Scalar) ? current.Floats(fieldOutput) : default;
            var parentIds = current.Integers(ParticleField.ParentParticleId);
            var parentIndices = current.Integers(ParticleField.ParentParticleIndex);

            var scales = scale.Fill(current, particleSystemState);
            var increments = increment.Fill(current, particleSystemState);
            var interpolations = interpolation.Fill(current, particleSystemState);

            var walk = reverse ? count - 1 : 0;
            var carried = 0f;

            for (var i = 0; i < current.Length; i++)
            {
                if (randomDistribution)
                {
                    walk = particleSystemState.Random.NextIndex(count);
                }
                else if (walk < 0 || walk > count - 1)
                {
                    walk = reverse ? count - 1 : 0;
                }

                var step = increments[i];
                var fractionalStep = subSample && step < 1f;

                if (parentIds[i] < 0 && parentIndices[i] < 0)
                {
                    parentIds[i] = parentParticles.ReadInteger(ParticleField.ParticleId, walk);
                    parentIndices[i] = walk;
                }

                var copy = true;

                if (parentIds[i] == int.MinValue)
                {
                    switch (missingParentBehavior)
                    {
                        case ParticleMissingParentBehavior.MISSING_PARENT_KILL:
                            particles.Kill(i);
                            copy = false;
                            break;
                        case ParticleMissingParentBehavior.MISSING_PARENT_FIND_NEW:
                            Rebind(parentIds, parentIndices, i, parentParticles, particleSystemState.Random.NextIndex(count));
                            break;
                        case ParticleMissingParentBehavior.MISSING_PARENT_SAME_INDEX:
                            Rebind(parentIds, parentIndices, i, parentParticles, Math.Clamp(parentIndices[i], 0, count - 1));
                            break;
                        default:
                            copy = false;
                            break;
                    }
                }

                if (copy && (uint)parentIndices[i] < (uint)count)
                {
                    var source = parentIndices[i];
                    var next = reverse ? Math.Max(source - 1, 0) : Math.Min(source + 1, count - 1);

                    switch (storage)
                    {
                        case ParticleFieldStorage.BoneIndices:
                            boneIndices[i] = parentParticles.ReadBoneIndices(source);
                            break;
                        case ParticleFieldStorage.Vector:
                            CopyVector(vectorOutput, i, parentParticles, source, next, scales[i], interpolations[i], carried);
                            break;
                        case ParticleFieldStorage.Integer:
                            scalarOutput[i] = parentParticles.ReadFloat(fieldOutput, source);
                            break;
                        case ParticleFieldStorage.Scalar:
                            CopyScalar(scalarOutput, i, parentParticles, source, next, scales[i], interpolations[i], carried);
                            break;
                    }
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

        private static void Rebind(Span<int> parentIds, Span<int> parentIndices, int i, ParticleSpan parentParticles, int index)
        {
            parentIndices[i] = index;
            parentIds[i] = parentParticles.ReadInteger(ParticleField.ParticleId, index);
        }

        /// <summary>
        /// Blends the output vector toward the scaled value of the parent at <paramref name="source"/> by
        /// <paramref name="blend"/>. A positive <paramref name="toNext"/> first moves that value that far
        /// toward the scaled value of the parent at <paramref name="next"/>.
        /// </summary>
        private void CopyVector(Span<Vector3> output, int i, ParticleSpan parents, int source, int next, float scaleValue, float blend, float toNext)
        {
            var value = parents.ReadVector(fieldOutput, source) * scaleValue;

            if (fieldOutput.IsNormalizedField())
            {
                value = Vector3.Clamp(value, Vector3.Zero, Vector3.One);
            }

            if (toNext > 0f)
            {
                value = MathUtils.Lerp(value, parents.ReadVector(fieldOutput, next) * scaleValue, toNext);
            }

            output[i] = MathUtils.Lerp(output[i], value, blend);
        }

        /// <inheritdoc cref="CopyVector"/>
        private void CopyScalar(Span<float> output, int i, ParticleSpan parents, int source, int next, float scaleValue, float blend, float toNext)
        {
            var value = parents.ReadFloat(fieldOutput, source) * scaleValue;

            if (toNext > 0f)
            {
                value = MathUtils.Lerp(value, parents.ReadFloat(fieldOutput, next) * scaleValue, MathUtils.Saturate(toNext));
            }

            value = MathUtils.Lerp(output[i], value, MathUtils.Saturate(blend));

            if (fieldOutput.IsAlpha())
            {
                value = MathUtils.Saturate(value);
            }

            output[i] = value;
        }
    }
}
