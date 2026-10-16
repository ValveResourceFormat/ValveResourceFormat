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
            var inputRange = maxInputValue == minInputValue ? 1f : maxInputValue - minInputValue;

            foreach (ref var particle in particles.Current)
            {
                var t = lengthSquared <= ParticleMath.FloatEpsilon ? 0f : Vector3.Dot(particle.Position - start, direction) / lengthSquared;

                if (!infiniteLine)
                {
                    t = MathUtils.Saturate(t);
                }

                var distance = Vector3.Distance(start + (direction * t), particle.Position);
                Write(ref particle, MathUtils.Saturate((distance - minInputValue) / inputRange));
            }
        }

        /// <summary>Writes the output for a distance remapped to [0, 1] across the input range.</summary>
        protected abstract void Write(ref Particle particle, float fraction);
    }
}
