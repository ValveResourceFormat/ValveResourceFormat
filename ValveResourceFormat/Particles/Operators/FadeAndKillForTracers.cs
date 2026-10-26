using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Fades a particle's alpha in over a configurable time window and then fades it out, killing
    /// the particle once it outlives its lifetime.
    /// </summary>
    /// <remarks>
    /// The tracer counterpart of <see cref="FadeAndKill"/>. A particle that runs out mid-frame is
    /// pulled back along its step to where it ran out and dies a frame later, so its tracer ends there.
    /// A particle landing exactly on its lifetime is spent, one tick sooner than <see cref="FadeAndKill"/>,
    /// and the alpha is written outright rather than blended by the run strength. It has no
    /// particle-order option.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_FadeAndKillForTracers">C_OP_FadeAndKillForTracers</seealso>
    class FadeAndKillForTracers : FadeAndKill
    {
        public FadeAndKillForTracers(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public override bool IsOrderImportant => false;

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Position.Mask() | ParticleField.Alpha.Mask() | ParticleField.TrailLength.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;
            var ages = current.ReadAges();

            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);
            var alphas = current.Floats(ParticleField.Alpha);
            var initialAlphas = particles.Initial.ReadFloats(ParticleField.Alpha);

            for (var i = 0; i < alphas.Length; i++)
            {
                if (lifetimes[i] <= ages[i])
                {
                    Expire(particles, current, i, ages[i], lifetimes[i], frameTime);
                    continue;
                }

                var time = ParticleMath.NormalizedAge(ages[i], lifetimes[i]);
                alphas[i] = FadedAlpha(time, initialAlphas[i], alphas[i]);
            }
        }

        private static void Expire(ParticleCollection particles, ParticleSpan current, int index, float age, float lifetime, float frameTime)
        {
            var ageAtFrameStart = age - frameTime;

            if (ageAtFrameStart >= lifetime || frameTime <= 0f)
            {
                particles.Kill(index);
                return;
            }

            // Pulled back along the step it died on, and left alive for one more frame so the tracer
            // is drawn ending there. The trail shrinks by the same fraction so its tail follows. The
            // clip applies to the dying particle; the game applies it to one of the first four
            // particles of the collection, which is not reproduced.
            var livedThisFrame = (lifetime - ageAtFrameStart) / frameTime;
            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.ReadVectors(ParticleField.PositionPrevious);
            var trailLengths = current.Floats(ParticleField.TrailLength);

            positions[index] = Vector3.Lerp(previousPositions[index], positions[index], livedThisFrame);
            trailLengths[index] *= livedThisFrame;
        }
    }
}
