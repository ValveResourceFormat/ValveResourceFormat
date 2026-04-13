namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Base class for spin operators that rotate particles at a constant rate.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CGeneralSpin">CGeneralSpin</seealso>
    abstract class CGeneralSpin : ParticleFunctionOperator
    {
        protected readonly int spinRateDegrees;
        protected readonly int spinRateMinDegrees;
        protected readonly float spinRateStopTime;

        protected CGeneralSpin(ParticleDefinitionParser parse) : base(parse)
        {
            spinRateDegrees = parse.Int32("m_nSpinRateDegrees", spinRateDegrees);
            spinRateMinDegrees = parse.Int32("m_nSpinRateMinDegrees", spinRateMinDegrees); // what is this
            spinRateStopTime = parse.Float("m_fSpinRateStopTime", spinRateStopTime);
        }
    }

    /// <summary>
    /// Continuously rotates a particle's roll angle at a constant spin rate (in degrees per second)
    /// until the particle exceeds the spin stop time.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_Spin">C_OP_Spin</seealso>
    class Spin : CGeneralSpin
    {
        public Spin(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                if (particle.Age < spinRateStopTime)
                {
                    particle.SetScalar(ParticleField.Roll, particle.Rotation.Z + spinRateDegrees * frameTime);
                }
            }
        }
    }

    /// <summary>
    /// Continuously rotates a particle's yaw angle at a constant spin rate (in degrees per second)
    /// until the particle exceeds the spin stop time.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SpinYaw">C_OP_SpinYaw</seealso>
    class SpinYaw : CGeneralSpin
    {
        public SpinYaw(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                if (particle.Age < spinRateStopTime)
                {
                    particle.SetScalar(ParticleField.Yaw, particle.Rotation.X + spinRateDegrees * frameTime);
                }
            }
        }
    }
}
