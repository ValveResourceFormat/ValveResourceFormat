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

        /// <inheritdoc/>
        public readonly Vector4 PositionScale => new(Position, Scale);

        /// <inheritdoc/>
        public readonly Vector3 ScaleVector => new(Scale);


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
        ///Blends to the target transform additively.
        /// </summary>
        public readonly FrameBone BlendAdd(FrameBone other, float t)
        {
            var positionScale = Vector4.FusedMultiplyAdd(other.PositionScale, new Vector4(t), PositionScale);
            var targetAngle = other.Angle * Angle;
            var angle = Quaternion.Slerp(Angle, targetAngle, t);
            return new(positionScale, angle);
        }

        /// <summary>
        /// Combines two transforms.
        /// </summary>
        public static FrameBone operator *(FrameBone lhs, FrameBone rhs)
        {
            // todo: multiply without matrix

            var rhsMatrix = rhs.ToMatrix();
            var lhsMatrix = lhs.ToMatrix();
            var resultMatrix = lhsMatrix * rhsMatrix;

            // Decompose to extract components
            Matrix4x4.Decompose(resultMatrix, out var scale, out var rotation, out var translation);

            return new FrameBone(translation, scale.X, Quaternion.Normalize(rotation));
        }

        /// <summary>
        /// Combines two transforms.
        /// </summary>
        public static FrameBone Multiply(FrameBone left, FrameBone right)
        {
            return left * right;
        }

        /// <summary>
        /// Converts the transform to a matrix.
        /// </summary>
        public readonly Matrix4x4 ToMatrix()
        {
            var scaleMatrix = Matrix4x4.CreateScale(Scale);
            var rotationMatrix = Matrix4x4.CreateFromQuaternion(Angle);
            var translationMatrix = Matrix4x4.CreateTranslation(Position);

            return scaleMatrix * rotationMatrix * translationMatrix;
        }

        /// <summary>
        /// Converts the transform to a matrix.
        /// </summary>
        public static FrameBone FromMatrix(Matrix4x4 matrix)
        {
            Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation);
            return new FrameBone(translation, scale.X, Quaternion.Normalize(rotation));
        }
    }
}
