using System;
using System.Globalization;
using System.Numerics;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Utils
{
    public static class EntityTransformHelper
    {
        public static Matrix4x4 CalculateTransformationMatrix(EntityLump.Entity entity)
        {
            var scale = entity.GetProperty<string>("scales");
            var position = entity.GetProperty<string>("origin");
            var angles = entity.GetProperty<string>("angles");
            if (scale == null || position == null || angles == null)
            {
                return default;
            }

            var scaleMatrix = Matrix4x4.CreateScale(ParseVector(scale));

            var positionVector = ParseVector(position);
            var positionMatrix = Matrix4x4.CreateTranslation(positionVector);

            var pitchYawRoll = ParseVector(angles);
            var rollMatrix = Matrix4x4.CreateRotationX(pitchYawRoll.Z * ((float)Math.PI / 180f)); // Roll
            var pitchMatrix = Matrix4x4.CreateRotationY(pitchYawRoll.X * ((float)Math.PI / 180f)); // Pitch
            var yawMatrix = Matrix4x4.CreateRotationZ(pitchYawRoll.Y * ((float)Math.PI / 180f)); // Yaw

            var rotationMatrix = rollMatrix * pitchMatrix * yawMatrix;
            return scaleMatrix * rotationMatrix * positionMatrix;
        }

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
