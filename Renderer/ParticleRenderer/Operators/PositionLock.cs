namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Locks particle positions to follow a transform input, optionally updating a previous position
    /// output field and locking orientation to the transform.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_PositionLock">C_OP_PositionLock</seealso>
    class PositionLock : ParticleFunctionOperator
    {
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly float startTimeMin = 1f;
        private readonly float startTimeMax = 1f;
        private readonly float startTimeExp = 1f;
        private readonly float endTimeMin = 1f;
        private readonly float endTimeMax = 1f;
        private readonly float endTimeExp = 1f;
        private readonly float fadeRange;
        private readonly INumberProvider rangeBias = new LiteralNumberProvider(0.2f);
        private readonly float instantJumpThreshold = 512f;
        private readonly float prevPosScale = 1f;
        private readonly bool lockRotation;
        private readonly Vector3 componentScale = Vector3.One;
        private readonly ParticleField outputField = ParticleField.Position;
        private readonly ParticleField outputFieldPrev = ParticleField.PositionPrevious;

        private Vector3 previousTransformPosition = new(float.MaxValue);

        public PositionLock(ParticleDefinitionParser parse) : base(parse)
        {
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            startTimeMin = parse.Float("m_flStartTime_min", startTimeMin);
            startTimeMax = parse.Float("m_flStartTime_max", startTimeMax);
            startTimeExp = parse.Float("m_flStartTime_exp", startTimeExp);
            endTimeMin = parse.Float("m_flEndTime_min", endTimeMin);
            endTimeMax = parse.Float("m_flEndTime_max", endTimeMax);
            endTimeExp = parse.Float("m_flEndTime_exp", endTimeExp);
            fadeRange = parse.Float("m_flRange", fadeRange);
            rangeBias = parse.NumberProvider("m_flRangeBias", rangeBias);
            instantJumpThreshold = parse.Float("m_flJumpThreshold", instantJumpThreshold);
            prevPosScale = parse.Float("m_flPrevPosScale", prevPosScale);
            lockRotation = parse.Boolean("m_bLockRot", lockRotation);
            componentScale = parse.Vector3("m_vecScale", componentScale);
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            outputFieldPrev = parse.ParticleField("m_nFieldOutputPrev", outputFieldPrev);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var transform = transformInput.NextTransform(ref particle, particleSystemState);
                var transformPosition = Vector3.Multiply(transform.Translation, componentScale);

                if (previousTransformPosition.X == float.MaxValue)
                {
                    previousTransformPosition = transformPosition;
                }

                var delta = (transformPosition - previousTransformPosition) * prevPosScale;
                var targetPosition = transformPosition + delta;

                if (fadeRange > 0f)
                {
                    var currentPosition = particle.GetVector(outputField);
                    var distance = Vector3.Distance(transformPosition, currentPosition);
                    var normalizedDistance = MathUtils.Saturate(distance / fadeRange);
                    var bias = rangeBias.NextNumber(ref particle, particleSystemState);
                    var remapped = bias <= 0f
                        ? normalizedDistance
                        : MathF.Pow(normalizedDistance, 1f / MathF.Max(0.0001f, bias));
                    var fadeWeight = 1f - remapped;
                    targetPosition = Vector3.Lerp(currentPosition, targetPosition, fadeWeight);
                }

                if (Vector3.Distance(transformPosition, previousTransformPosition) > instantJumpThreshold)
                {
                    targetPosition = transformPosition;
                }

                var startTime = ParticleCollection.RandomWithExponentBetween(particle.ParticleID, startTimeExp, startTimeMin, startTimeMax);
                var endTime = ParticleCollection.RandomWithExponentBetween(particle.ParticleID, endTimeExp, endTimeMin, endTimeMax);

                if (particle.Age < startTime || particle.Age > endTime)
                {
                    previousTransformPosition = transformPosition;
                    continue;
                }

                var previousOutput = particle.GetVector(outputField);
                particle.SetVector(outputFieldPrev, previousOutput);
                particle.SetVector(outputField, targetPosition);

                if (lockRotation)
                {
                    var orientation = transformInput.GetOrientation(ref particle, particleSystemState);
                    particle.Normal = orientation;
                }

                previousTransformPosition = transformPosition;
            }
        }
    }
}
