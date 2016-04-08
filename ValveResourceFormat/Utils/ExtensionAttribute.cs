using System;

namespace ValveResourceFormat
{
    [AttributeUsage(AttributeTargets.Field)]
    public class ExtensionAttribute : Attribute
    {
        public string Extension { get; }

        public ExtensionAttribute(string extension)
        {
            Extension = extension;
        }
    }
}
