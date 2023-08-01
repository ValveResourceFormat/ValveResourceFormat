namespace GUI.Types.ParticleRenderer
{
    abstract class ParticleFunction
    {
        //INumberProvider OpStrength; // operator strength
        //ParticleEndCapMode OpEndCapState; // operator end cap state
        public float OpStartFadeInTime; // operator start fadein
        public float OpEndFadeInTime; // operator end fadein
        public float OpStartFadeOutTime; // operator start fadeout
        public float OpEndFadeOutTime; // operator end fadeout
        public float OpFadeOscillatePeriod; // operator fade oscillate
        //bool NormalizeToStopTime; // normalize fade times to endcap
        //float OpTimeOffsetMin; // operator fade time offset min
        //float OpTimeOffsetMax; // operator fade time offset max
        //int OpTimeOffsetSeed; // operator fade time offset seed
        //int OpTimeScaleSeed; // operator fade time scale seed
        //float OpTimeScaleMin; // operator fade time scale min
        //float OpTimeScaleMax; // operator fade time scale max

        public bool StrengthFastPath;

        public ParticleFunction(ParticleDefinitionParser parse)
        {
            OpStartFadeInTime = parse.Float("m_flOpStartFadeInTime");
            OpEndFadeInTime = parse.Float("m_flOpEndFadeInTime");
            OpStartFadeOutTime = parse.Float("m_flOpStartFadeOutTime");
            OpEndFadeOutTime = parse.Float("m_flOpEndFadeOutTime");
            OpFadeOscillatePeriod = parse.Float("m_flOpFadeOscillatePeriod");

            StrengthFastPath =
                OpStartFadeInTime == 0f &&
                OpEndFadeInTime == 0f &&
                OpStartFadeOutTime == 0f &&
                OpEndFadeOutTime == 0f;
            //OpTimeOffsetMin == 0f &&
            //OpTimeOffsetMax == 0f &&
            //OpTimeScaleMin == 1f &&
            //OpTimeScaleMax == 1f &&
            //OpStrengthMaxScale == 1f &&
            //OpStrengthMinScale == 1f &&
            //OpEndCapState == ParticleEndCapMode.PARTICLE_ENDCAP_ALWAYS_ON);
        }
    }
}
