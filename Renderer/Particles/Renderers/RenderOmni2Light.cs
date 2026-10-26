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
                LuminaireRadii = luminaireRadius.Fill(current, systemState),
                InnerAngles = innerConeAngle.Fill(current, systemState),
                OuterAngles = outerConeAngle.Fill(current, systemState),
            };

            var lumens = brightnessLumens.Fill(current, systemState);
            var candelas = brightnessCandelas.Fill(current, systemState);
            var brightnesses = brightnessUnit == ParticleLightUnitChoiceList.PARTICLE_LIGHT_UNIT_CANDELAS ? candelas : lumens;

            var targets = target.Prepare(current, systemState);
            var normals = normal != null ? normal.Prepare(current, systemState) : default;

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
                slot.State = BuildLight(i, columns, inputs, blend, brightness, directions);
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
            Vector4 blend, float brightness, ReadOnlySpan<Vector3> directions)
        {
            var color = ParticleColorBlend.Apply(ColorBlendType, ParticleColorBlend.ToWrappedBytes(columns.Colors[index], 255f), blend);

            var outerAngle = Math.Clamp(inputs.OuterAngles[index], 0f, 180f);
            var innerAngle = Math.Clamp(inputs.InnerAngles[index], 0f, outerAngle);

            // Lumens are spread over the cone's solid angle
            var candelas = brightness;

            if (brightnessUnit != ParticleLightUnitChoiceList.PARTICLE_LIGHT_UNIT_CANDELAS)
            {
                candelas /= SceneLight.ComputeConeSolidAngle(outerAngle, innerAngle);
            }

            var linearBrightness = SceneLight.LinearBrightnessFromCandelas(candelas);

            return new ParticleLightPool.LightState(
                Color: color.AsVector3() / 255f,
                LinearBrightness: linearBrightness,
                BrightnessScale: MathF.Max(0f, columns.Alphas[index]),
                Range: inputs.Ranges[index],
                SpotInnerAngle: innerAngle,
                SpotOuterAngle: outerAngle,
                LuminaireSize: inputs.LuminaireRadii[index],
                FallOff: inputs.Skirts[index],
                Transform: LightRotation(index, columns, directions) * Matrix4x4.CreateTranslation(columns.Positions[index]));
        }
    }
}
