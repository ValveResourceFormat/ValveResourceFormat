namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents the transform of a bone in a single animation frame.
    /// </summary>
    public record struct FrameBone
    {
        /// <summary>
        /// Gets or sets the position of the bone.
        /// </summary>
        public Vector3 Position { get; set; }

        /// <summary>
        /// Gets or sets the scale of the bone.
        /// </summary>
        public float Scale { get; set; }

        /// <summary>
        /// Gets or sets the rotation of the bone.
        /// </summary>
        public Quaternion Angle { get; set; }

        /// <summary>
        /// Initializes with a position, scale, and rotation.
        /// </summary>
        public FrameBone(Vector3 position, float scale, Quaternion rotation)
        {
            Position = position;
            Scale = scale;
            Angle = rotation;
        }

        /// <summary>
        /// Initializes with a combined position+scale, and rotation.
        /// </summary>
        public FrameBone(Vector4 positionScale, Quaternion rotation)
        {
            Position = positionScale.AsVector3();
            Scale = positionScale.W;
            Angle = rotation;
        }

        /// <summary>
        /// The identity bone transform.
        /// </summary>
        public static FrameBone Identity => new(Vector3.Zero, 1.0f, Quaternion.Identity);

        /// <summary>
        /// Gets the position and scale of this bone combined into a <see cref="Vector4"/> .
        /// </summary>
        public readonly Vector4 PositionScale => new(Position, Scale);

        /// <summary>
        /// Gets the scale of this bone as a <see cref="Vector3"/> .
        /// </summary>
        public readonly Vector3 ScaleVector => new(Scale);

        /// <summary>
        /// Reads a transform with uniform scale from a matrix.
        /// </summary>
        public static FrameBone FromMatrix(in Matrix4x4 matrix)
        {
            Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation);
            return new(translation, scale.X, rotation);
        }

        /// <summary>Converts to a matrix.</summary>
        public readonly Matrix4x4 ToMatrix()
        {
            var matrix = Matrix4x4.CreateFromQuaternion(Angle);
            if (Scale != 1f)
            {
                matrix *= Matrix4x4.CreateScale(Scale);
            }

            matrix.Translation = Position;
            return matrix;
        }

        /// <summary>
        /// Composes a transform expressed in this one's space onto it, flipping the local rotation's sign
        /// to our hemisphere.
        /// </summary>
        public readonly FrameBone Concat(FrameBone local)
        {
            var angle = Quaternion.Dot(Angle, local.Angle) < 0f ? -local.Angle : local.Angle;
            return new(TransformPoint(local.Position), Scale * local.Scale, Angle * angle);
        }

        /// <summary>Gets the inverse transform.</summary>
        public readonly FrameBone Inverse()
        {
            var angle = Quaternion.Inverse(Angle);
            var scale = 1f / Scale;
            return new(Vector3.Transform(-Position, angle) * scale, scale, angle);
        }

        /// <summary>Transforms a point by this transform.</summary>
        public readonly Vector3 TransformPoint(Vector3 point) => Position + Vector3.Transform(point * Scale, Angle);

        /// <summary>
        /// Blends to the target transform normally.
        /// </summary>
        public readonly FrameBone Blend(FrameBone target, float t)
        {
            var positionScale = Vector4.Lerp(PositionScale, target.PositionScale, t);
            var angle = Quaternion.Slerp(Angle, target.Angle, t);

            return new(positionScale, angle);
        }

        /// <summary>
        /// Blends to the target transform additively.
        /// </summary>
        public readonly FrameBone BlendAdd(FrameBone other, float t)
        {
            var positionScale = Vector4.FusedMultiplyAdd(other.PositionScale, new Vector4(t), PositionScale);
            var targetAngle = other.Angle * Angle;
            var angle = Quaternion.Slerp(Angle, targetAngle, t);
            return new(positionScale, angle);
        }
    }
}
