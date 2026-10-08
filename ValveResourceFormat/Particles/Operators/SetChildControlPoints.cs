using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Places a run of control points on the child systems of a group at this system's particles, one
    /// particle per control point, optionally turning them to match the particles too. Every child of
    /// the group gets the same points, on its own copy of them.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetChildControlPoints">C_OP_SetChildControlPoints</seealso>
    class SetChildControlPoints : ParticleFunctionOperator
    {
        /// <summary>How a control point placed on a particle is turned.</summary>
        private enum OrientationType
        {
            PARTICLE_ORIENTATION_NONE = 0,
            PARTICLE_ORIENTATION_VELOCITY = 1,
            PARTICLE_ORIENTATION_NORMAL = 2,
            PARTICLE_ORIENTATION_ROTATION = 4,
        }

        private readonly int childGroupId;
        private readonly int firstControlPoint;
        private readonly int numControlPoints = 1;
        private readonly INumberProvider firstSourcePoint = new LiteralNumberProvider(0f);
        private readonly bool reverse;
        private readonly bool setOrientation;
        private readonly OrientationType orientationType = OrientationType.PARTICLE_ORIENTATION_VELOCITY;

        private readonly List<ParticleSystemState> matchingChildren = [];

        public SetChildControlPoints(ParticleDefinitionParser parse) : base(parse)
        {
            childGroupId = parse.Int32("m_nChildGroupID", childGroupId);
            firstControlPoint = parse.Int32("m_nFirstControlPoint", firstControlPoint);
            numControlPoints = parse.Int32("m_nNumControlPoints", numControlPoints);
            firstSourcePoint = parse.NumberProvider("m_nFirstSourcePoint", firstSourcePoint);
            reverse = parse.Boolean("m_bReverse", reverse);
            setOrientation = parse.Boolean("m_bSetOrientation", setOrientation);
            orientationType = parse.Enum("m_nOrientation", orientationType);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (particleSystemState.Data == null)
            {
                return;
            }

            matchingChildren.Clear();
            particleSystemState.Data.CollectChildStatesInGroup(childGroupId, matchingChildren);

            if (matchingChildren.Count == 0)
            {
                return;
            }

            var live = particles.Current;
            var first = (int)firstSourcePoint.NextNumber(particleSystemState);

            // Points past the live particles are left where they were
            for (var i = 0; i < numControlPoints; i++)
            {
                var index = reverse ? live.Length - 1 - (first + i) : first + i;

                if ((uint)index >= (uint)live.Length)
                {
                    break;
                }

                ref var particle = ref live[index];
                var rotation = setOrientation ? ParticleRotation(ref particle) : null;

                foreach (var child in matchingChildren)
                {
                    var controlPoint = child.OverrideControlPoint(firstControlPoint + i);
                    controlPoint.Position = particle.Position;

                    if (rotation is { } turned)
                    {
                        controlPoint.Rotation = turned;
                        controlPoint.Orientation = Vector3.Transform(Vector3.UnitX, turned);
                    }
                }
            }
        }

        /// <summary>The turn the authored method takes from a particle, or null when it gives none.</summary>
        private Quaternion? ParticleRotation(ref Particle particle)
        {
            switch (orientationType)
            {
                case OrientationType.PARTICLE_ORIENTATION_VELOCITY:
                    var velocity = particle.Position - particle.PositionPrevious;
                    return velocity.LengthSquared() > ParticleMath.MinimumLengthSquared
                        ? EntityTransformHelper.ForwardDirectionToQuaternion(Vector3.Normalize(velocity))
                        : null;

                case OrientationType.PARTICLE_ORIENTATION_NORMAL:
                    return particle.Normal.LengthSquared() > ParticleMath.MinimumLengthSquared
                        ? EntityTransformHelper.ForwardDirectionToQuaternion(Vector3.Normalize(particle.Normal))
                        : null;

                case OrientationType.PARTICLE_ORIENTATION_ROTATION:
                    // The particle holds yaw, pitch and roll in radians
                    var angles = particle.Rotation;
                    return EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(
                        float.RadiansToDegrees(angles.Y), float.RadiansToDegrees(angles.X), float.RadiansToDegrees(angles.Z)));

                default:
                    return null;
            }
        }
    }
}
