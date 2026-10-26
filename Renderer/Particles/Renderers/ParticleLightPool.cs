using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// The scene lights a light renderer keeps, each following one particle by id. Every simulation
    /// step marks the slots it updates; at its end the slots it did not touch go dark and come free
    /// for other particles. The steps only decide what each slot shows; <see cref="Apply"/> hands that
    /// to the scene lights, on the main thread, since the scene's light list is shared. Only grows.
    /// </summary>
    internal sealed class ParticleLightPool(Scene scene, Func<SceneLight> createLight)
    {
        /// <summary>What one light shows while it follows a particle.</summary>
        /// <param name="Color">The light colour, 0 to 1.</param>
        /// <param name="LinearBrightness">The brightness.</param>
        /// <param name="BrightnessScale">Scales the brightness.</param>
        /// <param name="Range">How far the light reaches.</param>
        /// <param name="SpotInnerAngle">Where the cone starts to fade, in degrees.</param>
        /// <param name="SpotOuterAngle">Where the cone ends, in degrees.</param>
        /// <param name="LuminaireSize">The size of the emitting shape.</param>
        /// <param name="FallOff">The fraction of the range the light fades over.</param>
        /// <param name="Transform">Where the light sits, facing along its first row.</param>
        /// <param name="TubeHalfLength">Half the length of a tube luminaire, along the second row.</param>
        /// <param name="NoAngleFade">Whether the light skips fading toward the edge of its cone.</param>
        /// <param name="BarnSizeParams">The frustum of a barn light, as <see cref="SceneLight.SizeParams"/>.</param>
        /// <param name="BarnShape">How round a barn light's frustum is, as <see cref="SceneLight.Shape"/>.</param>
        /// <param name="BarnSoft">How soft a barn light's frustum edges are, as <see cref="SceneLight.SoftX"/> and <see cref="SceneLight.SoftY"/>.</param>
        /// <param name="BarnDistanceSkirt">How a barn light fades with distance, as <see cref="SceneLight.BarnDistanceSkirt"/>.</param>
        public readonly record struct LightState(
            Vector3 Color,
            float LinearBrightness,
            float BrightnessScale,
            float Range,
            float SpotInnerAngle,
            float SpotOuterAngle,
            float LuminaireSize,
            float FallOff,
            Matrix4x4 Transform,
            float TubeHalfLength = 0f,
            bool NoAngleFade = false,
            Vector3 BarnSizeParams = default,
            float BarnShape = 0f,
            Vector2 BarnSoft = default,
            float? BarnDistanceSkirt = null);

        /// <summary>One light and the particle it follows.</summary>
        public sealed class Slot
        {
            /// <summary>Gets or sets the id of the particle the light follows.</summary>
            public int ParticleId { get; set; }

            /// <summary>Gets or sets whether the light follows a particle.</summary>
            public bool InUse { get; set; }

            /// <summary>Gets or sets whether the light was updated this step.</summary>
            public bool Touched { get; set; }

            /// <summary>Gets or sets what the light shows while it is in use.</summary>
            public LightState State { get; set; }

            /// <summary>Gets or sets the scene light, created by the first <see cref="Apply"/> after the slot.</summary>
            public SceneLight? Light { get; set; }
        }

        private readonly List<Slot> slots = [];

        /// <summary>Grows the pool to <paramref name="count"/> slots and starts a step with none touched.</summary>
        public void BeginStep(int count)
        {
            while (slots.Count < count)
            {
                slots.Add(new Slot());
            }

            foreach (var slot in slots)
            {
                slot.Touched = false;
            }
        }

        /// <summary>The slot following a particle, else the first free one, claimed for it, else none.</summary>
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

        /// <summary>Frees every slot the step did not touch.</summary>
        public void EndStep()
        {
            foreach (var slot in slots)
            {
                if (!slot.Touched)
                {
                    slot.InUse = false;
                }
            }
        }

        /// <summary>Frees every slot.</summary>
        public void ReleaseAll()
        {
            foreach (var slot in slots)
            {
                slot.InUse = false;
            }
        }

        /// <summary>Makes the scene lights show what the slots hold, putting out the free ones.</summary>
        public void Apply()
        {
            foreach (var slot in slots)
            {
                if (slot.Light == null)
                {
                    if (!slot.InUse)
                    {
                        continue;
                    }

                    slot.Light = createLight();
                    scene.LightingInfo.BarnLights.Add(slot.Light);
                }

                var light = slot.Light;

                if (!slot.InUse)
                {
                    light.IsDirty = light.IsDirty || light.BrightnessScale != 0f;
                    light.BrightnessScale = 0f;
                    continue;
                }

                var state = slot.State;

                light.Color = state.Color;
                light.LinearBrightness = state.LinearBrightness;
                light.BrightnessScale = state.BrightnessScale;
                light.Range = state.Range;
                light.SpotInnerAngle = state.SpotInnerAngle;
                light.SpotOuterAngle = state.SpotOuterAngle;
                light.LuminaireSize = state.LuminaireSize;
                light.FallOff = state.FallOff;
                light.NoAngleFade = state.NoAngleFade;

                if (light.Entity == SceneLight.EntityType.Barn)
                {
                    light.SizeParams = state.BarnSizeParams;
                    light.Shape = state.BarnShape;
                    light.SoftX = state.BarnSoft.X;
                    light.SoftY = state.BarnSoft.Y;
                    light.BarnDistanceSkirt = state.BarnDistanceSkirt;
                }
                else
                {
                    light.SizeParams = light.SizeParams with { Y = state.TubeHalfLength };
                }

                light.PlaceAt(state.Transform);
                light.IsDirty = true;
            }
        }

        /// <summary>Takes every light out of the scene.</summary>
        public void Delete()
        {
            foreach (var slot in slots)
            {
                if (slot.Light != null)
                {
                    scene.LightingInfo.BarnLights.Remove(slot.Light);
                }
            }

            slots.Clear();
        }
    }
}
