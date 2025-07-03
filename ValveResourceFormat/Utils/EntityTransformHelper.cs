using System.Globalization;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Utils
{
    public static class EntityTransformHelper
    {
        public static void DecomposeTransformationMatrix(Entity entity, out Vector3 scaleVector, out Matrix4x4 rotationMatrix, out Vector3 positionVector)
        {
            scaleVector = entity.GetVector3Property("scales");
            positionVector = entity.GetVector3Property("origin");
            var pitchYawRoll = entity.GetVector3Property("angles");

            rotationMatrix = CreateRotationMatrixFromEulerAngles(pitchYawRoll);
        }

        public static Matrix4x4 CreateRotationMatrixFromEulerAngles(Vector3 pitchYawRoll)
        {
            Matrix4x4 rotationMatrix;
            var rollMatrix = Matrix4x4.CreateRotationX(pitchYawRoll.Z * MathF.PI / 180f);
            var pitchMatrix = Matrix4x4.CreateRotationY(pitchYawRoll.X * MathF.PI / 180f);
            var yawMatrix = Matrix4x4.CreateRotationZ(pitchYawRoll.Y * MathF.PI / 180f);

            rotationMatrix = rollMatrix * pitchMatrix * yawMatrix;
            return rotationMatrix;
        }

        public static Matrix4x4 CalculateTransformationMatrix(Entity entity)
        {
            DecomposeTransformationMatrix(entity, out var scaleVector, out var rotationMatrix, out var positionVector);

            var scaleMatrix = Matrix4x4.CreateScale(scaleVector);
            var positionMatrix = Matrix4x4.CreateTranslation(positionVector);

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
