using System;

namespace ValveResourceFormat
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ExtensionAttribute : Attribute
    {
        public string Extension { get; }

        public ExtensionAttribute(string extension)
        {
            Extension = extension;
        }
    }
}
