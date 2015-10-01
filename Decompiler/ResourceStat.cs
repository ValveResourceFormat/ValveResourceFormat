using System;
using ValveResourceFormat;

namespace Decompiler
{
    public class ResourceStat
    {
        public ResourceType Type;
        public ushort Version;
        public uint Count;
        public string Info;

        public ResourceStat(Resource resource, string info = "")
        {
            Type = resource.ResourceType;
            Version = resource.Version;
            Count = 1;
            Info = info;
        }
    }
}
