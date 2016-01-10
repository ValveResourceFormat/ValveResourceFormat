using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;

namespace ValveResourceFormat.KeyValues
{

    // Class to hold type + value
    public class KVValue
    {
        public KVType Type { get; private set; }
        public object Value { get; private set; }

        public KVValue(KVType type, object value)
        {
            Type = type;
            Value = value;
        }
    }
}
