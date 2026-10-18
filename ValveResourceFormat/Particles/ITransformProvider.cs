using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Provides a transformation matrix that may be derived from a control point or other source.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/CParticleTransformInput">CParticleTransformInput</seealso>
    internal interface ITransformProvider
    {
        /// <summary>
        /// Returns a transformation matrix from the provider.
        /// </summary>
        Matrix4x4 NextTransform(ref Particle particle, ParticleSystemState renderState);

        /// <summary>
        /// Returns a transformation matrix using system-level state only.
        /// </summary>
        Matrix4x4 NextTransform(ParticleSystemState renderState)
            => NextTransform(ref Particle.Default, renderState);

        /// <summary>
        /// Returns the transform as it stood at <paramref name="time"/> within the step being simulated, so a
        /// particle spawned part way through a step starts from where its source was at that moment.
        /// Providers without a history answer with their current transform.
        /// </summary>
        Matrix4x4 NextTransformAtTime(ref Particle particle, ParticleSystemState renderState, float time)
            => NextTransform(ref particle, renderState);

        /// <summary>
        /// Returns the current rotation of the transform with its position as it stood at
        /// <paramref name="time"/> within the step being simulated. Providers without a history answer
        /// with their current transform.
        /// </summary>
        Matrix4x4 NextTransformWithPositionAtTime(ref Particle particle, ParticleSystemState renderState, float time)
            => NextTransform(ref particle, renderState);

        /// <summary>
        /// Gets the orientation, meaning the forward direction, that this provider carries.
        /// </summary>
        /// <remarks>
        /// Providers that only place things answer <see langword="false"/> rather than reporting the +X an
        /// identity rotation would give, so a caller does not overwrite a direction with a meaningless one.
        /// Returning false is the default, so a provider opts in to having an orientation.
        /// </remarks>
        /// <returns>Whether the provider has an orientation to report.</returns>
        bool TryGetOrientation(ref Particle particle, ParticleSystemState renderState, out Vector3 orientation)
        {
            orientation = Vector3.Zero;
            return false;
        }
    }

    /// <summary>
    /// Provides a transform from a control point's axes and position, or from one control point
    /// picked at random out of a range.
    /// </summary>
    readonly struct ControlPointTransformProvider : ITransformProvider
    {
        private readonly int controlPoint;
        private readonly bool useOrientation;
        private readonly int rangeMax;
        private readonly float endGrowthTime;
        private readonly bool isRange;

        public ControlPointTransformProvider(int controlPoint, bool useOrientation)
        {
            this.controlPoint = controlPoint;
            this.useOrientation = useOrientation;
        }

        /// <summary>
        /// A transform that picks one of the control points from <paramref name="controlPoint"/> to
        /// <paramref name="rangeMax"/> that have been set, admitting the higher ones over
        /// <paramref name="endGrowthTime"/> seconds of the system's life.
        /// </summary>
        public ControlPointTransformProvider(int controlPoint, bool useOrientation, int rangeMax, float endGrowthTime)
            : this(controlPoint, useOrientation)
        {
            this.rangeMax = rangeMax;
            this.endGrowthTime = endGrowthTime;
            isRange = true;
        }

        /// <summary>
        /// Control point 0 with its orientation, matching the defaults a transform input
        /// deserializes to when the effect leaves <c>m_TransformInput</c> unset.
        /// </summary>
        public ControlPointTransformProvider() : this(0, true)
        {
        }

        private int PickControlPoint(ParticleSystemState state)
        {
            if (!isRange)
            {
                return controlPoint;
            }

            var low = controlPoint;
            var high = Math.Max(controlPoint, rangeMax);
            Span<int> candidates = stackalloc int[ParticleSystemState.MaxControlPoint + 1];
            var count = 0;

            for (var cp = Math.Max(low, 0); cp <= Math.Min(high, ParticleSystemState.MaxControlPoint); cp++)
            {
                if (state.GetControlPoint(cp).ChangeTime != -1f)
                {
                    candidates[count++] = cp;
                }
            }

            var growth = endGrowthTime == 0f ? 1f : MathF.Min(state.Age, endGrowthTime) / endGrowthTime;

            if (growth < 1f)
            {
                var limit = MathF.Floor((MathUtils.Saturate(growth) * (high - low)) + low);
                var candidate = low + state.Random.NextIndex((int)limit - low + 1);

                if (candidates[..count].Contains(candidate))
                {
                    return candidate;
                }
            }
            else if (count == 0)
            {
                return controlPoint;
            }

            var index = state.Random.NextIndex(count);

            return count == 0 ? controlPoint : candidates[Math.Min(index, count - 1)];
        }

        private Matrix4x4 Rotation(ControlPoint cp)
            => useOrientation ? cp.GetRotationMatrix() : Matrix4x4.Identity;

        /// <inheritdoc/>
        public Matrix4x4 NextTransform(ref Particle particle, ParticleSystemState renderState)
        {
            var cp = renderState.GetControlPoint(PickControlPoint(renderState));
            var transform = Rotation(cp);
            transform.Translation = cp.Position;

            return transform;
        }

        /// <inheritdoc/>
        public Matrix4x4 NextTransformAtTime(ref Particle particle, ParticleSystemState renderState, float time)
        {
            var cp = renderState.GetControlPoint(PickControlPoint(renderState));
            var fraction = ControlPoint.StepFraction(renderState, time);

            var transform = useOrientation
                ? Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(cp.GetPreviousRotation(), cp.GetRotation(), fraction))
                : Matrix4x4.Identity;
            transform.Translation = MathUtils.Lerp(cp.PositionPrevious, cp.Position, fraction);

            return transform;
        }

        /// <inheritdoc/>
        public Matrix4x4 NextTransformWithPositionAtTime(ref Particle particle, ParticleSystemState renderState, float time)
        {
            var cp = renderState.GetControlPoint(PickControlPoint(renderState));
            var transform = Rotation(cp);
            transform.Translation = cp.GetPositionAtTime(time);

            return transform;
        }

        /// <inheritdoc/>
        public bool TryGetOrientation(ref Particle particle, ParticleSystemState renderState, out Vector3 orientation)
        {
            if (!useOrientation)
            {
                orientation = Vector3.Zero;
                return false;
            }

            orientation = renderState.GetControlPoint(PickControlPoint(renderState)).Forward;
            return true;
        }

        /// <summary>
        /// Transforms a local offset into control point <paramref name="cp"/>'s frame (rotation + translation).
        /// </summary>
        public static Vector3 TransformPosition(ParticleSystemState state, int cp, Vector3 localOffset)
            => Vector3.Transform(localOffset, new ControlPointTransformProvider(cp, true).NextTransform(ref Particle.Default, state));

        /// <summary>
        /// Rotates a local direction into control point <paramref name="cp"/>'s frame (rotation only, no translation).
        /// </summary>
        public static Vector3 TransformDirection(ParticleSystemState state, int cp, Vector3 localDir)
            => Vector3.TransformNormal(localDir, state.GetControlPoint(cp).GetRotationMatrix());

        /// <summary>
        /// Rotates a single Euler angle, given in radians as <paramref name="angleField"/>'s component, by control
        /// point <paramref name="cp"/>'s orientation, and returns that same component of the composed rotation.
        /// </summary>
        public static float TransformAngle(ParticleSystemState state, int cp, ParticleField angleField, float radians)
        {
            var degrees = float.RadiansToDegrees(radians);
            var angles = angleField switch
            {
                ParticleField.Pitch => new Vector3(degrees, 0f, 0f),
                ParticleField.Yaw => new Vector3(0f, degrees, 0f),
                _ => new Vector3(0f, 0f, degrees),
            };

            var result = TransformAngles(state, cp, angles);

            return float.DegreesToRadians(angleField switch
            {
                ParticleField.Pitch => result.X,
                ParticleField.Yaw => result.Y,
                _ => result.Z,
            });
        }

        /// <summary>
        /// Rotates (pitch, yaw, roll) angles in degrees by control point <paramref name="cp"/>'s
        /// orientation and returns the angles of the composed rotation.
        /// </summary>
        public static Vector3 TransformAngles(ParticleSystemState state, int cp, Vector3 pitchYawRoll)
        {
            var composed = EntityTransformHelper.EulerAnglesToRotationMatrix(pitchYawRoll)
                * state.GetControlPoint(cp).GetRotationMatrix();

            return EntityTransformHelper.ToEulerAngles(Quaternion.CreateFromRotationMatrix(composed));
        }
    }

    /// <summary>
    /// PT_TYPE_NAMED_VALUE. The system's transform named value, as it stands now when following it and
    /// as the system started with otherwise, blended from the previous step's value within a step.
    /// The identity when the system has none.
    /// </summary>
    readonly struct NamedValueTransformProvider : ITransformProvider
    {
        private readonly string name;
        private readonly bool follow;
        private readonly bool useOrientation;

        public NamedValueTransformProvider(ParticleDefinitionParser parse)
        {
            name = parse.String("m_NamedValue");
            follow = parse.Boolean("m_bFollowNamedValue");
            useOrientation = parse.Boolean("m_bUseOrientation", true);
        }

        private bool TryGet(ParticleSystemState state, out ParticleNamedValue value, out ParticleNamedValue previous)
        {
            if (state.Data is { } system)
            {
                return system.NamedValues.TryGetTransform(name, follow, out value, out previous);
            }

            value = previous = ParticleNamedValue.Default(ParticleNamedValueType.Transform);
            return false;
        }

        private Matrix4x4 Compose(Quaternion rotation, Vector3 position)
        {
            var transform = useOrientation ? Matrix4x4.CreateFromQuaternion(rotation) : Matrix4x4.Identity;
            transform.Translation = position;

            return transform;
        }

        /// <inheritdoc/>
        public Matrix4x4 NextTransform(ref Particle particle, ParticleSystemState renderState)
        {
            TryGet(renderState, out var value, out _);
            return Compose(value.Rotation, value.Vector);
        }

        /// <inheritdoc/>
        public Matrix4x4 NextTransformAtTime(ref Particle particle, ParticleSystemState renderState, float time)
        {
            TryGet(renderState, out var value, out var previous);
            var fraction = ControlPoint.StepFraction(renderState, time);

            return Compose(Quaternion.Slerp(previous.Rotation, value.Rotation, fraction), MathUtils.Lerp(previous.Vector, value.Vector, fraction));
        }

        /// <inheritdoc/>
        public Matrix4x4 NextTransformWithPositionAtTime(ref Particle particle, ParticleSystemState renderState, float time)
        {
            TryGet(renderState, out var value, out var previous);
            var fraction = ControlPoint.StepFraction(renderState, time);

            return Compose(value.Rotation, MathUtils.Lerp(previous.Vector, value.Vector, fraction));
        }

        /// <inheritdoc/>
        public bool TryGetOrientation(ref Particle particle, ParticleSystemState renderState, out Vector3 orientation)
        {
            TryGet(renderState, out var value, out _);
            orientation = Vector3.Transform(Vector3.UnitX, value.Rotation);
            return useOrientation;
        }
    }

    /// <summary>
    /// A transform provider that always returns the identity matrix.
    /// </summary>
    readonly struct IdentityTransformProvider : ITransformProvider
    {
        /// <inheritdoc/>
        public Matrix4x4 NextTransform(ref Particle particle, ParticleSystemState renderState)
        {
            return Matrix4x4.Identity;
        }
    }
}
