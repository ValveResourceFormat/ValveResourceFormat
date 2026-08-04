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
            scaleVector = entity.GetVector3Property("scales", Vector3.One);
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
        /// Converts a quaternion to Euler angles (pitch, yaw, roll) in degrees.
        /// Includes gimbal lock handling when pitch is near +/-90 degrees.
        /// </summary>
        /// <param name="q">The quaternion to convert.</param>
        /// <returns>The Euler angles in degrees.</returns>
        public static Vector3 ToEulerAngles(Quaternion q)
        {
            var forwardX = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            var forwardY = 2 * (q.X * q.Y + q.W * q.Z);
            var forwardZ = 2 * (q.X * q.Z - q.W * q.Y);

            var xyDist = MathF.Sqrt(forwardX * forwardX + forwardY * forwardY);

            Vector3 angles = new();
            angles.X = MathF.Atan2(-forwardZ, xyDist);

            if (xyDist > 0.001f)
            {
                var leftZ = 2 * (q.Y * q.Z + q.W * q.X);
                var upZ = 1 - 2 * (q.X * q.X + q.Y * q.Y);
                angles.Y = MathF.Atan2(forwardY, forwardX);
                angles.Z = MathF.Atan2(leftZ, upZ);
            }
            else
            {
                var leftX = 2 * (q.X * q.Y - q.W * q.Z);
                var leftY = 1 - 2 * (q.X * q.X + q.Z * q.Z);
                angles.Y = MathF.Atan2(-leftX, leftY);
                angles.Z = 0;
            }

            return Vector3.RadiansToDegrees(angles);
        }

        /// <summary>
        /// Converts Euler angles (pitch, yaw, roll) to a normalized forward direction vector.
        /// </summary>
        /// <param name="pitchYawRoll">The Euler angles.</param>
        /// <returns>The normalized forward direction.</returns>
        public static Vector3 QAngleToForwardDirection(Vector3 pitchYawRoll)
        {
            var rotationMatrix = CreateRotationMatrixFromEulerAngles(pitchYawRoll);
            return Vector3.Normalize(Vector3.Transform(new Vector3(1, 0, 0), rotationMatrix));
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
        /// Like <see cref="CalculateTransformationMatrix"/> but without scale; the transform a template passes to its children.
        /// </summary>
        /// <returns>The transform without the entity's scale.</returns>
        public static Matrix4x4 CalculateRigidTransformationMatrix(Entity entity)
        {
            DecomposeTransformationMatrix(entity, out _, out var rotationMatrix, out var positionVector);

            return rotationMatrix * Matrix4x4.CreateTranslation(positionVector);
        }

        /// <summary>
        /// Parses a string representation of a Vector2.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <returns>The parsed vector.</returns>
        public static Vector2 ParseVector2(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return default;
            }
            var split = input.Split(' ');

            if (split.Length != 2)
            {
                return default;
            }

            return new Vector2(
                float.Parse(split[0], CultureInfo.InvariantCulture),
                float.Parse(split[1], CultureInfo.InvariantCulture));
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
