using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Represents an entire particle system.
    /// </summary>
    public class ParticleSystemState
    {
        /// <summary>A state with no system behind it, read by functions evaluated outside a running system.</summary>
        public static readonly ParticleSystemState Default = new();

        /// <summary>The state of the system running this one as a child, or null for a root system.</summary>
        public ParticleSystemState? ParentSystem { get; }

        /// <summary>Creates the state for one system.</summary>
        /// <param name="parentSystem">The state of the system running this one as a child.</param>
        public ParticleSystemState(ParticleSystemState? parentSystem = null)
        {
            ParentSystem = parentSystem;
        }

        /// <summary>The system this state belongs to. Null for <see cref="Default"/>.</summary>
        public ParticleSystemSimulation? Data { get; init; }

        private float worldTime;

        /// <summary>
        /// The renderer's clock, in seconds, as fed to the shaders' <c>g_flTime</c>. Unlike
        /// <see cref="Age"/> it is shared by every system and survives a restart, so operators that use
        /// it to decorrelate repeat plays of an effect actually get a different sample each time. Child
        /// systems read the root's value.
        /// </summary>
        public float WorldTime
        {
            get => ParentSystem?.WorldTime ?? worldTime;
            set => worldTime = value;
        }

        private ParticleDetailLevel detailLevel = ParticleDetailLevel.PARTICLEDETAIL_ULTRA;

        /// <summary>
        /// Active particle detail tier, used by <c>PF_TYPE_PARTICLE_DETAIL_LEVEL</c> inputs and by the
        /// child systems' own <c>m_nDetailLevel</c>; child systems inherit the root system's tier.
        /// </summary>
        public ParticleDetailLevel DetailLevel
        {
            get => ParentSystem?.DetailLevel ?? detailLevel;
            set => detailLevel = value;
        }

        /// <summary>
        /// This system's access to the shared random table. Every random value the system produces
        /// comes from here.
        /// </summary>
        internal ParticleRandom Random { get; } = new();

        /// <summary>How many particles the system has spawned over its life.</summary>
        public long ParticleCount { get; set; }
        /// <summary>How long the system has been running, in seconds.</summary>
        public float Age { get; set; }

        /// <summary>World-space position of the render camera, updated once per simulation step.</summary>
        public Vector3 CameraPosition { get; set; }

        /// <summary>Whether the system stops at <see cref="EndTime"/> rather than running until it empties.</summary>
        public bool EndEarly { get; set; }

        /// <summary>Whether ending the system drops the particles still alive instead of letting them finish.</summary>
        public bool DestroyInstantlyOnEnd { get; private set; }

        /// <summary>
        /// Whether reaching <see cref="EndTime"/> replays the system rather than ending it.
        /// </summary>
        public bool RestartOnEnd { get; private set; }

        /// <summary>System age at which the scheduled stop or restart fires.</summary>
        public float EndTime { get; private set; }

        /// <summary>Whether reaching <see cref="EndTime"/> also starts the endcap.</summary>
        public bool PlayEndCapOnEnd { get; private set; }

        /// <summary>
        /// Whether the system is playing its endcap, the phase an effect runs once it has been told to
        /// stop. Only functions whose <c>m_nOpEndCapState</c> allows it run in each phase, so an endcap
        /// swaps part of the operator list for another.
        /// </summary>
        public bool InEndCap { get; private set; }

        /// <summary>System age at which the endcap began.</summary>
        public float EndCapStartAge { get; private set; }

        /// <summary>How long the endcap has been running, or 0 outside it.</summary>
        public float EndCapAge => InEndCap ? Age - EndCapStartAge : 0f;

        /// <summary>
        /// Whether simulation is held in place. <see cref="Operators.EndCapTimedFreeze"/> sets it when
        /// its timer runs out, and it stays set until the system restarts.
        /// </summary>
        public bool Frozen { get; set; }

        /// <summary>Enters the endcap phase, unless the system is in it already.</summary>
        public void StartEndCap()
        {
            if (InEndCap)
            {
                return;
            }

            InEndCap = true;
            EndCapStartAge = Age;
        }

        /// <summary>Leaves the endcap phase and unfreezes.</summary>
        public void ClearEndCap()
        {
            InEndCap = false;
            EndCapStartAge = 0f;
            Frozen = false;
        }

        /// <summary>
        /// Ends the system after <paramref name="duration"/>: emission stops, and with
        /// <paramref name="destroyInstantly"/> the particles still alive are dropped instead of being
        /// left to finish their lifetimes. With <paramref name="playEndCap"/> the stop also starts the
        /// endcap.
        /// </summary>
        public void SetStopTime(float duration, bool destroyInstantly, bool playEndCap)
        {
            EndEarly = true;
            EndTime = Age + duration;
            DestroyInstantlyOnEnd = destroyInstantly;
            RestartOnEnd = false;
            PlayEndCapOnEnd = playEndCap;
        }

        /// <summary>
        /// Replays the system from the beginning after <paramref name="duration"/>.
        /// </summary>
        public void SetRestartTime(float duration)
        {
            EndEarly = true;
            EndTime = Age + duration;
            DestroyInstantlyOnEnd = false;
            RestartOnEnd = true;
            PlayEndCapOnEnd = false;
        }

        // Control Points

        private readonly Dictionary<int, ControlPoint> controlPoints = new(64);

        // Control points this system holds in its own right rather than reading from its parent. Only
        // C_OP_SetParentControlPointsToChildCP creates them, so it stays null for almost every system.
        private Dictionary<int, ControlPoint>? controlPointOverrides;

        /// <summary>Gets a control point, creating it and every index below it on first use.</summary>
        /// <param name="cp">The control point index.</param>
        public ControlPoint GetControlPoint(int cp)
        {
            if (controlPointOverrides != null && controlPointOverrides.TryGetValue(cp, out var overridden))
            {
                return overridden;
            }

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

        /// <summary>Highest control point index this system has touched; some emitters scale their rate by it.</summary>
        public int HighestControlPoint { get; private set; }

        /// <summary>Replaces a control point, on the root system so every child sees it.</summary>
        /// <param name="cp">The control point index.</param>
        /// <param name="point">The control point to store.</param>
        public void SetControlPoint(int cp, ControlPoint point)
        {
            if (ParentSystem != null)
            {
                ParentSystem.SetControlPoint(cp, point);
            }

            HighestControlPoint = Math.Max(HighestControlPoint, cp);
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

        /// <summary>Writes one component of a control point's position.</summary>
        /// <param name="cp">The control point index.</param>
        /// <param name="component">Which of the three position components to write.</param>
        /// <param name="value">The value to write.</param>
        public void SetControlPointValueComponent(int cp, int component, float value)
        {
            GetControlPoint(cp).SetComponent(component, value);
        }

        /// <summary>
        /// Records every control point's current position and orientation as its previous-step state,
        /// along with the real time the move took. The root system calls this once per frame, after all
        /// consumers (including children, which share its control points) have run.
        /// </summary>
        /// <param name="elapsed">Real time covered since the previous snapshot, in seconds.</param>
        internal void SnapshotControlPointHistory(float elapsed)
        {
            foreach (var point in controlPoints.Values)
            {
                RecordStep(point, elapsed);
            }
        }

        /// <summary>
        /// Records the previous-step state of the control points this system overrides, which the root's
        /// own snapshot does not reach.
        /// </summary>
        /// <param name="elapsed">Real time covered since the previous snapshot, in seconds.</param>
        internal void SnapshotControlPointOverrideHistory(float elapsed)
        {
            if (controlPointOverrides == null)
            {
                return;
            }

            foreach (var point in controlPointOverrides.Values)
            {
                RecordStep(point, elapsed);
            }
        }

        private static void RecordStep(ControlPoint point, float elapsed)
        {
            point.PositionPrevious = point.Position;
            point.OrientationPrevious = point.Orientation;
            point.RotationPrevious = point.Rotation;
            point.PreviousStepTime = elapsed;
        }

        /// <summary>
        /// Gives this system its own control point at <paramref name="cp"/>, shadowing the one it would
        /// otherwise read from its parent, and returns it.
        /// </summary>
        /// <param name="cp">Control point index.</param>
        internal ControlPoint OverrideControlPoint(int cp)
        {
            controlPointOverrides ??= [];

            if (!controlPointOverrides.TryGetValue(cp, out var point))
            {
                point = new ControlPoint();
                controlPointOverrides.Add(cp, point);
            }

            return point;
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
            point.Orientation = Vector3.Transform(Vector3.UnitX, rotation);
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
        /// Real time the move recorded in <see cref="PositionPrevious"/> took, in seconds.
        /// </summary>
        public float PreviousStepTime { get; internal set; }

        /// <summary>
        /// How far the control point moved over the last recorded step.
        /// </summary>
        public Vector3 StepDelta => Position - PositionPrevious;

        /// <summary>
        /// The control point's velocity in units per second, derived from <see cref="PositionPrevious"/>
        /// over the real time that move took. Zero until a step has been recorded.
        /// </summary>
        /// <remarks>
        /// The move is timed by the root system's frame, not by the simulation step of whichever system
        /// is asking - a child running finer substeps would otherwise divide a whole frame of movement by
        /// a fraction of it and read a velocity several times too high.
        /// </remarks>
        public Vector3 Velocity
            => PreviousStepTime > 0f ? StepDelta / PreviousStepTime : Vector3.Zero;

        /// <summary>
        /// The orientation/direction of this control point.
        /// </summary>
        public Vector3 Orientation { get; set; }

        /// <summary>
        /// The orientation this control point had on the previous simulation step.
        /// </summary>
        public Vector3 OrientationPrevious { get; internal set; }

        /// <summary>
        /// The full rotation of this control point, when the source supplies one (e.g. a map entity's
        /// angles). Consumers fall back to synthesizing a frame from <see cref="Orientation"/> when unset,
        /// since most operators only drive the forward direction.
        /// </summary>
        public Quaternion? Rotation { get; set; }

        /// <summary>
        /// The full rotation this control point had on the previous simulation step, when one was recorded.
        /// </summary>
        public Quaternion? RotationPrevious { get; internal set; }

        /// <summary>
        /// The control point's full rotation, using <see cref="Rotation"/> when present and otherwise
        /// synthesizing a frame from the forward <see cref="Orientation"/> direction.
        /// </summary>
        public Quaternion GetRotation() => SynthesizeRotation(Rotation, Orientation);

        /// <summary>
        /// The control point's full rotation on the previous simulation step, derived the same way as
        /// <see cref="GetRotation"/>.
        /// </summary>
        public Quaternion GetPreviousRotation() => SynthesizeRotation(RotationPrevious, OrientationPrevious);

        private static Quaternion SynthesizeRotation(Quaternion? rotation, Vector3 orientation)
        {
            if (rotation is { } fullRotation)
            {
                return fullRotation;
            }

            if (orientation == Vector3.Zero)
            {
                return Quaternion.Identity;
            }

            return EntityTransformHelper.ForwardDirectionToQuaternion(orientation);
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
