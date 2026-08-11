using ValveResourceFormat.Renderer.Particles.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Provides a <see cref="Vector3"/> value that may vary per particle or per frame.
    /// </summary>
    interface IVectorProvider
    {
        /// <summary>
        /// Returns the next vector value, evaluated per particle.
        /// </summary>
        Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState);

        /// <summary>
        /// Returns the next vector value using system-level state only.
        /// </summary>
        Vector3 NextVector(ParticleSystemRenderState renderState)
            => NextVector(ref Particle.Default, renderState);
    }

    /// <summary>PVEC_TYPE_CLOSEST_CAMERA_POSITION. The single render camera is always the closest one.</summary>
    class ClosestCameraPositionVectorProvider : IVectorProvider
    {
        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState) => renderState.CameraPosition;
    }

    readonly struct LiteralVectorProvider : IVectorProvider
    {
        private readonly Vector3 value;

        public LiteralVectorProvider(Vector3 value)
        {
            this.value = value;
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState) => value;
    }

    // Literal Color
    readonly struct LiteralColorVectorProvider : IVectorProvider
    {
        private readonly Vector3 value;

        public LiteralColorVectorProvider(Vector3 value)
        {
            // Literal colors are authored as 0-255; providers return normalized 0-1 colors
            this.value = value / 255.0f;
        }

        public LiteralColorVectorProvider(int[] value)
        {
            this.value = new Vector3(value[0], value[1], value[2]) / 255.0f;
            // also, do linear to srgb
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState) => value;
    }

    // Per-Particle Vector
    readonly struct PerParticleVectorProvider : IVectorProvider
    {
        private readonly ParticleField vectorAttribute;
        private readonly Vector3 vectorAttributeScale = Vector3.One;
        private readonly int controlPoint = -1;
        private readonly Vector3 controlPointValueScale = Vector3.One;
        private readonly Vector3 controlPointRelativePosition = Vector3.Zero;
        private readonly Vector3 controlPointRelativeDirection = Vector3.Zero;

        public PerParticleVectorProvider(ParticleDefinitionParser parse)
        {
            vectorAttribute = parse.ParticleField("m_nVectorAttribute");
            vectorAttributeScale = parse.Vector3("m_vVectorAttributeScale", vectorAttributeScale);
            controlPoint = parse.Int32("m_nControlPoint", controlPoint);
            controlPointValueScale = parse.Vector3("m_vCPValueScale", controlPointValueScale);
            controlPointRelativePosition = parse.Vector3("m_vCPRelativePosition", controlPointRelativePosition);
            controlPointRelativeDirection = parse.Vector3("m_vCPRelativeDir", controlPointRelativeDirection);
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState)
        {
            var result = vectorAttributeScale * particle.GetVector(vectorAttribute);

            if (controlPoint < 0)
            {
                return result;
            }

            var cp = renderState.GetControlPoint(controlPoint);
            result = cp.Position * controlPointValueScale;
            result += controlPointRelativePosition;

            if (controlPointRelativeDirection != Vector3.Zero)
            {
                result += cp.Orientation != Vector3.Zero
                    ? cp.Orientation - controlPointRelativeDirection
                    : -controlPointRelativeDirection;
            }

            return result;
        }
    }

    // Particle Velocity
    readonly struct ParticleVelocityVectorProvider : IVectorProvider
    {
        // unknown if any other values are used
        public ParticleVelocityVectorProvider()
        {
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState) => particle.Velocity;
    }

    // CP Value
    readonly struct CPValueVectorProvider : IVectorProvider
    {
        private readonly int cp;
        private readonly Vector3 scale = Vector3.One;
        public CPValueVectorProvider(ParticleDefinitionParser parse)
        {
            cp = parse.Int32("m_nControlPoint");
            scale = parse.Vector3("m_vCPValueScale", scale);
        }

        public CPValueVectorProvider(int controlPoint)
        {
            cp = controlPoint;
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState)
        {
            return renderState.GetControlPoint(cp).Position * scale;
        }
    }

    // CP Delta: the difference between two control point positions.
    readonly struct CPDeltaVectorProvider : IVectorProvider
    {
        private readonly int cp;
        private readonly int deltaCP;
        public CPDeltaVectorProvider(ParticleDefinitionParser parse)
        {
            cp = parse.Int32("m_nControlPoint");
            deltaCP = parse.Int32("m_nDeltaControlPoint");
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState)
        {
            return renderState.GetControlPoint(cp).Position - renderState.GetControlPoint(deltaCP).Position;
        }
    }

    // CP Relative Position
    readonly struct CPRelativePositionProvider : IVectorProvider
    {
        private readonly int cp;
        private readonly Vector3 relativePosition = Vector3.Zero;
        public CPRelativePositionProvider(ParticleDefinitionParser parse)
        {
            cp = parse.Int32("m_nControlPoint");
            relativePosition = parse.Vector3("m_vCPRelativePosition", relativePosition);
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState)
        {
            return renderState.GetControlPoint(cp).Position + relativePosition; // this is weird as hell but its actually how it works
        }
    }

    // CP Relative Direction
    readonly struct CPRelativeDirectionProvider : IVectorProvider
    {
        private readonly int cp;
        private readonly Vector3 relativeDirection = Vector3.Zero;
        public CPRelativeDirectionProvider(ParticleDefinitionParser parse)
        {
            cp = parse.Int32("m_nControlPoint");
            relativeDirection = parse.Vector3("m_vCPRelativeDir", relativeDirection);
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState)
        {
            var cpDirection = renderState.GetControlPoint(cp).Orientation;
            if (renderState.GetControlPoint(cp).Orientation == Vector3.Zero || relativeDirection == Vector3.Zero)
            {
                return Vector3.Zero;
            }
            return cpDirection - relativeDirection;
        }
    }

    class RandomUniformVectorProvider : IVectorProvider
    {
        private readonly Vector3 min;
        private readonly Vector3 max;

        /// <summary>
        /// Whether the range is a single point. Such a range returns its endpoint and takes no draw at
        /// all, so it leaves the position of every later draw in the system unchanged.
        /// </summary>
        private readonly bool isDegenerate;

        public RandomUniformVectorProvider(ParticleDefinitionParser parse)
        {
            min = parse.Vector3("m_vRandomMin");
            max = parse.Vector3("m_vRandomMax");
            isDegenerate = min == max;
        }

        public virtual Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState)
        {
            return isDegenerate
                ? min
                : renderState.Random.NextBetweenPerComponent(min, max);
        }
    }

    class RandomUniformOffsetVectorProvider : RandomUniformVectorProvider
    {
        private readonly ParticleField vectorAttribute;
        private readonly Vector3 vectorAttributeScale = Vector3.One;

        public RandomUniformOffsetVectorProvider(ParticleDefinitionParser parse) : base(parse)
        {
            vectorAttribute = parse.ParticleField("m_nVectorAttribute");
            vectorAttributeScale = parse.Vector3("m_vVectorAttributeScale", vectorAttributeScale);
        }

        public override Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState)
        {
            var baseValue = vectorAttributeScale * particle.GetVector(vectorAttribute);
            return baseValue + base.NextVector(ref particle, renderState);
        }
    }

    // 3 Float Inputs
    readonly struct FloatComponentsVectorProvider : IVectorProvider
    {
        private readonly INumberProvider x = new LiteralNumberProvider(0);
        private readonly INumberProvider y = new LiteralNumberProvider(0);
        private readonly INumberProvider z = new LiteralNumberProvider(0);

        public FloatComponentsVectorProvider(ParticleDefinitionParser parse)
        {
            x = parse.NumberProvider("m_FloatComponentX", x);
            y = parse.NumberProvider("m_FloatComponentY", y);
            z = parse.NumberProvider("m_FloatComponentZ", z);
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState) => new(
            x.NextNumber(ref particle, renderState),
            y.NextNumber(ref particle, renderState),
            z.NextNumber(ref particle, renderState));
    }

    // Float Interp (Clamped) & Float Interp (Open)
    readonly struct FloatInterpolationVectorProvider : IVectorProvider
    {
        private readonly INumberProvider floatInterp = new LiteralNumberProvider(0);
        private readonly float input0;
        private readonly float input1;
        private readonly Vector3 output0;
        private readonly Vector3 output1;

        private readonly bool clamp;

        public FloatInterpolationVectorProvider(ParticleDefinitionParser parse, bool isClamped)
        {
            clamp = isClamped;
            floatInterp = parse.NumberProvider("m_FloatInterp", floatInterp);
            input0 = parse.Float("m_flInterpInput0");
            input1 = parse.Float("m_flInterpInput1");

            output0 = parse.Vector3("m_vInterpOutput0");
            output1 = parse.Vector3("m_vInterpOutput1");
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState)
        {
            var remappedInput = MathUtils.Remap(floatInterp.NextNumber(ref particle, renderState), input0, input1);

            if (clamp)
            {
                remappedInput = MathUtils.Saturate(remappedInput);
            }
            return Vector3.Lerp(output0, output1, remappedInput);
        }
    }

    // Color Gradient
    readonly struct ColorGradientVectorProvider : IVectorProvider
    {
        private struct GradientStop
        {
            public float Position { get; set; }
            public Vector3 Color { get; set; }
        }
        private readonly GradientStop[] gradientStops;
        private readonly INumberProvider floatInterp = new LiteralNumberProvider(0);
        private readonly float input0;
        private readonly float input1;

        public ColorGradientVectorProvider(ParticleDefinitionParser parse)
        {
            floatInterp = parse.NumberProvider("m_FloatInterp", floatInterp);
            input0 = parse.Float("m_flInterpInput0");
            input1 = parse.Float("m_flInterpInput1");

            var stops = parse.Data.GetSubCollection("m_Gradient")
                .GetArray("m_Stops");

            gradientStops = new GradientStop[stops.Count];

            for (var i = 0; i < stops.Count; i++)
            {
                var position = stops[i].GetFloatProperty("m_flPosition");
                var color = stops[i].GetArray<int>("m_Color");

                var newGradientStop = new GradientStop
                {
                    Position = position,
                    Color = new Vector3(color[0], color[1], color[2]) / 255.0f
                };
                gradientStops[i] = newGradientStop;
            }
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState)
        {
            var gradientInput = MathUtils.Remap(floatInterp.NextNumber(ref particle, renderState), input0, input1);

            if (gradientInput <= gradientStops[0].Position)
            {
                return gradientStops[0].Color;
            }
            else if (gradientInput >= gradientStops[^1].Position)
            {
                return gradientStops[^1].Color;
            }
            else
            {
                for (var i = 0; i < gradientStops.Length - 1; i++)
                {
                    var stop1 = gradientStops[i];
                    var stop2 = gradientStops[i + 1];
                    if (gradientInput >= stop1.Position && gradientInput <= stop2.Position)
                    {
                        var blend = MathUtils.Remap(gradientInput, stop1.Position, stop2.Position);
                        return Vector3.Lerp(stop1.Color, stop2.Color, blend);
                    }
                }

                throw new InvalidOperationException("Gradient error");
            }
        }
    }

    /* NOISE PARAMS
     * 		m_flNoiseOutputMin = 0.000000
			m_flNoiseOutputMax = 1.000000
			m_flNoiseScale = 0.100000
			m_vecNoiseOffsetRate =
			[
				0.000000,
				0.000000,
				0.000000,
			]
			m_flNoiseOffset = 0.000000
			m_nNoiseOctaves = 1
			m_nNoiseTurbulence = "PF_NOISE_TURB_NONE"
			m_nNoiseType = "PF_NOISE_TYPE_PERLIN"
			m_nNoiseModifier = "PF_NOISE_MODIFIER_NONE"
			m_flNoiseTurbulenceScale = 1.000000
			m_flNoiseTurbulenceMix = 0.500000
			m_flNoiseImgPreviewScale = 1.000000
			m_bNoiseImgPreviewLive = true
    */

    // Used for named value:
    // m_bFollowNamedValue

    /*ALSO NOTE
     * ParticleTransform Types
     * PT_TYPE_INVALID
     * PT_TYPE_NAMED_VALUE
     * PT_TYPE_CONTROL_POINT
     * PT_TYPE_CONTROL_POINT_RANGE
     */
}


