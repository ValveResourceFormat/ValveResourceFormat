using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Sets a rotation attribute of every particle to an offset minus one Euler angle (pitch, yaw or
    /// roll) of a control point's orientation. The offset is in radians.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapControlPointOrientationToRotation">C_OP_RemapControlPointOrientationToRotation</seealso>
    class RemapControlPointOrientationToRotation : ParticleFunctionOperator
    {
        private readonly int cp;
        private readonly ParticleField fieldOutput = ParticleField.Roll;
        private readonly float offsetRotation;
        private readonly int component = 1;

        public RemapControlPointOrientationToRotation(ParticleDefinitionParser parse) : base(parse)
        {
            cp = parse.Int32("m_nCP", cp);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            offsetRotation = parse.Float("m_flOffsetRot", offsetRotation);
            component = parse.Int32("m_nComponent", component);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var angles = EntityTransformHelper.ToEulerAngles(particleSystemState.GetControlPoint(cp).GetRotation());
            var value = offsetRotation - float.DegreesToRadians(angles.GetComponent(component));

            foreach (ref var particle in particles.Current)
            {
                particle.SetScalar(fieldOutput, value);
            }
        }
    }
}
