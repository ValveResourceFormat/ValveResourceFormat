using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents root motion movement data for an animation.
    /// </summary>
    public class AnimationMovement
    {
        /// <summary>
        /// Represents interpolated movement data containing position and angle.
        /// </summary>
        public readonly struct MovementData
        {
            /// <summary>
            /// Gets the position offset.
            /// </summary>
            public Vector3 Position { get; }

            /// <summary>
            /// Gets the angle offset in degrees.
            /// </summary>
            public float Angle { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="MovementData"/> struct.
            /// </summary>
            public MovementData(Vector3 position, float angle)
            {
                Position = position;
                Angle = angle;
            }
        }

        /// <summary>
        /// Gets the ending frame for this movement.
        /// </summary>
        public int EndFrame { get; }

        /// <summary>
        /// Gets the motion flags for this movement.
        /// </summary>
        public ModelAnimationMotionFlags MotionFlags { get; }

        /// <summary>
        /// Gets the first motion scalar value (v0) for this segment.
        /// </summary>
        public float V0 { get; }

        /// <summary>
        /// Gets the second motion scalar value (v1) for this segment.
        /// </summary>
        public float V1 { get; }

        /// <summary>
        /// Gets the rotation angle in degrees.
        /// </summary>
        public float Angle { get; }

        /// <summary>
        /// Gets the motion vector parameter stored for this segment.
        /// </summary>
        public Vector3 Vector { get; }

        /// <summary>
        /// Gets the translation offset parameter stored for this segment.
        /// </summary>
        public Vector3 Position { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationMovement"/> class.
        /// </summary>
        public AnimationMovement(KVObject frameBlock)
        {
            EndFrame = frameBlock.GetInt32Property("endframe");
            MotionFlags = (ModelAnimationMotionFlags)frameBlock.GetInt32Property("motionflags");
            V0 = frameBlock.GetInt32Property("v0");
            V1 = frameBlock.GetInt32Property("v1");
            Angle = frameBlock.GetFloatProperty("angle");
            Vector = new Vector3(frameBlock.GetFloatArray("vector"));
            Position = new Vector3(frameBlock.GetFloatArray("position"));
        }

        /// <summary>
        /// Interpolates linearly between two movement states using <paramref name="t"/> in the range [0, 1].
        /// </summary>
        public static MovementData Lerp(AnimationMovement a, AnimationMovement b, float t)
        {
            if (a == null && b == null)
            {
                return new();
            }

            if (a == null)
            {
                return Lerp(Vector3.Zero, 0, b.Position, b.Angle, t);
            }
            else if (b == null)
            {
                return Lerp(a.Position, a.Angle, Vector3.Zero, 0f, t);
            }
            else
            {
                return Lerp(a.Position, a.Angle, b.Position, b.Angle, t);
            }
        }

        private static MovementData Lerp(Vector3 aPos, float aAngle, Vector3 bPos, float bAngle, float t)
        {
            var position = Vector3.Lerp(aPos, bPos, t);
            var angle = float.Lerp(aAngle, bAngle, t);

            return new MovementData(position, angle);
        }
    }
}
