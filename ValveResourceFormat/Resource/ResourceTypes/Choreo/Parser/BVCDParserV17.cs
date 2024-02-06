using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes.Choreo.Parser
{
    internal class BVCDParserV17 : BVCDParser
    {
        public override byte Version => 17;
        public BVCDParserV17(BinaryReader reader, string[] strings) : base(reader, strings)
        {
        }
    }
}
