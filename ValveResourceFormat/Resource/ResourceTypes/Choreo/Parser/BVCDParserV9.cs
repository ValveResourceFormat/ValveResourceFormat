using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes.Choreo.Parser
{
    internal class BVCDParserV9 : BVCDParser
    {
        public override byte Version => 9;
        public BVCDParserV9(BinaryReader reader, string[] strings) : base(reader, strings)
        {
        }
    }
}
