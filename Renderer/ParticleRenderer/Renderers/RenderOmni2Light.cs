using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Render an Omni2 light from particle data.
    /// </summary>
    internal class RenderOmni2Light : ParticleFunctionRenderer
    {
        private readonly Scene scene;
        private readonly SceneLight light;
        private readonly ParticleOmni2LightTypeChoiceList lightType = ParticleOmni2LightTypeChoiceList.PARTICLE_OMNI2_LIGHT_TYPE_POINT;
        private readonly IVectorProvider colorBlend = new LiteralVectorProvider(Vector3.One);
        private readonly ParticleColorBlendType colorBlendType = ParticleColorBlendType.PARTICLE_COLOR_BLEND_MULTIPLY;
        private readonly ParticleLightUnitChoiceList brightnessUnit = ParticleLightUnitChoiceList.PARTICLE_LIGHT_UNIT_LUMENS;
        private readonly INumberProvider brightnessLumens = new LiteralNumberProvider(1000f);
        private readonly INumberProvider brightnessCandelas = new LiteralNumberProvider(1000f);
        private readonly bool castShadows;
        private readonly bool fog;
        private readonly INumberProvider fogScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider luminaireRadius = new LiteralNumberProvider(1f);
        private readonly INumberProvider skirt = new LiteralNumberProvider(0.1f);
        private readonly INumberProvider range = new LiteralNumberProvider(512f);
        private readonly INumberProvider innerConeAngle = new LiteralNumberProvider(180f);
        private readonly INumberProvider outerConeAngle = new LiteralNumberProvider(180f);
        private readonly string? cookiePath;
        private readonly bool sphericalCookie = true;

        public RenderOmni2Light(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene)
            : base(parse)
        {
            this.scene = scene;
            lightType = parse.Enum<ParticleOmni2LightTypeChoiceList>("m_nLightType", lightType);
            colorBlend = parse.VectorProvider("m_vColorBlend", colorBlend);
            colorBlendType = parse.Enum<ParticleColorBlendType>("m_nColorBlendType", colorBlendType);
            brightnessUnit = parse.Enum<ParticleLightUnitChoiceList>("m_nBrightnessUnit", brightnessUnit);
            brightnessLumens = parse.NumberProvider("m_flBrightnessLumens", brightnessLumens);
            brightnessCandelas = parse.NumberProvider("m_flBrightnessCandelas", brightnessCandelas);
            castShadows = parse.Boolean("m_bCastShadows", castShadows);
            fog = parse.Boolean("m_bFog", fog);
            fogScale = parse.NumberProvider("m_flFogScale", fogScale);
            luminaireRadius = parse.NumberProvider("m_flLuminaireRadius", luminaireRadius);
            skirt = parse.NumberProvider("m_flSkirt", skirt);
            range = parse.NumberProvider("m_flRange", range);
            innerConeAngle = parse.NumberProvider("m_flInnerConeAngle", innerConeAngle);
            outerConeAngle = parse.NumberProvider("m_flOuterConeAngle", outerConeAngle);
            cookiePath = parse.Data.GetStringProperty("m_hLightCookie");
            sphericalCookie = parse.Boolean("m_bSphericalCookie", sphericalCookie);

            light = CreateLight();
            scene.LightingInfo.BarnLights.Add(light);
        }

        public override void Update(ParticleCollection particles, ParticleSystemRenderState systemRenderState)
        {
            if (particles.Count == 0)
            {
                light.BrightnessScale = 0f;
                light.IsDirty = true;
                return;
            }

            ref var particle = ref particles.Current[0];
            UpdateLight(light, ref particle, systemRenderState);
        }

        public override void Render(ParticleCollection particles, ParticleSystemRenderState systemRenderState, Matrix4x4 modelViewMatrix)
        {
            // Light rendering is handled by the scene/light system.
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
                CastShadows = castShadows ? 1 : 0,
                SpotOuterAngle = outerConeAngle.NextNumber(ref Particle.Default, ParticleSystemRenderState.Default),
                SpotInnerAngle = innerConeAngle.NextNumber(ref Particle.Default, ParticleSystemRenderState.Default),
                BrightnessScale = 1f,
                Range = range.NextNumber(ref Particle.Default, ParticleSystemRenderState.Default),
                FallOff = skirt.NextNumber(ref Particle.Default, ParticleSystemRenderState.Default),
                LuminaireSize = luminaireRadius.NextNumber(ref Particle.Default, ParticleSystemRenderState.Default),
                LuminaireShape = lightType switch
                {
                    ParticleOmni2LightTypeChoiceList.PARTICLE_OMNI2_LIGHT_TYPE_POINT => -1,
                    ParticleOmni2LightTypeChoiceList.PARTICLE_OMNI2_LIGHT_TYPE_SPHERE => 0,
                    _ => 0
                },
                MinRoughness = 0.04f,
                Name = nameof(RenderOmni2Light),
                CookieTexturePath = cookiePath,
                // todo: "Cookie is Spherically Mapped"
            };
        }

        private void UpdateLight(SceneLight light, ref Particle particle, ParticleSystemRenderState systemRenderState)
        {
            var baseColor = colorBlend.NextVector(ref particle, systemRenderState) / 255f;
            var color = Vector3.Clamp(baseColor, Vector3.Zero, Vector3.One);

            var brightness = brightnessUnit switch
            {
                ParticleLightUnitChoiceList.PARTICLE_LIGHT_UNIT_CANDELAS => brightnessCandelas.NextNumber(ref particle, systemRenderState),
                _ => brightnessLumens.NextNumber(ref particle, systemRenderState)
            };

            var lightRange = MathF.Max(0f, range.NextNumber(ref particle, systemRenderState));
            var skirtValue = skirt.NextNumber(ref particle, systemRenderState);

            light.Color = color;
            light.Brightness = MathF.Max(0f, brightness);
            light.Range = lightRange;
            light.FallOff = skirtValue;
            light.Position = particle.Position;
            light.Transform = Matrix4x4.CreateTranslation(particle.Position);
            light.Direction = particle.GetVector(ParticleField.Normal) is { } normal && normal != Vector3.Zero
                ? Vector3.Normalize(normal)
                : Vector3.UnitX;
            light.IsDirty = true;
        }
    }
}
