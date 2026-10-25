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
    /// are not drawn, and projected textures are not applied. Only games that ship the deferred local
    /// light material create these lights.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderDeferredLight">C_OP_RenderDeferredLight</seealso>
    internal class RenderDeferredLight : ParticleLightRenderer
    {
        private const int MaxLights = 64;

        /// <summary>The fraction of the radius at which a light shines with its intensity.</summary>
        private const float IntensityDistanceFraction = 0.5f;

        private const string DeferredLocalLightMaterial = "materials/dev/deferred_local_light_sphere.vmat_c";

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

        /// <summary>Whether the game the files come from ships the material its deferred lights are drawn with.</summary>
        public static bool GameHasDeferredLights(RendererContext rendererContext)
            => rendererContext.FileLoader.FindFile(DeferredLocalLightMaterial, logNotFound: false) is not (null, _, null);

        /// <inheritdoc/>
        protected override int LightCount(int particleCount) => MaxLights;

        /// <inheritdoc/>
        protected override bool UpdateLights(ParticleCollection particles, ParticleSystemState systemState)
        {
            var scales = EvaluateScales(systemState);
            var colorScale = scales.Color with { W = 1f };
            var hsvShift = GetHsvShift(systemState);

            var current = particles.Current;
            var particleIds = current.ReadIntegers(ParticleField.ParticleId);
            var positions = current.ReadVectors(ParticleField.Position);
            var normals = current.ReadVectors(ParticleField.Normal);
            var colors = current.ReadVectors(ParticleField.Color);
            var radii = current.ReadFloats(ParticleField.Radius);
            var alphas = current.ReadFloats(ParticleField.Alpha);
            var alpha2s = ReadAlpha2(current);

            for (var i = 0; i < current.Length; i++)
            {
                if (Lights.Claim(particleIds[i]) is not { } slot)
                {
                    continue;
                }

                slot.Touched = true;
                slot.State = BuildLight(positions[i], normals[i], colors[i], radii[i], alphas[i] * alpha2s[i], scales, colorScale, hsvShift);
            }

            return true;
        }

        /// <summary>The light of a particle, whose alpha taken with its <see cref="ParticleFunctionRenderer.Alpha2Field"/> is <paramref name="alpha"/>.</summary>
        private ParticleLightPool.LightState BuildLight(Vector3 position, Vector3 normal, Vector3 particleColor, float radius, float alpha,
            in RendererScales scales, Vector4 colorScale, Vector3 hsvShift)
        {
            var color = ParticleColorBlend.ApplyRounded(ColorBlendType, ParticleColorBlend.ToWrappedBytes(particleColor, 255f), colorScale);
            var rgb = color.AsVector3() / 255f;

            if (HsvShiftControlPoint >= 0)
            {
                rgb = ParticleColorBlend.ShiftHsv(rgb, hsvShift);
            }

            var range = radius * scales.Radius;
            var intensity = alpha * scales.Alpha;
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
                    ? EntityTransformHelper.ForwardDirectionToRotationMatrix(normal) * Matrix4x4.CreateTranslation(position)
                    : Matrix4x4.CreateTranslation(position));
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
                LuminaireShape = -1,
                Name = nameof(RenderDeferredLight),
            };
        }
    }
}
