using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Render a standard point light.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderStandardLight">C_OP_RenderStandardLight</seealso>
    internal class RenderStandardLight : ParticleFunctionRenderer
    {
        private readonly Scene scene;
        private readonly SceneLight light;
        private readonly IVectorProvider colorScale = new LiteralVectorProvider(Vector3.One);
        private readonly INumberProvider intensity = new LiteralNumberProvider(1f);
        private INumberProvider radiusMultiplier = new LiteralNumberProvider(8f);
        private readonly INumberProvider zeroPercentFalloff = new LiteralNumberProvider(1f);
        private readonly bool castShadows;

        /// <summary>Light radius multiplier</summary>
        internal INumberProvider RadiusMultiplier
        {
            get => radiusMultiplier;
            set => radiusMultiplier = value;
        }

        internal SceneLight Light => light;

        public RenderStandardLight(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene)
            : base(parse)
        {
            this.scene = scene;
            colorScale = parse.VectorProvider("m_vecColorScale", colorScale);
            intensity = parse.NumberProvider("m_flIntensity", intensity);
            radiusMultiplier = parse.NumberProvider("m_flRadiusMultiplier", radiusMultiplier);
            zeroPercentFalloff = parse.NumberProvider("m_flZeroPercentFalloff", zeroPercentFalloff);
            castShadows = parse.Boolean("m_bCastShadows", castShadows);

            // todo: one light per particle?
            light = CreateLight();
            scene.LightingInfo.BarnLights.Add(light);
        }

        public override void Simulate(ParticleCollection particles, ParticleSystemState systemState)
        {
            if (particles.Count == 0)
            {
                light.IsDirty = light.IsDirty || light.BrightnessScale != 0f;
                light.BrightnessScale = 0f;
                return;
            }

            ref var particle = ref particles.Current[0];
            UpdateLight(light, ref particle, systemState);
        }

        public override void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            // Light rendering is handled externally by the scene/light system.
        }

        public override void Delete()
        {
            scene.LightingInfo.BarnLights.Remove(light);
        }

        private SceneLight CreateLight()
        {
            // todo: this should be legacy light (and it should not render in barn lighting scenes)
            // forcing this to barn light path for now, to showcase the new lights

            return new SceneLight(scene)
            {
                Type = SceneLight.LightType.Point,
                Entity = SceneLight.EntityType.Omni2,
                DirectLight = SceneLight.DirectLightType.Dynamic,
                FallOff = 0.5f,
                SpotOuterAngle = 180f,
                BrightnessScale = 0f,

                CastShadows = castShadows ? 1 : 0,
                LuminaireSize = 0f,

                Name = nameof(RenderStandardLight),
            };
        }

        private void UpdateLight(SceneLight light, ref Particle particle, ParticleSystemState systemState)
        {
            var color = colorScale.NextVector(ref particle, systemState) * particle.Color;
            var range = particle.Radius
                * radiusMultiplier.NextNumber(ref particle, systemState)
                * zeroPercentFalloff.NextNumber(ref particle, systemState);
            var brightness = MathF.Max(0f, intensity.NextNumber(ref particle, systemState));

            const float IntensityScaleBarnPath = 3f;

            light.Color = color;
            light.BrightnessLegacy = brightness * IntensityScaleBarnPath;
            light.BrightnessScale = 1f;
            light.Range = range;
            light.Position = particle.Position;
            light.Transform = Matrix4x4.CreateTranslation(particle.Position);
            light.Direction = particle.GetVector(ParticleField.Normal);
            light.IsDirty = true;
        }
    }
}
