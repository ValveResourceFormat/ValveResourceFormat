using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.KeyValues
{
    public enum KVFlag
    {
        None,
        Resource,
        DeferredResource
    }

    public class KVFlaggedValue : KVValue
    {
        public KVFlag Flag { get; private set; }

        public KVFlaggedValue(KVType type, object value)
            : base(type, value)
        {
            Flag = KVFlag.None;
        }

        public KVFlaggedValue(KVType type, KVFlag flag, object value)
            : base(type, value)
        {
            Flag = flag;
        }
    }
}
