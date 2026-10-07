namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    internal static class SmartPropMath
    {
        public const float Epsilon = 1.1920929e-7f;

        public static Quaternion AngleQuaternion(Vector3 angles) => EntityTransformHelper.EulerAnglesToQuaternion(angles);

        public static Vector3 QuaternionAngles(Quaternion rotation) => EntityTransformHelper.ToEulerAngles(rotation);

        public static Quaternion QuaternionFromBasis(Vector3 forward, Vector3 left, Vector3 up)
        {
            var matrix = new Matrix4x4(
                forward.X, forward.Y, forward.Z, 0f,
                left.X, left.Y, left.Z, 0f,
                up.X, up.Y, up.Z, 0f,
                0f, 0f, 0f, 1f);

            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix));
        }

        public static Vector3 OrthogonalFallback(Vector3 k)
        {
            var w = MathUtils.SafeNormalize(new Vector3((1f - k.Z) * k.Y * k.Y + k.Z, 0f, -k.X));
            return MathUtils.SafeNormalize(w - k * Vector3.Dot(k, w));
        }

        /// <summary>
        /// Normalized component of <paramref name="v"/> perpendicular to <paramref name="n"/>, or an
        /// arbitrary perpendicular of <paramref name="n"/> when that is degenerate.
        /// </summary>
        public static Vector3 PerpendicularComponent(Vector3 v, Vector3 n)
        {
            var perpendicular = v - n * Vector3.Dot(v, n);
            var length = perpendicular.Length();
            return length < Epsilon ? OrthogonalFallback(n) : perpendicular / length;
        }

        /// <summary>
        /// Rotation with +X along <paramref name="forward"/> and +Z along <paramref name="up"/>; the
        /// prioritized vector is kept and the other made orthogonal to it.
        /// </summary>
        public static Quaternion OrientationFromForwardUp(Vector3 forward, Vector3 up, bool prioritizeUp)
        {
            if (prioritizeUp)
            {
                up = MathUtils.SafeNormalize(up);
                forward = PerpendicularComponent(forward, up);
            }
            else
            {
                forward = MathUtils.SafeNormalize(forward);
                up = PerpendicularComponent(up, forward);
            }

            return QuaternionFromBasis(forward, Vector3.Cross(up, forward), up);
        }

        public static Quaternion QuaternionFromTwoVectors(Vector3 from, Vector3 to)
        {
            var half = (from + to) * 0.5f;

            if (half.LengthSquared() <= Epsilon)
            {
                var axis = OrthogonalFallback(from);
                return new Quaternion(axis, 0f);
            }

            return Quaternion.Normalize(new Quaternion(Vector3.Cross(from, half), Vector3.Dot(half, from)));
        }

        /// <summary>
        /// Rotation with +X along <paramref name="direction"/> and the roll fixed by <paramref name="up"/>.
        /// </summary>
        public static Quaternion QuaternionFromForwardUp(Vector3 direction, Vector3 up)
        {
            var forward = MathUtils.SafeNormalize(direction, Vector3.UnitX);
            var cross = Vector3.Cross(forward, up);
            var length = cross.Length();

            if (length < 1e-6f)
            {
                return QuaternionFromTwoVectors(Vector3.UnitX, forward);
            }

            var right = cross / length;

            if (length < 0.001f)
            {
                right = MathUtils.SafeNormalize(right - up * Vector3.Dot(right, up));
            }

            return QuaternionFromBasis(forward, -right, Vector3.Cross(right, forward));
        }

        public static Quaternion QuaternionFromAxisAngleDegrees(Vector3 axis, float degrees)
        {
            if (axis == Vector3.Zero || degrees == 0f)
            {
                return Quaternion.Identity;
            }

            var (sin, cos) = MathF.SinCos(float.DegreesToRadians(degrees) * 0.5f);
            return new Quaternion(axis * sin, cos);
        }

        public static Color32 MultiplyColor(Color32 a, Color32 b)
        {
            static byte Channel(byte x, byte y) => (byte)((x * y + 127) / 255);
            return new Color32(Channel(a.R, b.R), Channel(a.G, b.G), Channel(a.B, b.B), Channel(a.A, b.A));
        }

        public static Color32 LerpColor(Color32 a, Color32 b, float t)
        {
            static byte Channel(byte x, byte y, float t) => (byte)(int)((y - x) * t + x);
            return new Color32(Channel(a.R, b.R, t), Channel(a.G, b.G, t), Channel(a.B, b.B, t), Channel(a.A, b.A, t));
        }

        public static float SrgbToLinear(byte value)
        {
            var c = value / 255f;
            return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        public static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    }
}
