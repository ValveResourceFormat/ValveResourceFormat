namespace ValveResourceFormat.Particles.Operators
{
    class InheritFromParentParticlesV2 : ParticleFunctionOperator
    {
        private readonly INumberProvider scale = new LiteralNumberProvider(1f);
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly INumberProvider increment = new LiteralNumberProvider(1f);
        private readonly bool subSample;
        private readonly bool randomDistribution;
        private readonly bool reverse;
        private readonly ParticleMissingParentBehavior missingParentBehavior = (ParticleMissingParentBehavior)(-1);
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
            var parentCount = parentParticles.Length;
            if (parentCount == 0)
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

            var lastIndex = parentCount - 1;
            var runningRow = reverse ? lastIndex : 0;
            var subSamplePhase = 0f;
            var isVector = fieldOutput.FieldType() == "vector";
            var isNormalized = fieldOutput.IsNormalizedField();

            foreach (ref var particle in particles.Current)
            {
                var step = increment.NextNumber(ref particle, particleSystemState);
                var blendsNeighbours = subSample && step < 1f;

                int row;
                if (randomDistribution)
                {
                    row = Math.Clamp((int)(parentCount * particleSystemState.Random.Next()), 0, lastIndex);
                    runningRow = row;
                }
                else
                {
                    if (reverse)
                    {
                        if (runningRow < 0)
                        {
                            runningRow = lastIndex;
                        }
                    }
                    else if (runningRow > lastIndex)
                    {
                        runningRow = 0;
                    }

                    row = runningRow;
                }

                var parentId = particle.ParentParticleId;
                var parentIndex = particle.ParentParticleIndex;
                if (parentId == int.MinValue)
                {
                    switch (missingParentBehavior)
                    {
                        case ParticleMissingParentBehavior.MISSING_PARENT_FIND_NEW:
                            row = Math.Clamp((int)(parentCount * particleSystemState.Random.Next()), 0, lastIndex);
                            break;
                        default:
                            row = Math.Clamp(runningRow, 0, lastIndex);
                            break;
                    }

                    particle.ParentParticleId = parentParticles[row].ParticleId;
                    particle.ParentParticleIndex = row;
                }
                else if (parentId < 0 && parentIndex < 0)
                {
                    row = Math.Clamp(runningRow, 0, lastIndex);
                    particle.ParentParticleId = parentParticles[row].ParticleId;
                    particle.ParentParticleIndex = row;
                }
                else
                {
                    row = Math.Clamp(parentIndex, 0, lastIndex);
                }

                var scaleValue = scale.NextNumber(ref particle, particleSystemState);
                var interpolationValue = interpolation.NextNumber(ref particle, particleSystemState);
                var neighbourRow = reverse ? Math.Max(row - 1, 0) : Math.Min(row + 1, lastIndex);

                if (isVector)
                {
                    var target = parentParticles[row].GetVector(fieldOutput) * scaleValue;
                    if (isNormalized)
                    {
                        target = Vector3.Clamp(target, Vector3.Zero, Vector3.One);
                    }

                    if (subSamplePhase > 0f)
                    {
                        var neighbour = parentParticles[neighbourRow].GetVector(fieldOutput) * scaleValue;
                        if (isNormalized)
                        {
                            neighbour = Vector3.Clamp(neighbour, Vector3.Zero, Vector3.One);
                        }

                        target += (neighbour - target) * subSamplePhase;
                    }

                    var current = particle.GetVector(fieldOutput);
                    var result = current + (target - current) * interpolationValue;
                    particle.SetVector(fieldOutput, result);
                }
                else
                {
                    var target = parentParticles[row].GetScalar(fieldOutput) * scaleValue;
                    if (subSamplePhase > 0f)
                    {
                        var neighbour = parentParticles[neighbourRow].GetScalar(fieldOutput) * scaleValue;
                        target += (neighbour - target) * MathF.Min(subSamplePhase, 1f);
                    }

                    var current = particle.GetScalar(fieldOutput);
                    var result = current + (target - current) * Math.Clamp(interpolationValue, 0f, 1f);
                    if (isNormalized)
                    {
                        result = Math.Clamp(result, 0f, 1f);
                    }

                    particle.SetScalar(fieldOutput, result);
                }

                AdvanceWalk(ref runningRow, ref subSamplePhase, step, blendsNeighbours);
            }
        }

        private void AdvanceWalk(ref int runningRow, ref float subSamplePhase, float step, bool blendsNeighbours)
        {
            if (!blendsNeighbours)
            {
                runningRow += (int)(reverse ? -step : step);
                return;
            }

            subSamplePhase += step;
            if (subSamplePhase >= 1f)
            {
                subSamplePhase -= MathF.Round(subSamplePhase, MidpointRounding.ToEven);
                runningRow += reverse ? -1 : 1;
            }
        }
    }
}
