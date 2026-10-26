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
    /// written for the first-writer rule; when no function declares it, the lifetime is not stored and
    /// the leftover particle lives. A fractional component keeps its fraction in the lowest digit.
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
                var fields = ParticleField.SequenceNumber.Mask();

                if (offset != Vector3.Zero)
                {
                    fields |= ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();
                }

                if (killUnused)
                {
                    fields |= ParticleField.LifeDuration.Mask();
                }

                return fields;
            }
        }

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var numbers = particleSystemState.GetControlPoint(cp).Position;
            var digitCount = Math.Min(run.Length, DigitCount(numbers.X) + DigitCount(numbers.Y) + DigitCount(numbers.Z));
            var layout = (digitCount - 1) * offset * 0.5f;
            var component = 2;
            var placesDigits = offset != Vector3.Zero;
            var sequenceNumbers = run.Floats(ParticleField.SequenceNumber);
            var storesLifetime = run.TryGetFloats(ParticleField.LifeDuration, out var lifetimes, out _);
            var positions = placesDigits ? run.Vectors(ParticleField.Position) : default;
            var previousPositions = placesDigits ? run.Vectors(ParticleField.PositionPrevious) : default;
            var radii = placesDigits && radiusScale ? run.ReadFloats(ParticleField.Radius) : default;

            for (var i = 0; i < run.Length; i++)
            {
                while (component >= 0 && numbers[component] < 1f)
                {
                    component--;
                }

                if (component < 0)
                {
                    if (storesLifetime)
                    {
                        lifetimes[i] = -1f;
                    }

                    continue;
                }

                var number = numbers[component];
                sequenceNumbers[i] = (number % 10f) + (10f * component);
                numbers[component] = MathF.Max(MathF.Floor(number / 10f), 0f);

                if (placesDigits)
                {
                    var shift = layout * (radiusScale ? radii[i] : 1f);
                    positions[i] += shift;
                    previousPositions[i] += shift;
                    layout -= offset;
                }
            }
        }

        private static int DigitCount(float number) => Math.Max(0, (int)MathF.Log10(number * 10f));
    }
}
