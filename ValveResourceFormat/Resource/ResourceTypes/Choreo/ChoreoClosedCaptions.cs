using ValveResourceFormat.ResourceTypes.Choreo.Enums;

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    public class ChoreoClosedCaptions
    {
        public ChoreoClosedCaptionsType Type { get; private set; }
        public string Token { get; private set; }
        public ChoreoClosedCaptionsFlags Flags { get; private set; }
        public ChoreoClosedCaptions(ChoreoClosedCaptionsType type, string token, ChoreoClosedCaptionsFlags flags)
        {
            Type = type;
            Token = token;
            Flags = flags;
        }
    }
}
