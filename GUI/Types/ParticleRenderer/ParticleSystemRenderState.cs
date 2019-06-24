using System.Collections.Generic;
using System.Numerics;

namespace GUI.Types.ParticleRenderer
{
    public class ParticleSystemRenderState
    {
        public float Lifetime { get; set; } = 0f;

        private readonly Dictionary<int, Vector3> controlPoints = new Dictionary<int, Vector3>();

        public Vector3 GetControlPoint(int cp)
            => controlPoints.TryGetValue(cp, out var value)
            ? value
            : Vector3.Zero;

        public ParticleSystemRenderState SetControlPoint(int cp, Vector3 value)
        {
            controlPoints[cp] = value;

            return this;
        }
    }
}
