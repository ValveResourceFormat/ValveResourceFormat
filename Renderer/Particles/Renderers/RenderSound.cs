using ValveResourceFormat.Particles;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Plays a sound event for each particle, up to sixteen at once, following the particle by id: the
    /// sound moves with it and takes its volume from it, and stops when it dies or when the system goes
    /// a frame without being drawn. Particles past the sixteenth wait for a sound to come free. Has no
    /// visual output.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderSound">C_OP_RenderSound</seealso>
    internal class RenderSound : ParticleFunctionRenderer
    {
        private const int MaxSounds = 16;
        private const float StopFadeFallbackSeconds = 0.05f;

        private readonly string soundName = string.Empty;
        private readonly float volumeScale = 1f;
        private readonly ParticleField volumeField = ParticleField.Alpha;
        private readonly bool suppressStopSoundEvent;

        private struct SoundSlot
        {
            public Audio.SoundHandle Handle;
            public int ParticleId;
            public bool Allocated;
            public bool Used;
            public bool Played;
            public Vector3 Position;
            public float Volume;
        }

        private readonly SoundSlot[] slots = new SoundSlot[MaxSounds];

        /// <summary>Sounds the simulation let go of, stopped by the next <see cref="Act"/>.</summary>
        private readonly List<Audio.SoundHandle> released = [];

        private bool drawn;

        public RenderSound(ParticleDefinitionParser parse) : base(parse)
        {
            if (parse.Data.ContainsKey("m_pszSoundName"))
            {
                soundName = parse.Data.GetStringProperty("m_pszSoundName") ?? string.Empty;
            }

            volumeScale = parse.Float("m_flVolumeScale", volumeScale);
            volumeField = parse.ParticleField("m_nVolumeField", volumeField);
            suppressStopSoundEvent = parse.Boolean("m_bSuppressStopSoundEvent");

            // Decoding a vsnd on the update thread would stall the first particle that wants it.
            if (soundName.Length > 0)
            {
                Sound.Cache(soundName);
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Sounds are kept only by a step that follows a draw, so every step after the first of a frame
        /// split into several lets them all go, and the next drawn frame starts them again.
        /// </remarks>
        public override void Simulate(ParticleCollection particles, ParticleSystemState systemState)
        {
            if (soundName.Length == 0)
            {
                return;
            }

            for (var i = 0; i < slots.Length; i++)
            {
                slots[i].Used = false;
            }

            if (drawn && GetOperatorRunStrength(systemState) > 0f)
            {
                foreach (ref var particle in particles.Current)
                {
                    var index = FindSlot(particle.ParticleId);

                    if (index < 0)
                    {
                        continue;
                    }

                    ref var slot = ref slots[index];
                    slot.Used = true;
                    slot.Position = particle.Position;
                    slot.Volume = volumeField is ParticleField.NoneDisabled or < 0 ? 1f : particle.GetScalar(volumeField);
                }
            }

            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i].Allocated && !slots[i].Used)
                {
                    Release(ref slots[i]);
                }
            }

            drawn = false;
        }

        /// <inheritdoc/>
        public override void Act(ParticleCollection particles, ParticleSystemState systemState)
        {
            StopReleased();

            for (var i = 0; i < slots.Length; i++)
            {
                ref var slot = ref slots[i];

                if (!slot.Allocated)
                {
                    continue;
                }

                if (!slot.Played)
                {
                    slot.Handle = Sound.Play(soundName, slot.Position, volume: volumeScale);
                    slot.Played = true;
                }

                slot.Handle.Position = slot.Position;
                slot.Handle.Volume = slot.Volume;
            }
        }

        /// <summary>The slot following a particle, else the first free one, claimed for it, else -1.</summary>
        private int FindSlot(int particleId)
        {
            var free = -1;

            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i].Allocated && slots[i].ParticleId == particleId)
                {
                    return i;
                }

                if (!slots[i].Allocated && free < 0)
                {
                    free = i;
                }
            }

            if (free >= 0)
            {
                slots[free] = new SoundSlot { ParticleId = particleId, Allocated = true };
            }

            return free;
        }

        /// <summary>Frees a slot, leaving its sound for <see cref="Act"/> to stop.</summary>
        private void Release(ref SoundSlot slot)
        {
            if (slot.Played)
            {
                released.Add(slot.Handle);
            }

            slot = default;
        }

        private void StopReleased()
        {
            foreach (var handle in released)
            {
                if (!suppressStopSoundEvent)
                {
                    handle.FadeOutAndStop(StopFadeFallbackSeconds);
                }
            }

            released.Clear();
        }

        /// <summary>Stops every sound, as when the system stops drawing.</summary>
        public override void Hide()
        {
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i].Allocated)
                {
                    Release(ref slots[i]);
                }
            }

            StopReleased();
        }

        /// <inheritdoc/>
        public override void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            drawn = true;
        }

        public override void Delete()
        {
            foreach (var handle in released)
            {
                handle.Stop();
            }

            released.Clear();

            for (var i = 0; i < slots.Length; i++)
            {
                slots[i].Handle.Stop();
                slots[i] = default;
            }
        }
    }
}
