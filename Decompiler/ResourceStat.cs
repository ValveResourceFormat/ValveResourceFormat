using System;
using ValveResourceFormat;

namespace Decompiler
{
    public class ResourceStat
    {
        public ResourceType Type;
        public ushort Version;
        public uint Count;

        public ResourceStat(Resource resource)
        {
            Type = resource.ResourceType;
            Version = resource.Version;
            Count = 1;
        }
    }
}
