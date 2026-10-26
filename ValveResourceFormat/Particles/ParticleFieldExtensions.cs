using System.Numerics.Tensors;

namespace ValveResourceFormat.Particles
{
    static class ParticleFieldExtensions
    {
        public static float GetComponent(this Vector3 vector, int component)
        {
            component = Math.Clamp(component, 0, 2);

            return component switch
            {
                0 => vector.X,
                1 => vector.Y,
                2 => vector.Z,
                _ => vector.Z,
            };
        }

        /// <summary>Whether <paramref name="setMethod"/> combines the value with the particle's initial value.</summary>
        public static bool ReadsInitialValue(this ParticleSetMethod setMethod)
            => setMethod is ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE or ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE;

        /// <summary>
        /// Combines <paramref name="value"/> with a particle's initial and current values as
        /// <paramref name="setMethod"/> says. A ramp adds the value as a rate over <paramref name="deltaTime"/>.
        /// </summary>
        public static float ApplySetMethod(float value, float initialValue, float currentValue, float deltaTime, ParticleSetMethod setMethod)
            => setMethod switch
            {
                ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE => value,
                ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE => value * initialValue,
                ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE => value + initialValue,
                ParticleSetMethod.PARTICLE_SET_SCALE_CURRENT_VALUE => value * currentValue,
                ParticleSetMethod.PARTICLE_SET_ADD_TO_CURRENT_VALUE => value + currentValue,
                ParticleSetMethod.PARTICLE_SET_RAMP_CURRENT_VALUE => currentValue + (value * deltaTime),
                _ => throw new NotImplementedException($"Unknown particle set type {Enum.GetName(setMethod)}!"),
            };

        /// <inheritdoc cref="ApplySetMethod(float, float, float, float, ParticleSetMethod)"/>
        public static Vector3 ApplySetMethod(Vector3 value, Vector3 initialValue, Vector3 currentValue, float deltaTime, ParticleSetMethod setMethod)
            => setMethod switch
            {
                ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE => value,
                ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE => value * initialValue,
                ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE => value + initialValue,
                ParticleSetMethod.PARTICLE_SET_SCALE_CURRENT_VALUE => value * currentValue,
                ParticleSetMethod.PARTICLE_SET_ADD_TO_CURRENT_VALUE => value + currentValue,
                // new in DeskJob
                ParticleSetMethod.PARTICLE_SET_RAMP_CURRENT_VALUE => currentValue + (value * deltaTime),
                _ => throw new NotImplementedException($"Unknown particle set type {Enum.GetName(setMethod)}!"),
            };

        /// <summary>
        /// Combines every value in <paramref name="values"/> in place, as
        /// <see cref="ApplySetMethod(float, float, float, float, ParticleSetMethod)"/> does.
        /// </summary>
        public static void ApplySetMethod(Span<float> values, ReadOnlySpan<float> initialValues, ReadOnlySpan<float> currentValues, float deltaTime, ParticleSetMethod setMethod)
        {
            switch (setMethod)
            {
                case ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE:
                    break;
                case ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE:
                    TensorPrimitives.Multiply(values, initialValues, values);
                    break;
                case ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE:
                    TensorPrimitives.Add(values, initialValues, values);
                    break;
                case ParticleSetMethod.PARTICLE_SET_SCALE_CURRENT_VALUE:
                    TensorPrimitives.Multiply(values, currentValues, values);
                    break;
                case ParticleSetMethod.PARTICLE_SET_ADD_TO_CURRENT_VALUE:
                    TensorPrimitives.Add(values, currentValues, values);
                    break;
                case ParticleSetMethod.PARTICLE_SET_RAMP_CURRENT_VALUE:
                    TensorPrimitives.MultiplyAdd(values, deltaTime, currentValues, values);
                    break;
                default:
                    throw new NotImplementedException($"Unknown particle set type {Enum.GetName(setMethod)}!");
            }
        }

        /// <summary>
        /// Whether the field holds an angle, and so is stored in radians while content authors it in
        /// degrees. <see cref="ParticleField.RollSpeed"/> counts: an angular rate is authored in degrees
        /// per second.
        /// </summary>
        public static bool IsAngleField(this ParticleField field)
            => field is ParticleField.Roll or ParticleField.Yaw or ParticleField.Pitch or ParticleField.RollSpeed;

        /// <summary>
        /// Whether the field holds an orientation angle, so is stored in radians while content authors
        /// it in degrees. Narrower than <see cref="IsAngleField"/>: the angular rate is excluded.
        /// </summary>
        public static bool IsOrientationField(this ParticleField field)
            => field is ParticleField.Roll or ParticleField.Yaw or ParticleField.Pitch;

        /// <summary>
        /// Whether the field holds an integer. Functions read and write it as the bits of a float; only
        /// inputs convert it to its value.
        /// </summary>
        public static bool IsIntegerAttribute(this ParticleField field) => ParticleBuffer.StorageOf(field) == ParticleFieldStorage.Integer;

        /// <summary>The bit of the field in a <see cref="ParticleField"/> bit mask.</summary>
        public static ulong Mask(this ParticleField field) => 1UL << (int)field;

        /// <summary>
        /// The range the float set operators hold a written value inside. Alpha is bounded to [0, 1],
        /// radius and trail length to the non-negative half, and every other field is left free.
        /// </summary>
        public static (float Min, float Max) SetFloatRange(this ParticleField field)
            => field switch
            {
                ParticleField.Alpha or ParticleField.AlphaAlternate => (0f, 1f),
                ParticleField.Radius or ParticleField.TrailLength => (0f, float.MaxValue),
                _ => (float.MinValue, float.MaxValue),
            };

        /// <summary>
        /// Whether the field is normalized to [0, 1], which the operators that write it clamp into.
        /// </summary>
        public static bool IsNormalizedField(this ParticleField field)
            => field is ParticleField.Color or ParticleField.Alpha or ParticleField.AlphaAlternate
                or ParticleField.GlowRgb or ParticleField.GlowAlpha;

        /// <summary>
        /// Whether the field is <see cref="ParticleField.Alpha"/> or <see cref="ParticleField.AlphaAlternate"/>.
        /// </summary>
        public static bool IsAlpha(this ParticleField field)
            => field is ParticleField.Alpha or ParticleField.AlphaAlternate;
    }
}
