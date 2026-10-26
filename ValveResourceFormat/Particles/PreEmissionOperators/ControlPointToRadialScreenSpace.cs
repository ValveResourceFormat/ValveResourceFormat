namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Finds where a control point lies around the centre of the local player's view, for indicators
    /// at the edge of the screen. The angle of its direction on the view plane, 0 to the left of the
    /// centre and growing counter-clockwise, goes into one component of <c>m_nCPOut</c>;
    /// <c>m_nCPSSPosOut</c> receives that direction times 10 and how squarely the player looks at it.
    /// </summary>
    /// <remarks>
    /// Without a player the view sits at the world origin looking along +X. <c>m_vecCP1Pos</c> is not
    /// read.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ControlPointToRadialScreenSpace">C_OP_ControlPointToRadialScreenSpace</seealso>
    class ControlPointToRadialScreenSpace : ParticleFunctionPreEmissionOperator
    {
        private readonly int cpIn;
        private readonly int cpOut = 1;
        private readonly int cpOutField;
        private readonly int cpScreenSpacePositionOut = 4;

        public ControlPointToRadialScreenSpace(ParticleDefinitionParser parse) : base(parse)
        {
            cpIn = parse.ControlPoint("m_nCPIn", cpIn);
            cpOut = parse.ControlPoint("m_nCPOut", cpOut);
            cpOutField = parse.Int32("m_nCPOutField", cpOutField);
            cpScreenSpacePositionOut = parse.ControlPoint("m_nCPSSPosOut", cpScreenSpacePositionOut);
        }

        public override void Operate(ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (cpOutField < 0)
            {
                return;
            }

            var eye = Vector3.Zero;
            var rotation = Quaternion.Identity;

            if (particleSystemState.Player is { } player)
            {
                if (player.TryGetPosition(ParticleEntityPos.PARTICLE_EYES, out var position))
                {
                    eye = position;
                }

                rotation = player.GetOrientation(eyes: true);
            }

            var forward = MathUtils.SafeNormalize(Vector3.Transform(Vector3.UnitX, rotation));
            var left = Vector3.Transform(Vector3.UnitY, rotation);
            var up = Vector3.Transform(Vector3.UnitZ, rotation);

            var target = particleSystemState.GetControlPoint(cpIn).Position;
            var facing = Vector3.Dot(MathUtils.SafeNormalize(target - eye), forward);
            var onViewPlane = MathUtils.ProjectOntoPlane(target - eye, forward);
            var direction = new Vector2(Vector3.Dot(left, onViewPlane), Vector3.Dot(up, onViewPlane));
            var length = direction.Length();
            direction = length == 0f ? Vector2.Zero : direction / length;

            if (cpOutField <= 2)
            {
                particleSystemState.SetControlPointValueComponent(cpOut, cpOutField, MathF.Atan2(direction.Y, direction.X));
            }

            particleSystemState.SetControlPointValue(cpScreenSpacePositionOut, new Vector3(direction * 10f, facing));
        }
    }
}
