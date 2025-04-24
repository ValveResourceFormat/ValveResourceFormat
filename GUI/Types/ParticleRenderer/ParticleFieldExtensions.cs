using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer
{
    static class ParticleFieldExtensions
    {
        public static float GetComponent(this Vector3 vector, int component)
        {
            component = Math.Clamp(component, 0, 2);

            return component switch
            {
                0 => vector.X,
                1 => vector.Y,
                2 => vector.Z,
                _ => vector.Z,
            };
        }

        // Extra utility for when one operator can set either scalars or vectors
        public static string? FieldType(this ParticleField field)
        {
#pragma warning disable IDE0066 // Convert switch statement to expression
            switch (field)
            {
                case ParticleField.Position:
                case ParticleField.PositionPrevious:
                case ParticleField.Color:
                case ParticleField.HitboxOffsetPosition:
                case ParticleField.ScratchVector:
                case ParticleField.Normal:
                case ParticleField.GlowRgb:
                case ParticleField.ScratchVector2:
                case ParticleField.BoneIndices:
                case ParticleField.BoneWeights:
                    return "vector";
                case ParticleField.LifeDuration:
                case ParticleField.Radius:
                case ParticleField.Roll:
                case ParticleField.RollSpeed:
                case ParticleField.Alpha:
                case ParticleField.CreationTime:
                case ParticleField.TrailLength:
                case ParticleField.Yaw:
                case ParticleField.SecondSequenceNumber:
                case ParticleField.AlphaAlternate:
                case ParticleField.ScratchFloat:
                case ParticleField.Pitch:
                case ParticleField.GlowAlpha:
                case ParticleField.SceneObjectPointer:
                case ParticleField.ModelHelperPointer:
                case ParticleField.ScratchFloat1:
                case ParticleField.ScratchFloat2:
                case ParticleField.SceneObjectPointer2:
                case ParticleField.RefCountedPointer:
                case ParticleField.ParentParticleIndex:
                case ParticleField.ForceScale:
                case ParticleField.ModelHelperPointer2:
                case ParticleField.ModelHelperPointer3:
                case ParticleField.ModelHelperPointer4:
                case ParticleField.ManualAnimationFrame:
                case ParticleField.SequenceNumber: // and i guess these too
                case ParticleField.ParticleId:
                case ParticleField.HitboxIndex:
                    return "float";
                default:
                    return null;
            }
#pragma warning restore IDE0066 // Convert switch statement to expression
        }


        // Scalar fields
        public static float GetScalar(this ref Particle particle, ParticleField field)
        {
            return field switch
            {
                ParticleField.Alpha => particle.Alpha,
                ParticleField.AlphaAlternate => particle.AlphaAlternate,
                ParticleField.Radius => particle.Radius,
                ParticleField.TrailLength => particle.TrailLength,
                ParticleField.CreationTime => particle.CreationTime,
                ParticleField.Yaw => particle.Rotation.X,
                ParticleField.ParticleId => particle.ParticleID,
                ParticleField.Pitch => particle.Rotation.Y,
                ParticleField.Roll => particle.Rotation.Z,
                ParticleField.RollSpeed => particle.RotationSpeed.Z,
                ParticleField.SecondSequenceNumber => particle.AlphaWindowThreshold,
                ParticleField.ScratchFloat => particle.ScratchFloat0,
                ParticleField.ScratchFloat1 => particle.ScratchFloat1,
                ParticleField.ScratchFloat2 => particle.ScratchFloat2,
                _ => 0f,
            };
        }
        public static void SetScalar(this ref Particle particle, ParticleField field, float value)
        {
            switch (field)
            {
                case ParticleField.Radius:
                    particle.Radius = value;
                    break;
                case ParticleField.Alpha:
                    particle.Alpha = value;
                    break;
                case ParticleField.AlphaAlternate:
                    particle.AlphaAlternate = value;
                    break;
                case ParticleField.TrailLength:
                    particle.TrailLength = value;
                    break;
                case ParticleField.RollSpeed:
                    particle.RotationSpeed = new Vector3(particle.RotationSpeed.X, particle.RotationSpeed.Y, value);
                    break;
                case ParticleField.Yaw:
                    particle.Rotation = new Vector3(value, particle.Rotation.Y, particle.Rotation.Z);
                    break;
                case ParticleField.Pitch:
                    particle.Rotation = new Vector3(particle.Rotation.X, value, particle.Rotation.Z);
                    break;
                case ParticleField.Roll:
                    particle.Rotation = new Vector3(particle.Rotation.X, particle.Rotation.Y, value);
                    break;
                case ParticleField.LifeDuration:
                    particle.Lifetime = value;
                    break;
                case ParticleField.ScratchFloat:
                    particle.ScratchFloat0 = value;
                    break;
                case ParticleField.ScratchFloat1:
                    particle.ScratchFloat1 = value;
                    break;
                case ParticleField.ScratchFloat2:
                    particle.ScratchFloat2 = value;
                    break;
                default:
                    break;
            }
        }

        public static int GetScalarInt(this ref Particle particle, ParticleField field)
        {
            return field switch
            {
                ParticleField.SequenceNumber => particle.Sequence,
                ParticleField.SecondSequenceNumber => particle.Sequence2,
                ParticleField.ParticleId => particle.ParticleID, // dangerous to set, right?
                _ => 0,
            };
        }

        // Vector field
        public static Vector3 GetVector(this ref Particle particle, ParticleField field)
        {
            return field switch
            {
                ParticleField.Position => particle.Position,
                ParticleField.PositionPrevious => particle.PositionPrevious,
                ParticleField.Color => particle.Color,
                ParticleField.ScratchVector => particle.ScratchVector,
                ParticleField.ScratchVector2 => particle.ScratchVector2,
                _ => Vector3.Zero,
            };
        }

        public static float GetVectorComponent(this ref Particle particle, ParticleField field, int component)
        {
            var vector = particle.GetVector(field);
            return vector.GetComponent(component);
        }

        public static void SetVectorComponent(this ref Particle particle, ParticleField field, float value, int component)
        {

            var joinedVector = particle.GetVector(field);

            switch (component)
            {
                case 0:
                    joinedVector.X = value;
                    break;
                case 1:
                    joinedVector.Y = value;
                    break;
                case 2:
                    joinedVector.Z = value;
                    break;
                default:
                    throw new NotImplementedException($"Unknown vector component with ID {component}");
            }

            particle.SetVector(field, joinedVector);
        }


        public static void SetVector(this ref Particle particle, ParticleField field, Vector3 value)
        {
            switch (field)
            {
                case ParticleField.Color:
                    particle.Color = value;
                    break;
                case ParticleField.Position:
                    particle.Position = value;
                    break;
                case ParticleField.PositionPrevious:
                    particle.PositionPrevious = value;
                    break;
                case ParticleField.ScratchVector:
                    particle.ScratchVector = value;
                    break;
                case ParticleField.ScratchVector2:
                    particle.ScratchVector2 = value;
                    break;
                default:
                    break;
            }
        }

        // Initial Scalars
        public static float GetInitialScalar(this Particle particle, ParticleCollection particles, ParticleField field)
        {
            var initialParticle = particles.Initial[particle.Index];
            return initialParticle.GetScalar(field);
        }

        // Initial vector
        public static Vector3 GetInitialVector(this Particle particle, ParticleCollection particles, ParticleField field)
        {
            var initialParticle = particles.Initial[particle.Index];
            return initialParticle.GetVector(field);
        }

        // Set methods, shared by a bunch of different operators and initializers
        public static float ModifyScalarBySetMethod(this ref Particle particle, ParticleCollection particles, ParticleField field, float value, ParticleSetMethod setMethod)
        {
            switch (setMethod)
            {
                case ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE:
                    break;
                case ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE:
                    value *= particle.GetInitialScalar(particles, field);
                    break;
                case ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE:
                    value += particle.GetInitialScalar(particles, field);
                    break;
                case ParticleSetMethod.PARTICLE_SET_SCALE_CURRENT_VALUE:
                    value *= particle.GetScalar(field);
                    break;
                case ParticleSetMethod.PARTICLE_SET_ADD_TO_CURRENT_VALUE:
                    value += particle.GetScalar(field);
                    break;
                case ParticleSetMethod.PARTICLE_SET_RAMP_CURRENT_VALUE: // new in DeskJob. Exponential, unlike other ramps
                    value = particle.GetScalar(field) + (value * particle.Age);
                    break;
                default:
                    //throw new NotImplementedException($"Unknown particle set type {Enum.GetName(setMethod)}!");
                    break;
            }
            return value;
        }

        public static Vector3 ModifyVectorBySetMethod(this ref Particle particle, ParticleCollection particles, ParticleField field, Vector3 value, ParticleSetMethod setMethod)
        {
            switch (setMethod)
            {
                case ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE:
                    break;
                case ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE:
                    value *= particle.GetInitialVector(particles, field);
                    break;
                case ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE:
                    value += particle.GetInitialVector(particles, field);
                    break;
                case ParticleSetMethod.PARTICLE_SET_SCALE_CURRENT_VALUE:
                    value *= particle.GetVector(field);
                    break;
                case ParticleSetMethod.PARTICLE_SET_ADD_TO_CURRENT_VALUE:
                    value += particle.GetVector(field);
                    break;
                case ParticleSetMethod.PARTICLE_SET_RAMP_CURRENT_VALUE: // new in DeskJob
                    value = particle.GetVector(field) + (value * particle.Age);
                    break;
                default:
                    //throw new NotImplementedException($"Unknown particle set type {Enum.GetName(setMethod)}!");
                    break;
            }
            return value;
        }
    }
}
