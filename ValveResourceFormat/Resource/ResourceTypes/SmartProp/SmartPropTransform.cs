namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// Transform helpers for smart prop evaluation. Matrices follow the repo wide Source 2
    /// convention: they are row-vector transforms stored directly in <see cref="Matrix4x4"/>,
    /// so a rotation's rows are the forward, left and up basis vectors and the translation
    /// is the bottom row (M41, M42, M43). Child transforms compose into world space as
    /// local * parent.
    /// </summary>
    public static class SmartPropTransform
    {
        /// <summary>
        /// Builds a frame matrix for a position and forward tangent. The up reference
        /// picks the roll: left = up x forward, and up is re-orthogonalized as
        /// forward x left. Nearly collinear forward/up pairs fall back to a stable
        /// alternative up so the frame never degenerates.
        /// </summary>
        public static Matrix4x4 CreateFrame(Vector3 position, Vector3 forward, Vector3? up = null)
        {
            var f = forward.LengthSquared() > 1e-14f ? Vector3.Normalize(forward) : Vector3.UnitX;

            var u = up is { } upValue
                ? (upValue.LengthSquared() > 1e-14f ? Vector3.Normalize(upValue) : Vector3.UnitZ)
                : Vector3.UnitZ;

            if (MathF.Abs(Vector3.Dot(f, u)) > 0.999f)
            {
                u = MathF.Abs(f.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            }

            var l = Vector3.Cross(u, f);
            l = l.LengthSquared() > 1e-14f ? Vector3.Normalize(l) : Vector3.UnitY;

            var uOrtho = Vector3.Cross(f, l);
            uOrtho = uOrtho.LengthSquared() > 1e-14f ? Vector3.Normalize(uOrtho) : Vector3.UnitZ;

            return new Matrix4x4(
                f.X, f.Y, f.Z, 0f,
                l.X, l.Y, l.Z, 0f,
                uOrtho.X, uOrtho.Y, uOrtho.Z, 0f,
                position.X, position.Y, position.Z, 1f);
        }

        /// <summary>
        /// Decomposes a row-vector TRS matrix into position, Euler angles in degrees
        /// (pitch, yaw, roll) and per-axis scale. Yaw and roll are wrapped to 0..360.
        /// Handles the gimbal lock cases where pitch is near +/-90 degrees.
        /// </summary>
        public static (Vector3 Position, Vector3 PitchYawRoll, Vector3 Scale) DecomposeTRS(Matrix4x4 matrix)
        {
            var position = new Vector3(matrix.M41, matrix.M42, matrix.M43);

            var sx = RowLength(matrix, 0);
            var sy = RowLength(matrix, 1);
            var sz = RowLength(matrix, 2);
            var scale = new Vector3(sx, sy, sz);

            // Row i of the rotation is the i-th basis vector, normalized by its scale
            var r00 = matrix.M11 / (sx > 1e-8f ? sx : 1f);
            var r01 = matrix.M12 / (sx > 1e-8f ? sx : 1f);
            var r02 = matrix.M13 / (sx > 1e-8f ? sx : 1f);
            var r10 = matrix.M21 / (sy > 1e-8f ? sy : 1f);
            var r11 = matrix.M22 / (sy > 1e-8f ? sy : 1f);
            var r12 = matrix.M23 / (sy > 1e-8f ? sy : 1f);
            var r22 = matrix.M33 / (sz > 1e-8f ? sz : 1f);

            var sinPitch = Math.Clamp(-r02, -1f, 1f);
            var pitch = MathF.Asin(sinPitch);

            float yaw;
            float roll;
            if (MathF.Abs(MathF.Cos(pitch)) > 1e-5f)
            {
                yaw = MathF.Atan2(r01, r00);
                roll = MathF.Atan2(r12, r22);
            }
            else
            {
                // Gimbal lock: yaw is absorbed into roll
                yaw = 0f;
                roll = sinPitch < 0f ? MathF.Atan2(-r10, r11) : MathF.Atan2(r10, r11);
            }

            var rotation = new Vector3(
                float.RadiansToDegrees(pitch),
                float.RadiansToDegrees(yaw) % 360f,
                float.RadiansToDegrees(roll) % 360f);

            return (position, rotation, scale);
        }

        /// <summary>
        /// Applies a path offset to a frame matrix. World space offsets shift the
        /// translation along the world axes; local space offsets shift along the frame's
        /// left axis by X and up axis by Y.
        /// </summary>
        public static Matrix4x4 ApplyPathOffset(Matrix4x4 matrix, Vector3 pathOffset, bool worldSpace)
        {
            if (pathOffset.LengthSquared() < 1e-12f)
            {
                return matrix;
            }

            if (worldSpace)
            {
                matrix.M41 += pathOffset.X;
                matrix.M42 += pathOffset.Y;
                matrix.M43 += pathOffset.Z;
                return matrix;
            }

            var left = new Vector3(matrix.M21, matrix.M22, matrix.M23);
            var up = new Vector3(matrix.M31, matrix.M32, matrix.M33);
            var shift = (left * pathOffset.X) + (up * pathOffset.Y);

            matrix.M41 += shift.X;
            matrix.M42 += shift.Y;
            matrix.M43 += shift.Z;
            return matrix;
        }

        /// <summary>
        /// Transforms a point by a row-vector matrix: point * matrix. Equivalent to
        /// <see cref="Vector3.Transform(Vector3, Matrix4x4)"/>, spelled out because the
        /// row-vector storage convention makes that equivalence non-obvious.
        /// </summary>
        public static Vector3 TransformPoint(Matrix4x4 matrix, Vector3 point) => new(
            (point.X * matrix.M11) + (point.Y * matrix.M21) + (point.Z * matrix.M31) + matrix.M41,
            (point.X * matrix.M12) + (point.Y * matrix.M22) + (point.Z * matrix.M32) + matrix.M42,
            (point.X * matrix.M13) + (point.Y * matrix.M23) + (point.Z * matrix.M33) + matrix.M43);

        private static float RowLength(Matrix4x4 matrix, int row) => row switch
        {
            0 => MathF.Sqrt((matrix.M11 * matrix.M11) + (matrix.M12 * matrix.M12) + (matrix.M13 * matrix.M13)),
            1 => MathF.Sqrt((matrix.M21 * matrix.M21) + (matrix.M22 * matrix.M22) + (matrix.M23 * matrix.M23)),
            _ => MathF.Sqrt((matrix.M31 * matrix.M31) + (matrix.M32 * matrix.M32) + (matrix.M33 * matrix.M33)),
        };
    }
}
