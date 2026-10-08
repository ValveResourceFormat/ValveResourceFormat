namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Keeps a control point on the local player, so the effect can react to where the player is.
    /// Leaves the control point alone when there is no player.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointToPlayer">C_OP_SetControlPointToPlayer</seealso>
    class SetControlPointToPlayer : ParticleFunctionPreEmissionOperator
    {
        private readonly int cp1 = 1;
        private readonly Vector3 cp1Pos = Vector3.Zero;
        private readonly bool orientToEyes;
        private readonly ParticleEntityPos position = ParticleEntityPos.PARTICLE_WORLDSPACE_CENTER;

        public SetControlPointToPlayer(ParticleDefinitionParser parse) : base(parse)
        {
            cp1 = parse.Int32("m_nCP1", cp1);
            cp1Pos = parse.Vector3("m_vecCP1Pos", cp1Pos);
            orientToEyes = parse.Boolean("m_bOrientToEyes", orientToEyes);
            position = parse.Enum("m_nPosition", position);
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime)
        {
            if (particleSystemState.Player is not { } player || !player.TryGetPosition(position, out var playerPosition))
            {
                return;
            }

            // Held by this system alone: its parent and siblings use the same index for other things
            var point = particleSystemState.OverrideControlPoint(cp1);

            point.Position = playerPosition + cp1Pos;
            point.Orientation = player.GetForward(orientToEyes);
            point.Rotation = null;
        }
    }
}
