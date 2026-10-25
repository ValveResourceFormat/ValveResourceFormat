using ValveResourceFormat.Blocks;
using ValveResourceFormat.Particles.Constraints;
using ValveResourceFormat.Particles.Operators;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Represents an entire particle system.
    /// </summary>
    public class ParticleSystemState
    {
        /// <summary>The highest control point index a system holds.</summary>
        public const int MaxControlPoint = 63;

        /// <summary>
        /// How many control points a system holds. Functions clamp control point parameters to it, so
        /// the top of that range reads an unbound point.
        /// </summary>
        public const int ControlPointCount = MaxControlPoint + 1;

        /// <summary>A state with no system behind it, read by functions evaluated outside a running system.</summary>
        public static readonly ParticleSystemState Default = new();

        /// <summary>The state of the system running this one as a child, or null for a root system.</summary>
        public ParticleSystemState? ParentSystem { get; }

        /// <summary>The states of the systems this one runs as children, in definition order.</summary>
        internal List<ParticleSystemState> Children { get; } = [];

        /// <summary>Creates the state for one system.</summary>
        /// <param name="parentSystem">The state of the system running this one as a child.</param>
        public ParticleSystemState(ParticleSystemState? parentSystem = null)
        {
            ParentSystem = parentSystem;
            parentSystem?.Children.Add(this);
        }

        /// <summary>The system this state belongs to. Null for <see cref="Default"/>.</summary>
        public ParticleSystemSimulation? Data { get; init; }

        /// <summary>The top of the tree this system belongs to.</summary>
        public ParticleSystemState Root => ParentSystem?.Root ?? this;

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
        /// comes from here, except the draws taken from <see cref="ThreadRandom"/>.
        /// </summary>
        internal ParticleRandom Random { get; } = new();

        /// <summary>
        /// The generator the functions of this system draw from when they do not use the shared
        /// random table.
        /// </summary>
        internal ThreadRandom ThreadRandom { get; } = new();

        /// <summary>
        /// Whether the system is running its initializers over new particles. Inputs that read a
        /// particle's initial value read its current one meanwhile, and particle numbers count spawns.
        /// </summary>
        internal bool InitializingParticles { get; set; }

        /// <summary>While initializers run, the collection index of the first particle of the batch.</summary>
        internal int InitializedFirst { get; set; }

        /// <summary>
        /// While an initializer runs, the collection index the batch ends at, else 0. A varying input
        /// evaluated up to the end of the batch's whole blocks of four goes on drawing for the block of
        /// the batch's remaining particles, which the call that initializes them draws again.
        /// </summary>
        internal int InitializerBatchEnd { get; set; }

        /// <summary>While an initializer runs, the collection index the particles of its current call end at, else 0.</summary>
        internal int InitializerRunEnd { get; set; }

        /// <summary>
        /// The particles the float inputs of a vector input are evaluated over to fill <paramref name="particles"/>:
        /// a fill that reaches the end of an initializer call goes on to the end of the batch, drawing for
        /// the blocks of the particles the later calls initialize.
        /// </summary>
        internal ParticleSpan VectorInputParticles(ParticleSpan particles)
        {
            var end = particles.Start + particles.Length;

            if (end != InitializerRunEnd || InitializerBatchEnd <= end)
            {
                return particles;
            }

            return particles.WithEnd(InitializerBatchEnd);
        }

        /// <summary>
        /// While initializers run, what <see cref="SpawnIndex"/> adds to a particle's index: the particles
        /// the system's emitters created in earlier frames, less <see cref="InitializedFirst"/>.
        /// </summary>
        internal int SpawnIndexBase { get; set; }

        /// <summary>
        /// While initializers run, what <see cref="ParticleNumber"/> adds to a particle's index:
        /// <see cref="SpawnIndexBase"/> plus the particles created earlier in the same emission pass.
        /// </summary>
        internal int ParticleNumberBase { get; set; }

        /// <summary>
        /// Gets the spawn index of a particle being initialized, which sequential and evenly distributed
        /// placement count from: the particles the emitters created in earlier frames plus its place in
        /// the batch.
        /// </summary>
        internal int SpawnIndex(int index) => SpawnIndexBase + index;

        /// <summary>Gets the particle number a particle being initialized reads, its spawn index counting this emission pass too.</summary>
        internal int ParticleNumber(int index) => ParticleNumberBase + index;

        /// <summary>
        /// While initializers run, the <see cref="ParticleField"/> bit mask of the attributes the
        /// particles' creator and the initializers before the running one have declared written.
        /// </summary>
        internal ulong InitializedFields { get; set; }

        /// <summary>
        /// The state <paramref name="particles"/> were left in by their initializers, matched by index, or
        /// the particles themselves while initializers still run or when no running system holds them.
        /// </summary>
        internal ParticleSpan InitialOf(ParticleSpan particles)
        {
            if (InitializingParticles || Data == null || particles.Start + particles.Length > Data.Particles.Count)
            {
                return particles;
            }

            return Data.Particles.Initial.Slice(particles.Start, particles.Length);
        }

        /// <summary>How many particles the system currently holds.</summary>
        public long ParticleCount { get; set; }

        /// <summary>
        /// The system's clock, in seconds. It starts below zero for a delayed child, and sits at
        /// <see cref="float.MinValue"/> for an endcap child until its parent's endcap starts it.
        /// </summary>
        public float Age { get; set; }

        /// <summary>Gets <see cref="Age"/>, or 0 for an endcap child that has not started.</summary>
        public float StartedAge => Age == float.MinValue ? 0f : Age;

        /// <summary>
        /// Multiplies every frame time handed to <see cref="ParticleSystemSimulation.Update"/>, 1 by
        /// default.
        /// </summary>
        public float TimeScale { get; set; } = 1f;

        /// <summary>The <see cref="Age"/> at the start of the step being simulated.</summary>
        internal float StepStartAge { get; set; } = 1e23f;

        /// <summary>
        /// The real time the system has been asked to simulate, summed over its frames. A minimum time
        /// step makes <see cref="Age"/> run ahead of it, and frames are skipped until it catches up.
        /// </summary>
        public float RequestedAge { get; internal set; }

        /// <summary>The length of the step being simulated, 0 before the first one.</summary>
        internal float CurrentStep { get; set; }

        /// <summary>
        /// The length of the previous step, which scales the inertia of the next. It also takes the
        /// whole frame time once a frame is done.
        /// </summary>
        internal float PreviousStep { get; set; } = 0.05f;

        /// <summary>
        /// Scales <paramref name="value"/> by the inverse of <see cref="PreviousStep"/>, turning a
        /// displacement over that step into a rate per second.
        /// </summary>
        internal float PerPreviousSecond(float value) => value / MathF.Max(1e-20f, PreviousStep);

        /// <summary>The <see cref="Age"/> the fade window of every function is measured from, set by a restart.</summary>
        internal float OperatorTimeBase { get; set; }

        /// <summary>World-space position of the render camera, updated once per simulation step.</summary>
        public Vector3 CameraPosition { get; set; }

        /// <summary>The local player, or null where there is none to find.</summary>
        public IParticlePlayer? Player { get; set; }

        /// <summary>
        /// How long each sequence of the sheet the drawing layer animates this system's particles with
        /// runs, in frames, or null when nothing it draws with carries a sheet. Set by whatever draws
        /// the system; <c>C_INIT_SequenceLifeTime</c> measures a particle's life against it.
        /// </summary>
        public float[]? SequenceDurations { get; set; }

        /// <summary>
        /// The runs of control points this system's <see cref="Operators.SetControlPointsToModelParticles"/>
        /// bind to particles, for whatever draws those particles as models to bind the models to.
        /// </summary>
        internal List<ModelParticleControlPoints> ModelParticleControlPoints { get; } = [];

        /// <summary>The scene lighting</summary>
        public IParticleLighting Lighting { get; set; } = IParticleLighting.Unlit;

        /// <summary>
        /// The control point the light in <see cref="TintCacheColor"/> was last sampled at for a random
        /// colour tint, or -1 before any was. Child systems tinting by the same control point reuse it.
        /// </summary>
        internal int TintCacheControlPoint { get; set; } = -1;

        /// <summary>The light last sampled at <see cref="TintCacheControlPoint"/>.</summary>
        internal Vector3 TintCacheColor { get; set; }

        /// <summary>The world geometry the system's collision and ground placement trace against.</summary>
        public IParticleCollision Collision { get; set; } = IParticleCollision.None;

        private CollisionPlaneSet?[]? collisionPlaneSets;

        /// <summary>
        /// The world collision plane sets of the whole system hierarchy, indexed by collision mode, held
        /// by the root system.
        /// </summary>
        internal CollisionPlaneSet?[] CollisionPlaneSets
            => ParentSystem?.CollisionPlaneSets ?? (collisionPlaneSets ??= new CollisionPlaneSet?[(int)ParticleCollisionMode.COLLISION_MODE_PER_PARTICLE_TRACE]);

        /// <summary>
        /// Whether the system is playing its endcap, the phase an effect runs once it has been told to
        /// stop. Only functions whose <c>m_nOpEndCapState</c> allows it run in each phase, so an endcap
        /// swaps part of the operator list for another.
        /// </summary>
        public bool InEndCap { get; internal set; }

        /// <summary>
        /// System age at which the endcap began, or <see cref="float.MinValue"/> when the system was
        /// stopped without one.
        /// </summary>
        public float EndCapStartAge { get; internal set; } = float.MinValue;

        /// <summary>How long the endcap has been running, or 0 outside it.</summary>
        public float EndCapAge => InEndCap ? Age - EndCapStartAge : 0f;

        /// <summary>
        /// Whether simulation is held in place, with every system below it. Set by
        /// <see cref="Operators.EndCapTimedFreeze"/> and once the system outlives
        /// <c>m_flStopSimulationAfterTime</c>; a stop clears it.
        /// </summary>
        public bool Frozen { get; set; }

        /// <summary>Whether the system was told to stop emitting.</summary>
        public bool EmissionStopped { get; internal set; }

        /// <summary>
        /// The parent's indices of the particles the event being handed to this system's emitters was
        /// raised on. Only set while a parent applies an event list, and null otherwise.
        /// </summary>
        internal IReadOnlyList<int>? EmitterEventParticles { get; set; }

        /// <summary>How many parent particles the event being handed to this system's emitters covers.</summary>
        internal int EmitterEventCount => EmitterEventParticles?.Count ?? 0;

        /// <summary>Whether a child of this system emits from <paramref name="eventType"/>.</summary>
        internal bool ListensFor(ParticleEventType eventType) => Data is { } simulation && (simulation.ListenedEvents & eventType) != 0;

        // Control Points

        private readonly ControlPoint?[] controlPoints = new ControlPoint?[ControlPointCount];

        private Dictionary<int, ControlPoint.SavedState>? functionWrites;
        private int? highestBeforeFunctionWrites;
        private bool functionsRunning;

        /// <summary>
        /// Whether the functions of this system's tree are running, so the control point writes made
        /// meanwhile are theirs rather than the host's. Only the root's value counts.
        /// </summary>
        internal bool FunctionsRunning
        {
            get => ParentSystem?.FunctionsRunning ?? functionsRunning;
            set => functionsRunning = value;
        }

        /// <summary>
        /// Gets one of this system's control points, creating it at its default on first use. An index
        /// outside 0 to <see cref="MaxControlPoint"/> gives a detached point that nothing reads back.
        /// </summary>
        /// <param name="cp">The control point index.</param>
        public ControlPoint GetControlPoint(int cp)
        {
            if ((uint)cp > MaxControlPoint)
            {
                return new ControlPoint();
            }

            return controlPoints[cp] ??= new ControlPoint(this, cp);
        }

        /// <summary>Gets the snapshot bound to a control point, or null when none is.</summary>
        public ParticleSnapshot? GetControlPointSnapshot(int cp)
            => (uint)cp <= MaxControlPoint ? controlPoints[cp]?.Snapshot : null;

        /// <summary>Gets the model bound to a control point, or null when none is.</summary>
        public Model? GetControlPointModel(int cp)
            => (uint)cp <= MaxControlPoint ? controlPoints[cp]?.Model : null;

        /// <summary>Binds a snapshot to a control point of this system and every system below it.</summary>
        internal void SetControlPointSnapshot(int cp, ParticleSnapshot snapshot)
        {
            if ((uint)cp > MaxControlPoint)
            {
                return;
            }

            GetControlPoint(cp).Snapshot = snapshot;

            foreach (var child in Children)
            {
                child.SetControlPointSnapshot(cp, snapshot);
            }
        }

        /// <summary>The highest control point index any write has reached; some emitters scale their rate by it.</summary>
        public int HighestControlPoint { get; private set; }

        internal void RaiseHighestControlPoint(int cp) => HighestControlPoint = Math.Max(HighestControlPoint, cp);

        /// <summary>
        /// Called before <paramref name="point"/> is written. A write by a function remembers the point as
        /// it was before the first such write, and a write by the host drops that memory, so
        /// <see cref="UndoFunctionControlPointWrites"/> keeps what the host set.
        /// </summary>
        internal void NoteControlPointWrite(ControlPoint point)
        {
            if (!FunctionsRunning)
            {
                functionWrites?.Remove(point.Index);

                if (highestBeforeFunctionWrites is { } highest)
                {
                    highestBeforeFunctionWrites = Math.Max(highest, point.Index);
                }

                return;
            }

            highestBeforeFunctionWrites ??= HighestControlPoint;
            (functionWrites ??= []).TryAdd(point.Index, point.Save());
        }

        /// <summary>
        /// Puts every control point of this system and the systems below it that functions have written
        /// back to how it was before they first did, leaving the points the host has written since.
        /// </summary>
        internal void UndoFunctionControlPointWrites()
        {
            if (functionWrites != null)
            {
                foreach (var (index, state) in functionWrites)
                {
                    controlPoints[index]!.Restore(state);
                }

                functionWrites.Clear();
            }

            if (highestBeforeFunctionWrites is { } highest)
            {
                HighestControlPoint = highest;
                highestBeforeFunctionWrites = null;
            }

            foreach (var child in Children)
            {
                child.UndoFunctionControlPointWrites();
            }
        }

        /// <summary>
        /// Copies a control point's position and orientation into this system's one at
        /// <paramref name="cp"/> and into every system below it, and its attachment into this system's one only.
        /// </summary>
        /// <param name="cp">The control point index.</param>
        /// <param name="point">The control point to copy.</param>
        public void SetControlPoint(int cp, ControlPoint point)
        {
            var target = GetControlPoint(cp);
            target.Position = point.Position;
            target.SetOrientation(point.Forward, point.Left, point.Up);
            target.AttachType = point.AttachType;
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
        /// Set the orientation of a control point from a forward direction alone; left and up are
        /// built around it.
        /// </summary>
        /// <param name="cp">Control point index.</param>
        /// <param name="orientation">Forward direction to assign.</param>
        public void SetControlPointOrientation(int cp, Vector3 orientation)
        {
            GetControlPoint(cp).Orientation = orientation;
        }

        /// <summary>Set the full orientation of a control point.</summary>
        /// <param name="cp">Control point index.</param>
        /// <param name="forward">The forward axis.</param>
        /// <param name="left">The left axis.</param>
        /// <param name="up">The up axis.</param>
        public void SetControlPointOrientation(int cp, Vector3 forward, Vector3 left, Vector3 up)
        {
            GetControlPoint(cp).SetOrientation(forward, left, up);
        }

        /// <summary>
        /// Set the full rotation of a control point.
        /// </summary>
        /// <param name="cp">Control point index.</param>
        /// <param name="rotation">Full rotation to assign.</param>
        public void SetControlPointRotation(int cp, Quaternion rotation)
        {
            GetControlPoint(cp).SetOrientation(rotation);
        }

        /// <summary>The system a function's control point writes go to under <paramref name="mode"/>.</summary>
        internal ParticleSystemState GetSetParentTarget(ParticleParentSetMode mode) => mode switch
        {
            ParticleParentSetMode.PARTICLE_SET_PARENT_IMMEDIATE => ParentSystem ?? this,
            ParticleParentSetMode.PARTICLE_SET_PARENT_ROOT => Root,
            _ => this,
        };

        /// <summary>
        /// Records every control point's current position and orientation as its previous state, and
        /// <paramref name="elapsed"/> as the time that move took and the previous step length.
        /// </summary>
        internal void UpdatePreviousControlPoints(float elapsed)
        {
            PreviousStep = elapsed;

            foreach (var point in controlPoints)
            {
                point?.RecordStep(elapsed);
            }
        }

        /// <summary>
        /// Moves every control point that has changed back to a change at age 0, for a system whose
        /// clock restarts while it keeps its control points.
        /// </summary>
        internal void RewindControlPointChanges()
        {
            foreach (var point in controlPoints)
            {
                if (point == null)
                {
                    continue;
                }

                point.RewindChange();
                point.RecordStep(0f);
            }
        }
    }

    /// <summary>
    /// Control point used in Valve particle systems. Control point 0 is the default spawn position.
    /// Each system holds its own copy, and a write reaches the same control point of every system
    /// below the one it is made on, never the systems above.
    /// </summary>
    public class ControlPoint
    {
        private Vector3 position;
        private Vector3 forward = Vector3.UnitX;
        private Vector3 left = Vector3.UnitY;
        private Vector3 up = Vector3.UnitZ;

        /// <summary>Creates a control point that belongs to no system.</summary>
        public ControlPoint()
        {
        }

        internal ControlPoint(ParticleSystemState owner, int index)
        {
            Owner = owner;
            Index = index;
        }

        /// <summary>The system holding this control point, whose age times its changes.</summary>
        internal ParticleSystemState? Owner { get; }

        /// <summary>The control point index within <see cref="Owner"/>.</summary>
        internal int Index { get; }

        /// <summary>
        /// The position of this control point. Sometimes this is used for things other than position.
        /// Setting a different value counts as a change of the control point.
        /// </summary>
        public Vector3 Position
        {
            get => position;
            set
            {
                Owner?.NoteControlPointWrite(this);

                if (value != position)
                {
                    position = value;
                    Stamp();
                }

                if (Owner == null)
                {
                    return;
                }

                Owner.RaiseHighestControlPoint(Index);

                foreach (var child in Owner.Children)
                {
                    child.GetControlPoint(Index).Position = value;
                }
            }
        }

        /// <summary>The forward axis, (1, 0, 0) until something orients the control point.</summary>
        public Vector3 Forward => forward;

        /// <summary>The left axis, (0, 1, 0) until something orients the control point.</summary>
        public Vector3 Left => left;

        /// <summary>The up axis, (0, 0, 1) until something orients the control point.</summary>
        public Vector3 Up => up;

        /// <summary>Sets the three axes. A change of any of them counts as a change of the control point.</summary>
        /// <param name="forward">The forward axis.</param>
        /// <param name="left">The left axis.</param>
        /// <param name="up">The up axis.</param>
        public void SetOrientation(Vector3 forward, Vector3 left, Vector3 up)
        {
            Owner?.NoteControlPointWrite(this);

            if (forward != this.forward || left != this.left || up != this.up)
            {
                this.forward = forward;
                this.left = left;
                this.up = up;
                Stamp();
            }

            if (Owner == null)
            {
                return;
            }

            Owner.RaiseHighestControlPoint(Index);

            foreach (var child in Owner.Children)
            {
                child.GetControlPoint(Index).SetOrientation(forward, left, up);
            }
        }

        /// <summary>Sets the three axes from a rotation.</summary>
        /// <param name="rotation">The rotation, which carries +X to forward.</param>
        public void SetOrientation(Quaternion rotation)
            => SetOrientation(Vector3.Transform(Vector3.UnitX, rotation), Vector3.Transform(Vector3.UnitY, rotation), Vector3.Transform(Vector3.UnitZ, rotation));

        /// <summary>
        /// The forward direction of this control point. Setting it builds left and up around the new
        /// forward, and a zero direction restores the default axes.
        /// </summary>
        public Vector3 Orientation
        {
            get => forward;
            set
            {
                if (value == Vector3.Zero)
                {
                    SetOrientation(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
                    return;
                }

                var direction = Vector3.Normalize(value);
                var (newLeft, newUp) = ParticleMath.VectorVectors(direction);
                SetOrientation(direction, newLeft, newUp);
            }
        }

        /// <summary>
        /// The rotation carrying +X to <see cref="Forward"/>, +Y to <see cref="Left"/> and +Z to
        /// <see cref="Up"/>; never null when read. Setting null restores the default axes.
        /// </summary>
        public Quaternion? Rotation
        {
            get => GetRotation();
            set => SetOrientation(value ?? Quaternion.Identity);
        }

        /// <summary>The age of <see cref="Owner"/> when the position or orientation last changed, or -1 when neither has.</summary>
        public float ChangeTime { get; internal set; } = -1f;

        /// <summary>
        /// How long ago the control point last changed. One more than the owner's age when it never has.
        /// </summary>
        public float ChangeAge => (Owner?.Age ?? 0f) - ChangeTime;

        private void Stamp() => ChangeTime = Owner?.Age ?? 0f;

        /// <summary>The position, axes and change time of a control point.</summary>
        internal readonly record struct SavedState(Vector3 Position, Vector3 Forward, Vector3 Left, Vector3 Up, float ChangeTime);

        internal SavedState Save() => new(position, forward, left, up, ChangeTime);

        internal void Restore(SavedState state)
        {
            position = state.Position;
            forward = state.Forward;
            left = state.Left;
            up = state.Up;
            ChangeTime = state.ChangeTime;
        }

        internal void RewindChange()
        {
            if (ChangeTime != -1f)
            {
                ChangeTime = 0f;
            }
        }

        /// <summary>
        /// The position this control point had at the end of the previous frame, used to derive the
        /// control point's velocity.
        /// </summary>
        public Vector3 PositionPrevious { get; private set; }

        /// <summary>The forward axis at the end of the previous frame.</summary>
        public Vector3 ForwardPrevious { get; private set; } = Vector3.UnitX;

        /// <summary>The left axis at the end of the previous frame.</summary>
        public Vector3 LeftPrevious { get; private set; } = Vector3.UnitY;

        /// <summary>The up axis at the end of the previous frame.</summary>
        public Vector3 UpPrevious { get; private set; } = Vector3.UnitZ;

        /// <summary>The forward direction at the end of the previous frame.</summary>
        public Vector3 OrientationPrevious => ForwardPrevious;

        /// <summary>The rotation at the end of the previous frame.</summary>
        public Quaternion? RotationPrevious => GetPreviousRotation();

        /// <summary>
        /// Real time the move recorded in <see cref="PositionPrevious"/> took, in seconds.
        /// </summary>
        public float PreviousStepTime { get; private set; }

        internal void RecordStep(float elapsed)
        {
            PositionPrevious = position;
            ForwardPrevious = forward;
            LeftPrevious = left;
            UpPrevious = up;
            PreviousStepTime = elapsed;
        }

        /// <summary>
        /// How far the control point moved over the last recorded step.
        /// </summary>
        public Vector3 StepDelta => Position - PositionPrevious;

        /// <summary>
        /// The control point's velocity in units per second, derived from <see cref="PositionPrevious"/>
        /// over the time that move took. Zero until a step has been recorded.
        /// </summary>
        public Vector3 Velocity
            => PreviousStepTime > 0f ? StepDelta / PreviousStepTime : Vector3.Zero;

        /// <summary>
        /// How far from the previous to the current state <paramref name="time"/> falls within the step
        /// <paramref name="state"/> is simulating, clamped to [0, 1]. A time at the end of the step, or a
        /// zero step, reads as current.
        /// </summary>
        /// <param name="state">The asking system, whose clock <paramref name="time"/> is on.</param>
        /// <param name="time">The time to locate, such as a particle's creation time.</param>
        public static float StepFraction(ParticleSystemState state, float time)
        {
            var step = state.CurrentStep;

            if (step == 0f || state.Age == time)
            {
                return 1f;
            }

            return MathUtils.Saturate((step - (state.Age - time)) / step);
        }

        /// <summary>
        /// The position at <paramref name="time"/> within the step <see cref="Owner"/> is simulating,
        /// between the previous and the current position. A point without an owner answers with its
        /// current position.
        /// </summary>
        /// <param name="time">The time to locate, such as a particle's creation time.</param>
        public Vector3 GetPositionAtTime(float time)
            => Owner == null ? position : MathUtils.Lerp(PositionPrevious, position, StepFraction(Owner, time));

        /// <summary>
        /// The axes as rows and <see cref="GetPositionAtTime"/> as the translation. The axes are the
        /// previous ones at exactly the start of the step and the current ones otherwise.
        /// </summary>
        /// <param name="time">The time to locate, such as a particle's creation time.</param>
        public Matrix4x4 GetBasisAtTime(float time)
        {
            var atStepStart = Owner != null && Owner.CurrentStep != 0f && time == Owner.Age - Owner.CurrentStep;
            var basis = atStepStart ? GetPreviousRotationMatrix() : GetRotationMatrix();
            basis.Translation = GetPositionAtTime(time);

            return basis;
        }

        /// <summary>The rotation matrix with <see cref="Forward"/>, <see cref="Left"/> and <see cref="Up"/> as its rows.</summary>
        public Matrix4x4 GetRotationMatrix() => MathUtils.CreateBasis(forward, left, up);

        /// <summary>The rotation matrix with <see cref="Position"/> as its translation.</summary>
        public Matrix4x4 GetTransform()
        {
            var transform = GetRotationMatrix();
            transform.Translation = Position;

            return transform;
        }

        /// <summary>The rotation matrix of the previous frame's axes.</summary>
        public Matrix4x4 GetPreviousRotationMatrix() => MathUtils.CreateBasis(ForwardPrevious, LeftPrevious, UpPrevious);

        /// <summary>The control point's rotation.</summary>
        public Quaternion GetRotation() => Quaternion.CreateFromRotationMatrix(GetRotationMatrix());

        /// <summary>The control point's rotation at the end of the previous frame.</summary>
        public Quaternion GetPreviousRotation() => Quaternion.CreateFromRotationMatrix(GetPreviousRotationMatrix());

        /// <summary>
        /// Different attachment styles.
        /// </summary>
        public ParticleAttachment AttachType { get; set; }

        /// <summary>The snapshot bound to this control point, or null when none is.</summary>
        internal ParticleSnapshot? Snapshot { get; set; }

        private Model? model;

        /// <summary>
        /// The model bound to this control point, such as the model an effect is attached to, or null
        /// when none is. Model inputs of type <c>PM_TYPE_CONTROL_POINT</c> read it. Setting it does not
        /// count as a change of the control point.
        /// </summary>
        public Model? Model
        {
            get => model;
            set
            {
                model = value;

                if (Owner == null)
                {
                    return;
                }

                foreach (var child in Owner.Children)
                {
                    child.GetControlPoint(Index).Model = value;
                }
            }
        }

        private IParticleModelObject? modelObject;
        private ControlPointModelObject? standInModelObject;

        /// <summary>
        /// The object carrying a model this control point is bound to, such as the entity an effect is
        /// attached to, or null when none is. Setting it also sets <see cref="Model"/>, and like that it
        /// does not count as a change of the control point.
        /// </summary>
        public IParticleModelObject? ModelObject
        {
            get => modelObject;
            set
            {
                modelObject = value;
                Model = value?.Model;

                if (Owner == null)
                {
                    return;
                }

                foreach (var child in Owner.Children)
                {
                    child.GetControlPoint(Index).ModelObject = value;
                }
            }
        }

        /// <summary>
        /// The object the model functions read: <see cref="ModelObject"/>, or for a control point that
        /// carries only a <see cref="Model"/>, that model in its bind pose placed on the control point.
        /// </summary>
        internal IParticleModelObject? ResolveModelObject()
        {
            if (modelObject != null)
            {
                return modelObject;
            }

            if (model == null)
            {
                return null;
            }

            if (standInModelObject?.Model != model)
            {
                standInModelObject = new ControlPointModelObject(this, model);
            }

            return standInModelObject;
        }

        /// <summary>
        /// The hitboxes or bones of the control point's model, as the model functions of this system
        /// last read them.
        /// </summary>
        internal HitBoxCache HitBoxes => hitBoxes ??= new HitBoxCache();

        private HitBoxCache? hitBoxes;

        private sealed class ControlPointModelObject(ControlPoint controlPoint, Model model) : IParticleModelObject
        {
            public Model Model => model;

            public Matrix4x4 Transform
            {
                get
                {
                    var transform = controlPoint.GetTransform();
                    return transform;
                }
            }
        }

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
