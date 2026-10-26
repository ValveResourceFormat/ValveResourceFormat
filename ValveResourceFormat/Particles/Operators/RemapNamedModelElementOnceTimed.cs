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

        public override void Restart(ParticleSystemState particleSystemState) => Reset();

        public override void Initialize(ParticleSystemState particleSystemState)
        {
            base.Initialize(particleSystemState);
            remap.Resolve(particleSystemState);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (proportional)
            {
                OperateProportional(particles, frameTime, particleSystemState);
                return;
            }

            if (done || particleSystemState.Age <= remapTime)
            {
                return;
            }

            done = true;

            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var current = particles.Current;
            var inputs = current.ReadFloats(fieldInput);
            var output = current.Floats(fieldOutput);

            for (var i = 0; i < output.Length; i++)
            {
                output[i] = remap.Remap((int)inputs[i], particleSystemState);
            }
        }

        private void OperateProportional(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState)
        {
            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var current = particles.Current;
            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);
            var ages = current.ReadAges();
            var inputs = current.ReadFloats(fieldInput);
            var output = current.Floats(fieldOutput);

            for (var i = 0; i < output.Length; i++)
            {
                var life = lifetimes[i] + ParticleMath.FloatEpsilon;
                var lifeFraction = ages[i] / life;

                if (lifeFraction > remapTime && (frameTime / life) + remapTime > lifeFraction)
                {
                    output[i] = remap.Remap((int)inputs[i], particleSystemState);
                }
            }
        }
    }
}
