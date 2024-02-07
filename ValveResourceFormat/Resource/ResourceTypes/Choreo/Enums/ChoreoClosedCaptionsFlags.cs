using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes.Choreo.Enums
{
    [Flags]
    public enum ChoreoClosedCaptionsFlags
    {
        UsingCombinedFile = 1,
        CombinedUsingGenderToken = 2,
        SuppressingCaptionAttenuation = 4,
        HardStopSpeakEvent = 8,
        VolumeMatchesEventRamp = 16
    }
}
