using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Writes the gravity at a control point, scaled, into the position and orientation of another
    /// one. Without a game the gravity is the standard world gravity.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetGravityToCP">C_OP_SetGravityToCP</seealso>
    class SetGravityToCP : ParticleFunctionPreEmissionOperator
    {
        private static readonly Vector3 WorldGravity = new(0f, 0f, -800f);

        private readonly int cpOutput = 1;
        private readonly INumberProvider scale = new LiteralNumberProvider(1f);
        private readonly bool setPosition = true;
        private readonly bool setOrientation = true;
        private readonly bool setZDown = true;

        public SetGravityToCP(ParticleDefinitionParser parse) : base(parse)
        {
            cpOutput = Math.Clamp(parse.Int32("m_nCPOutput", cpOutput), 0, ParticleSystemState.MaxControlPoint);
            scale = parse.NumberProvider("m_flScale", scale);
            setPosition = parse.Boolean("m_bSetPosition", setPosition);
            setOrientation = parse.Boolean("m_bSetOrientation", setOrientation);
            setZDown = parse.Boolean("m_bSetZDown", setZDown);
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            var gravity = WorldGravity * scale.NextNumber(particleSystemState);

            if (setPosition)
            {
                particleSystemState.SetControlPointValue(cpOutput, gravity);
            }

            if (!setOrientation)
            {
                return;
            }

            var forward = gravity == Vector3.Zero ? Vector3.UnitZ : Vector3.Normalize(gravity);
            var (left, up) = ParticleMath.VectorVectors(forward);

            if (setZDown)
            {
                particleSystemState.SetControlPointOrientation(cpOutput, up, left, -forward);
            }
            else
            {
                particleSystemState.SetControlPointOrientation(cpOutput, forward, left, up);
            }
        }
    }
}
