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
            => EulerAnglesToRotationMatrixRadians(Vector3.DegreesToRadians(pitchYawRoll));

        /// <summary>
        /// Converts Euler angles (pitch, yaw, roll) in radians to a rotation matrix, for the callers
        /// that hold their angles that way, such as a particle's rotation attributes.
        /// </summary>
        /// <param name="pitchYawRoll">The Euler angles, in radians.</param>
        /// <returns>The rotation matrix.</returns>
        public static Matrix4x4 EulerAnglesToRotationMatrixRadians(Vector3 pitchYawRoll)
        {
            var rollMatrix = Matrix4x4.CreateRotationX(pitchYawRoll.Z);
            var pitchMatrix = Matrix4x4.CreateRotationY(pitchYawRoll.X);
            var yawMatrix = Matrix4x4.CreateRotationZ(pitchYawRoll.Y);

            return rollMatrix * pitchMatrix * yawMatrix;
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
            // The first row of the matrix the angles build, which is already unit length. Roll turns about
            // forward, so it cannot move it, and does not appear.
            var (sinPitch, cosPitch) = MathF.SinCos(float.DegreesToRadians(pitchYawRoll.X));
            var (sinYaw, cosYaw) = MathF.SinCos(float.DegreesToRadians(pitchYawRoll.Y));

            return new Vector3(cosPitch * cosYaw, cosPitch * sinYaw, -sinPitch);
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
        /// Completes a rotation frame around a forward direction, with zero roll: the same frame
        /// <see cref="EulerAnglesToRotationMatrix"/> builds from <see cref="ForwardDirectionToEulerAngles"/>,
        /// so forward lands on the first row.
        /// </summary>
        /// <remarks>
        /// A direction cannot express roll, so one is chosen rather than recovered, and world up is the
        /// reference that chooses it. Computed straight from the direction rather than by going through
        /// angles, since this runs per particle in places.
        /// </remarks>
        /// <param name="direction">The forward direction. Does not need to be normalized.</param>
        /// <returns>The rotation matrix, with forward, left and up as its rows.</returns>
        public static Matrix4x4 ForwardDirectionToRotationMatrix(Vector3 direction)
        {
            var (forward, left, up) = ForwardDirectionToFrame(direction);

            return new Matrix4x4(
                forward.X, forward.Y, forward.Z, 0,
                left.X, left.Y, left.Z, 0,
                up.X, up.Y, up.Z, 0,
                0, 0, 0, 1);
        }

        /// <summary>
        /// The rotation <see cref="ForwardDirectionToRotationMatrix"/> builds, as a quaternion.
        /// </summary>
        /// <param name="direction">The forward direction. Does not need to be normalized.</param>
        /// <returns>The rotation quaternion.</returns>
        public static Quaternion ForwardDirectionToQuaternion(Vector3 direction)
            => Quaternion.CreateFromRotationMatrix(ForwardDirectionToRotationMatrix(direction));

        private static (Vector3 Forward, Vector3 Left, Vector3 Up) ForwardDirectionToFrame(Vector3 direction)
        {
            var xyDist = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);

            if (xyDist <= 0.001f)
            {
                // Straight up or down, where yaw is pinned to zero the same way the angles pin it
                return direction.Z > 0f
                    ? (Vector3.UnitZ, Vector3.UnitY, -Vector3.UnitX)
                    : (-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX);
            }

            var forward = Vector3.Normalize(direction);

            // Yaw's sine and cosine come straight off the flattened direction, and pitch's off forward's
            // own components, so the frame needs no trigonometry at all
            var flat = MathF.Sqrt(forward.X * forward.X + forward.Y * forward.Y);
            var cosYaw = forward.X / flat;
            var sinYaw = forward.Y / flat;

            return (
                forward,
                new Vector3(-sinYaw, cosYaw, 0f),
                new Vector3(-forward.Z * cosYaw, -forward.Z * sinYaw, flat));
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
        /// <returns>The parsed vector, or zero if the input is not two numbers.</returns>
        public static Vector2 ParseVector2(string input)
            => TryParseVector2(input, out var value) ? value : default;

        /// <summary>
        /// Parses a string representation of a Vector2, reporting whether it was one.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <param name="value">The parsed vector, or zero if the input is not two numbers.</param>
        /// <returns>Whether the input parsed.</returns>
        public static bool TryParseVector2(string input, out Vector2 value)
        {
            value = default;

            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            var split = input.Split(' ');

            if (split.Length != 2
                || !float.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !float.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                return false;
            }

            value = new Vector2(x, y);
            return true;
        }

        /// <summary>
        /// Parses a string representation of a Vector3.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <returns>The parsed vector, or zero if the input is not three numbers.</returns>
        public static Vector3 ParseVector3(string input)
            => TryParseVector3(input, out var value) ? value : default;

        /// <summary>
        /// Parses a string representation of a Vector3, reporting whether it was one.
        /// </summary>
        /// <remarks>
        /// Callers with a meaningful default need this rather than <see cref="ParseVector3"/>, whose zero
        /// is indistinguishable from a genuine "0 0 0" - a malformed "scales" falling back to zero rather
        /// than to one collapses the thing being scaled.
        /// </remarks>
        /// <param name="input">The input string.</param>
        /// <param name="value">The parsed vector, or zero if the input is not three numbers.</param>
        /// <returns>Whether the input parsed.</returns>
        public static bool TryParseVector3(string input, out Vector3 value)
        {
            value = default;

            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            var split = input.Split(' ');

            if (split.Length != 3
                || !float.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !float.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !float.TryParse(split[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                return false;
            }

            value = new Vector3(x, y, z);
            return true;
        }
    }
}
