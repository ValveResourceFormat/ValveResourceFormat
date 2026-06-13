namespace ValveResourceFormat.CompiledShader
{
    /// <summary>
    /// Exception thrown during shader parsing.
    /// </summary>
    public class ShaderParserException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the ShaderParserException class.
        /// </summary>
        public ShaderParserException() { }

        /// <summary>
        /// Initializes a new instance with a message.
        /// </summary>
        public ShaderParserException(string message) : base(message) { }

        /// <summary>
        /// Initializes a new instance with a message and inner exception.
        /// </summary>
        public ShaderParserException(string message, Exception innerException) : base(message, innerException) { }
    }
}
