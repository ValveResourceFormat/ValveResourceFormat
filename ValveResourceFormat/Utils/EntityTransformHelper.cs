using System.Globalization;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Utils
{
    /// <summary>
    /// Helper methods for entity transformations.
    /// </summary>
    /// <remarks>
    /// Source 2 is Z-up and right-handed: +X is forward, +Y is left, +Z is up.
    /// An entity's "angles" is a QAngle, held here as (pitch, yaw, roll) in degrees. The components are not
    /// in axis order: pitch turns about Y, yaw about Z, roll about X, and they compose in that order.
    /// Pitch is positive downwards, so a forward vector's Z is -sin(pitch) - cameras usually do the opposite,
    /// which is the sign most often flipped by mistake. Yaw is positive turning left.
    /// Matrices are row-vector, so a rotation's rows are forward, left and up, and forward is read by
    /// transforming <see cref="Vector3.UnitX"/>.
    /// At a pitch of +/-90 forward is vertical and yaw and roll share an axis (gimbal lock), so converting
    /// back to angles puts the whole rotation into yaw and pins roll to zero.
    /// </remarks>
    public static class EntityTransformHelper
    {
        /// <summary>
        /// Reads an entity's scale, rotation and position from its "scales", "angles" and "origin" keyvalues.
        /// </summary>
        /// <param name="entity">The entity to read from.</param>
        /// <param name="scaleVector">The scale vector.</param>
        /// <param name="rotationMatrix">The rotation matrix.</param>
        /// <param name="positionVector">The position vector.</param>
        public static void GetTransformComponents(Entity entity, out Vector3 scaleVector, out Matrix4x4 rotationMatrix, out Vector3 positionVector)
        {
            scaleVector = entity.GetVector3Property("scales", Vector3.One);
            positionVector = entity.GetVector3Property("origin");
            var pitchYawRoll = entity.GetVector3Property("angles");

            rotationMatrix = EulerAnglesToRotationMatrix(pitchYawRoll);
        }

        /// <summary>
        /// Converts Euler angles (pitch, yaw, roll) to a rotation matrix.
        /// </summary>
        /// <param name="pitchYawRoll">The Euler angles.</param>
        /// <returns>The rotation matrix.</returns>
        public static Matrix4x4 EulerAnglesToRotationMatrix(Vector3 pitchYawRoll)
        {
            Matrix4x4 rotationMatrix;
            var rollMatrix = Matrix4x4.CreateRotationX(float.DegreesToRadians(pitchYawRoll.Z));
            var pitchMatrix = Matrix4x4.CreateRotationY(float.DegreesToRadians(pitchYawRoll.X));
            var yawMatrix = Matrix4x4.CreateRotationZ(float.DegreesToRadians(pitchYawRoll.Y));

            rotationMatrix = rollMatrix * pitchMatrix * yawMatrix;
            return rotationMatrix;
        }

        /// <summary>
        /// Converts Euler angles (pitch, yaw, roll) to a rotation quaternion, the same rotation
        /// <see cref="EulerAnglesToRotationMatrix"/> builds as a matrix. Inverse of
        /// <see cref="ToEulerAngles"/>.
        /// </summary>
        /// <param name="pitchYawRoll">The Euler angles.</param>
        /// <returns>The rotation quaternion.</returns>
        public static Quaternion EulerAnglesToQuaternion(Vector3 pitchYawRoll)
        {
            var (sp, cp) = MathF.SinCos(float.DegreesToRadians(pitchYawRoll.X) * 0.5f);
            var (sy, cy) = MathF.SinCos(float.DegreesToRadians(pitchYawRoll.Y) * 0.5f);
            var (sr, cr) = MathF.SinCos(float.DegreesToRadians(pitchYawRoll.Z) * 0.5f);

            return new Quaternion(
                sr * cp * cy - cr * sp * sy,
                cr * sp * cy + sr * cp * sy,
                cr * cp * sy - sr * sp * cy,
                cr * cp * cy + sr * sp * sy);
        }

        /// <summary>
        /// Converts a quaternion to Euler angles (pitch, yaw, roll) in degrees.
        /// Includes gimbal lock handling when pitch is near +/-90 degrees.
        /// Inverse of <see cref="EulerAnglesToQuaternion"/>.
        /// </summary>
        /// <param name="rotation">The quaternion to convert.</param>
        /// <returns>The Euler angles in degrees.</returns>
        public static Vector3 ToEulerAngles(Quaternion rotation)
        {
            var forwardX = 1 - 2 * (rotation.Y * rotation.Y + rotation.Z * rotation.Z);
            var forwardY = 2 * (rotation.X * rotation.Y + rotation.W * rotation.Z);
            var forwardZ = 2 * (rotation.X * rotation.Z - rotation.W * rotation.Y);

            var xyDist = MathF.Sqrt(forwardX * forwardX + forwardY * forwardY);

            Vector3 angles = new();
            angles.X = MathF.Atan2(-forwardZ, xyDist);

            if (xyDist > 0.001f)
            {
                var leftZ = 2 * (rotation.Y * rotation.Z + rotation.W * rotation.X);
                var upZ = 1 - 2 * (rotation.X * rotation.X + rotation.Y * rotation.Y);
                angles.Y = MathF.Atan2(forwardY, forwardX);
                angles.Z = MathF.Atan2(leftZ, upZ);
            }
            else
            {
                var leftX = 2 * (rotation.X * rotation.Y - rotation.W * rotation.Z);
                var leftY = 1 - 2 * (rotation.X * rotation.X + rotation.Z * rotation.Z);
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
        public static Vector3 EulerAnglesToForwardDirection(Vector3 pitchYawRoll)
        {
            var rotationMatrix = EulerAnglesToRotationMatrix(pitchYawRoll);
            return Vector3.Normalize(Vector3.Transform(new Vector3(1, 0, 0), rotationMatrix));
        }

        /// <summary>
        /// Converts a forward direction vector to Euler angles (pitch, yaw, roll) in degrees, with zero roll.
        /// Inverse of <see cref="EulerAnglesToForwardDirection"/>.
        /// </summary>
        /// <remarks>
        /// A direction that is straight up or down leaves yaw undetermined, and reading it out of the
        /// residual horizontal components would be numerical noise, so it is pinned to zero the way the
        /// engine's <c>VectorAngles</c> does. That test is on absolute length, matching the engine, so a
        /// direction shorter than a thousandth of a unit reads as vertical whichever way it points.
        /// </remarks>
        /// <param name="direction">The forward direction. Need not be normalized, but see the remarks on very short ones.</param>
        /// <returns>The Euler angles in degrees.</returns>
        public static Vector3 ForwardDirectionToEulerAngles(Vector3 direction)
        {
            var xyDist = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);

            if (xyDist <= 0.001f)
            {
                return new Vector3(direction.Z > 0f ? -90f : 90f, 0f, 0f);
            }

            var pitch = MathF.Atan2(-direction.Z, xyDist);
            var yaw = MathF.Atan2(direction.Y, direction.X);

            return Vector3.RadiansToDegrees(new Vector3(pitch, yaw, 0f));
        }

        /// <summary>
        /// Builds a rotation whose forward axis is <paramref name="forward"/>, picking an arbitrary but
        /// stable roll about it. For callers that have a direction and need a full frame; a direction
        /// cannot express roll, so one is chosen rather than recovered.
        /// </summary>
        /// <param name="forward">The forward direction. Must be normalized and non-zero.</param>
        /// <returns>The rotation matrix, with right, up and forward as its rows.</returns>
        public static Matrix4x4 ForwardDirectionToRotationMatrix(Vector3 forward)
        {
            var up = MathF.Abs(forward.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitZ;
            var right = Vector3.Normalize(Vector3.Cross(up, forward));
            up = Vector3.Cross(forward, right);

            return new Matrix4x4(
                right.X, right.Y, right.Z, 0,
                up.X, up.Y, up.Z, 0,
                forward.X, forward.Y, forward.Z, 0,
                0, 0, 0, 1
            );
        }

        /// <summary>
        /// Calculates the full transformation matrix for an entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <returns>The transformation matrix.</returns>
        public static Matrix4x4 ToTransformationMatrix(Entity entity)
        {
            GetTransformComponents(entity, out var scaleVector, out var rotationMatrix, out var positionVector);

            var scaleMatrix = Matrix4x4.CreateScale(scaleVector);
            var positionMatrix = Matrix4x4.CreateTranslation(positionVector);

            return scaleMatrix * rotationMatrix * positionMatrix;
        }

        /// <summary>
        /// Like <see cref="ToTransformationMatrix"/> but without scale; the transform a template passes to its children.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <returns>The transform without the entity's scale.</returns>
        public static Matrix4x4 ToRigidTransformationMatrix(Entity entity)
        {
            GetTransformComponents(entity, out _, out var rotationMatrix, out var positionVector);

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
        public static Vector3 ParseVector3(string input)
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
