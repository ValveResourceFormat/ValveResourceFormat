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

        private readonly ulong storedFields;

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

            storedFields = fieldOutput.Mask();

            if (fieldOutputPrev != ParticleField.NoneDisabled)
            {
                storedFields |= fieldOutputPrev.Mask();
            }

            if (rotationSetType == RotationLockType.PARTICLE_ROTATION_LOCK_ROTATIONS)
            {
                storedFields |= ParticleField.Yaw.Mask() | ParticleField.Pitch.Mask() | ParticleField.Roll.Mask();
            }
            else if (rotationSetType == RotationLockType.PARTICLE_ROTATION_LOCK_NORMAL)
            {
                storedFields |= ParticleField.Normal.Mask();
            }
        }

        /// <inheritdoc/>
        public override ulong StoredFields => storedFields;

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            // The cache never holds boxes from a snapshot, whose hitbox index table would remap the hitbox
            // indices and move nothing that step
            if (frameTime <= 0f || modelControlPoint < 0 || HitBoxCache.Refresh(particleSystemState, modelControlPoint, hitboxSetName, useBones) is not { IsStable: true } cache)
            {
                return;
            }

            var current = cache.Current;
            var previous = cache.Previous;

            var position = transformInput.NextTransform(particleSystemState).Translation;
            var previousPosition = transformInput.NextTransformAtTime(particleSystemState, particleSystemState.StepStartAge).Translation;
            var jumped = Vector3.DistanceSquared(position, previousPosition) > jumpThreshold;

            var fadeEnd = lifeTimeFadeEnd > 0f ? lifeTimeFadeEnd : ParticleMath.NoFadeEnd;
            var inverseFade = lifeTimeFadeEnd > lifeTimeFadeStart ? 1f / (lifeTimeFadeEnd - lifeTimeFadeStart) : 0f;
            var prevScale = jumped ? 1f : prevPosScale;
            var writePrevious = prevScale != 0f && ParticleBuffer.IsVector(fieldOutputPrev);
            var lockRotation = rotationSetType != RotationLockType.PARTICLE_ROTATION_LOCK_NONE;

            if (lockRotation)
            {
                BuildBoxRotations(current, previous);
            }

            var particleSpan = particles.Current;
            var ages = particleSpan.ReadAges();
            var hitboxIndices = particleSpan.ReadIntegers(ParticleField.HitboxIndex);
            var hitboxOffsets = particleSpan.ReadVectors(ParticleField.HitboxOffsetPosition);

            var writeOutput = ParticleBuffer.IsVector(fieldOutput);
            var outputs = writeOutput ? particleSpan.Vectors(fieldOutput) : default;
            var previousOutputs = writePrevious ? particleSpan.Vectors(fieldOutputPrev) : default;

            var lockNormal = rotationSetType == RotationLockType.PARTICLE_ROTATION_LOCK_NORMAL;
            var normals = lockRotation && lockNormal ? particleSpan.Vectors(ParticleField.Normal) : default;
            var yaws = lockRotation && !lockNormal ? particleSpan.Floats(ParticleField.Yaw) : default;
            var pitches = lockRotation && !lockNormal ? particleSpan.Floats(ParticleField.Pitch) : default;
            var rolls = lockRotation && !lockNormal ? particleSpan.Floats(ParticleField.Roll) : default;

            var rotationAngles = rotation.Prepare(particleSpan, particleSystemState);
            var rotationLerps = rotationLerp.Fill(particleSpan, particleSystemState);
            var nextRotationBlock = 0;

            // The rotation lerp is read one block of four at a time by the blocks that lock a rotation, so
            // the nth such block reads the values of the nth block
            var lerpBlockStart = -4;

            // Locking stops for every later particle of the step once a block of four has none to lock, because that block
            // is skipped without advancing the streams; every particle is considered here
            for (var i = 0; i < particleSpan.Length; i++)
            {
                var age = ages[i];
                var index = hitboxIndices[i];

                // Skipped particles that share a block of four with a locked one still have their normal or rotation
                // lerped toward the rotation of the last cached hitbox; they are left untouched here
                if ((age >= fadeEnd && !jumped) || (uint)index >= (uint)current.Length)
                {
                    continue;
                }

                var stepFraction = MathF.Min(age, frameTime) / frameTime;
                var fraction = hitboxOffsets[i];
                var target = current[index].PointAt(fraction);
                var weight = (1f - ((age - lifeTimeFadeStart) * inverseFade)) * strength;

                if (rigid)
                {
                    if (writeOutput)
                    {
                        outputs[i] = target;
                    }

                    if (writePrevious)
                    {
                        previousOutputs[i] = target;
                    }
                }
                else
                {
                    var delta = (target - previous[index].PointAt(fraction)) * stepFraction;

                    if (lifeTimeFadeStart < age && !jumped)
                    {
                        delta *= weight;
                    }

                    if (writeOutput)
                    {
                        outputs[i] += delta;
                    }

                    if (writePrevious)
                    {
                        previousOutputs[i] += delta * prevScale;
                    }
                }

                if (!lockRotation)
                {
                    continue;
                }

                var boxRotation = boxRotations[index];
                var rotationWeight = stepFraction;

                if (!rigid)
                {
                    rotationWeight *= weight;
                }

                if (rigidRotationLock)
                {
                    // A per-block rotation draws only for the blocks of four that have a particle on a hitbox
                    if (i >= nextRotationBlock)
                    {
                        var (offset, block) = particleSpan.BlockAt(i);
                        rotation.FillPerBlock(block, block.Start & 3, particleSystemState, rotationAngles[offset..]);
                        nextRotationBlock = offset + block.Length;
                        lerpBlockStart += 4;
                    }

                    var angles = rotationAngles[i];
                    boxRotation *= EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(angles.Y, angles.Z, angles.X));
                    rotationWeight = rotationLerps[lerpBlockStart + ((particleSpan.Start + i) & 3)];
                }

                if (lockNormal)
                {
                    LockNormal(normals, i, boxRotation, rotationWeight);
                }
                else
                {
                    LockRotationAngles(yaws, pitches, rolls, i, boxRotation, rotationWeight);
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

                boxRotations[i] = current[i].Orientation * Quaternion.Inverse(previous[i].Orientation);
            }
        }

        private void LockNormal(Span<Vector3> normals, int i, Quaternion boxRotation, float weight)
        {
            var target = rigidRotationLock ? Vector3.Transform(Vector3.UnitX, boxRotation) : Vector3.Transform(normals[i], boxRotation);
            normals[i] = MathUtils.Lerp(normals[i], target, weight);
        }

        private static void LockRotationAngles(Span<float> yaws, Span<float> pitches, Span<float> rolls, int i, Quaternion boxRotation, float weight)
        {
            var pitchYawRoll = Vector3.DegreesToRadians(EntityTransformHelper.ToEulerAngles(boxRotation));
            var targetRotation = new Vector3(pitchYawRoll.Y, pitchYawRoll.X, pitchYawRoll.Z);
            var newRotation = MathUtils.Lerp(new Vector3(yaws[i], pitches[i], rolls[i]), targetRotation, weight);
            yaws[i] = newRotation.X;
            pitches[i] = newRotation.Y;
            rolls[i] = newRotation.Z;
        }
    }
}
