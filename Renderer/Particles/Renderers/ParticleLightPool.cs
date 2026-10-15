using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// The scene lights a light renderer keeps, each following one particle by id. A step marks every
    /// light it updates; at its end the lights it did not touch go dark and come free for other
    /// particles. Only grows, and only on the main thread, since the scene's light list is shared.
    /// </summary>
    internal sealed class ParticleLightPool(Scene scene, Func<SceneLight> createLight)
    {
        /// <summary>One light and the particle it follows.</summary>
        public sealed class Slot(SceneLight light)
        {
            /// <summary>Gets the scene light.</summary>
            public SceneLight Light { get; } = light;

            /// <summary>Gets or sets the id of the particle the light follows.</summary>
            public int ParticleId { get; set; }

            /// <summary>Gets or sets whether the light follows a particle.</summary>
            public bool InUse { get; set; }

            /// <summary>Gets or sets whether the light was updated this step.</summary>
            public bool Touched { get; set; }
        }

        private readonly List<Slot> slots = [];

        /// <summary>Grows the pool to <paramref name="count"/> lights and starts a step with none touched.</summary>
        public void BeginStep(int count)
        {
            while (slots.Count < count)
            {
                var light = createLight();
                slots.Add(new Slot(light));
                scene.LightingInfo.BarnLights.Add(light);
            }

            foreach (var slot in slots)
            {
                slot.Touched = false;
            }
        }

        /// <summary>The light following a particle, else the first free one, claimed for it, else none.</summary>
        public Slot? Claim(int particleId)
        {
            Slot? free = null;

            foreach (var slot in slots)
            {
                if (slot.InUse && slot.ParticleId == particleId)
                {
                    return slot;
                }

                if (!slot.InUse)
                {
                    free ??= slot;
                }
            }

            if (free != null)
            {
                free.InUse = true;
                free.ParticleId = particleId;
            }

            return free;
        }

        /// <summary>Puts out and frees every light the step did not touch.</summary>
        public void EndStep()
        {
            foreach (var slot in slots)
            {
                if (slot.InUse && !slot.Touched)
                {
                    Release(slot);
                }
            }
        }

        /// <summary>Puts out and frees every light.</summary>
        public void ReleaseAll()
        {
            foreach (var slot in slots)
            {
                Release(slot);
            }
        }

        /// <summary>Takes every light out of the scene.</summary>
        public void Delete()
        {
            foreach (var slot in slots)
            {
                scene.LightingInfo.BarnLights.Remove(slot.Light);
            }

            slots.Clear();
        }

        private static void Release(Slot slot)
        {
            slot.InUse = false;
            slot.Light.IsDirty = slot.Light.IsDirty || slot.Light.BrightnessScale != 0f;
            slot.Light.BrightnessScale = 0f;
        }
    }
}
