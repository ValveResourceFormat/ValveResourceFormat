using System;
using System.Collections.Generic;
using ValveResourceFormat;

namespace Decompiler
{
    public class ResourceStat
    {
        public ResourceType Type;
        public ushort Version;
        public uint Count;
        public string Info;
        public List<string> FilePaths;

        public ResourceStat(Resource resource, string info = "", string filePath = "")
        {
            Type = resource.ResourceType;
            Version = resource.Version;
            Count = 1;
            Info = info;
            FilePaths = new List<string>() { filePath };
        }
    }
}
