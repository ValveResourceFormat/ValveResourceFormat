using ValveResourceFormat.Animation;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Render a standard point or spot light per particle. Up to <c>m_nMaxAllowed</c> particles with a
    /// positive intensity get a light each, which follows its particle by id and faces along its angles.
    /// A capsule light is a tube from the particle along its facing, toward the next particle of a chain,
    /// or back along the particle's trail.
    /// </summary>
    /// <remarks>
    /// With <c>m_bReverseOrder</c> the lights go to the particles from the last one, each taking the
    /// intensity read for the particle as many places from the first.
    /// <para>
    /// The old attenuation falls off as <c>1 / (q*d^2 + l*d)</c> with <c>l</c> the falloff linearity and
    /// <c>q = 1 - l</c>, brightened by <c>q*10000 + l*100</c>, and the range is cut to where the light
    /// falls to a thousandth of its luminance.
    /// The new attenuation fits <c>1 / (c + l*d + q*d^2)</c> through 1 at the light, 1/2 at
    /// <c>m_flFiftyPercentFalloff</c> and 1/256 at <c>m_flZeroPercentFalloff</c>, both as fractions of
    /// the range. Either curve is drawn as an inverse square falloff that matches it at a fraction of
    /// the range.
    /// </para>
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderStandardLight">C_OP_RenderStandardLight</seealso>
    internal class RenderStandardLight : ParticleLightRenderer
    {
        private const float LightFallOff = 0.25f;

        /// <summary>The fraction of the range at which the inverse square falloff matches the light's own.</summary>
        private const float FalloffMatchFraction = 0.15f;

        /// <summary>The distance at which the falloffs match for a light without a range.</summary>
        private const float FalloffMatchDistanceWithoutRange = 100f;

        /// <summary>Attenuation terms below this are dropped.</summary>
        private const float MinimumAttenuationTerm = 0.001f;

        private const float MinimumZeroPercentFalloff = 1f / (1 << 22);
        private const float FiftyPercentFalloffMargin = 1f / (1 << 23);

        /// <summary>The shortest distance a trail capsule's moved distance is taken as.</summary>
        private const float MinimumTrailDistance = 1.1920929e-7f;

        private readonly ParticleLightTypeChoiceList lightType;
        private readonly StandardLightingAttenuationStyle attenuationStyle;
        private readonly INumberProvider intensity = new LiteralNumberProvider(1f);
        private readonly INumberProvider theta = new LiteralNumberProvider(45f);
        private readonly INumberProvider phi = new LiteralNumberProvider(60f);
        private readonly INumberProvider radiusMultiplier = new LiteralNumberProvider(8f);
        private readonly INumberProvider falloffLinearity = new LiteralNumberProvider(0f);
        private readonly INumberProvider fiftyPercentFalloff = new LiteralNumberProvider(0.5f);
        private readonly INumberProvider zeroPercentFalloff = new LiteralNumberProvider(1f);
        private readonly bool castShadows;
        private readonly bool renderDiffuse = true;
        private readonly bool renderSpecular = true;
        private readonly bool reverseOrder;
        private readonly ParticleLightBehaviorChoiceList capsuleBehavior;
        private readonly float capsuleLength = 100f;
        private readonly bool closedLoop;
        private readonly ParticleField previousPointSource = ParticleField.PositionPrevious;
        private readonly float maxLength = 2000f;
        private readonly float minLength;
        private readonly bool ignoreDeltaTime;
        private readonly float lengthScale = 1f;
        private readonly float lengthFadeInTime;

        public RenderStandardLight(ParticleDefinitionParser parse, Scene scene)
            : base(parse, scene)
        {
            lightType = parse.Enum("m_nLightType", lightType);
            attenuationStyle = parse.Enum("m_nAttenuationStyle", attenuationStyle);
            intensity = parse.NumberProvider("m_flIntensity", intensity);
            theta = parse.NumberProvider("m_flTheta", theta);
            phi = parse.NumberProvider("m_flPhi", phi);
            radiusMultiplier = parse.NumberProvider("m_flRadiusMultiplier", radiusMultiplier);
            falloffLinearity = parse.NumberProvider("m_flFalloffLinearity", falloffLinearity);
            fiftyPercentFalloff = parse.NumberProvider("m_flFiftyPercentFalloff", fiftyPercentFalloff);
            zeroPercentFalloff = parse.NumberProvider("m_flZeroPercentFalloff", zeroPercentFalloff);
            castShadows = parse.Boolean("m_bCastShadows", castShadows);
            renderDiffuse = parse.Boolean("m_bRenderDiffuse", renderDiffuse);
            renderSpecular = parse.Boolean("m_bRenderSpecular", renderSpecular);
            reverseOrder = parse.Boolean("m_bReverseOrder", reverseOrder);
            capsuleBehavior = parse.Enum("m_nCapsuleLightBehavior", capsuleBehavior);
            capsuleLength = parse.Float("m_flCapsuleLength", capsuleLength);
            closedLoop = parse.Boolean("m_bClosedLoop", closedLoop);
            previousPointSource = parse.ParticleField("m_nPrevPntSource", previousPointSource);
            maxLength = parse.Float("m_flMaxLength", maxLength);
            minLength = parse.Float("m_flMinLength", minLength);
            ignoreDeltaTime = parse.Boolean("m_bIgnoreDT", ignoreDeltaTime);
            lengthScale = parse.Float("m_flLengthScale", lengthScale);
            lengthFadeInTime = parse.Float("m_flLengthFadeInTime", lengthFadeInTime);

            // m_flConstrainRadiusToLengthRatio has no effect on the light
        }

        private bool IsCapsule => lightType == ParticleLightTypeChoiceList.PARTICLE_LIGHT_TYPE_CAPSULE;

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

            var colorScale = new Vector4(ParticleColorBlend.ConvertScale(ColorBlendType, ColorScale.NextVector(systemState)), 1f);
            var cone = lightType == ParticleLightTypeChoiceList.PARTICLE_LIGHT_TYPE_SPOT
                ? (Inner: theta.NextNumber(systemState), Outer: phi.NextNumber(systemState))
                : (Inner: 0f, Outer: 180f);

            var newAttenuation = attenuationStyle == StandardLightingAttenuationStyle.LIGHT_STYLE_NEW;
            var zeroPercent = MathF.Max(zeroPercentFalloff.NextNumber(systemState), MinimumZeroPercentFalloff);
            var fiftyPercent = MathF.Min(MathF.Max(fiftyPercentFalloff.NextNumber(systemState), FiftyPercentFalloffMargin), zeroPercent - FiftyPercentFalloffMargin);
            var (oldFalloff, oldBrightnessScale) = OldAttenuation(falloffLinearity.NextNumber(systemState));

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

            // A trail runs back to the previous position whatever m_nPrevPntSource names, which only
            // decides whether its length is scaled by the step
            var trails = IsCapsule && capsuleBehavior == ParticleLightBehaviorChoiceList.PARTICLE_LIGHT_BEHAVIOR_TRAILS;
            var previousPositions = trails ? current.ReadVectors(ParticleField.PositionPrevious) : default;
            var trailLengths = trails ? current.ReadFloats(ParticleField.TrailLength) : default;
            var ages = trails ? current.ReadAges() : default;
            var trailLengthScale = lengthScale * TrailDeltaTimeScale(systemState);
            var lengthFadeInRate = 1f / MathF.Max(lengthFadeInTime, 1e-9f);

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

                var rope = IsCapsule && capsuleBehavior == ParticleLightBehaviorChoiceList.PARTICLE_LIGHT_BEHAVIOR_ROPE;
                var neighbour = rope ? RopeNeighbour(n, current.Length) : index;

                // A rope capsule takes its colour from the particle it runs to
                var color = ParticleColorBlend.ApplyRounded(ColorBlendType, ParticleColorBlend.ToWrappedBytes(colors[neighbour], 255f), colorScale);
                var rgb = color.AsVector3() / 255f;
                var rotation = EntityTransformHelper.EulerAnglesToRotationMatrixRadians(new Vector3(pitches[index], yaws[index], rolls[index]));
                var range = radiusScale * radii[index];

                LightFalloff falloff;
                float brightnessScale;

                if (newAttenuation)
                {
                    falloff = LightFalloff.Solve(fiftyPercent * range, zeroPercent * range);
                    brightnessScale = 1f;
                }
                else
                {
                    falloff = oldFalloff;
                    brightnessScale = oldBrightnessScale;

                    var linearColor = ColorSpace.SrgbGammaToLinear(rgb) * (brightness * alphas[index] * brightnessScale);
                    var cutoff = falloff.CutoffFor(linearColor);

                    if (cutoff.Range > 0f)
                    {
                        range = range > 0f ? MathF.Min(range, cutoff.Range) : cutoff.Range;
                    }
                }

                var matchDistance = range > 0f ? range * FalloffMatchFraction : FalloffMatchDistanceWithoutRange;
                var matchBrightness = brightness * brightnessScale / falloff.Denominator(matchDistance);
                var linearBrightness = matchBrightness * (matchDistance * matchDistance) / (SceneLight.LinearBrightnessDistance * SceneLight.LinearBrightnessDistance);

                var transform = rotation * Matrix4x4.CreateTranslation(positions[index]);
                var tubeHalfLength = 0f;
                var luminaireSize = radii[index] * radiusScale;

                if (IsCapsule)
                {
                    // The tube runs from the particle to the end of this axis
                    Vector3 axis;

                    switch (capsuleBehavior)
                    {
                        case ParticleLightBehaviorChoiceList.PARTICLE_LIGHT_BEHAVIOR_ROPE:
                            // The axis to the next particle is normalized, so a rope tube is one unit long however
                            // far apart its particles are
                            axis = MathUtils.SafeNormalize(positions[neighbour] - positions[index], Vector3.UnitZ);
                            luminaireSize = 0.5f * (radii[index] + radii[neighbour]) * radiusScale;
                            break;

                        case ParticleLightBehaviorChoiceList.PARTICLE_LIGHT_BEHAVIOR_TRAILS:
                            var toPrevious = previousPositions[index] - positions[index];
                            var distance = MathF.Max(toPrevious.Length(), MinimumTrailDistance);
                            var fadeIn = MathF.Min(1f, ages[index] * lengthFadeInRate);
                            var length = MathF.Max(minLength, MathF.Min(maxLength, fadeIn * (trailLengthScale * trailLengths[index] * distance)));
                            axis = toPrevious / distance * length;
                            break;

                        default:
                            axis = rotation.GetRow(0).AsVector3() * capsuleLength;
                            break;
                    }

                    var direction = MathUtils.SafeNormalize(axis, Vector3.UnitZ);
                    var tubeRotation = Matrix4x4.CreateFromQuaternion(TransformMath.FromRotationBetweenUnitVectors(Vector3.UnitY, direction));

                    transform = tubeRotation * Matrix4x4.CreateTranslation(positions[index] + axis * 0.5f);
                    tubeHalfLength = axis.Length() * 0.5f;
                }

                slot.State = new ParticleLightPool.LightState(
                    Color: rgb,
                    LinearBrightness: linearBrightness,
                    BrightnessScale: alphas[index],
                    Range: range,
                    SpotInnerAngle: cone.Inner,
                    SpotOuterAngle: cone.Outer,
                    LuminaireSize: luminaireSize,
                    FallOff: LightFallOff,
                    Transform: transform,
                    TubeHalfLength: tubeHalfLength);
            }

            return true;
        }

        /// <summary>
        /// The particle a rope capsule runs to from the one <paramref name="n"/> places along the walk: the
        /// next one, wrapping to the first on a closed loop and staying on itself at the open end.
        /// </summary>
        private int RopeNeighbour(int n, int count)
        {
            var next = n + 1;

            if (next == count)
            {
                next = closedLoop ? 0 : n;
            }

            return reverseOrder ? count - 1 - next : next;
        }

        /// <summary>
        /// Turns a trail's moved distance into a velocity when its previous point is the one the step
        /// moved from, unless the renderer opts out.
        /// </summary>
        private float TrailDeltaTimeScale(ParticleSystemState systemState)
        {
            if (previousPointSource != ParticleField.PositionPrevious || ignoreDeltaTime || systemState.CurrentStep == 0f)
            {
                return 1f;
            }

            return 1f / systemState.CurrentStep;
        }

        /// <summary>
        /// The old attenuation for a falloff linearity, and the brightness scale that goes with it.
        /// Terms too small to matter are dropped, and a light left without any falls off not at all.
        /// </summary>
        private (LightFalloff Falloff, float BrightnessScale) OldAttenuation(float linearity)
        {
            var linear = lightType == ParticleLightTypeChoiceList.PARTICLE_LIGHT_TYPE_FX ? 1f : MathUtils.Saturate(linearity);
            var quadratic = 1f - linear;
            var constant = 0f;

            if (linear < MinimumAttenuationTerm)
            {
                linear = 0f;
            }

            if (quadratic < MinimumAttenuationTerm)
            {
                quadratic = 0f;
            }

            if (linear == 0f && quadratic == 0f)
            {
                constant = 1f;
            }

            var brightnessScale = (quadratic * 10000f) + (linear * 100f) + constant;
            return (new LightFalloff(constant, linear, quadratic), brightnessScale);
        }

        /// <inheritdoc/>
        protected override SceneLight CreateLight(Scene scene)
        {
            var fx = lightType == ParticleLightTypeChoiceList.PARTICLE_LIGHT_TYPE_FX;

            // A capsule is a tube luminaire along the light's second row
            return new SceneLight(scene)
            {
                Type = SceneLight.LightType.Point,
                Entity = SceneLight.EntityType.Omni2,
                DirectLight = SceneLight.DirectLightType.Dynamic,
                CastShadows = castShadows && !fx ? 1 : 0,
                LuminaireShape = IsCapsule ? 1 : -1,
                RenderDiffuse = fx || renderDiffuse,
                RenderSpecular = !fx && renderSpecular,
                Name = nameof(RenderStandardLight),
            };
        }
    }
}
