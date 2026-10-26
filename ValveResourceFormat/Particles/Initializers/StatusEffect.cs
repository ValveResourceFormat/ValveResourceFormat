namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Stores constant status effect material values (second detail texture, specular, rim light and
    /// blends) in the attributes of every new particle, for the game's status effect renderer to send to
    /// the material of the model it affects. That renderer draws nothing in the viewer, but the attributes
    /// it takes over, position and radius among them, still feed the rest of the system. A value of -1
    /// tells the renderer to leave the material's own value alone.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_StatusEffect">C_INIT_StatusEffect</seealso>
    class StatusEffect : ParticleFunctionInitializer
    {
        private readonly Detail2Combo detail2Combo = Detail2Combo.DETAIL_2_COMBO_UNINITIALIZED;
        private readonly float detail2Rotation = -1f;
        private readonly float detail2Scale = -1f;
        private readonly float detail2BlendFactor = -1f;
        private readonly float colorWarpIntensity = -1f;
        private readonly float diffuseWarpBlendToFull = -1f;
        private readonly float envMapIntensity = -1f;
        private readonly float ambientScale = -1f;
        private readonly Vector3 specularColor;
        private readonly float specularScale = -1f;
        private readonly float specularExponent = -1f;
        private readonly float specularExponentBlendToFull = -1f;
        private readonly float specularBlendToFull = -1f;
        private readonly Vector3 rimLightColor;
        private readonly float rimLightScale = -1f;
        private readonly float reflectionsTintByBaseBlendToNone = -1f;
        private readonly float metalnessBlendToFull = -1f;
        private readonly float selfIllumBlendToFull = -1f;

        public StatusEffect(ParticleDefinitionParser parse) : base(parse)
        {
            detail2Combo = parse.Enum("m_nDetail2Combo", detail2Combo);
            detail2Rotation = parse.Float("m_flDetail2Rotation", detail2Rotation);
            detail2Scale = parse.Float("m_flDetail2Scale", detail2Scale);
            detail2BlendFactor = parse.Float("m_flDetail2BlendFactor", detail2BlendFactor);
            colorWarpIntensity = parse.Float("m_flColorWarpIntensity", colorWarpIntensity);
            diffuseWarpBlendToFull = parse.Float("m_flDiffuseWarpBlendToFull", diffuseWarpBlendToFull);
            envMapIntensity = parse.Float("m_flEnvMapIntensity", envMapIntensity);
            ambientScale = parse.Float("m_flAmbientScale", ambientScale);
            specularColor = parse.Color24("m_specularColor", specularColor);
            specularScale = parse.Float("m_flSpecularScale", specularScale);
            specularExponent = parse.Float("m_flSpecularExponent", specularExponent);
            specularExponentBlendToFull = parse.Float("m_flSpecularExponentBlendToFull", specularExponentBlendToFull);
            specularBlendToFull = parse.Float("m_flSpecularBlendToFull", specularBlendToFull);
            rimLightColor = parse.Color24("m_rimLightColor", rimLightColor);
            rimLightScale = parse.Float("m_flRimLightScale", rimLightScale);
            reflectionsTintByBaseBlendToNone = parse.Float("m_flReflectionsTintByBaseBlendToNone", reflectionsTintByBaseBlendToNone);
            metalnessBlendToFull = parse.Float("m_flMetalnessBlendToFull", metalnessBlendToFull);
            selfIllumBlendToFull = parse.Float("m_flSelfIllumBlendToFull", selfIllumBlendToFull);
        }

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask()
            | ParticleField.Radius.Mask() | ParticleField.Roll.Mask() | ParticleField.RollSpeed.Mask()
            | ParticleField.Color.Mask() | ParticleField.Alpha.Mask() | ParticleField.SequenceNumber.Mask()
            | ParticleField.TrailLength.Mask() | ParticleField.Yaw.Mask() | ParticleField.SecondSequenceNumber.Mask()
            | ParticleField.HitboxOffsetPosition.Mask() | ParticleField.GlowRgb.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            run.Floats(ParticleField.SequenceNumber).Fill((float)detail2Combo);
            run.Floats(ParticleField.Radius).Fill(detail2Scale);
            run.Floats(ParticleField.Roll).Fill(colorWarpIntensity);
            run.Floats(ParticleField.RollSpeed).Fill(detail2Rotation);
            run.Floats(ParticleField.Alpha).Fill(detail2BlendFactor);
            run.Floats(ParticleField.TrailLength).Fill(envMapIntensity);
            run.Floats(ParticleField.Yaw).Fill(ambientScale);
            run.Floats(ParticleField.SecondSequenceNumber).Fill(rimLightScale);
            run.Vectors(ParticleField.Position).Fill(new Vector3(specularScale, 0f, diffuseWarpBlendToFull));
            run.Vectors(ParticleField.PositionPrevious).Fill(new Vector3(specularExponent, specularExponentBlendToFull, specularBlendToFull));
            run.Vectors(ParticleField.HitboxOffsetPosition).Fill(new Vector3(reflectionsTintByBaseBlendToNone, metalnessBlendToFull, selfIllumBlendToFull));
            run.Vectors(ParticleField.Color).Fill(specularColor);
            run.Vectors(ParticleField.GlowRgb).Fill(rimLightColor);
        }
    }
}
