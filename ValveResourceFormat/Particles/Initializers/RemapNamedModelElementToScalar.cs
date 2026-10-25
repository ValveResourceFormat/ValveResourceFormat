using System.Numerics.Tensors;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Finds the sequence, body part or mesh group a new particle holds in one attribute among a
    /// list of names of a model, and writes the value at the same place of a list of values into
    /// another attribute. A held value below every listed one takes the first value and one above
    /// them the last; one in between that is not listed takes 0.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RemapNamedModelSequenceToScalar">C_INIT_RemapNamedModelSequenceToScalar</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RemapNamedModelBodyPartToScalar">C_INIT_RemapNamedModelBodyPartToScalar</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RemapNamedModelMeshGroupToScalar">C_INIT_RemapNamedModelMeshGroupToScalar</seealso>
    class RemapNamedModelElementToScalar : ParticleFunctionInitializer
    {
        private readonly ModelElementKind kind;
        private readonly ModelReference model;
        private readonly string[] names;
        private readonly float[] values;
        private readonly ParticleField fieldInput = ParticleField.SequenceNumber;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        private int[] indices = [];
        private (int Min, int Max) range;

        public RemapNamedModelElementToScalar(ParticleDefinitionParser parse, ModelElementKind kind) : base(parse)
        {
            this.kind = kind;
            model = new ModelReference(parse);
            names = parse.Data.GetArray<string>("m_names") ?? [];
            values = parse.Data.GetFloatArray("m_values");
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            setMethod = parse.Enum("m_nSetMethod", setMethod);

            if (fieldOutput.IsAlpha())
            {
                TensorPrimitives.Clamp(values, 0f, 1f, values);
            }
        }

        public override ulong WrittenFields => fieldOutput.Mask();

        public override void Initialize(ParticleSystemState particleSystemState)
        {
            indices = ModelElements.Resolve(kind, names, model.Resolve(particleSystemState), particleSystemState.Data?.FileLoader);
            range = ModelElements.Range(indices);
        }

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var inputs = run.ReadFloats(fieldInput);
            var outputs = run.Floats(fieldOutput);
            var isInteger = fieldOutput.IsIntegerAttribute();

            for (var i = 0; i < outputs.Length; i++)
            {
                var position = ModelElements.PlaceOf(indices, range, (int)inputs[i], values.Length - 1);
                var value = (uint)position < (uint)values.Length ? values[position] : 0f;
                var current = outputs[i];
                var output = ParticleFieldExtensions.ApplySetMethod(value, current, current, particleSystemState.CurrentStep, setMethod);

                outputs[i] = isInteger ? BitConverter.Int32BitsToSingle((int)output) : output;
            }
        }
    }
}
