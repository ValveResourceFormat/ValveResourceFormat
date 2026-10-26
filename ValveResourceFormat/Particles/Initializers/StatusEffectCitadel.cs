namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Stores constant status effect material values (color warp, normal, metalness, roughness, self
    /// illumination and the scrolling effect and detail textures) in the attributes of every new particle,
    /// for the game's status effect renderer to send to the material of the model it affects. That renderer
    /// draws nothing in the viewer, but the attributes it takes over, position and radius among them, still
    /// feed the rest of the system.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_StatusEffectCitadel">C_INIT_StatusEffectCitadel</seealso>
    class StatusEffectCitadel : ParticleFunctionInitializer
    {
        private readonly float colorWarpAmount;
        private readonly float normalAmount;
        private readonly float metalnessAmount;
        private readonly float roughnessAmount;
        private readonly float selfIllumAmount;
        private readonly float textureScale = 1f;
        private readonly Vector3 textureScroll;
        private readonly Vector3 textureOffset;
        private readonly DetailCombo detailCombo = DetailCombo.DETAIL_COMBO_OFF;
        private readonly float detailAmount;
        private readonly float detailScale = 1f;
        private readonly Vector3 detailScroll;
        private readonly float useModelUVs;

        public StatusEffectCitadel(ParticleDefinitionParser parse) : base(parse)
        {
            colorWarpAmount = parse.Float("m_flSFXColorWarpAmount", colorWarpAmount);
            normalAmount = parse.Float("m_flSFXNormalAmount", normalAmount);
            metalnessAmount = parse.Float("m_flSFXMetalnessAmount", metalnessAmount);
            roughnessAmount = parse.Float("m_flSFXRoughnessAmount", roughnessAmount);
            selfIllumAmount = parse.Float("m_flSFXSelfIllumAmount", selfIllumAmount);
            textureScale = parse.Float("m_flSFXSScale", textureScale);
            textureScroll = new Vector3(
                parse.Float("m_flSFXSScrollX"),
                parse.Float("m_flSFXSScrollY"),
                parse.Float("m_flSFXSScrollZ"));
            textureOffset = new Vector3(
                parse.Float("m_flSFXSOffsetX"),
                parse.Float("m_flSFXSOffsetY"),
                parse.Float("m_flSFXSOffsetZ"));
            detailCombo = parse.Enum("m_nDetailCombo", detailCombo);
            detailAmount = parse.Float("m_flSFXSDetailAmount", detailAmount);
            detailScale = parse.Float("m_flSFXSDetailScale", detailScale);
            detailScroll = new Vector3(
                parse.Float("m_flSFXSDetailScrollX"),
                parse.Float("m_flSFXSDetailScrollY"),
                parse.Float("m_flSFXSDetailScrollZ"));
            useModelUVs = parse.Float("m_flSFXSUseModelUVs", useModelUVs);
        }

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask()
            | ParticleField.Radius.Mask() | ParticleField.Roll.Mask() | ParticleField.RollSpeed.Mask()
            | ParticleField.Alpha.Mask() | ParticleField.SequenceNumber.Mask() | ParticleField.TrailLength.Mask()
            | ParticleField.Yaw.Mask() | ParticleField.SecondSequenceNumber.Mask() | ParticleField.HitboxOffsetPosition.Mask()
            | ParticleField.AlphaAlternate.Mask() | ParticleField.Pitch.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            run.Floats(ParticleField.SequenceNumber).Fill((float)detailCombo);
            run.Floats(ParticleField.Radius).Fill(colorWarpAmount);
            run.Floats(ParticleField.Roll).Fill(normalAmount);
            run.Floats(ParticleField.RollSpeed).Fill(metalnessAmount);
            run.Floats(ParticleField.Alpha).Fill(roughnessAmount);
            run.Floats(ParticleField.TrailLength).Fill(selfIllumAmount);
            run.Floats(ParticleField.Yaw).Fill(detailAmount);
            run.Floats(ParticleField.SecondSequenceNumber).Fill(detailScale);
            run.Floats(ParticleField.AlphaAlternate).Fill(textureScale);
            run.Floats(ParticleField.Pitch).Fill(useModelUVs);
            run.Vectors(ParticleField.Position).Fill(detailScroll);
            run.Vectors(ParticleField.PositionPrevious).Fill(textureScroll);
            run.Vectors(ParticleField.HitboxOffsetPosition).Fill(textureOffset);
        }
    }
}
