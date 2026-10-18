using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Render a deferred point light per particle, or a spot light along the particle's normal when
    /// <c>m_flLightDistance</c> is set. Up to 64 particles get a light each, which follows its particle
    /// by id; particles past that wait for a light to come free. The light reaches the particle's radius
    /// and shines with its alpha as intensity. Lights with a negative intensity darken in the game and
    /// are not drawn, and projected textures are not applied.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderDeferredLight">C_OP_RenderDeferredLight</seealso>
    internal class RenderDeferredLight : ParticleLightRenderer
    {
        private const int MaxLights = 64;

        /// <summary>The fraction of the radius at which a light shines with its intensity.</summary>
        private const float IntensityDistanceFraction = 0.5f;

        private readonly float lightDistance;
        private readonly float startFalloff = 0.25f;
        private readonly float spotFoV = 60f;

        public RenderDeferredLight(ParticleDefinitionParser parse, Scene scene)
            : base(parse, scene)
        {
            lightDistance = parse.Float("m_flLightDistance", lightDistance);
            startFalloff = parse.Float("m_flStartFalloff", startFalloff);
            spotFoV = parse.Float("m_flSpotFoV", spotFoV);
        }

        /// <inheritdoc/>
        protected override int LightCount(int particleCount) => MaxLights;

        /// <inheritdoc/>
        protected override bool UpdateLights(ParticleCollection particles, ParticleSystemState systemState)
        {
            var scales = EvaluateScales(systemState);
            var colorScale = scales.Color with { W = 1f };
            var hsvShift = GetHsvShift(systemState);

            foreach (ref var particle in particles.Current)
            {
                if (Lights.Claim(particle.ParticleId) is not { } slot)
                {
                    continue;
                }

                slot.Touched = true;
                slot.State = BuildLight(ref particle, scales, colorScale, hsvShift);
            }

            return true;
        }

        private ParticleLightPool.LightState BuildLight(ref Particle particle, in RendererScales scales, Vector4 colorScale, Vector3 hsvShift)
        {
            var tint = particle.Color * 255f;
            var color = ParticleColorBlend.Apply(ColorBlendType, new Vector4((byte)(int)tint.X, (byte)(int)tint.Y, (byte)(int)tint.Z, 255f), colorScale);
            var rgb = color.AsVector3() / 255f;

            if (HsvShiftControlPoint >= 0)
            {
                rgb = ParticleColorBlend.ShiftHsv(rgb, hsvShift);
            }

            var range = particle.Radius * scales.Radius;
            var intensity = particle.Alpha * GetAlpha2(ref particle) * scales.Alpha;
            var referenceDistance = range * IntensityDistanceFraction / SceneLight.LinearBrightnessDistance;
            var spot = lightDistance != 0f;

            return new ParticleLightPool.LightState(
                Color: rgb,
                LinearBrightness: referenceDistance * referenceDistance,
                BrightnessScale: range > 0f ? MathF.Max(0f, intensity) : 0f,
                Range: range,
                SpotInnerAngle: 0f,
                SpotOuterAngle: spot ? spotFoV * 0.5f : 180f,
                LuminaireSize: 0f,
                FallOff: MathUtils.Saturate(1f - startFalloff),
                Transform: spot
                    ? EntityTransformHelper.ForwardDirectionToRotationMatrix(particle.Normal) * Matrix4x4.CreateTranslation(particle.Position)
                    : Matrix4x4.CreateTranslation(particle.Position));
        }

        /// <inheritdoc/>
        protected override SceneLight CreateLight(Scene scene)
        {
            return new SceneLight(scene)
            {
                Type = SceneLight.LightType.Point,
                Entity = SceneLight.EntityType.Omni2,
                DirectLight = SceneLight.DirectLightType.Dynamic,
                CastShadows = 0,
                BrightnessScale = 0f,
                SpotOuterAngle = 180f,
                LuminaireShape = -1,
                LuminaireSize = 0f,
                Name = nameof(RenderDeferredLight),
            };
        }
    }
}
