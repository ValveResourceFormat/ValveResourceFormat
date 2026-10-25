namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Keeps a control point on the local player, so the effect can react to where the player is.
    /// The control point is written on this system and every system below it, and goes to the world
    /// origin with no rotation when there is no player.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointToPlayer">C_OP_SetControlPointToPlayer</seealso>
    class SetControlPointToPlayer : ParticleFunctionPreEmissionOperator
    {
        private readonly int cp1 = 1;
        private readonly Vector3 cp1Pos = Vector3.Zero;
        private readonly bool orientToEyes;
        private readonly ParticleEntityPos position = ParticleEntityPos.PARTICLE_WORLDSPACE_CENTER;
        private readonly int radiusCP = 2;
        private readonly int radiusCPField;

        public SetControlPointToPlayer(ParticleDefinitionParser parse) : base(parse)
        {
            cp1 = parse.ControlPoint("m_nCP1", cp1);
            cp1Pos = parse.Vector3("m_vecCP1Pos", cp1Pos);
            orientToEyes = parse.Boolean("m_bOrientToEyes", orientToEyes);
            position = parse.Enum("m_nPosition", position);
            radiusCP = parse.ControlPoint("m_nRadiusCP", radiusCP);
            radiusCPField = Math.Clamp(parse.Int32("m_nRadiusCPField", radiusCPField), -1, 2);

            if (position == ParticleEntityPos.PARTICLE_FLASHLIGHT)
            {
                orientToEyes = true;
            }
        }

        public override void Operate(ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            var player = particleSystemState.Player;
            var playerPosition = Vector3.Zero;
            var orientation = Quaternion.Identity;

            if (player != null && player.TryGetPosition(position, out var found))
            {
                playerPosition = found;
                orientation = player.GetOrientation(orientToEyes);
            }

            particleSystemState.GetControlPoint(cp1).Position = playerPosition + cp1Pos;

            // The player has no flashlight radius to give, so the radius control point gets zero
            if (position == ParticleEntityPos.PARTICLE_FLASHLIGHT && radiusCPField >= 0)
            {
                particleSystemState.SetControlPointValueComponent(radiusCP, radiusCPField, 0f);
            }

            particleSystemState.SetControlPointRotation(cp1, orientation);
        }
    }
}
