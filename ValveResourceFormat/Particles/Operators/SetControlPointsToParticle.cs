using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Places a run of control points on a run of particles, optionally turned to match them, on this
    /// system or on its parent or root, and on every system below the one written.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointsToParticle">C_OP_SetControlPointsToParticle</seealso>
    class SetControlPointsToParticle : ParticleFunctionOperator
    {
        private readonly int firstControlPoint;
        private readonly int numControlPoints = 1;
        private readonly int firstSourcePoint;
        private readonly bool reverse;
        private readonly bool setOrientation;
        private readonly ParticleOrientationSetMode orientationMode = ParticleOrientationSetMode.PARTICLE_ORIENTATION_SET_FROM_VELOCITY;
        private readonly ParticleParentSetMode setParent = ParticleParentSetMode.PARTICLE_SET_PARENT_NO;

        public SetControlPointsToParticle(ParticleDefinitionParser parse) : base(parse)
        {
            firstControlPoint = Math.Clamp(parse.Int32("m_nFirstControlPoint", firstControlPoint), 0, ParticleSystemState.MaxControlPoint);
            firstSourcePoint = parse.Int32("m_nFirstSourcePoint", firstSourcePoint);
            numControlPoints = Math.Min(parse.Int32("m_nNumControlPoints", numControlPoints), 64 - firstSourcePoint);
            reverse = parse.Boolean("m_bReverse", reverse);
            setOrientation = parse.Boolean("m_bSetOrientation", setOrientation);
            orientationMode = parse.Enum("m_nOrientationMode", orientationMode);
            setParent = parse.Enum("m_nSetParent", setParent);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var live = particles.Current;
            var target = particleSystemState.GetSetParentTarget(setParent);
            var count = Math.Min(Math.Min(live.Length - firstSourcePoint, numControlPoints), 64 - firstControlPoint);

            for (var i = 0; i < count; i++)
            {
                var source = reverse ? live.Length - i - firstSourcePoint - 1 : i + firstSourcePoint;
                ref var particle = ref live[Math.Clamp(source, 0, live.Length - 1)];

                var controlPoint = target.GetControlPoint(firstControlPoint + i);
                controlPoint.Position = particle.Position;

                if (setOrientation)
                {
                    SetParticleOrientation(controlPoint, ref particle);
                }
            }
        }

        private void SetParticleOrientation(ControlPoint controlPoint, ref Particle particle)
        {
            switch (orientationMode)
            {
                case ParticleOrientationSetMode.PARTICLE_ORIENTATION_SET_FROM_VELOCITY:
                    SetForward(controlPoint, particle.Position - particle.PositionPrevious);
                    break;

                case ParticleOrientationSetMode.PARTICLE_ORIENTATION_SET_FROM_NORMAL:
                    SetForward(controlPoint, particle.Normal);
                    break;

                case ParticleOrientationSetMode.PARTICLE_ORIENTATION_SET_FROM_ROTATIONS:
                    var angles = Vector3.RadiansToDegrees(particle.Rotation);
                    controlPoint.SetOrientation(EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(angles.Y, angles.X, angles.Z)));
                    break;
            }
        }

        private static void SetForward(ControlPoint controlPoint, Vector3 direction)
        {
            if (direction == Vector3.Zero)
            {
                return;
            }

            var forward = Vector3.Normalize(direction);
            var (left, up) = ParticleMath.VectorVectors(forward);
            controlPoint.SetOrientation(forward, left, up);
        }
    }
}
