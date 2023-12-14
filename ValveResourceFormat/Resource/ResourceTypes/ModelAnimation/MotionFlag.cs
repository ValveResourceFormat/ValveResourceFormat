using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    [Flags]
    public enum MotionFlag
    {
        TX = 64,
        TY = 128,
        TZ = 256,
        RZ = 2048,
        Linear = 4096,
    }
}
