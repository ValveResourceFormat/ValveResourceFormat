namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Fades a particle's alpha in over a configurable time window and then fades it out, killing
    /// the particle once it outlives its lifetime.
    /// </summary>
    /// <remarks>
    /// The tracer counterpart of <see cref="FadeAndKill"/>. A particle that runs out mid-frame is
    /// first pulled back along its own step to the point it ran out at, and only dies on the frame
    /// after, so the tracer it draws ends where the particle expired rather than wherever the
    /// frame's movement carried it. It carries no particle-order option.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_FadeAndKillForTracers">C_OP_FadeAndKillForTracers</seealso>
    class FadeAndKillForTracers : FadeAndKill
    {
        public FadeAndKillForTracers(ParticleDefinitionParser parse) : base(parse)
        {
        }

        /// <summary>A particle landing exactly on its lifetime is spent here, one tick sooner than the base does.</summary>
        protected override bool HasExpired(float age, float lifetime) => lifetime <= age;

        /// <summary>The alpha is written outright; this operator does not read its run strength.</summary>
        protected override bool BlendsWithStrength => false;

        protected override void Expire(ref Particle particle, float frameTime)
        {
            var ageAtFrameStart = particle.Age - frameTime;

            if (ageAtFrameStart >= particle.Lifetime || frameTime <= 0f)
            {
                particle.Kill();
                return;
            }

            // Pulled back along the step it died on, and left alive for one more frame so the tracer
            // is drawn ending there. The trail shrinks by the same fraction so its tail follows.
            var livedThisFrame = (particle.Lifetime - ageAtFrameStart) / frameTime;

            particle.Position = Vector3.Lerp(particle.PositionPrevious, particle.Position, livedThisFrame);
            particle.TrailLength *= livedThisFrame;
        }
    }
}
