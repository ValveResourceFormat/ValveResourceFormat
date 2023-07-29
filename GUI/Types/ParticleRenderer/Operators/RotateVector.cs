using System;
using System.Numerics;
using System.Collections.Generic;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class RotateVector : IParticleOperator
    {
        private readonly ParticleField field = ParticleField.Normal;
        private readonly Vector3 axisMin = new(0, 0, 1);
        private readonly Vector3 axisMax = new(0, 0, 1);

        private readonly float rotRateMin = 180f;
        private readonly float rotRateMax = 180f;

        private readonly INumberProvider perParticleScale = new LiteralNumberProvider(1f);
        private readonly bool normalize;

        public RotateVector(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nOutputField"))
            {
                field = keyValues.GetParticleField("m_nOutputField");
            }

            if (keyValues.ContainsKey("m_vecRotAxisMin"))
            {
                axisMin = keyValues.GetArray<double>("m_vecRotAxisMin").ToVector3();
            }

            if (keyValues.ContainsKey("m_vecRotAxisMax"))
            {
                axisMax = keyValues.GetArray<double>("m_vecRotAxisMax").ToVector3();
            }

            if (keyValues.ContainsKey("m_flRotRateMin"))
            {
                rotRateMin = keyValues.GetFloatProperty("m_flRotRateMin");
            }

            if (keyValues.ContainsKey("m_flRotRateMax"))
            {
                rotRateMax = keyValues.GetFloatProperty("m_flRotRateMax");
            }

            if (keyValues.ContainsKey("m_flScale"))
            {
                perParticleScale = keyValues.GetNumberProvider("m_flScale");
            }

            if (keyValues.ContainsKey("m_bNormalize"))
            {
                normalize = keyValues.GetProperty<bool>("m_bNormalize");
            }
        }

        private static Vector3 MatrixMul(Vector3 vector, Matrix4x4 rotatedMatrix)
        {
            return vector.X * new Vector3(rotatedMatrix.M11, rotatedMatrix.M12, rotatedMatrix.M13) +
                vector.Y * new Vector3(rotatedMatrix.M21, rotatedMatrix.M22, rotatedMatrix.M23) +
                vector.Z * new Vector3(rotatedMatrix.M31, rotatedMatrix.M32, rotatedMatrix.M33);
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                // TODO: Consistent rng
                var axis = Vector3.Normalize(MathUtils.RandomBetween(axisMin, axisMax));
                var rotationRate = MathUtils.ToRadians(MathUtils.RandomBetween(rotRateMin, rotRateMax));

                var scale = perParticleScale.NextNumber(ref particle, particleSystemState);

                // probably slow but who knows???
                var rotatedVector = MatrixMul(particle.GetVector(field), Matrix4x4.CreateFromAxisAngle(axis, rotationRate * scale * frameTime));

                rotatedVector = normalize
                    ? Vector3.Normalize(rotatedVector)
                    : rotatedVector;

                particle.SetVector(field, rotatedVector);
            }
        }
    }
}
