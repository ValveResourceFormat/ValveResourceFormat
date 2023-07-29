using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    interface IVectorProvider
    {
        Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState);

        Vector3 NextVector(ParticleSystemRenderState renderState)
            => NextVector(ref Particle.Default, renderState);
    }

    readonly struct LiteralVectorProvider : IVectorProvider
    {
        private readonly Vector3 value;

        public LiteralVectorProvider(Vector3 value)
        {
            this.value = value;
        }

        public LiteralVectorProvider(float[] value)
        {
            this.value = value.ToVector3();
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState) => value;
    }

    // Literal Color
    readonly struct LiteralColorVectorProvider : IVectorProvider
    {
        private readonly Vector3 value;

        public LiteralColorVectorProvider(Vector3 value)
        {
            this.value = value;
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
        private readonly ParticleField field;
        private readonly Vector3 scale = Vector3.One;
        public PerParticleVectorProvider(IKeyValueCollection keyValues)
        {
            field = (ParticleField)keyValues.GetIntegerProperty("m_nVectorAttribute");
            if (keyValues.ContainsKey("m_vVectorAttributeScale"))
            {
                scale = keyValues.GetArray<double>("m_vVectorAttributeScale").ToVector3();
            }
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState) => scale * particle.GetVector(field);
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
        public CPValueVectorProvider(IKeyValueCollection keyValues)
        {
            cp = keyValues.GetInt32Property("m_nControlPoint");
            if (keyValues.ContainsKey("m_vCPValueScale"))
            {
                scale = keyValues.GetArray<double>("m_vCPValueScale").ToVector3();
            }
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState)
        {
            return renderState.GetControlPoint(cp).Position * scale;
        }
    }

    // CP Relative Position
    readonly struct CPRelativePositionProvider : IVectorProvider
    {
        private readonly int cp;
        private readonly Vector3 relativePosition = Vector3.Zero;
        public CPRelativePositionProvider(IKeyValueCollection keyValues)
        {
            cp = keyValues.GetInt32Property("m_nControlPoint");
            if (keyValues.ContainsKey("m_vCPRelativePosition"))
            {
                relativePosition = keyValues.GetArray<double>("m_vCPRelativePosition").ToVector3();
            }
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
        public CPRelativeDirectionProvider(IKeyValueCollection keyValues)
        {
            cp = keyValues.GetInt32Property("m_nControlPoint");
            if (keyValues.ContainsKey("m_vCPRelativeDir"))
            {
                relativeDirection = keyValues.GetArray<double>("m_vCPRelativeDir").ToVector3();
            }
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

    // 3 Float Inputs
    readonly struct FloatComponentsVectorProvider : IVectorProvider
    {
        private readonly INumberProvider X;
        private readonly INumberProvider Y;
        private readonly INumberProvider Z;

        public FloatComponentsVectorProvider(IKeyValueCollection keyValues)
        {
            X = keyValues.GetNumberProvider("m_FloatComponentX");
            Y = keyValues.GetNumberProvider("m_FloatComponentY");
            Z = keyValues.GetNumberProvider("m_FloatComponentZ");
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState) => new(
            X.NextNumber(ref particle, renderState),
            Y.NextNumber(ref particle, renderState),
            Z.NextNumber(ref particle, renderState));
    }

    // Float Interp (Clamped) & Float Interp (Open)
    readonly struct FloatInterpolationVectorProvider : IVectorProvider
    {
        private readonly INumberProvider floatInterp;
        private readonly float input0;
        private readonly float input1;
        private readonly Vector3 output0;
        private readonly Vector3 output1;

        private readonly bool clamp;

        public FloatInterpolationVectorProvider(IKeyValueCollection keyValues, bool isClamped)
        {
            clamp = isClamped;
            floatInterp = keyValues.GetNumberProvider("m_FloatInterp");
            input0 = keyValues.GetFloatProperty("m_flInterpInput0");
            input1 = keyValues.GetFloatProperty("m_flInterpInput1");

            output0 = keyValues.GetArray<double>("m_vInterpOutput0").ToVector3();
            output1 = keyValues.GetArray<double>("m_vInterpOutput1").ToVector3();
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemRenderState renderState)
        {
            var remappedInput = MathUtils.Remap(floatInterp.NextNumber(ref particle, renderState), input0, input1);

            if (clamp)
            {
                remappedInput = MathUtils.Saturate(remappedInput);
            }
            return MathUtils.Lerp(remappedInput, output0, output1);
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
        private readonly INumberProvider floatInterp;
        private readonly float input0;
        private readonly float input1;

        public ColorGradientVectorProvider(IKeyValueCollection keyValues)
        {
            floatInterp = keyValues.GetNumberProvider("m_FloatInterp");
            input0 = keyValues.GetFloatProperty("m_flInterpInput0");
            input1 = keyValues.GetFloatProperty("m_flInterpInput1");

            var stops = keyValues.GetSubCollection("m_Gradient")
                .GetArray("m_Stops");

            gradientStops = new GradientStop[stops.Length];

            for (var i = 0; i < stops.Length; i++)
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
                        return MathUtils.Lerp(blend, stop1.Color, stop2.Color);
                    }
                }
                throw new IndexOutOfRangeException("gradient error wtf lol???");
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

    static class IVectorProviderExtensions
    {
        public static IVectorProvider GetVectorProvider(this IKeyValueCollection keyValues, string propertyName)
        {
            var property = keyValues.GetProperty<object>(propertyName);

            if (property is IKeyValueCollection numberProviderParameters && numberProviderParameters.ContainsKey("m_nType"))
            {
                var type = numberProviderParameters.GetProperty<string>("m_nType");
                switch (type)
                {
                    case "PVEC_TYPE_LITERAL":
                        return new LiteralVectorProvider(numberProviderParameters.GetFloatArray("m_vLiteralValue"));
                    case "PVEC_TYPE_LITERAL_COLOR":
                        return new LiteralColorVectorProvider(numberProviderParameters.GetArray<int>("m_LiteralColor"));
                    case "PVEC_TYPE_PARTICLE_VECTOR":
                        return new PerParticleVectorProvider(numberProviderParameters);
                    case "PVEC_TYPE_PARTICLE_VELOCITY":
                        return new ParticleVelocityVectorProvider();
                    case "PVEC_TYPE_CP_VALUE":
                        return new CPValueVectorProvider(numberProviderParameters);
                    case "PVEC_TYPE_CP_RELATIVE_POSITION":
                        return new CPRelativePositionProvider(numberProviderParameters);
                    case "PVEC_TYPE_CP_RELATIVE_DIR":
                        return new CPRelativeDirectionProvider(numberProviderParameters);
                    case "PVEC_TYPE_FLOAT_COMPONENTS":
                        return new FloatComponentsVectorProvider(numberProviderParameters);
                    case "PVEC_TYPE_FLOAT_INTERP_CLAMPED":
                        return new FloatInterpolationVectorProvider(numberProviderParameters, true);
                    case "PVEC_TYPE_FLOAT_INTERP_OPEN":
                        return new FloatInterpolationVectorProvider(numberProviderParameters, false);
                    case "PVEC_TYPE_FLOAT_INTERP_GRADIENT":
                        return new ColorGradientVectorProvider(numberProviderParameters);
                    /* UNSUPPORTED:
                     * PVEC_TYPE_NAMED_VALUE - new in dota
                     * PVEC_TYPE_PARTICLE_VELOCITY - new in dota
                     * PVEC_TYPE_CP_RELATIVE_RANDOM_DIR - new in dota. presumably relative dir but the value is random per particle?
                     * PVEC_TYPE_RANDOM_UNIFORM - new in dota. uses vRandomMin and vRandomMax
                     * PVEC_TYPE_RANDOM_UNIFORM_OFFSET - new in dota
                     */
                    default:
                        if (numberProviderParameters.ContainsKey("m_vLiteralValue"))
                        {
                            Console.Error.WriteLine($"Vector provider of type {type} is not directly supported, but it has m_vLiteralValue.");
                            return new LiteralVectorProvider(numberProviderParameters.GetFloatArray("m_vLiteralValue"));
                        }

                        throw new InvalidCastException($"Could not create vector provider of type {type}.");
                }
            }

            return new LiteralVectorProvider(keyValues.GetFloatArray(propertyName));
        }
    }
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


