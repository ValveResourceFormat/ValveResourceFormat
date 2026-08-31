using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Starts a sound event per spawned particle, then steers its position and volume by handle while
    /// the particle lives. Has no visual output.
    /// </summary>
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

        private struct TrackedSound
        {
            public Audio.SoundHandle Handle;
            public bool FieldSeen; // see ApplyFieldVolume
            public bool SeenThisFrame;
        }

        private readonly Dictionary<int, TrackedSound> trackedSounds = [];
        private readonly List<int> sweepScratch = [];

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

        public override void Act(ParticleCollection particles, ParticleSystemState systemState)
        {
            if (soundName.Length == 0)
            {
                return;
            }

            foreach (var particleId in trackedSounds.Keys)
            {
                ref var tracked = ref CollectionsMarshal.GetValueRefOrNullRef(trackedSounds, particleId);
                tracked.SeenThisFrame = false;
            }

            var highestId = nextParticleId;

            foreach (ref var particle in particles.Current)
            {
                if (particle.UniqueParticleId < nextParticleId)
                {
                    UpdateSound(ref particle, systemState);
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

                StartSound(ref particle, systemState);
            }

            nextParticleId = highestId;

            // A dead particle's sound is let go rather than stopped, so a one-shot finishes its tail
            sweepScratch.Clear();

            foreach (var (particleId, tracked) in trackedSounds)
            {
                if (!tracked.SeenThisFrame)
                {
                    sweepScratch.Add(particleId);
                }
            }

            foreach (var particleId in sweepScratch)
            {
                trackedSounds.Remove(particleId);
            }
        }

        private void StartSound(ref Particle particle, ParticleSystemState systemState)
        {
            var handle = Sound.Play(soundName, GetSoundPosition(ref particle, systemState), volume: volumeScale);

            if (!handle.IsValid)
            {
                return;
            }

            var tracked = new TrackedSound
            {
                Handle = handle,
                SeenThisFrame = true,
            };

            ApplyFieldVolume(ref tracked, ref particle);
            trackedSounds[particle.UniqueParticleId] = tracked;
        }

        private void UpdateSound(ref Particle particle, ParticleSystemState systemState)
        {
            ref var tracked = ref CollectionsMarshal.GetValueRefOrNullRef(trackedSounds, particle.UniqueParticleId);

            if (Unsafe.IsNullRef(ref tracked))
            {
                return;
            }

            // A sound that finished on its own stays unseen and gets swept
            if (tracked.Handle.Started)
            {
                tracked.SeenThisFrame = true;
                tracked.Handle.Position = GetSoundPosition(ref particle, systemState);
                ApplyFieldVolume(ref tracked, ref particle);
            }
        }

        // Fields often sit at zero on spawn until their operators run, so the field only steers the
        // volume once seen non-zero - one that never populates leaves the sound at full scale, not silent
        private void ApplyFieldVolume(ref TrackedSound tracked, ref Particle particle)
        {
            var fieldVolume = particle.GetScalar(volumeField);

            tracked.FieldSeen |= fieldVolume > 0f;
            tracked.Handle.Volume = tracked.FieldSeen ? fieldVolume : 1f;
        }

        private Vector3 GetSoundPosition(ref Particle particle, ParticleSystemState systemState)
            => controlPointReference >= 0
                ? systemState.GetControlPoint(controlPointReference).Position
                : particle.Position;

        public override void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            // Nothing to draw; the sound is started from Act.
        }
    }
}
