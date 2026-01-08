namespace GUI.Types.ParticleRenderer
{
    /// <summary>
    /// Represents an entire particle system.
    /// </summary>
    class ParticleSystemRenderState
    {
        public static readonly ParticleSystemRenderState Default = new();

        public ParticleRenderer? Data { get; init; }

        // Properties
        public long ParticleCount { get; set; }
        public float Age { get; set; }

        public bool EndEarly { get; set; }

        public bool DestroyInstantlyOnEnd { get; private set; }
        public float Duration { get; private set; }

        // We don't yet support endcaps (effects that play for when a particle system ends), but if we ever do:
        // This can be set by PlayEndCapWhenFinished and StopAfterDuration
        public bool PlayEndCap { get; private set; }

        public void SetStopTime(float duration, bool destroyInstantly)
        {
            EndEarly = true;
            Duration = duration;
            DestroyInstantlyOnEnd = destroyInstantly;
        }

        // Control Points

        private readonly Dictionary<int, ControlPoint> controlPoints = new(64);

        public ControlPoint GetControlPoint(int cp)
        {
            if (!controlPoints.TryGetValue(cp, out var point))
            {
                point = new ControlPoint();
                SetControlPoint(cp, point);
            }

            return point;
        }

        public void SetControlPoint(int cp, ControlPoint point)
        {
            controlPoints[cp] = point;
        }

        /// <summary>
        /// Set the value/position of a control point in the particle system.
        /// </summary>
        /// <param name="cp"></param>
        /// <param name="position"></param>
        public void SetControlPointValue(int cp, Vector3 position)
        {
            GetControlPoint(cp).Position = position;
        }

        public void SetControlPointValueComponent(int cp, int component, float value)
        {
            GetControlPoint(cp).SetComponent(component, value);
        }

        /// <summary>
        /// Set the orientation/direction of a control point in the particle system.
        /// </summary>
        /// <param name="cp"></param>
        /// <param name="orientation"></param>
        public void SetControlPointOrientation(int cp, Vector3 orientation)
        {
            GetControlPoint(cp).Orientation = orientation;
        }
    }

    /// <summary>
    /// Control point used in Valve particle systems. System 0 is the default spawn position.
    /// These are used in numerous different ways, for many different effects. We only support a few of them.
    /// </summary>
    class ControlPoint
    {
        public Vector3 Position { get; set; }
        public Vector3 Orientation { get; set; }

        public ParticleAttachment AttachType { get; set; }

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
