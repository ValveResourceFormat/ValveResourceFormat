namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places particles on a 3-D grid with configurable per-axis cell counts and spacing, one cell per
    /// particle. A cursor walks the cells along X, then Y, then Z and carries over between emissions,
    /// wrapping around the grid. Fractional counts limit the cursor, so a count of 2.5 gives three
    /// cells. Supports centering on the control point, the control point's own axes, and a hollow
    /// grid that skips the interior cells.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateOnGrid">C_INIT_CreateOnGrid</seealso>
    class CreateOnGrid : ParticleFunctionInitializer
    {
        /// <summary>How many interior cells a hollow grid skips at most for one particle, bounding huge or NaN counts.</summary>
        private const int MaxHollowSkips = 1 << 20;

        private readonly INumberProvider countX = new LiteralNumberProvider(0);
        private readonly INumberProvider countY = new LiteralNumberProvider(0);
        private readonly INumberProvider countZ = new LiteralNumberProvider(0);

        private readonly INumberProvider spacingX = new LiteralNumberProvider(0);
        private readonly INumberProvider spacingY = new LiteralNumberProvider(0);
        private readonly INumberProvider spacingZ = new LiteralNumberProvider(0);

        private readonly int controlPointNumber;
        private readonly bool localSpace = true;
        private readonly bool center = true;
        private readonly bool hollow;

        private Vector3 cursor;

        public CreateOnGrid(ParticleDefinitionParser parse) : base(parse)
        {
            countX = parse.CollectionNumberProvider("m_nXCount", countX);
            countY = parse.CollectionNumberProvider("m_nYCount", countY);
            countZ = parse.CollectionNumberProvider("m_nZCount", countZ);
            spacingX = parse.CollectionNumberProvider("m_nXSpacing", spacingX);
            spacingY = parse.CollectionNumberProvider("m_nYSpacing", spacingY);
            spacingZ = parse.CollectionNumberProvider("m_nZSpacing", spacingZ);
            controlPointNumber = parse.ControlPoint("m_nControlPointNumber", controlPointNumber);
            localSpace = parse.Boolean("m_bLocalSpace", localSpace);
            center = parse.Boolean("m_bCenter", center);
            hollow = parse.Boolean("m_bHollow", hollow);
        }

        public override void Reset()
        {
            cursor = Vector3.Zero;
        }

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var counts = new Vector3(
                MathF.Max(countX.NextNumber(particleSystemState), 1f),
                MathF.Max(countY.NextNumber(particleSystemState), 1f),
                MathF.Max(countZ.NextNumber(particleSystemState), 1f));
            var spacing = new Vector3(
                spacingX.NextNumber(particleSystemState),
                spacingY.NextNumber(particleSystemState),
                spacingZ.NextNumber(particleSystemState));

            var halfExtent = center ? (counts - Vector3.One) * 0.5f * spacing : Vector3.Zero;
            var controlPoint = particleSystemState.GetControlPoint(controlPointNumber);
            var controlPointPosition = controlPoint.Position;
            var rotation = localSpace ? controlPoint.GetRotationMatrix() : Matrix4x4.Identity;

            var positions = run.Vectors(ParticleField.Position);
            var previous = run.Vectors(ParticleField.PositionPrevious);

            for (var i = 0; i < run.Length; i++)
            {
                Wrap(counts);

                if (hollow && counts != Vector3.One)
                {
                    for (var skipped = 0; !IsOnSurface(counts) && skipped < MaxHollowSkips; skipped++)
                    {
                        cursor.X += 1f;
                        Wrap(counts);
                    }
                }

                var offset = (cursor * spacing) - halfExtent;

                var position = localSpace
                    ? Vector3.TransformNormal(offset, rotation) + controlPointPosition
                    : offset + controlPointPosition;

                positions[i] = position;
                previous[i] = position;

                cursor.X += 1f;
            }
        }

        private void Wrap(Vector3 counts)
        {
            if (cursor.X >= counts.X)
            {
                cursor.X = 0f;
                cursor.Y += 1f;
            }

            if (cursor.Y >= counts.Y)
            {
                cursor.Y = 0f;
                cursor.Z += 1f;
            }

            if (cursor.Z >= counts.Z)
            {
                cursor.Z = 0f;
            }
        }

        /// <summary>Whether the cursor sits on the first or last cell of some axis more than one cell long.</summary>
        private bool IsOnSurface(Vector3 counts)
            => IsOnSurface(cursor.X, counts.X) || IsOnSurface(cursor.Y, counts.Y) || IsOnSurface(cursor.Z, counts.Z);

        private static bool IsOnSurface(float index, float count)
            => count != 1f && (index == 0f || index == count - 1f);
    }
}
