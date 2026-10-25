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

        private const float MinZeroPercentFalloff = 1f / (1 << 22);

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
                radiusScale *= MathF.Max(zeroPercentFalloff.NextNumber(systemState), MinZeroPercentFalloff);
            }

            var colorScale = new Vector4(ParticleColorBlend.ConvertScale(ColorBlendType, ColorScale.NextVector(systemState)), 1f);
            var cone = lightType == ParticleLightTypeChoiceList.PARTICLE_LIGHT_TYPE_SPOT
                ? (Inner: theta.NextNumber(systemState) * 0.5f, Outer: phi.NextNumber(systemState) * 0.5f)
                : (Inner: 0f, Outer: 180f);

            var current = particles.Current;
            var particleIds = current.ReadIntegers(ParticleField.ParticleId);
            var positions = current.ReadVectors(ParticleField.Position);
            var colors = current.ReadVectors(ParticleField.Color);
            var radii = current.ReadFloats(ParticleField.Radius);
            var alphas = current.ReadFloats(ParticleField.Alpha);
            var yaws = current.ReadFloats(ParticleField.Yaw);
            var pitches = current.ReadFloats(ParticleField.Pitch);
            var rolls = current.ReadFloats(ParticleField.Roll);
            var intensities = intensity.Fill(current, systemState);

            for (var n = 0; n < current.Length; n++)
            {
                var brightness = intensities[n];

                if (brightness <= 0f)
                {
                    continue;
                }

                var index = reverseOrder ? current.Length - 1 - n : n;

                if (Lights.Claim(particleIds[index]) is not { } slot || radii[index] == 0f || alphas[index] == 0f)
                {
                    continue;
                }

                slot.Touched = true;

                var color = ParticleColorBlend.ApplyRounded(ColorBlendType, ParticleColorBlend.ToWrappedBytes(colors[index], 255f), colorScale);
                var rotation = EntityTransformHelper.EulerAnglesToRotationMatrixRadians(new Vector3(pitches[index], yaws[index], rolls[index]));

                slot.State = new ParticleLightPool.LightState(
                    Color: color.AsVector3() / 255f,
                    LinearBrightness: brightness * IntensityScaleBarnPath,
                    BrightnessScale: alphas[index],
                    Range: radiusScale * radii[index],
                    SpotInnerAngle: cone.Inner,
                    SpotOuterAngle: cone.Outer,
                    LuminaireSize: 0f,
                    FallOff: LightFallOff,
                    Transform: rotation * Matrix4x4.CreateTranslation(positions[index]));
            }

            return true;
        }

        /// <inheritdoc/>
        protected override SceneLight CreateLight(Scene scene)
        {
            // TODO: These are legacy lights, which scenes lit by barn lights do not draw; they are drawn as barn lights instead
            var fx = lightType == ParticleLightTypeChoiceList.PARTICLE_LIGHT_TYPE_FX;

            return new SceneLight(scene)
            {
                Type = SceneLight.LightType.Point,
                Entity = SceneLight.EntityType.Omni2,
                DirectLight = SceneLight.DirectLightType.Dynamic,
                CastShadows = castShadows && !fx ? 1 : 0,
                RenderDiffuse = fx || renderDiffuse,
                RenderSpecular = !fx && renderSpecular,
                Name = nameof(RenderStandardLight),
            };
        }
    }
}
