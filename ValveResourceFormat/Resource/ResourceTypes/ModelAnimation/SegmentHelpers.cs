namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    internal class SegmentHelpers
    {
        /// <summary>
        /// Read and decode encoded quaternion.
        /// </summary>
        /// <param name="reader">Binary reader.</param>
        /// <returns>Quaternion.</returns>
        public static Quaternion ReadQuaternion(ReadOnlySpan<byte> bytes)
        {
            // Values
            var i1 = bytes[0] + ((bytes[1] & 63) << 8);
            var i2 = bytes[2] + ((bytes[3] & 63) << 8);
            var i3 = bytes[4] + ((bytes[5] & 63) << 8);

            // Signs
            var s1 = bytes[1] & 128;
            var s2 = bytes[3] & 128;
            var s3 = bytes[5] & 128;

            var c = MathF.Sin(MathF.PI / 4.0f) / 16384.0f;
            var x = (bytes[1] & 64) == 0 ? c * (i1 - 16384) : c * i1;
            var y = (bytes[3] & 64) == 0 ? c * (i2 - 16384) : c * i2;
            var z = (bytes[5] & 64) == 0 ? c * (i3 - 16384) : c * i3;

            var w = MathF.Sqrt(1 - (x * x) - (y * y) - (z * z));

            // Apply sign 3
            if (s3 == 128)
            {
                w *= -1;
            }

            // Apply sign 1 and 2
            if (s1 == 128)
            {
                return s2 == 128 ? new Quaternion(y, z, w, x) : new Quaternion(z, w, x, y);
            }

            return s2 == 128 ? new Quaternion(w, x, y, z) : new Quaternion(x, y, z, w);
        }

        public static Vector3 ReadHalfVector3(ReadOnlySpan<byte> bytes)
        {
            var x = BitConverter.ToHalf(bytes);
            var y = BitConverter.ToHalf(bytes[2..]);
            var z = BitConverter.ToHalf(bytes[4..]);
            return new Vector3((float)x, (float)y, (float)z);
        }
    }
}
