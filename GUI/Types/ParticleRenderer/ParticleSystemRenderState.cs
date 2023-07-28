using System;
using System.Collections.Generic;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    /// <summary>
    /// Represents an entire particle system.
    /// </summary>
    class ParticleSystemRenderState
    {
        public static readonly ParticleSystemRenderState Default = new();

        public int BehaviorVersion { get; init; }
        public int MaxParticles { get; init; } = 1000;

        // Properties
        public long ParticleCount { get; set; }
        public float Age { get; set; }

        public bool EndEarly { get; set; }

        public bool DestroyInstantlyOnEnd { get; private set; }
        public float Duration { get; private set; }

        // We don't yet support endcaps (effects that play for when a particle system ends), but if we ever do:
        // This can be set by PlayEndCapWhenFinished and StopAfterDuration
        public bool PlayEndCap { get; private set; }

        private ParticleSystemRenderState()
        {
        }

        public ParticleSystemRenderState(IKeyValueCollection particleSystemDefinition)
        {
            // What should be the default otherwise?
            if (particleSystemDefinition.ContainsKey("m_nBehaviorVersion"))
            {
                BehaviorVersion = particleSystemDefinition.GetInt32Property("m_nBehaviorVersion");
            }

            if (particleSystemDefinition.ContainsKey("m_nMaxParticles"))
            {
                MaxParticles = particleSystemDefinition.GetInt32Property("m_nMaxParticles");
            }
        }

        public void SetStopTime(float duration, bool destroyInstantly)
        {
            EndEarly = true;
            Duration = duration;
            DestroyInstantlyOnEnd = destroyInstantly;
        }

        // Control Points

        private readonly Dictionary<int, ControlPoint> controlPoints = new();

        public ControlPoint GetControlPoint(int cp)
            => controlPoints.TryGetValue(cp, out var value)
            ? value
            : new ControlPoint();

        private void EnsureControlPointExists(int cp)
        {
            if (!controlPoints.TryGetValue(cp, out var value))
            {
                controlPoints[cp] = new ControlPoint();
            }
        }

        /// <summary>
        /// Set the value/position of a control point in the particle system.
        /// </summary>
        /// <param name="cp"></param>
        /// <param name="position"></param>
        public void SetControlPointValue(int cp, Vector3 position)
        {
            EnsureControlPointExists(cp);

            controlPoints[cp].Position = position;
        }

        public void SetControlPointValueComponent(int cp, int component, float value)
        {
            EnsureControlPointExists(cp);

            controlPoints[cp].SetComponent(component, value);
        }

        /// <summary>
        /// Set the orientation/direction of a control point in the particle system.
        /// </summary>
        /// <param name="cp"></param>
        /// <param name="orientation"></param>
        public void SetControlPointOrientation(int cp, Vector3 orientation)
        {
            EnsureControlPointExists(cp);

            controlPoints[cp].Orientation = orientation;
        }
    }

    /// <summary>
    /// Control point used in Valve particle systems. System 0 is the default spawn position.
    /// These are used in numerous different ways, for many different effects. We only support a few of them.
    /// </summary>
    class ControlPoint
    {
        public Vector3 Position { get; set; } = Vector3.Zero;
        public Vector3 Orientation { get; set; } = Vector3.Zero;

        public void SetComponent(int component, float value)
        {
            component = Math.Clamp(component, 0, 2);
            Position = component switch
            {
                0 => Position = new Vector3(value, Position.Y, Position.Z),
                1 => Position = new Vector3(Position.X, value, Position.Z),
                _ => Position = new Vector3(Position.X, Position.Y, value),
            };
        }
    }
}
