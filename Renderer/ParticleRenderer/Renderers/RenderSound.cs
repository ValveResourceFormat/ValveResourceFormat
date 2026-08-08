using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Starts a sound event as particles spawn. Has no visual output.
    /// </summary>
    /// <remarks>
    /// One sound per particle, fired the first time that particle is seen, positioned at the particle
    /// unless m_nCPReference names a control point to take the position from instead.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderSound">C_OP_RenderSound</seealso>
    internal class RenderSound : ParticleFunctionRenderer
    {
        private readonly string soundName = string.Empty;
        private readonly float volumeScale = 1f;
        private readonly ParticleField volumeField = ParticleField.Alpha;
        // -1 leaves the sound on the particle rather than attaching it to a control point.
        private readonly int controlPointReference = -1;

        // Particle ids are handed out in ascending order and never reused, so a high-water mark is all
        // the state needed to tell which particles appeared since the last update.
        private int nextParticleId;

        public RenderSound(ParticleDefinitionParser parse) : base(parse)
        {
            if (parse.Data.ContainsKey("m_pszSoundName"))
            {
                soundName = parse.Data.GetStringProperty("m_pszSoundName") ?? string.Empty;
            }

            volumeScale = parse.Float("m_flVolumeScale", volumeScale);
            volumeField = parse.ParticleField("m_nVolumeField", volumeField);
            controlPointReference = parse.Int32("m_nCPReference", controlPointReference);

            // Decoding a vsnd on the update thread would stall the first particle that wants it.
            if (soundName.Length > 0)
            {
                Sound.Cache(soundName);
            }
        }

        public override void Update(ParticleCollection particles, ParticleSystemRenderState systemRenderState)
        {
            if (soundName.Length == 0)
            {
                return;
            }

            var highestId = nextParticleId;

            foreach (ref var particle in particles.Current)
            {
                if (particle.UniqueParticleId < nextParticleId)
                {
                    continue;
                }

                if (particle.UniqueParticleId >= highestId)
                {
                    highestId = particle.UniqueParticleId + 1;
                }

                // Renderers do not run during pre-simulation, so the first real update meets every
                // particle the burst created at once. Only the ones born this frame should sound.
                if (particle.Age > particles.PreviousFrameTime)
                {
                    continue;
                }

                var position = controlPointReference >= 0
                    ? systemRenderState.GetControlPoint(controlPointReference).Position
                    : particle.Position;

                var fieldVolume = particle.GetInitialScalar(particles, volumeField);

                // hack: ignore field volume because it is often 0 on spawn
                // todo: grab handle and update position+volume every frame with the particle
                var volume = fieldVolume > 0f ? volumeScale * fieldVolume : volumeScale;

                Sound.Play(soundName, position, volume: volume);
            }

            nextParticleId = highestId;
        }

        public override void Render(ParticleCollection particles, ParticleSystemRenderState systemRenderState, Camera camera)
        {
            // Nothing to draw; the sound is started from Update.
        }
    }
}
