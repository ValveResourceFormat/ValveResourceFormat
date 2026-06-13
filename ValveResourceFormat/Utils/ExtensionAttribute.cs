namespace ValveResourceFormat
{
    /// <summary>
    /// Attribute to specify file extension for resource types.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ExtensionAttribute : Attribute
    {
        /// <summary>
        /// Gets the file extension.
        /// </summary>
        public string Extension { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExtensionAttribute"/> class.
        /// </summary>
        /// <param name="extension">The file extension.</param>
        public ExtensionAttribute(string extension)
        {
            Extension = extension;
        }
    }
}
