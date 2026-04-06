using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Opaque particle renderer placeholder for <c>C_OP_RenderStandardLight</c>.
    /// This renderer owns an Omni2 light while particles are alive.
    /// </summary>
    internal class RenderStandardLight : ParticleFunctionRenderer
    {
        private readonly Scene scene;
        private readonly SceneLight light;
        private readonly IVectorProvider colorScale = new LiteralVectorProvider(Vector3.One);
        private readonly INumberProvider intensity = new LiteralNumberProvider(1f);
        private readonly INumberProvider radiusMultiplier = new LiteralNumberProvider(1f);

        public RenderStandardLight(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene)
            : base(parse)
        {
            this.scene = scene;
            colorScale = parse.VectorProvider("m_vecColorScale", colorScale);
            intensity = parse.NumberProvider("m_flIntensity", intensity);
            radiusMultiplier = parse.NumberProvider("m_flRadiusMultiplier", radiusMultiplier);

            light = CreateLight();
            scene.LightingInfo.BarnLights.Add(light);
        }

        public override void Update(ParticleCollection particles, ParticleSystemRenderState systemRenderState)
        {
            // particle is being killed off too fast?
            if (particles.Count == 0)
            {
                light.BrightnessScale *= 0.8f;
                light.IsDirty = true;
                return;
            }

            ref var particle = ref particles.Current[0];
            UpdateLight(light, ref particle, systemRenderState);
        }

        public override void Render(ParticleCollection particles, ParticleSystemRenderState systemRenderState, Matrix4x4 modelViewMatrix)
        {
            // Light rendering is handled externally by the scene/light system.
        }

        public void Delete()
        {
            scene.LightingInfo.BarnLights.Remove(light);
        }

        private SceneLight CreateLight()
        {
            return new SceneLight(scene)
            {
                Type = SceneLight.LightType.Point,
                Entity = SceneLight.EntityType.Omni2,
                DirectLight = SceneLight.DirectLightType.Dynamic,
                CastShadows = 0,
                SpotOuterAngle = 180f,
                BrightnessScale = 1f,
                FallOff = 1f,
                StationaryLightIndex = -1,
                Name = nameof(RenderStandardLight),
            };
        }

        private void UpdateLight(SceneLight light, ref Particle particle, ParticleSystemRenderState systemRenderState)
        {
            // Should we use the particle color?
            var color = colorScale.NextVector(ref particle, systemRenderState) / 255f;
            var radius = particle.Radius;
            var range = radius * radiusMultiplier.NextNumber(ref particle, systemRenderState);
            var brightness = MathF.Max(0f, intensity.NextNumber(ref particle, systemRenderState));

            light.Color = color;
            light.Brightness = brightness;
            light.BrightnessScale = 300f;
            light.Range = range;
            light.Position = particle.Position;
            light.Transform = Matrix4x4.CreateTranslation(particle.Position);
            light.Direction = particle.GetVector(ParticleField.Normal) is { } normal && normal != Vector3.Zero
                ? Vector3.Normalize(normal)
                : Vector3.UnitX;

            light.IsDirty = true;
        }
    }
}
