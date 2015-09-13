using System;
using System.IO;

namespace ValveResourceFormat
{
    public abstract class IResourceType
    {
        public abstract void Read(BinaryReader reader);
    }
}
