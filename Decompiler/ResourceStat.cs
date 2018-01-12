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
        public string FilePath;

        public ResourceStat(Resource resource, string info = "", string filePath = "")
        {
            Type = resource.ResourceType;
            Version = resource.Version;
            Count = 1;
            Info = info;
            FilePath = filePath;
        }
    }
}
