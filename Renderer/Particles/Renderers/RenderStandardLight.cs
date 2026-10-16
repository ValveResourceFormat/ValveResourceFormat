using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Render a standard point or spot light per particle. Up to <c>m_nMaxAllowed</c> particles with a
    /// positive intensity get a light each, which follows its particle by id and faces along its angles.
    /// The scene draws the lights; drawing the renderer only lets the next simulation step keep them.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderStandardLight">C_OP_RenderStandardLight</seealso>
    internal class RenderStandardLight : ParticleFunctionRenderer
    {
        /// <summary>What kind of light each particle casts.</summary>
        /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleLightTypeChoiceList_t">ParticleLightTypeChoiceList_t</seealso>
        private enum StandardLightType
        {
            PARTICLE_LIGHT_TYPE_POINT = 0,
            PARTICLE_LIGHT_TYPE_SPOT = 1,
            PARTICLE_LIGHT_TYPE_FX = 2,
            PARTICLE_LIGHT_TYPE_CAPSULE = 3,
        }

        /// <summary>How a light falls off with distance.</summary>
        /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/StandardLightingAttenuationStyle_t">StandardLightingAttenuationStyle_t</seealso>
        private enum AttenuationStyle
        {
            LIGHT_STYLE_OLD = 0,
            LIGHT_STYLE_NEW = 1,
        }

        // The barn light path these are drawn through runs brighter per unit than a standard light
        private const float IntensityScaleBarnPath = 3f;

        private const float LightFallOff = 0.5f;

        private readonly ParticleLightPool lights;
        private readonly StandardLightType lightType;
        private readonly int maxAllowed = 32;
        private readonly INumberProvider intensity = new LiteralNumberProvider(1f);
        private readonly INumberProvider theta = new LiteralNumberProvider(45f);
        private readonly INumberProvider phi = new LiteralNumberProvider(60f);
        private readonly INumberProvider radiusMultiplier = new LiteralNumberProvider(8f);
        private readonly AttenuationStyle attenuationStyle;
        private readonly INumberProvider zeroPercentFalloff = new LiteralNumberProvider(1f);
        private readonly bool castShadows;
        private readonly bool renderDiffuse = true;
        private readonly bool renderSpecular = true;
        private readonly bool reverseOrder;

        public RenderStandardLight(ParticleDefinitionParser parse, Scene scene)
            : base(parse)
        {
            lights = new ParticleLightPool(scene, () => CreateLight(scene));
            lightType = parse.Enum("m_nLightType", lightType);
            maxAllowed = parse.Int32("m_nMaxAllowed", maxAllowed);
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

        private bool drawn;

        /// <inheritdoc/>
        public override void Hide()
        {
            lights.ReleaseAll();
            lights.Apply();
        }

        /// <inheritdoc/>
        public override void Act(ParticleCollection particles, ParticleSystemState systemState) => lights.Apply();

        /// <inheritdoc/>
        /// <remarks>
        /// Lights are kept only by a step that follows a draw, so every step after the first of a frame
        /// split into several puts them all out.
        /// </remarks>
        public override void Simulate(ParticleCollection particles, ParticleSystemState systemState)
        {
            lights.BeginStep(Math.Min(particles.Count, maxAllowed));

            if (GetOperatorRunStrength(systemState) <= 0f)
            {
                lights.ReleaseAll();
                return;
            }

            if (drawn && particles.Count > 0 && !UpdateLights(particles, systemState))
            {
                return;
            }

            lights.EndStep();
            drawn = false;
        }

        /// <summary>Places a light on each lit particle, or returns false to leave every light as it was.</summary>
        private bool UpdateLights(ParticleCollection particles, ParticleSystemState systemState)
        {
            var radiusScale = radiusMultiplier.NextNumber(systemState);

            if (radiusScale == 0f)
            {
                return false;
            }

            if (attenuationStyle == AttenuationStyle.LIGHT_STYLE_NEW)
            {
                radiusScale *= Math.Clamp(zeroPercentFalloff.NextNumber(systemState), 1f / (1 << 22), 1f);
            }

            var colorScale = new Vector4(ParticleColorBlend.ConvertScale(ColorBlendType, ColorScale.NextVector(systemState)), 1f);
            var cone = lightType == StandardLightType.PARTICLE_LIGHT_TYPE_SPOT
                ? (Inner: theta.NextNumber(systemState) * 0.5f, Outer: phi.NextNumber(systemState) * 0.5f)
                : (Inner: 0f, Outer: 180f);

            var current = particles.Current;

            for (var n = 0; n < current.Length; n++)
            {
                ref var particle = ref current[reverseOrder ? current.Length - 1 - n : n];
                var brightness = intensity.NextNumber(ref particle, systemState);

                if (brightness <= 0f || lights.Claim(particle.ParticleId) is not { } slot
                    || particle.Radius == 0f || particle.Alpha == 0f)
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
        public override void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            drawn = true;
        }

        public override void Delete() => lights.Delete();

        private SceneLight CreateLight(Scene scene)
        {
            // todo: this should be legacy light (and it should not render in barn lighting scenes)
            // forcing this to barn light path for now, to showcase the new lights

            var fx = lightType == StandardLightType.PARTICLE_LIGHT_TYPE_FX;

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
