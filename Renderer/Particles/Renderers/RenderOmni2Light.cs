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
        /// <summary>The smallest near size a barn light's frustum is given.</summary>
        private const float BarnMinimumNearSize = 1.1920929e-7f;

        /// <summary>The largest one minus softness a barn light's frustum edges are given.</summary>
        private const float BarnMaximumHardness = 0.99999988f;

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
        private readonly CurveLightStyle? lightStyle;
        private readonly INumberProvider lightStyleTime = new ParticleAgeNumberProvider();
        private readonly INumberProvider fieldOfView = new LiteralNumberProvider(30f);
        private readonly INumberProvider barnShape = new LiteralNumberProvider(1f);
        private readonly INumberProvider barnNearSizeX = new LiteralNumberProvider(1f);
        private readonly INumberProvider barnNearSizeY = new LiteralNumberProvider(1f);
        private readonly INumberProvider barnSoftX = new LiteralNumberProvider(0.25f);
        private readonly INumberProvider barnSoftY = new LiteralNumberProvider(0.25f);

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
            lightStyle = FindLightStyle(scene, parse.String("m_strLightStyle"));
            lightStyleTime = parse.NumberProvider("m_flLightStyleTime", lightStyleTime);
            fieldOfView = parse.NumberProvider("m_flFOVAngle", fieldOfView);
            barnShape = parse.NumberProvider("m_flBarnShape", barnShape);
            barnNearSizeX = parse.NumberProvider("m_flBarnNearSizeX", barnNearSizeX);
            barnNearSizeY = parse.NumberProvider("m_flBarnNearSizeY", barnNearSizeY);
            barnSoftX = parse.NumberProvider("m_flBarnSoftX", barnSoftX);
            barnSoftY = parse.NumberProvider("m_flBarnSoftY", barnSoftY);
        }

        private bool IsBarn => lightType == ParticleOmni2LightTypeChoiceList.PARTICLE_OMNI2_LIGHT_TYPE_BARN;

        /// <summary>Whether the light faces its target rather than a normal or the particle's angles.</summary>
        private bool TargetsDirection => orientationType is ParticleOmni2LightOrientationChoiceList.PARTICLE_OMNI2_LIGHT_ORIENTATION_TARGET
            or ParticleOmni2LightOrientationChoiceList.PARTICLE_OMNI2_LIGHT_ORIENTATION_TARGET_ROLL;

        /// <inheritdoc/>
        protected override bool UpdateLights(ParticleCollection particles, ParticleSystemState systemState)
        {
            var current = particles.Current;
            var particleIds = current.ReadIntegers(ParticleField.ParticleId);
            var columns = new LightColumns(current);

            // The colour blend is set up before the other inputs and read after them, so only an input drawing per
            // block draws after them
            var setUpColorBlend = colorBlend.DrawsPerBlock ? Vector3.Zero : colorBlend.NextVector(systemState);

            // Both brightness inputs are evaluated whichever unit is used, so the draws stay in this order
            var inputs = new LightInputs
            {
                Ranges = range.Fill(current, systemState),
                Skirts = skirt.Fill(current, systemState),
                // Evaluated for every light type so the random draws stay in order, though no light uses the radius
                LuminaireRadii = luminaireRadius.Fill(current, systemState),
                InnerAngles = innerConeAngle.Fill(current, systemState),
                OuterAngles = outerConeAngle.Fill(current, systemState),
            };

            var lumens = brightnessLumens.Fill(current, systemState);
            var candelas = brightnessCandelas.Fill(current, systemState);
            var brightnesses = brightnessUnit == ParticleLightUnitChoiceList.PARTICLE_LIGHT_UNIT_CANDELAS ? candelas : lumens;
            var styleTimes = lightStyleTime.Fill(current, systemState);

            var targets = target.Prepare(current, systemState);
            var normals = normal != null ? normal.Prepare(current, systemState) : default;

            // The barn inputs are evaluated for every light type so the random draws stay in order
            inputs = inputs with
            {
                FieldsOfView = fieldOfView.Fill(current, systemState),
                BarnShapes = barnShape.Fill(current, systemState),
                BarnSoftsX = barnSoftX.Fill(current, systemState),
                BarnSoftsY = barnSoftY.Fill(current, systemState),
                BarnNearSizesX = barnNearSizeX.Fill(current, systemState),
                BarnNearSizesY = barnNearSizeY.Fill(current, systemState),
            };

            var colorBlendValue = colorBlend.DrawsPerBlock ? colorBlend.NextVector(systemState) : setUpColorBlend;
            var blend = new Vector4(ParticleColorBlend.ConvertScale(ColorBlendType, colorBlendValue), 1f);

            var directionInput = normal;
            var directions = normals;

            if (TargetsDirection)
            {
                directionInput = target;
                directions = targets;
            }
            else if (orientationType == ParticleOmni2LightOrientationChoiceList.PARTICLE_OMNI2_LIGHT_ORIENTATION_ROTATIONS)
            {
                directionInput = null;
            }

            var nextDirectionBlock = 0;

            for (var i = 0; i < current.Length; i++)
            {
                var lightRange = inputs.Ranges[i];
                var brightness = brightnesses[i];

                if (lightRange <= 0f || brightness == 0f || Lights.Claim(particleIds[i]) is not { } slot)
                {
                    continue;
                }

                // A direction input drawing per block of four draws for the first particle of a block that claims a light
                if (directionInput != null && i >= nextDirectionBlock)
                {
                    var (offset, block) = current.BlockAt(i);
                    directionInput.FillPerBlock(block, block.Start & 3, systemState, directions[offset..]);
                    nextDirectionBlock = offset + block.Length;
                }

                slot.Touched = true;
                slot.State = BuildLight(i, columns, inputs, blend, brightness, styleTimes[i], directions);
            }

            return true;
        }

        /// <summary>The attributes a light is built from, for every particle of a span.</summary>
        private readonly ref struct LightColumns(ParticleSpan particles)
        {
            public ReadOnlySpan<Vector3> Positions { get; } = particles.ReadVectors(ParticleField.Position);

            public ReadOnlySpan<Vector3> Normals { get; } = particles.ReadVectors(ParticleField.Normal);

            public ReadOnlySpan<Vector3> Colors { get; } = particles.ReadVectors(ParticleField.Color);

            public ReadOnlySpan<float> Alphas { get; } = particles.ReadFloats(ParticleField.Alpha);

            public ReadOnlySpan<float> Yaws { get; } = particles.ReadFloats(ParticleField.Yaw);

            public ReadOnlySpan<float> Pitches { get; } = particles.ReadFloats(ParticleField.Pitch);

            public ReadOnlySpan<float> Rolls { get; } = particles.ReadFloats(ParticleField.Roll);
        }

        /// <summary>The evaluated inputs a light is built from, for every particle of a span.</summary>
        private readonly ref struct LightInputs
        {
            public ReadOnlySpan<float> Ranges { get; init; }

            public ReadOnlySpan<float> Skirts { get; init; }

            public ReadOnlySpan<float> LuminaireRadii { get; init; }

            public ReadOnlySpan<float> InnerAngles { get; init; }

            public ReadOnlySpan<float> OuterAngles { get; init; }

            public ReadOnlySpan<float> FieldsOfView { get; init; }

            public ReadOnlySpan<float> BarnShapes { get; init; }

            public ReadOnlySpan<float> BarnNearSizesX { get; init; }

            public ReadOnlySpan<float> BarnNearSizesY { get; init; }

            public ReadOnlySpan<float> BarnSoftsX { get; init; }

            public ReadOnlySpan<float> BarnSoftsY { get; init; }
        }

        /// <inheritdoc/>
        protected override SceneLight CreateLight(Scene scene)
        {
            return new SceneLight(scene)
            {
                Type = IsBarn ? SceneLight.LightType.Spot : SceneLight.LightType.Point,
                Entity = IsBarn ? SceneLight.EntityType.Barn : SceneLight.EntityType.Omni2,
                DirectLight = SceneLight.DirectLightType.Dynamic,
                CastShadows = castShadows ? 1 : 0,
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
        private Matrix4x4 LightRotation(int index, in LightColumns columns, ReadOnlySpan<Vector3> directions)
        {
            if (orientationType == ParticleOmni2LightOrientationChoiceList.PARTICLE_OMNI2_LIGHT_ORIENTATION_ROTATIONS)
            {
                return EntityTransformHelper.EulerAnglesToRotationMatrixRadians(new Vector3(columns.Pitches[index], columns.Yaws[index], columns.Rolls[index]));
            }

            var direction = TargetsDirection
                ? MathUtils.SafeNormalize(directions[index] - columns.Positions[index], -Vector3.UnitZ)
                : MathUtils.SafeNormalize(normal != null ? directions[index] : columns.Normals[index]);

            var frame = EntityTransformHelper.ForwardDirectionToRotationMatrix(direction);

            return orientationType is ParticleOmni2LightOrientationChoiceList.PARTICLE_OMNI2_LIGHT_ORIENTATION_NORMAL_ROLL
                    or ParticleOmni2LightOrientationChoiceList.PARTICLE_OMNI2_LIGHT_ORIENTATION_TARGET_ROLL
                ? Matrix4x4.CreateRotationX(columns.Rolls[index]) * frame
                : frame;
        }

        private ParticleLightPool.LightState BuildLight(int index, in LightColumns columns, in LightInputs inputs,
            Vector4 blend, float brightness, float styleTime, ReadOnlySpan<Vector3> directions)
        {
            var particleColor = ParticleColorBlend.ToWrappedBytes(columns.Colors[index], 255f);

            if (lightStyle != null)
            {
                var styleBrightness = lightStyle.GetBrightness(styleTime);
                particleColor = new Vector4(particleColor.AsVector3() * styleBrightness, particleColor.W);
            }

            var color = ParticleColorBlend.Apply(ColorBlendType, particleColor, blend);

            var outerAngle = Math.Clamp(inputs.OuterAngles[index], 0f, 180f);
            var innerAngle = Math.Clamp(inputs.InnerAngles[index], 0f, outerAngle);

            // Lumens are spread over the cone's solid angle
            var candelas = brightness;

            if (brightnessUnit != ParticleLightUnitChoiceList.PARTICLE_LIGHT_UNIT_CANDELAS)
            {
                candelas /= SceneLight.ComputeConeSolidAngle(outerAngle, innerAngle);
            }

            var linearBrightness = SceneLight.LinearBrightnessFromCandelas(candelas);

            var state = new ParticleLightPool.LightState(
                Color: color.AsVector3() / 255f,
                LinearBrightness: linearBrightness,
                BrightnessScale: MathF.Max(0f, columns.Alphas[index]) * VisibleAlpha,
                Range: inputs.Ranges[index] * VisibleRadius,
                SpotInnerAngle: innerAngle,
                SpotOuterAngle: outerAngle,
                // The sphere type's radius makes the light a sphere luminaire with its intensity scaled by 1/r^2, which is not represented yet
                LuminaireSize: 0f,
                FallOff: inputs.Skirts[index],
                Transform: LightRotation(index, columns, directions) * Matrix4x4.CreateTranslation(columns.Positions[index]));

            if (!IsBarn)
            {
                return state;
            }

            var nearSize = new Vector2(
                MathF.Max(BarnMinimumNearSize, inputs.BarnNearSizesX[index]),
                MathF.Max(BarnMinimumNearSize, inputs.BarnNearSizesY[index]));

            // The near size is the frustum's half size at the light, and the field of view is the angle from its
            // axis to that edge along X, which places the apex behind the light
            var fieldOfView = float.DegreesToRadians(Math.Clamp(inputs.FieldsOfView[index], 0f, 89f));
            var inverseApexDistance = MathF.Tan(fieldOfView) / nearSize.X;

            var hardness = Vector2.One - new Vector2(inputs.BarnSoftsX[index], inputs.BarnSoftsY[index]);
            var soft = Vector2.One - Vector2.Clamp(hardness, Vector2.Zero, new Vector2(BarnMaximumHardness));

            return state with
            {
                LinearBrightness = BarnLinearBrightness(brightness, nearSize, inverseApexDistance),
                // The skirt fades a barn light with distance from its apex over the last skirt fraction of its
                // range, as it fades a point light, rather than over the frustum's far depth
                FallOff = 0f,
                BarnDistanceSkirt = inputs.Skirts[index],
                BarnSizeParams = new Vector3(nearSize, inverseApexDistance),
                BarnShape = inputs.BarnShapes[index],
                BarnSoft = soft,
            };
        }

        /// <summary>
        /// The <see cref="SceneLight.LinearBrightness"/> of a barn light. Its intensity is divided by the squared
        /// distance from the frustum's apex to the light, and lumens are spread over the solid angle of the
        /// rectangular frustum whatever its shape. The barn light scales that back up by the squared distance from
        /// its apex to the brightness reference distance, which is divided out here.
        /// </summary>
        private float BarnLinearBrightness(float brightness, Vector2 nearSize, float inverseApexDistance)
        {
            var lumens = brightnessUnit != ParticleLightUnitChoiceList.PARTICLE_LIGHT_UNIT_CANDELAS;
            var referenceDistanceSquared = SceneLight.LinearBrightnessDistance * SceneLight.LinearBrightnessDistance;

            if (inverseApexDistance <= 0f)
            {
                // TODO: Whether the near rectangle's area counts as positive here is not known; a negative one
                // leaves the light off
                var parallelCandelas = lumens ? brightness / (4f * nearSize.X * nearSize.Y) : brightness;
                return SceneLight.LinearBrightnessFromCandelas(parallelCandelas) * referenceDistanceSquared;
            }

            var apexDistance = 1f / inverseApexDistance;
            var candelas = brightness;

            if (lumens)
            {
                var diagonal = MathF.Sqrt(apexDistance * apexDistance + nearSize.X * nearSize.X + nearSize.Y * nearSize.Y);
                var solidAngle = 4f * MathF.Atan(nearSize.X * nearSize.Y / (apexDistance * diagonal));
                candelas /= solidAngle;
            }

            var referenceDistance = apexDistance + SceneLight.LinearBrightnessDistance;
            return SceneLight.LinearBrightnessFromCandelas(candelas) * referenceDistanceSquared / (referenceDistance * referenceDistance);
        }
    }
}
