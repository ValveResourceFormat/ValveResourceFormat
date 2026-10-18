using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Sets each particle's step to a speed read from a control point, along the direction it is
    /// already moving. Young particles move at three times that speed, falling to one times over a
    /// latency in milliseconds read from another control point, so a projectile spawned late catches up.
    /// </summary>
    /// <remarks>
    /// The desired speed is one component of its control point, or the length of its position for
    /// <c>m_nDesiredVelocityCPField</c> -1. Nothing happens unless both control points are set.
    /// Operator strength is not read.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LagCompensation">C_OP_LagCompensation</seealso>
    class LagCompensation : ParticleFunctionOperator
    {
        private readonly int desiredVelocityCP = -1;
        private readonly int latencyCP = -1;
        private readonly int latencyCPField;
        private readonly int desiredVelocityCPField = -1;

        public LagCompensation(ParticleDefinitionParser parse) : base(parse)
        {
            desiredVelocityCP = parse.Int32("m_nDesiredVelocityCP", desiredVelocityCP);
            latencyCP = parse.Int32("m_nLatencyCP", latencyCP);
            latencyCPField = Math.Clamp(parse.Int32("m_nLatencyCPField", latencyCPField), 0, 2);
            desiredVelocityCPField = Math.Clamp(parse.Int32("m_nDesiredVelocityCPField", desiredVelocityCPField), -1, 2);
        }

        public override void AddControlPointDefaults(ControlPointDefaults defaults)
        {
            if (desiredVelocityCPField < 0)
            {
                defaults.AddScale(desiredVelocityCP);
            }
            else
            {
                defaults.AddScale(desiredVelocityCP, desiredVelocityCPField);
            }
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (desiredVelocityCP < 0 || latencyCP < 0)
            {
                return;
            }

            var latency = (particleSystemState.GetControlPoint(latencyCP).Position.GetComponent(latencyCPField) + ParticleMath.FloatEpsilon) / 1000f;

            var desiredVelocityPosition = particleSystemState.GetControlPoint(desiredVelocityCP).Position;
            var speed = desiredVelocityCPField == -1
                ? desiredVelocityPosition.Length()
                : desiredVelocityPosition.GetComponent(desiredVelocityCPField);
            var catchUpSpeed = speed * 3f;

            foreach (ref var particle in particles.Current)
            {
                var stepSpeed = latency == 0f
                    ? (particle.Age < 0f ? catchUpSpeed : speed)
                    : MathUtils.Lerp(catchUpSpeed, speed, MathUtils.Saturate(particle.Age / latency));

                var step = particle.Position - particle.PositionPrevious;
                var direction = ParticleMath.NormalizeWithEpsilon(step);

                particle.Position = (direction * (stepSpeed * frameTime)) + particle.PositionPrevious;
            }
        }
    }
}
