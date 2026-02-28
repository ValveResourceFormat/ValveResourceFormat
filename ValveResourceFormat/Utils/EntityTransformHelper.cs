using System.Globalization;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Utils
{
    /// <summary>
    /// Helper methods for entity transformations.
    /// </summary>
    public static class EntityTransformHelper
    {
        /// <summary>
        /// Extracts scale, rotation, and position components from an entity's transformation.
        /// </summary>
        /// <param name="entity">The entity to extract from.</param>
        /// <param name="scaleVector">The scale vector.</param>
        /// <param name="rotationMatrix">The rotation matrix.</param>
        /// <param name="positionVector">The position vector.</param>
        public static void DecomposeTransformationMatrix(Entity entity, out Vector3 scaleVector, out Matrix4x4 rotationMatrix, out Vector3 positionVector)
        {
            scaleVector = entity.GetVector3Property("scales");
            positionVector = entity.GetVector3Property("origin");
            var pitchYawRoll = entity.GetVector3Property("angles");

            rotationMatrix = CreateRotationMatrixFromEulerAngles(pitchYawRoll);
        }

        /// <summary>
        /// Creates a rotation matrix from Euler angles (pitch, yaw, roll).
        /// </summary>
        /// <param name="pitchYawRoll">The Euler angles.</param>
        /// <returns>The rotation matrix.</returns>
        public static Matrix4x4 CreateRotationMatrixFromEulerAngles(Vector3 pitchYawRoll)
        {
            Matrix4x4 rotationMatrix;
            var rollMatrix = Matrix4x4.CreateRotationX(float.DegreesToRadians(pitchYawRoll.Z));
            var pitchMatrix = Matrix4x4.CreateRotationY(float.DegreesToRadians(pitchYawRoll.X));
            var yawMatrix = Matrix4x4.CreateRotationZ(float.DegreesToRadians(pitchYawRoll.Y));

            rotationMatrix = rollMatrix * pitchMatrix * yawMatrix;
            return rotationMatrix;
        }

        /// <summary>
        /// Convert a quaternion to Euler angles in degrees (pitch=X, yaw=Y, roll=Z). 0-360 range.
        /// </summary>
        public static Vector3 ToEulerAngles(Quaternion q)
        {
            var angles = new Vector3();

            // pitch / x
            var sinp = 2f * (q.W * q.Y - q.Z * q.X);
            if (MathF.Abs(sinp) >= 1f)
            {
                angles.X = MathF.CopySign(MathF.PI / 2f, sinp);
            }
            else
            {
                angles.X = MathF.Asin(sinp);
            }

            // yaw / y
            var siny_cosp = 2f * (q.W * q.Z + q.X * q.Y);
            var cosy_cosp = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
            angles.Y = MathF.Atan2(siny_cosp, cosy_cosp);

            // roll / z
            var sinr_cosp = 2f * (q.W * q.X + q.Y * q.Z);
            var cosr_cosp = 1f - 2f * (q.X * q.X + q.Y * q.Y);
            angles.Z = MathF.Atan2(sinr_cosp, cosr_cosp);

            return angles * (180f / MathF.PI);
        }

        /// <summary>
        /// Calculates the full transformation matrix for an entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <returns>The transformation matrix.</returns>
        public static Matrix4x4 CalculateTransformationMatrix(Entity entity)
        {
            DecomposeTransformationMatrix(entity, out var scaleVector, out var rotationMatrix, out var positionVector);

            var scaleMatrix = Matrix4x4.CreateScale(scaleVector);
            var positionMatrix = Matrix4x4.CreateTranslation(positionVector);

            return scaleMatrix * rotationMatrix * positionMatrix;
        }

        /// <summary>
        /// Parses a string representation of a Vector3.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <returns>The parsed vector.</returns>
        public static Vector3 ParseVector(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return default;
            }
            var split = input.Split(' ');

            if (split.Length != 3)
            {
                return default;
            }

            return new Vector3(
                float.Parse(split[0], CultureInfo.InvariantCulture),
                float.Parse(split[1], CultureInfo.InvariantCulture),
                float.Parse(split[2], CultureInfo.InvariantCulture));
        }
    }
}
