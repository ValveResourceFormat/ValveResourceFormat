using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Render an Omni2 light from particle data.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderOmni2Light">C_OP_RenderOmni2Light</seealso>
    internal class RenderOmni2Light : ParticleFunctionRenderer
    {
        private readonly Scene scene;
        private readonly List<SceneLight> lights = [];
        private readonly ParticleOmni2LightTypeChoiceList lightType = ParticleOmni2LightTypeChoiceList.PARTICLE_OMNI2_LIGHT_TYPE_POINT;
        private readonly IVectorProvider colorBlend = new LiteralVectorProvider(Vector3.One);
        private readonly ParticleColorBlendType colorBlendType = ParticleColorBlendType.PARTICLE_COLOR_BLEND_MULTIPLY;
        private readonly ParticleLightUnitChoiceList brightnessUnit = ParticleLightUnitChoiceList.PARTICLE_LIGHT_UNIT_LUMENS;
        private readonly INumberProvider brightnessLumens = new LiteralNumberProvider(1000f);
        private readonly INumberProvider brightnessCandelas = new LiteralNumberProvider(1000f);
        private readonly bool castShadows;
        private readonly INumberProvider luminaireRadius = new LiteralNumberProvider(1f);
        private readonly INumberProvider skirt = new LiteralNumberProvider(0.1f);
        private readonly INumberProvider range = new LiteralNumberProvider(512f);
        private readonly INumberProvider innerConeAngle = new LiteralNumberProvider(180f);
        private readonly INumberProvider outerConeAngle = new LiteralNumberProvider(180f);
        private readonly string? cookiePath;

        public RenderOmni2Light(ParticleDefinitionParser parse, Scene scene)
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
            luminaireRadius = parse.NumberProvider("m_flLuminaireRadius", luminaireRadius);
            skirt = parse.NumberProvider("m_flSkirt", skirt);
            range = parse.NumberProvider("m_flRange", range);
            innerConeAngle = parse.NumberProvider("m_flInnerConeAngle", innerConeAngle);
            outerConeAngle = parse.NumberProvider("m_flOuterConeAngle", outerConeAngle);
            cookiePath = parse.Data.GetStringProperty("m_hLightCookie") is { Length: > 0 } cookie ? cookie : null;
        }

        /// <inheritdoc/>
        public override void Hide()
        {
            foreach (var light in lights)
            {
                light.IsDirty = light.IsDirty || light.BrightnessScale != 0f;
                light.BrightnessScale = 0f;
            }
        }

        // Every live particle is its own light. The pool only grows on the main thread, since the
        // scene's light list is shared.
        public override void Act(ParticleCollection particles, ParticleSystemState systemState)
        {
            while (lights.Count < particles.Count)
            {
                var light = CreateLight();
                lights.Add(light);
                scene.LightingInfo.BarnLights.Add(light);
            }

            for (var i = 0; i < lights.Count; i++)
            {
                var light = lights[i];

                if (i < particles.Count)
                {
                    UpdateLight(light, ref particles.Current[i], systemState);
                    continue;
                }

                light.IsDirty = light.IsDirty || light.BrightnessScale != 0f;
                light.BrightnessScale = 0f;
            }
        }

        public override void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            // Light rendering is handled by the scene/light system.
        }

        public override void Delete()
        {
            foreach (var light in lights)
            {
                scene.LightingInfo.BarnLights.Remove(light);
            }

            lights.Clear();
        }

        private SceneLight CreateLight()
        {
            return new SceneLight(scene)
            {
                Type = SceneLight.LightType.Point,
                Entity = SceneLight.EntityType.Omni2,
                DirectLight = SceneLight.DirectLightType.Dynamic,
                CastShadows = castShadows ? 1 : 0,
                SpotOuterAngle = outerConeAngle.NextNumber(ref Particle.Default, ParticleSystemState.Default),
                SpotInnerAngle = innerConeAngle.NextNumber(ref Particle.Default, ParticleSystemState.Default),
                BrightnessScale = 1f,
                Range = range.NextNumber(ref Particle.Default, ParticleSystemState.Default),
                FallOff = skirt.NextNumber(ref Particle.Default, ParticleSystemState.Default),
                LuminaireSize = luminaireRadius.NextNumber(ref Particle.Default, ParticleSystemState.Default),
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

        /// <summary>
        /// Combines the particle color with the blend color. The particle color is quantized to bytes
        /// while the blend color stays normalized, so the modes that do not multiply the two mix
        /// both scales.
        /// </summary>
        private static Vector3 BlendColor(ParticleColorBlendType type, Vector3 blend, Vector3 particleColor)
        {
            var color = Vector3.Truncate(Vector3.Clamp(particleColor, Vector3.Zero, Vector3.One) * 255f);
            var white = new Vector3(255f);

            var blended = type switch
            {
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_DIVIDE => color / blend,
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_ADD => blend + color,
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_SUBTRACT => color - blend,
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_SCREEN => white - ((white - color) * blend),
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_MAX => Vector3.Max(color, blend),
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_MIN => Vector3.Min(color, blend),
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_REPLACE => blend,
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_AVERAGE => (blend + color) * 0.5f,
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_NEGATE => white - (blend * color),
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_LUMINANCE => Vector3.Dot(blend, new Vector3(0.2125f, 0.7154f, 0.0721f)) * color,
                _ => blend * color,
            };

            return Vector3.Round(Vector3.Clamp(blended, Vector3.Zero, white)) / 255f;
        }

        private void UpdateLight(SceneLight light, ref Particle particle, ParticleSystemState systemState)
        {
            var color = BlendColor(colorBlendType, colorBlend.NextVector(ref particle, systemState), particle.Color);

            var brightness = brightnessUnit switch
            {
                ParticleLightUnitChoiceList.PARTICLE_LIGHT_UNIT_CANDELAS => light.ComputeConeSolidAngle() * brightnessCandelas.NextNumber(ref particle, systemState),
                _ => brightnessLumens.NextNumber(ref particle, systemState)
            };

            var lightRange = MathF.Max(0f, range.NextNumber(ref particle, systemState));
            var skirtValue = skirt.NextNumber(ref particle, systemState);

            // Lumens spread over the cone, as the intensity the faces are lit with 100 units away
            light.Color = color;
            light.LinearBrightness = MathF.Max(0f, brightness) * 4f * MathF.PI * 10f / (light.ComputeConeSolidAngle() * 100f * 100f);
            light.BrightnessScale = MathF.Max(0f, particle.Alpha);
            light.Range = lightRange;
            light.FallOff = skirtValue;
            light.Position = particle.Position;
            light.Transform = Matrix4x4.CreateTranslation(particle.Position);
            light.Direction = particle.GetVector(ParticleField.Normal);
            light.IsDirty = true;
        }
    }
}
