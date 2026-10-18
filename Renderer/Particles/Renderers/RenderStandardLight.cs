using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Render a standard point or spot light per particle. Up to <c>m_nMaxAllowed</c> particles with a
    /// positive intensity get a light each, which follows its particle by id and faces along its angles.
    /// </summary>
    /// <remarks>
    /// With <c>m_bReverseOrder</c> the lights go to the particles from the last one, each taking the
    /// intensity read for the particle as many places from the first.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderStandardLight">C_OP_RenderStandardLight</seealso>
    internal class RenderStandardLight : ParticleLightRenderer
    {
        // The barn light path these are drawn through runs brighter per unit than a standard light
        private const float IntensityScaleBarnPath = 3f;

        private const float LightFallOff = 0.5f;

        private readonly ParticleLightTypeChoiceList lightType;
        private readonly INumberProvider intensity = new LiteralNumberProvider(1f);
        private readonly INumberProvider theta = new LiteralNumberProvider(45f);
        private readonly INumberProvider phi = new LiteralNumberProvider(60f);
        private readonly INumberProvider radiusMultiplier = new LiteralNumberProvider(8f);
        private readonly StandardLightingAttenuationStyle attenuationStyle;
        private readonly INumberProvider zeroPercentFalloff = new LiteralNumberProvider(1f);
        private readonly bool castShadows;
        private readonly bool renderDiffuse = true;
        private readonly bool renderSpecular = true;
        private readonly bool reverseOrder;

        public RenderStandardLight(ParticleDefinitionParser parse, Scene scene)
            : base(parse, scene)
        {
            lightType = parse.Enum("m_nLightType", lightType);
            intensity = parse.NumberProvider("m_flIntensity", intensity);
            theta = parse.NumberProvider("m_flTheta", theta);
            phi = parse.NumberProvider("m_flPhi", phi);
            radiusMultiplier = parse.NumberProvider("m_flRadiusMultiplier", radiusMultiplier);
            attenuationStyle = parse.Enum("m_nAttenuationStyle", attenuationStyle);
            zeroPercentFalloff = parse.NumberProvider("m_flZeroPercentFalloff", zeroPercentFalloff);
            castShadows = parse.Boolean("m_bCastShadows", castShadows);
            renderDiffuse = parse.Boolean("m_bRenderDiffuse", renderDiffuse);
            renderSpecular = parse.Boolean("m_bRenderSpecular", renderSpecular);
            reverseOrder = parse.Boolean("m_bReverseOrder", reverseOrder);
        }

        /// <inheritdoc/>
        protected override bool UpdateLights(ParticleCollection particles, ParticleSystemState systemState)
        {
            if (particles.Count == 0)
            {
                return true;
            }

            var radiusScale = radiusMultiplier.NextNumber(systemState);

            if (radiusScale == 0f)
            {
                return false;
            }

            if (attenuationStyle == StandardLightingAttenuationStyle.LIGHT_STYLE_NEW)
            {
                radiusScale *= Math.Clamp(zeroPercentFalloff.NextNumber(systemState), 1f / (1 << 22), 1f);
            }

            var colorScale = new Vector4(ParticleColorBlend.ConvertScale(ColorBlendType, ColorScale.NextVector(systemState)), 1f);
            var cone = lightType == ParticleLightTypeChoiceList.PARTICLE_LIGHT_TYPE_SPOT
                ? (Inner: theta.NextNumber(systemState) * 0.5f, Outer: phi.NextNumber(systemState) * 0.5f)
                : (Inner: 0f, Outer: 180f);

            var current = particles.Current;

            for (var n = 0; n < current.Length; n++)
            {
                var brightness = intensity.NextNumber(ref current[n], systemState);

                if (brightness <= 0f)
                {
                    continue;
                }

                ref var particle = ref current[reverseOrder ? current.Length - 1 - n : n];

                if (Lights.Claim(particle.ParticleId) is not { } slot || particle.Radius == 0f || particle.Alpha == 0f)
                {
                    continue;
                }

                slot.Touched = true;

                var color = ParticleColorBlend.Apply(ColorBlendType, ParticleColorBlend.ToBytes(particle.Color, 1f), colorScale);

                slot.State = new ParticleLightPool.LightState(
                    Color: color.AsVector3() / 255f,
                    LinearBrightness: brightness * IntensityScaleBarnPath,
                    BrightnessScale: particle.Alpha,
                    Range: radiusScale * particle.Radius,
                    SpotInnerAngle: cone.Inner,
                    SpotOuterAngle: cone.Outer,
                    LuminaireSize: 0f,
                    FallOff: LightFallOff,
                    Transform: particle.GetRotationMatrix() * Matrix4x4.CreateTranslation(particle.Position));
            }

            return true;
        }

        /// <inheritdoc/>
        protected override SceneLight CreateLight(Scene scene)
        {
            // todo: this should be legacy light (and it should not render in barn lighting scenes)
            // forcing this to barn light path for now, to showcase the new lights

            var fx = lightType == ParticleLightTypeChoiceList.PARTICLE_LIGHT_TYPE_FX;

            return new SceneLight(scene)
            {
                Type = SceneLight.LightType.Point,
                Entity = SceneLight.EntityType.Omni2,
                DirectLight = SceneLight.DirectLightType.Dynamic,
                FallOff = LightFallOff,
                SpotOuterAngle = 180f,
                BrightnessScale = 0f,

                CastShadows = castShadows && !fx ? 1 : 0,
                RenderDiffuse = fx || renderDiffuse,
                RenderSpecular = !fx && renderSpecular,
                LuminaireSize = 0f,

                Name = nameof(RenderStandardLight),
            };
        }
    }
}
