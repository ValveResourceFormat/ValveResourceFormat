using System;
using System.Numerics;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

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

        public static ParticleField GetParticleField(this IKeyValueCollection keyValues, string name)
        {
            return (ParticleField)keyValues.GetIntegerProperty(name);
        }

        // Extra utility for when one operator can set either scalars or vectors
        public static string FieldType(this ParticleField field)
        {
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
        }


        // Scalar fields
        public static float GetScalar(this Particle particle, ParticleField field)
        {
            return field switch
            {
                ParticleField.Alpha => particle.Alpha,
                ParticleField.AlphaAlternate => particle.AlphaAlternate,
                ParticleField.Radius => particle.Radius,
                ParticleField.TrailLength => particle.TrailLength,
                ParticleField.CreationTime => particle.CreationTime,
                ParticleField.Yaw => particle.Rotation.X,
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
        public static void SetScalar(this Particle particle, ParticleField field, float value)
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

        public static int GetScalarInt(this Particle particle, ParticleField field)
        {
            return field switch
            {
                ParticleField.SequenceNumber => particle.Sequence,
                ParticleField.SecondSequenceNumber => particle.Sequence2,
                ParticleField.ParticleId => particle.ParticleCount, // dangerous to set, right?
                _ => 0,
            };
        }

        // Vector field
        public static Vector3 GetVector(this Particle particle, ParticleField field)
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

        public static float GetVectorComponent(this Particle particle, ParticleField field, int component)
        {
            var vector = particle.GetVector(field);
            return vector.GetComponent(component);
        }

        public static void SetVectorComponent(this Particle particle, ParticleField field, float value, int component)
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


        public static void SetVector(this Particle particle, ParticleField field, Vector3 value)
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
        public static float GetInitialScalar(this Particle particle, ParticleField field)
        {
            return field switch
            {
                ParticleField.Alpha => particle.InitialAlpha,
                ParticleField.Radius => particle.InitialRadius,
                ParticleField.Yaw => particle.InitialRotation.X,
                ParticleField.Pitch => particle.InitialRotation.Y,
                ParticleField.Roll => particle.InitialRotation.Z,
                ParticleField.RollSpeed => particle.InitialRotationSpeed.Z,
                _ => particle.GetScalar(field),
            };
        }
        public static void SetInitialScalar(this Particle particle, ParticleField field, float value)
        {
            switch (field)
            {
                case ParticleField.Radius:
                    particle.InitialRadius = value;
                    particle.Radius = value;
                    break;
                case ParticleField.Alpha:
                    particle.InitialAlpha = value;
                    particle.Alpha = value;
                    break;
                case ParticleField.RollSpeed:
                    particle.InitialRotationSpeed = new Vector3(particle.InitialRotationSpeed.X, particle.InitialRotationSpeed.Y, value);
                    particle.RotationSpeed = particle.InitialRotationSpeed;
                    break;
                case ParticleField.Yaw:
                    particle.InitialRotation = new Vector3(value, particle.InitialRotation.Y, particle.InitialRotation.Z);
                    particle.Rotation = particle.InitialRotation;
                    break;
                case ParticleField.Pitch:
                    particle.InitialRotation = new Vector3(particle.InitialRotation.X, value, particle.InitialRotation.Z);
                    particle.Rotation = particle.InitialRotation;
                    break;
                case ParticleField.Roll:
                    particle.InitialRotation = new Vector3(particle.InitialRotation.X, particle.InitialRotation.Y, value);
                    particle.Rotation = particle.InitialRotation;
                    break;
                case ParticleField.LifeDuration:
                    particle.InitialLifetime = value;
                    particle.Lifetime = value;
                    break;
                default:
                    particle.SetScalar(field, value);
                    break;
            }
        }

        // Initial vector
        public static Vector3 GetInitialVector(this Particle particle, ParticleField field)
        {
            return field switch
            {
                ParticleField.Position => particle.InitialPosition,
                ParticleField.PositionPrevious => particle.InitialPosition, // I assume?
                ParticleField.Color => particle.InitialColor,
                _ => particle.GetVector(field),
            };
        }
        public static void SetInitialVector(this Particle particle, ParticleField field, Vector3 value)
        {
            switch (field)
            {
                case ParticleField.Color:
                    particle.InitialColor = value;
                    particle.Color = value;
                    break;
                case ParticleField.Position:
                    particle.InitialPosition = value;
                    particle.Position = value;
                    break;
                default:
                    particle.SetVector(field, value);
                    break;
            }
        }


        // Set methods, shared by a bunch of different operators and initializers
        public static float ModifyScalarBySetMethod(this Particle particle, ParticleField field, float value, ParticleSetMethod setMethod)
        {
            switch (setMethod)
            {
                case ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE:
                    break;
                case ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE:
                    value *= particle.GetInitialScalar(field);
                    break;
                case ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE:
                    value += particle.GetInitialScalar(field);
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
        public static Vector3 ModifyVectorBySetMethod(this Particle particle, ParticleField field, Vector3 value, ParticleSetMethod setMethod)
        {
            switch (setMethod)
            {
                case ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE:
                    break;
                case ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE:
                    value *= particle.GetInitialVector(field);
                    break;
                case ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE:
                    value += particle.GetInitialVector(field);
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
