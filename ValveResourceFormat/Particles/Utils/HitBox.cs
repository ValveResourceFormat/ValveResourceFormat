namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// One box of a <see cref="HitBoxCache"/>: a model hitbox, or a bone, placed in the world.
    /// </summary>
    /// <param name="Mins">The box's low corner, in the space of its transform.</param>
    /// <param name="Maxs">The box's high corner, in the space of its transform.</param>
    /// <param name="Orientation">The rotation of the transform.</param>
    /// <param name="Position">The world position of the transform.</param>
    /// <param name="Scale">The uniform scale of the transform.</param>
    /// <param name="Volume">The box's volume when the cache was asked to weigh boxes by volume, 1 otherwise.</param>
    internal readonly record struct HitBox(Vector3 Mins, Vector3 Maxs, Quaternion Orientation, Vector3 Position, float Scale, float Volume)
    {
        /// <summary>Carries a point from the box's space into the world.</summary>
        public Vector3 TransformPoint(Vector3 local) => (Vector3.Transform(local, Orientation) * Scale) + Position;

        /// <summary>
        /// The world position of the point at <paramref name="fraction"/> of the box's extent per axis,
        /// from its low corner at 0 to its high corner at 1.
        /// </summary>
        public Vector3 PointAt(Vector3 fraction) => TransformPoint(((Maxs - Mins) * fraction) + Mins);

        /// <summary>
        /// The low and high fractions of a box's extent, per axis, that bound the part of it
        /// <paramref name="scale"/> covers, centred on the box.
        /// </summary>
        public static (Vector3 Low, Vector3 High) ScaledFractions(Vector3 scale)
        {
            var half = scale * 0.5f;
            return (new Vector3(0.5f) - half, new Vector3(0.5f) + half);
        }

        /// <summary>Carries a world point into the box's space.</summary>
        public Vector3 InverseTransformPoint(Vector3 world)
            => Vector3.Transform((world - Position) / Scale, Quaternion.Conjugate(Orientation));

        /// <summary>The transform as a matrix, scale included.</summary>
        public Matrix4x4 Matrix
        {
            get
            {
                var matrix = Matrix4x4.CreateFromQuaternion(Orientation) * Scale;
                matrix.M44 = 1f;
                matrix.Translation = Position;
                return matrix;
            }
        }

        /// <summary>Splits a transform into the rotation, position and largest axis scale a box keeps.</summary>
        public static HitBox FromTransform(Matrix4x4 transform, Vector3 mins, Vector3 maxs, float volume)
        {
            var scale = transform.MaxAxisScale();
            var rotation = transform;
            rotation.Translation = Vector3.Zero;

            if (scale > 0f)
            {
                rotation = NormalizeRows(rotation);
            }

            var orientation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotation));
            return new HitBox(mins, maxs, orientation, transform.Translation, scale, volume);
        }

        private static Matrix4x4 NormalizeRows(Matrix4x4 m)
        {
            var x = MathUtils.SafeNormalize(m.GetRow(0).AsVector3());
            var y = MathUtils.SafeNormalize(m.GetRow(1).AsVector3());
            var z = MathUtils.SafeNormalize(m.GetRow(2).AsVector3());

            return new Matrix4x4(
                x.X, x.Y, x.Z, 0f,
                y.X, y.Y, y.Z, 0f,
                z.X, z.Y, z.Z, 0f,
                0f, 0f, 0f, 1f);
        }
    }
}
