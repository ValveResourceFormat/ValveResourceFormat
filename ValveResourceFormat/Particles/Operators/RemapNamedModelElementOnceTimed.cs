using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Once the system passes a time, or with <c>m_bProportional</c> once each particle passes that
    /// fraction of its life, maps the sequence, body part or mesh group a particle holds in one
    /// attribute through lists of names of a model into another attribute.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapNamedModelSequenceOnceTimed">C_OP_RemapNamedModelSequenceOnceTimed</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapNamedModelBodyPartOnceTimed">C_OP_RemapNamedModelBodyPartOnceTimed</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapNamedModelMeshGroupOnceTimed">C_OP_RemapNamedModelMeshGroupOnceTimed</seealso>
    class RemapNamedModelElementOnceTimed : ParticleFunctionOperator
    {
        private readonly NamedModelElementRemap remap;
        private readonly bool proportional;
        private readonly ParticleField fieldInput = ParticleField.SequenceNumber;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly float remapTime = 1f;

        private bool done;

        public RemapNamedModelElementOnceTimed(ParticleDefinitionParser parse, ModelElementKind kind) : base(parse)
        {
            remap = new NamedModelElementRemap(parse, kind);
            proportional = parse.Boolean("m_bProportional", proportional);
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            remapTime = parse.Float("m_flRemapTime", remapTime);
        }

        public override void Reset()
        {
            done = false;
        }

        public override void Initialize(ParticleSystemState particleSystemState)
        {
            Reset();
            remap.Resolve(particleSystemState);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (proportional)
            {
                foreach (var particle in particles.Current)
                {
                    var life = particle.Lifetime + ParticleMath.FloatEpsilon;
                    var lifeFraction = (particleSystemState.Age - particle.CreationTime) / life;

                    if (lifeFraction > remapTime && (frameTime / life) + remapTime > lifeFraction)
                    {
                        particle.SetScalar(fieldOutput, remap.Remap((int)particle.GetScalar(fieldInput), particleSystemState));
                    }
                }

                return;
            }

            if (done || particleSystemState.Age <= remapTime)
            {
                return;
            }

            done = true;

            foreach (var particle in particles.Current)
            {
                particle.SetScalar(fieldOutput, remap.Remap((int)particle.GetScalar(fieldInput), particleSystemState));
            }
        }
    }
}
