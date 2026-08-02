namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Scales down the motion of particles inside a radius around a control point, damping them to a
    /// standstill at the control point itself.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_DampenToCP">C_OP_DampenToCP</seealso>
    class DampenToCP : ParticleFunctionOperator
    {
        private readonly int controlPoint;
        private readonly float range = 100f;
        private readonly float scale = 1f;

        public DampenToCP(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
            range = parse.Float("m_flRange", range);
            scale = parse.Float("m_flScale", scale);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            if (range <= 0f)
            {
                return;
            }

            var controlPointPosition = particleSystemState.GetControlPoint(controlPoint).Position;

            foreach (ref var particle in particles.Current)
            {
                var distance = Vector3.Distance(particle.Position, controlPointPosition);

                if (distance > range)
                {
                    continue;
                }

                var dampening = MathF.Pow(distance / range, scale);

                // Motion lives in the Verlet position pair, so shortening the step damps the particle
                var dampened = particle.PositionPrevious + ((particle.Position - particle.PositionPrevious) * dampening);

                particle.Position = Vector3.Lerp(particle.Position, dampened, strength);
            }
        }
    }
}
