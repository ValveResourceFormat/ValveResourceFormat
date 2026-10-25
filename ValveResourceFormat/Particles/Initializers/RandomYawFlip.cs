namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Randomly flips the yaw of a particle by 180 degrees based on a configurable flip percentage. The
    /// flipped yaw is folded into one half turn first, so it lands between 180 and 360 degrees for a
    /// positive yaw.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomYawFlip">C_INIT_RandomYawFlip</seealso>
    class RandomYawFlip : ParticleFunctionInitializer
    {
        private readonly float percent;

        public RandomYawFlip(ParticleDefinitionParser parse) : base(parse)
        {
            percent = parse.Float("m_flPercent", 0.5f);
        }

        public override ulong WrittenFields => ParticleField.Yaw.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var yaws = run.Floats(ParticleField.Yaw);

            for (var i = 0; i < yaws.Length; i++)
            {
                if (particleSystemState.Random.Next() < percent)
                {
                    yaws[i] = (yaws[i] % MathF.PI) + MathF.PI;
                }
            }
        }
    }
}
