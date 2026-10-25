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
            firstControlPoint = parse.ControlPoint("m_nFirstControlPoint", firstControlPoint);
            firstSourcePoint = parse.Int32("m_nFirstSourcePoint", firstSourcePoint);
            // The count is bounded by the first source particle, not the first control point
            numControlPoints = Math.Min(parse.Int32("m_nNumControlPoints", numControlPoints), ParticleSystemState.ControlPointCount - firstSourcePoint);
            reverse = parse.Boolean("m_bReverse", reverse);
            setOrientation = parse.Boolean("m_bSetOrientation", setOrientation);
            orientationMode = parse.Enum("m_nOrientationMode", orientationMode);
            setParent = parse.Enum("m_nSetParent", setParent);
        }

        public override bool IsOrderImportant => true;

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var live = particles.Current;
            var target = particleSystemState.GetSetParentTarget(setParent);
            var count = Math.Min(Math.Min(live.Length - firstSourcePoint, numControlPoints), ParticleSystemState.ControlPointCount - firstControlPoint);

            for (var i = 0; i < count; i++)
            {
                var source = reverse ? live.Length - i - firstSourcePoint - 1 : i + firstSourcePoint;
                var index = Math.Clamp(source, 0, live.Length - 1);

                var controlPoint = target.GetControlPoint(firstControlPoint + i);
                controlPoint.Position = live.ReadVector(ParticleField.Position, index);

                if (setOrientation)
                {
                    SetParticleOrientation(controlPoint, live, index);
                }
            }
        }

        private void SetParticleOrientation(ControlPoint controlPoint, ParticleSpan live, int index)
        {
            switch (orientationMode)
            {
                case ParticleOrientationSetMode.PARTICLE_ORIENTATION_SET_FROM_VELOCITY:
                    OrientToVelocity(controlPoint, live, index);
                    break;

                case ParticleOrientationSetMode.PARTICLE_ORIENTATION_SET_FROM_NORMAL:
                    OrientToDirection(controlPoint, live.ReadVector(ParticleField.Normal, index));
                    break;

                case ParticleOrientationSetMode.PARTICLE_ORIENTATION_SET_FROM_ROTATIONS:
                    OrientToRotations(controlPoint, live, index);
                    break;
            }
        }

        /// <summary>Points <paramref name="controlPoint"/> along the last move of the particle at <paramref name="index"/>.</summary>
        internal static void OrientToVelocity(ControlPoint controlPoint, ParticleSpan live, int index)
        {
            var position = live.ReadVector(ParticleField.Position, index);
            var previousPosition = live.ReadVector(ParticleField.PositionPrevious, index);
            OrientToDirection(controlPoint, position - previousPosition);
        }

        /// <summary>Points <paramref name="controlPoint"/> along <paramref name="direction"/>, leaving it as it is for a zero direction.</summary>
        internal static void OrientToDirection(ControlPoint controlPoint, Vector3 direction)
        {
            if (direction != Vector3.Zero)
            {
                controlPoint.Orientation = direction;
            }
        }

        /// <summary>Turns <paramref name="controlPoint"/> to the yaw, pitch and roll, in radians, of the particle at <paramref name="index"/>.</summary>
        internal static void OrientToRotations(ControlPoint controlPoint, ParticleSpan live, int index)
        {
            var pitch = float.RadiansToDegrees(live.ReadFloat(ParticleField.Pitch, index));
            var yaw = float.RadiansToDegrees(live.ReadFloat(ParticleField.Yaw, index));
            var roll = float.RadiansToDegrees(live.ReadFloat(ParticleField.Roll, index));
            controlPoint.SetOrientation(EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(pitch, yaw, roll)));
        }
    }
}
