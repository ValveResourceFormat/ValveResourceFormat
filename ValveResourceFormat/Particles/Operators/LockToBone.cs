using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Keeps particles on the hitboxes, or bones, of the model its model input reads: the box and
    /// the place in it recorded at creation are turned back into a position every step, which the
    /// particle either snaps to or follows the box's motion toward, fading out over its life. The
    /// rotation of the box can be passed on to the particle's normal or rotation angles.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LockToBone">C_OP_LockToBone</seealso>
    class LockToBone : ParticleFunctionOperator
    {
        private enum RotationLockType
        {
            PARTICLE_ROTATION_LOCK_NONE = 0,
            PARTICLE_ROTATION_LOCK_ROTATIONS = 1,
            PARTICLE_ROTATION_LOCK_NORMAL = 2,
        }

        private readonly int modelControlPoint;
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly float lifeTimeFadeStart;
        private readonly float lifeTimeFadeEnd;
        private readonly float jumpThreshold = 100f;
        private readonly float prevPosScale = 1f;
        private readonly string hitboxSetName = "default";
        private readonly bool rigid;
        private readonly bool useBones;
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly ParticleField fieldOutputPrev = ParticleField.PositionPrevious;
        private readonly RotationLockType rotationSetType = RotationLockType.PARTICLE_ROTATION_LOCK_NONE;
        private readonly bool rigidRotationLock;
        private readonly IVectorProvider rotation = new LiteralVectorProvider(Vector3.Zero);
        private readonly INumberProvider rotationLerp = new LiteralNumberProvider(1f);

        private Quaternion[] boxRotations = [];

        public LockToBone(ParticleDefinitionParser parse) : base(parse)
        {
            modelControlPoint = ModelInput.ControlPoint(parse);
            transformInput = parse.TransformInput("m_transformInput", transformInput);
            lifeTimeFadeStart = parse.Float("m_flLifeTimeFadeStart", lifeTimeFadeStart);
            lifeTimeFadeEnd = parse.Float("m_flLifeTimeFadeEnd", lifeTimeFadeEnd);
            jumpThreshold = parse.Float("m_flJumpThreshold", jumpThreshold);
            jumpThreshold *= jumpThreshold;
            prevPosScale = parse.Float("m_flPrevPosScale", prevPosScale);
            hitboxSetName = parse.String("m_HitboxSetName", hitboxSetName);
            rigid = parse.Boolean("m_bRigid", rigid);
            useBones = parse.Boolean("m_bUseBones", useBones);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            fieldOutputPrev = parse.ParticleField("m_nFieldOutputPrev", fieldOutputPrev);
            rotationSetType = parse.Enum("m_nRotationSetType", rotationSetType);
            rigidRotationLock = parse.Boolean("m_bRigidRotationLock", rigidRotationLock);
            rotation = parse.VectorProvider("m_vecRotation", rotation);
            rotationLerp = parse.NumberProvider("m_flRotLerp", rotationLerp);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (frameTime <= 0f || modelControlPoint < 0 || HitBoxCache.Refresh(particleSystemState, modelControlPoint, hitboxSetName, useBones) is not { IsStable: true } cache)
            {
                return;
            }

            var current = cache.Current;
            var previous = cache.Previous;

            var position = transformInput.NextTransform(particleSystemState).Translation;
            var previousPosition = transformInput.NextTransformAtTime(ref Particle.Default, particleSystemState, particleSystemState.StepStartAge).Translation;
            var jumped = Vector3.DistanceSquared(position, previousPosition) > jumpThreshold;

            var fadeEnd = lifeTimeFadeEnd > 0f ? lifeTimeFadeEnd : 1e20f;
            var inverseFade = lifeTimeFadeEnd > lifeTimeFadeStart ? 1f / (lifeTimeFadeEnd - lifeTimeFadeStart) : 0f;
            var prevScale = jumped ? 1f : prevPosScale;
            var writePrevious = prevScale != 0f && fieldOutputPrev != ParticleField.NoneDisabled;
            var lockRotation = rotationSetType != RotationLockType.PARTICLE_ROTATION_LOCK_NONE;

            if (lockRotation)
            {
                BuildBoxRotations(current, previous);
            }

            var time = particleSystemState.Age;

            foreach (ref var particle in particles.Current)
            {
                var age = time - particle.CreationTime;
                var index = particle.HitboxIndex;

                if ((age >= fadeEnd && !jumped) || (uint)index >= (uint)current.Length)
                {
                    continue;
                }

                var stepFraction = MathF.Min(age, frameTime) / frameTime;
                var fraction = particle.HitboxOffsetPosition;
                var target = current[index].PointAt(fraction);
                var weight = (1f - ((age - lifeTimeFadeStart) * inverseFade)) * strength;

                if (rigid)
                {
                    particle.SetVector(fieldOutput, target);

                    if (writePrevious)
                    {
                        particle.SetVector(fieldOutputPrev, target);
                    }
                }
                else
                {
                    var delta = (target - previous[index].PointAt(fraction)) * stepFraction;

                    if (lifeTimeFadeStart < age && !jumped)
                    {
                        delta *= weight;
                    }

                    particle.SetVector(fieldOutput, particle.GetVector(fieldOutput) + delta);

                    if (writePrevious)
                    {
                        particle.SetVector(fieldOutputPrev, particle.GetVector(fieldOutputPrev) + (delta * prevScale));
                    }
                }

                if (lockRotation)
                {
                    LockRotation(ref particle, particleSystemState, boxRotations[index], weight * stepFraction);
                }
            }
        }

        private void BuildBoxRotations(ReadOnlySpan<HitBox> current, ReadOnlySpan<HitBox> previous)
        {
            if (boxRotations.Length < current.Length)
            {
                boxRotations = new Quaternion[current.Length];
            }

            for (var i = 0; i < current.Length; i++)
            {
                if (rigidRotationLock)
                {
                    boxRotations[i] = current[i].Orientation;
                    continue;
                }

                var previousOrientation = previous[i].Orientation;
                var lengthSquared = previousOrientation.LengthSquared();
                var inverse = lengthSquared == 0f ? Quaternion.Identity : Quaternion.Conjugate(previousOrientation) * (1f / lengthSquared);
                boxRotations[i] = current[i].Orientation * inverse;
            }
        }

        private void LockRotation(ref Particle particle, ParticleSystemState particleSystemState, Quaternion boxRotation, float weight)
        {
            if (rigidRotationLock)
            {
                var angles = rotation.NextVector(ref particle, particleSystemState);
                boxRotation *= EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(angles.Y, angles.Z, angles.X));
                weight = rotationLerp.NextNumber(ref particle, particleSystemState);
            }

            if (rotationSetType == RotationLockType.PARTICLE_ROTATION_LOCK_NORMAL)
            {
                var target = rigidRotationLock ? Vector3.Transform(Vector3.UnitX, boxRotation) : Vector3.Transform(particle.Normal, boxRotation);
                particle.Normal += (target - particle.Normal) * weight;
                return;
            }

            var pitchYawRoll = Vector3.DegreesToRadians(EntityTransformHelper.ToEulerAngles(boxRotation));
            var targetRotation = new Vector3(pitchYawRoll.Y, pitchYawRoll.X, pitchYawRoll.Z);
            particle.Rotation += (targetRotation - particle.Rotation) * weight;
        }
    }
}
