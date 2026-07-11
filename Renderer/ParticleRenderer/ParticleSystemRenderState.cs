namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Represents an entire particle system.
    /// </summary>
    class ParticleSystemRenderState
    {
        public static readonly ParticleSystemRenderState Default = new();

        public ParticleSystemRenderState? ParentSystem { get; }

        public ParticleSystemRenderState(ParticleSystemRenderState? parentSystem = null)
        {
            ParentSystem = parentSystem;
        }

        public ParticleRenderer? Data { get; init; }

        /// <summary>
        /// The scene node the root system renders under; child systems inherit it from their parent.
        /// </summary>
        public SceneNode? OwnerNode => Data?.OwnerNode ?? ParentSystem?.OwnerNode;

        private int detailLevel = 3;

        /// <summary>
        /// Active particle detail tier (0 = Low .. 3 = Ultra) used by <c>PF_TYPE_PARTICLE_DETAIL_LEVEL</c>
        /// inputs; child systems inherit the root system's level.
        /// </summary>
        public int DetailLevel
        {
            get => ParentSystem?.DetailLevel ?? detailLevel;
            set => detailLevel = value;
        }

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
            if (ParentSystem != null)
            {
                return ParentSystem.GetControlPoint(cp);
            }

            if (controlPoints.TryGetValue(cp, out var point))
            {
                return point;
            }

            point = new ControlPoint();
            SetControlPoint(cp, point);
            return point;
        }

        /// <summary>
        /// Gets the snapshot bound to a control point, preferring this system's own binding and
        /// falling back to an ancestor so a child can read a snapshot inherited from a parent control point.
        /// </summary>
        public ValveResourceFormat.Blocks.ParticleSnapshot? GetControlPointSnapshot(int cp)
        {
            return Data?.GetControlPointSnapshot(cp) ?? ParentSystem?.GetControlPointSnapshot(cp);
        }

        public void SetControlPoint(int cp, ControlPoint point)
        {
            if (ParentSystem != null)
            {
                ParentSystem.SetControlPoint(cp, point);
            }

            controlPoints[cp] = point;
        }

        /// <summary>
        /// Set the value/position of a control point in the particle system.
        /// </summary>
        /// <param name="cp">Control point index.</param>
        /// <param name="position">World-space position to assign.</param>
        public void SetControlPointValue(int cp, Vector3 position)
        {
            GetControlPoint(cp).Position = position;
        }

        public void SetControlPointValueComponent(int cp, int component, float value)
        {
            GetControlPoint(cp).SetComponent(component, value);
        }

        /// <summary>
        /// Records every control point's current position as its previous-step position. The root
        /// system calls this once per simulation step, after all consumers have run.
        /// </summary>
        internal void SnapshotControlPointHistory()
        {
            foreach (var point in controlPoints.Values)
            {
                point.PositionPrevious = point.Position;
            }
        }

        /// <summary>
        /// Set the orientation/direction of a control point in the particle system.
        /// </summary>
        /// <param name="cp">Control point index.</param>
        /// <param name="orientation">Orientation direction to assign.</param>
        public void SetControlPointOrientation(int cp, Vector3 orientation)
        {
            var point = GetControlPoint(cp);
            point.Orientation = orientation;
            // The setter only expresses a forward direction; drop any stale full rotation so
            // consumers don't keep preferring an outdated frame.
            point.Rotation = null;
        }

        /// <summary>
        /// Set the full rotation of a control point, keeping its forward direction in sync.
        /// </summary>
        /// <param name="cp">Control point index.</param>
        /// <param name="rotation">Full rotation to assign.</param>
        public void SetControlPointRotation(int cp, Quaternion rotation)
        {
            var point = GetControlPoint(cp);
            point.Rotation = rotation;
            point.Orientation = Vector3.Transform(Vector3.UnitZ, rotation);
        }

        /// <summary>
        /// Return a random float in the range [flLow, flHigh). The distribution is uniform.
        /// </summary>
        internal static float RandomFloat(float flLow, float flHigh)
        {
            var random = Random.Shared.NextSingle();
            return float.Lerp(flLow, flHigh, random);
        }
    }

    /// <summary>
    /// Control point used in Valve particle systems. System 0 is the default spawn position.
    /// These are used in numerous different ways, for many different effects. We only support a few of them.
    /// </summary>
    public class ControlPoint
    {
        /// <summary>
        /// The position of this control point. Sometimes this is used for things other than position.
        /// </summary>
        public Vector3 Position { get; set; }

        /// <summary>
        /// The position this control point had on the previous simulation step, used to derive the
        /// control point's velocity.
        /// </summary>
        public Vector3 PositionPrevious { get; set; }

        /// <summary>
        /// The control point's velocity over the current simulation step in units per second, derived
        /// from <see cref="PositionPrevious"/>. Zero when the step duration is unknown.
        /// </summary>
        public Vector3 GetVelocity(float frameTime)
            => frameTime > 0f ? (Position - PositionPrevious) / frameTime : Vector3.Zero;

        /// <summary>
        /// The orientation/direction of this control point.
        /// </summary>
        public Vector3 Orientation { get; set; }

        /// <summary>
        /// The full rotation of this control point, when the source supplies one (e.g. a map entity's
        /// angles). Consumers fall back to synthesizing a frame from <see cref="Orientation"/> when unset,
        /// since most operators only drive the forward direction.
        /// </summary>
        public Quaternion? Rotation { get; set; }

        /// <summary>
        /// The control point's full rotation, using <see cref="Rotation"/> when present and otherwise
        /// synthesizing a frame from the forward <see cref="Orientation"/> direction.
        /// </summary>
        public Quaternion GetRotation()
        {
            if (Rotation is { } rotation)
            {
                return rotation;
            }

            if (Orientation == Vector3.Zero)
            {
                return Quaternion.Identity;
            }

            var forward = Vector3.Normalize(Orientation);
            var up = MathF.Abs(forward.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitZ;
            var right = Vector3.Normalize(Vector3.Cross(up, forward));
            up = Vector3.Cross(forward, right);

            var matrix = new Matrix4x4(
                right.X, right.Y, right.Z, 0,
                up.X, up.Y, up.Z, 0,
                forward.X, forward.Y, forward.Z, 0,
                0, 0, 0, 1
            );

            return Quaternion.CreateFromRotationMatrix(matrix);
        }

        /// <summary>
        /// Different attachment styles.
        /// </summary>
        public ParticleAttachment AttachType { get; set; }

        /// <summary>
        /// Write potentially non positional data to the control point, for the particle to read.
        /// </summary>
        /// <param name="component">0, 1, 2</param>
        /// <param name="value">Number</param>
        public void SetComponent(int component, float value)
        {
            component = Math.Clamp(component, 0, 2);
            Position = component switch
            {
                0 => new Vector3(value, Position.Y, Position.Z),
                1 => new Vector3(Position.X, value, Position.Z),
                _ => new Vector3(Position.X, Position.Y, value),
            };
        }
    }
}
