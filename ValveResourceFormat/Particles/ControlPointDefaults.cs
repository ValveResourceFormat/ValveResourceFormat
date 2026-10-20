namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// The values the particle editor previews a system's unset control points at. Each function adds the
    /// control points it reads with the default their use implies, such as white for one read as a colour
    /// or one for a scale. Only the nonzero defaults are kept, since an unset control point is zero anyway.
    /// </summary>
    /// <remarks>
    /// Per component the first nonzero default wins. Control point 0 is left out, the preview always
    /// places it.
    /// </remarks>
    internal sealed class ControlPointDefaults
    {
        private readonly Dictionary<int, Vector3> values = [];

        /// <summary>The default of each control point that has a nonzero one.</summary>
        public IReadOnlyDictionary<int, Vector3> Values => values;

        /// <summary>A control point read as a colour, white on the 0 to 255 scale.</summary>
        public void AddColor(int controlPoint) => Add(controlPoint, new Vector3(255f));

        /// <summary>A control point read as a scale or a multiplier, one on every component.</summary>
        public void AddScale(int controlPoint) => Add(controlPoint, Vector3.One);

        /// <summary>One component of a control point read as a scale or a multiplier.</summary>
        public void AddScale(int controlPoint, int component) => Add(controlPoint, component, 1f);

        /// <summary>A control point remapped into a particle field, at that field's default.</summary>
        public void AddField(int controlPoint, ParticleField field) => Add(controlPoint, FieldDefault(field));

        /// <summary>One component of a control point remapped into a particle field.</summary>
        public void AddField(int controlPoint, int component, ParticleField field) => Add(controlPoint, component, FieldDefault(field).X);

        /// <summary>A control point with an explicit default.</summary>
        public void Add(int controlPoint, Vector3 value)
        {
            for (var component = 0; component < 3; component++)
            {
                Add(controlPoint, component, value[component]);
            }
        }

        private void Add(int controlPoint, int component, float value)
        {
            if (controlPoint is <= 0 or > ParticleSystemState.MaxControlPoint || (uint)component > 2 || value == 0f)
            {
                return;
            }

            values.TryGetValue(controlPoint, out var current);

            if (current[component] != 0f)
            {
                return;
            }

            current[component] = value;
            values[controlPoint] = current;
        }

        private static Vector3 FieldDefault(ParticleField field) => field switch
        {
            ParticleField.Color or ParticleField.GlowRgb => new Vector3(255f),
            ParticleField.Normal => Vector3.UnitZ,
            ParticleField.LifeDuration or ParticleField.Radius or ParticleField.RollSpeed or ParticleField.Alpha
                or ParticleField.TrailLength or ParticleField.SecondSequenceNumber or ParticleField.AlphaAlternate
                or ParticleField.GlowAlpha or ParticleField.ForceScale => Vector3.One,
            ParticleField.ParentParticleIndex or ParticleField.ParentParticleId or ParticleField.RopeSegmentId => new Vector3(-1f),
            _ => Vector3.Zero,
        };
    }
}
