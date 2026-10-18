using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// The scene lights the functions of one system place on their own, each following one
    /// <see cref="ParticleLightSource"/>. Updates from the simulation only record what each light shows;
    /// <see cref="Apply"/> hands that to the scene lights on the main thread, since the scene's light
    /// list is shared.
    /// </summary>
    internal sealed class ParticleSourceLights(Scene scene)
    {
        /// <summary>The fraction of the range the light fades over, the default skirt of an omni2 light.</summary>
        private const float LightFallOff = 0.1f;

        private readonly record struct LightState(bool IsLit, Vector3 Position, Vector3 Color, float Range);

        private readonly Dictionary<ParticleLightSource, LightState> states = [];
        private readonly Dictionary<ParticleLightSource, SceneLight> lights = [];

        /// <summary>Records what <paramref name="source"/> shows now.</summary>
        public void Update(ParticleLightSource source)
            => states[source] = new LightState(source.IsLit, source.Position, source.Color, source.Range);

        /// <summary>Makes the scene lights show what was last recorded, or puts them all out.</summary>
        /// <param name="visible">Whether the lights may show at all.</param>
        public void Apply(bool visible)
        {
            foreach (var (source, state) in states)
            {
                var lit = visible && state.IsLit;

                if (!lights.TryGetValue(source, out var light))
                {
                    if (!lit)
                    {
                        continue;
                    }

                    light = CreateLight();
                    lights[source] = light;
                    scene.LightingInfo.BarnLights.Add(light);
                }

                if (!lit)
                {
                    light.IsDirty = light.IsDirty || light.BrightnessScale != 0f;
                    light.BrightnessScale = 0f;
                    continue;
                }

                var peak = state.Color.MaxComponent();

                light.Color = ColorSpace.SrgbLinearToGamma(state.Color / peak);
                light.LinearBrightness = peak / (SceneLight.LinearBrightnessDistance * SceneLight.LinearBrightnessDistance);
                light.BrightnessScale = 1f;
                light.Range = state.Range;
                light.PlaceAt(Matrix4x4.CreateTranslation(state.Position));
                light.IsDirty = true;
            }
        }

        /// <summary>Takes every light out of the scene.</summary>
        public void Delete()
        {
            foreach (var light in lights.Values)
            {
                scene.LightingInfo.BarnLights.Remove(light);
            }

            lights.Clear();
            states.Clear();
        }

        private SceneLight CreateLight()
        {
            return new SceneLight(scene)
            {
                Type = SceneLight.LightType.Point,
                Entity = SceneLight.EntityType.Omni2,
                DirectLight = SceneLight.DirectLightType.Dynamic,
                FallOff = LightFallOff,
                SpotOuterAngle = 180f,
                CastShadows = 0,
                LuminaireSize = 0f,
                Name = nameof(ParticleSourceLights),
            };
        }
    }
}
