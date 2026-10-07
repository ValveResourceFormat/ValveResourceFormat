namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// A Source 2 <c>CTransform</c>: position, uniform scale and rotation. A point maps as
    /// <c>Position + rotate(Rotation, p) * Scale</c>.
    /// </summary>
    /// <param name="Position">Translation.</param>
    /// <param name="Scale">Uniform scale.</param>
    /// <param name="Rotation">Rotation.</param>
    public readonly record struct SmartPropTransform(Vector3 Position, float Scale, Quaternion Rotation)
    {
        /// <summary>The identity transform.</summary>
        public static SmartPropTransform Identity => new(Vector3.Zero, 1f, Quaternion.Identity);

        /// <summary>Maps a point from this transform's local space.</summary>
        /// <param name="point">Local point.</param>
        /// <returns>The transformed point.</returns>
        public Vector3 TransformPoint(Vector3 point) => Position + Vector3.Transform(point, Rotation) * Scale;

        /// <summary>Maps a point into this transform's local space.</summary>
        /// <param name="point">Point in the parent space.</param>
        /// <returns>The local point.</returns>
        public Vector3 InverseTransformPoint(Vector3 point) => Vector3.Transform((point - Position) / Scale, Quaternion.Conjugate(Rotation));

        /// <summary>
        /// Concatenates <paramref name="child"/> onto this transform: the child is expressed in this transform's frame.
        /// </summary>
        /// <param name="child">Local transform.</param>
        /// <returns>The combined transform.</returns>
        public SmartPropTransform Concat(SmartPropTransform child)
        {
            var rotation = Rotation * child.Rotation;
            var length = rotation.Length();
            rotation = length > 0f ? Quaternion.Multiply(rotation, 1f / length) : Quaternion.Identity;

            return new(TransformPoint(child.Position), Scale * child.Scale, rotation);
        }

        /// <summary>The inverse transform.</summary>
        /// <returns>The inverse.</returns>
        public SmartPropTransform Invert()
        {
            var rotation = Quaternion.Conjugate(Rotation);
            var scale = 1f / Scale;
            return new(Vector3.Transform(-Position, rotation) * scale, scale, rotation);
        }

        /// <summary>Builds a row-vector matrix: scale, then rotation, then translation.</summary>
        /// <returns>The matrix.</returns>
        public Matrix4x4 ToMatrix() => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

        /// <summary>
        /// Builds a transform from basis vectors: the scale is the longest basis vector, the rotation comes from the normalized basis.
        /// </summary>
        /// <param name="forward">X axis.</param>
        /// <param name="left">Y axis.</param>
        /// <param name="up">Z axis.</param>
        /// <param name="position">Origin.</param>
        /// <returns>The transform.</returns>
        public static SmartPropTransform FromBasis(Vector3 forward, Vector3 left, Vector3 up, Vector3 position)
        {
            var scale = MathF.Max(forward.Length(), MathF.Max(left.Length(), up.Length()));
            return new(position, scale, SmartPropMath.QuaternionFromBasis(MathUtils.SafeNormalize(forward), MathUtils.SafeNormalize(left), MathUtils.SafeNormalize(up)));
        }
    }
}
