using System;

namespace ValveResourceFormat.ShaderParser
{
    public class ShaderParserException : Exception
    {
        public ShaderParserException() { }
        public ShaderParserException(string message) : base(message) { }
        public ShaderParserException(string message, Exception innerException) : base(message, innerException) { }
    }
}
