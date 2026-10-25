using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Base for the operators that remap each particle's distance to the segment between two control
    /// points, or to the infinite line through them, into an attribute.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapDistanceToLineSegmentBase">C_OP_RemapDistanceToLineSegmentBase</seealso>
    abstract class RemapDistanceToLineSegmentBase : ParticleFunctionOperator
    {
        private readonly int cp0;
        private readonly int cp1;
        private readonly float minInputValue;
        private readonly float maxInputValue = 100f;
        private readonly bool infiniteLine;

        protected RemapDistanceToLineSegmentBase(ParticleDefinitionParser parse) : base(parse)
        {
            cp0 = parse.Int32("m_nCP0", cp0);
            cp1 = parse.Int32("m_nCP1", cp1);
            minInputValue = parse.Float("m_flMinInputValue", minInputValue);
            maxInputValue = parse.Float("m_flMaxInputValue", maxInputValue);
            infiniteLine = parse.Boolean("m_bInfiniteLine", infiniteLine);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var start = particleSystemState.GetControlPoint(cp0).Position;
            var direction = particleSystemState.GetControlPoint(cp1).Position - start;
            var lengthSquared = direction.LengthSquared();

            var current = particles.Current;
            var positions = current.ReadVectors(ParticleField.Position);
            var fractions = ParticleScratch.Floats(current.Length);

            for (var i = 0; i < positions.Length; i++)
            {
                var position = positions[i];
                var t = lengthSquared <= ParticleMath.FloatEpsilon ? 0f : Vector3.Dot(position - start, direction) / lengthSquared;

                if (!infiniteLine)
                {
                    t = MathUtils.Saturate(t);
                }

                var distance = Vector3.Distance(start + (direction * t), position);
                fractions[i] = ParticleMath.ClampedFraction(distance, minInputValue, maxInputValue);
            }

            Write(current, fractions);
        }

        /// <summary>Writes the output for each particle's distance remapped to [0, 1] across the input range.</summary>
        protected abstract void Write(ParticleSpan particles, ReadOnlySpan<float> fractions);
    }
}
