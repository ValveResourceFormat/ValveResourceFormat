namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Writes the offset from a control point to each particle, turned about an axis and scaled, into
    /// a vector attribute. Unnormalized, the offset is divided by the previous step's length, so it
    /// reads in units per second. A strength attribute, when set, blends toward the result per particle.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapDirectionToCPToVector">C_OP_RemapDirectionToCPToVector</seealso>
    class RemapDirectionToCPToVector : ParticleFunctionOperator
    {
        private readonly int cp;
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly float scale = 1f;
        private readonly bool normalize;
        private readonly ParticleField fieldStrength = ParticleField.NoneDisabled;

        private readonly Matrix4x4 rotation;

        public RemapDirectionToCPToVector(ParticleDefinitionParser parse) : base(parse)
        {
            cp = parse.Int32("m_nCP", cp);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            scale = parse.Float("m_flScale", scale);
            normalize = parse.Boolean("m_bNormalize", normalize);
            fieldStrength = parse.ParticleField("m_nFieldStrength", fieldStrength);

            rotation = Matrix4x4.CreateFromAxisAngle(parse.Vector3("m_vecOffsetAxis"), float.DegreesToRadians(parse.Float("m_flOffsetRot")));
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var center = particleSystemState.GetControlPoint(cp).Position;
            var factor = normalize ? scale : scale / MathF.Max(1e-20f, particles.PreviousFrameTime);

            foreach (var particle in particles.Current)
            {
                var direction = Vector3.Transform(particle.Position - center, rotation);

                if (normalize)
                {
                    direction = MathUtils.SafeNormalize(direction);
                }

                var value = direction * factor;

                if (fieldStrength != ParticleField.NoneDisabled)
                {
                    value = MathUtils.Lerp(particle.GetVector(fieldOutput), value, MathUtils.Saturate(particle.GetScalar(fieldStrength)));
                }

                particle.SetVector(fieldOutput, value);
            }
        }
    }
}
