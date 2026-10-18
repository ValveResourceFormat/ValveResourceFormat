using System.Runtime.InteropServices;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Displays up to three numbers held in a control point's position, one decimal digit per particle.
    /// Each particle's sequence is its digit plus ten times the component it came from, so a sheet can
    /// hold a separate set of digits per number. The digits are taken least significant first, Z
    /// before Y before X, and laid out along <c>m_vecOffset</c>, centred on the spawn position.
    /// </summary>
    /// <remarks>
    /// A particle left over once every digit is placed gets a lifetime of -1, which kills it unless a
    /// later initializer sets the lifetime again. <c>m_bKillUnused</c> only declares the lifetime as
    /// written for the first-writer rule. A fractional component keeps its fraction in the lowest digit.
    /// An emission of 16 or more particles starts the digits over in each run
    /// <see cref="ParticleFunctionInitializer.SplitIntoCalls"/> splits it into.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_SequenceFromCP">C_INIT_SequenceFromCP</seealso>
    class SequenceFromCP : ParticleFunctionInitializer
    {
        private readonly bool killUnused;
        private readonly bool radiusScale;
        private readonly int cp = 1;
        private readonly Vector3 offset;

        public SequenceFromCP(ParticleDefinitionParser parse) : base(parse)
        {
            killUnused = parse.Boolean("m_bKillUnused", killUnused);
            radiusScale = parse.Boolean("m_bRadiusScale", radiusScale);
            cp = parse.Int32("m_nCP", cp);
            offset = parse.Vector3("m_vecOffset", offset);
        }

        public override void AddControlPointDefaults(ControlPointDefaults defaults)
        {
            defaults.Add(cp, new Vector3(10f, 0f, 0f));
        }

        public override ulong WrittenFields
        {
            get
            {
                var fields = FieldMask(ParticleField.SequenceNumber);

                if (offset != Vector3.Zero)
                {
                    fields |= FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious);
                }

                if (killUnused)
                {
                    fields |= FieldMask(ParticleField.LifeDuration);
                }

                return fields;
            }
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            Initialize(MemoryMarshal.CreateSpan(ref particle, 1), particles, particleSystemState);
            return particle;
        }

        public override void Initialize(Span<Particle> batch, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var (head, blocks, tail) = SplitIntoCalls(batch);

            InitializeCall(batch[head], particleSystemState);
            InitializeCall(batch[blocks], particleSystemState);
            InitializeCall(batch[tail], particleSystemState);
        }

        private void InitializeCall(Span<Particle> batch, ParticleSystemState particleSystemState)
        {
            var numbers = particleSystemState.GetControlPoint(cp).Position;
            var digitCount = Math.Min(batch.Length, DigitCount(numbers.X) + DigitCount(numbers.Y) + DigitCount(numbers.Z));
            var layout = (digitCount - 1) * offset * 0.5f;
            var component = 2;

            foreach (ref var particle in batch)
            {
                while (component >= 0 && numbers[component] < 1f)
                {
                    component--;
                }

                if (component < 0)
                {
                    particle.Lifetime = -1f;
                    continue;
                }

                var number = numbers[component];
                particle.SequenceNumber = (number % 10f) + (10f * component);
                numbers[component] = MathF.Max(MathF.Floor(number / 10f), 0f);

                if (offset != Vector3.Zero)
                {
                    var shift = layout * (radiusScale ? particle.Radius : 1f);
                    particle.Position += shift;
                    particle.PositionPrevious += shift;
                    layout -= offset;
                }
            }
        }

        private static int DigitCount(float number) => Math.Max(0, (int)MathF.Log10(number * 10f));
    }
}
