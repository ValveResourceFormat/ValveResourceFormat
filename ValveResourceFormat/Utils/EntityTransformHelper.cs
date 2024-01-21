using System.Globalization;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Utils
{
    public static class EntityTransformHelper
    {
        public static Matrix4x4 CalculateTransformationMatrix(EntityLump.Entity entity)
        {
            var scale = entity.GetProperty<string>("scales");
            var position = entity.GetProperty<string>("origin");

            var anglesUntyped = entity.GetProperty("angles");

            if (scale == null || position == null || anglesUntyped == default)
            {
                return default;
            }

            var scaleMatrix = Matrix4x4.CreateScale(ParseVector(scale));

            var positionVector = ParseVector(position);
            var positionMatrix = Matrix4x4.CreateTranslation(positionVector);

            var pitchYawRoll = anglesUntyped.Type switch
            {
                EntityFieldType.CString => ParseVector((string)anglesUntyped.Data),
                EntityFieldType.Vector => (Vector3)anglesUntyped.Data,
                _ => throw new NotImplementedException($"Unsupported angles type {anglesUntyped.Type}"),
            };

            var rollMatrix = Matrix4x4.CreateRotationX(pitchYawRoll.Z * MathF.PI / 180f);
            var pitchMatrix = Matrix4x4.CreateRotationY(pitchYawRoll.X * MathF.PI / 180f);
            var yawMatrix = Matrix4x4.CreateRotationZ(pitchYawRoll.Y * MathF.PI / 180f);

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
