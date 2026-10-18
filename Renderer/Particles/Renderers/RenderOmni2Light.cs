using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Render an Omni2 light from particle data. Up to <c>m_nMaxAllowed</c> particles get a light each,
    /// which follows its particle by id; particles past that wait for a light to come free.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderOmni2Light">C_OP_RenderOmni2Light</seealso>
    internal class RenderOmni2Light : ParticleLightRenderer
    {
        private readonly ParticleOmni2LightTypeChoiceList lightType = ParticleOmni2LightTypeChoiceList.PARTICLE_OMNI2_LIGHT_TYPE_POINT;
        private readonly IVectorProvider colorBlend = new LiteralVectorProvider(Vector3.One);
        private readonly ParticleLightUnitChoiceList brightnessUnit = ParticleLightUnitChoiceList.PARTICLE_LIGHT_UNIT_LUMENS;
        private readonly INumberProvider brightnessLumens = new LiteralNumberProvider(1000f);
        private readonly INumberProvider brightnessCandelas = new LiteralNumberProvider(1000f);
        private readonly bool castShadows;
        private readonly INumberProvider luminaireRadius = new LiteralNumberProvider(1f);
        private readonly INumberProvider skirt = new LiteralNumberProvider(0.1f);
        private readonly INumberProvider range = new LiteralNumberProvider(512f);
        private readonly INumberProvider innerConeAngle = new LiteralNumberProvider(180f);
        private readonly INumberProvider outerConeAngle = new LiteralNumberProvider(180f);
        private readonly ParticleOmni2LightOrientationChoiceList orientationType;

        /// <summary>The direction for the normal orientations, or null to read the particle's normal.</summary>
        private readonly IVectorProvider? normal;

        private readonly IVectorProvider target = new LiteralVectorProvider(Vector3.Zero);
        private readonly string? cookiePath;

        public RenderOmni2Light(ParticleDefinitionParser parse, Scene scene)
            : base(parse, scene)
        {
            lightType = parse.Enum<ParticleOmni2LightTypeChoiceList>("m_nLightType", lightType);
            colorBlend = parse.VectorProvider("m_vColorBlend", colorBlend);
            brightnessUnit = parse.Enum<ParticleLightUnitChoiceList>("m_nBrightnessUnit", brightnessUnit);
            brightnessLumens = parse.NumberProvider("m_flBrightnessLumens", brightnessLumens);
            brightnessCandelas = parse.NumberProvider("m_flBrightnessCandelas", brightnessCandelas);
            castShadows = parse.Boolean("m_bCastShadows", castShadows);
            luminaireRadius = parse.NumberProvider("m_flLuminaireRadius", luminaireRadius);
            skirt = parse.NumberProvider("m_flSkirt", skirt);
            range = parse.NumberProvider("m_flRange", range);
            innerConeAngle = parse.NumberProvider("m_flInnerConeAngle", innerConeAngle);
            outerConeAngle = parse.NumberProvider("m_flOuterConeAngle", outerConeAngle);
            orientationType = parse.Enum("m_nOrientationType", orientationType);
            normal = parse.Data.ContainsKey("m_vNormal") ? parse.VectorProvider("m_vNormal", new LiteralVectorProvider(Vector3.Zero)) : null;
            target = parse.VectorProvider("m_vTarget", target);
            cookiePath = parse.Data.GetStringProperty("m_hLightCookie") is { Length: > 0 } cookie ? cookie : null;
        }

        /// <inheritdoc/>
        protected override bool UpdateLights(ParticleCollection particles, ParticleSystemState systemState)
        {
            var blend = new Vector4(ParticleColorBlend.ConvertScale(ColorBlendType, colorBlend.NextVector(systemState)), 1f);

            foreach (ref var particle in particles.Current)
            {
                var lightRange = range.NextNumber(ref particle, systemState);
                var brightness = brightnessUnit == ParticleLightUnitChoiceList.PARTICLE_LIGHT_UNIT_CANDELAS
                    ? brightnessCandelas.NextNumber(ref particle, systemState)
                    : brightnessLumens.NextNumber(ref particle, systemState);

                if (lightRange <= 0f || brightness == 0f || Lights.Claim(particle.ParticleId) is not { } slot)
                {
                    continue;
                }

                slot.Touched = true;
                slot.State = BuildLight(ref particle, systemState, blend, lightRange, brightness);
            }

            return true;
        }

        /// <inheritdoc/>
        protected override SceneLight CreateLight(Scene scene)
        {
            return new SceneLight(scene)
            {
                Type = SceneLight.LightType.Point,
                Entity = SceneLight.EntityType.Omni2,
                DirectLight = SceneLight.DirectLightType.Dynamic,
                CastShadows = castShadows ? 1 : 0,
                BrightnessScale = 0f,
                LuminaireShape = lightType == ParticleOmni2LightTypeChoiceList.PARTICLE_OMNI2_LIGHT_TYPE_POINT ? -1 : 0,
                MinRoughness = SceneLight.MinSpecularRoughness,
                Name = nameof(RenderOmni2Light),
                CookieTexturePath = cookiePath,
                // todo: "Cookie is Spherically Mapped"
            };
        }

        /// <summary>
        /// The light's frame, facing along its first row: the particle's angles, its normal or the way to
        /// its target, the roll variants turning that about the facing direction by the particle's roll.
        /// </summary>
        private Matrix4x4 LightRotation(ref Particle particle, ParticleSystemState systemState)
        {
            if (orientationType == ParticleOmni2LightOrientationChoiceList.PARTICLE_OMNI2_LIGHT_ORIENTATION_ROTATIONS)
            {
                return particle.GetRotationMatrix();
            }

            var direction = orientationType is ParticleOmni2LightOrientationChoiceList.PARTICLE_OMNI2_LIGHT_ORIENTATION_TARGET
                    or ParticleOmni2LightOrientationChoiceList.PARTICLE_OMNI2_LIGHT_ORIENTATION_TARGET_ROLL
                ? MathUtils.SafeNormalize(target.NextVector(ref particle, systemState) - particle.Position, -Vector3.UnitZ)
                : MathUtils.SafeNormalize(normal?.NextVector(ref particle, systemState) ?? particle.Normal);

            var frame = EntityTransformHelper.ForwardDirectionToRotationMatrix(direction);

            return orientationType is ParticleOmni2LightOrientationChoiceList.PARTICLE_OMNI2_LIGHT_ORIENTATION_NORMAL_ROLL
                    or ParticleOmni2LightOrientationChoiceList.PARTICLE_OMNI2_LIGHT_ORIENTATION_TARGET_ROLL
                ? Matrix4x4.CreateRotationX(particle.Rotation.Z) * frame
                : frame;
        }

        private ParticleLightPool.LightState BuildLight(ref Particle particle, ParticleSystemState systemState, Vector4 blend, float lightRange, float brightness)
        {
            var color = ParticleColorBlend.Apply(ColorBlendType, ParticleColorBlend.ToWrappedBytes(particle.Color, 255f), blend);

            var outerAngle = Math.Clamp(outerConeAngle.NextNumber(ref particle, systemState), 0f, 180f);
            var innerAngle = Math.Clamp(innerConeAngle.NextNumber(ref particle, systemState), 0f, outerAngle);
            var luminaireSize = luminaireRadius.NextNumber(ref particle, systemState);

            // Lumens spread over the cone, as the intensity the faces are lit with 100 units away
            var solidAngle = SceneLight.ComputeConeSolidAngle(outerAngle, innerAngle);
            var linearBrightness = brightness * 4f * MathF.PI * 10f
                * (brightnessUnit == ParticleLightUnitChoiceList.PARTICLE_LIGHT_UNIT_CANDELAS ? 1f : 1f / solidAngle)
                / (SceneLight.LinearBrightnessDistance * SceneLight.LinearBrightnessDistance);

            return new ParticleLightPool.LightState(
                Color: color.AsVector3() / 255f,
                LinearBrightness: linearBrightness,
                BrightnessScale: MathF.Max(0f, particle.Alpha),
                Range: lightRange,
                SpotInnerAngle: innerAngle,
                SpotOuterAngle: outerAngle,
                LuminaireSize: luminaireSize,
                FallOff: skirt.NextNumber(ref particle, systemState),
                Transform: LightRotation(ref particle, systemState) * Matrix4x4.CreateTranslation(particle.Position));
        }
    }
}
