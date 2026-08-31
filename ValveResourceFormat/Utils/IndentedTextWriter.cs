using System.Globalization;
using System.IO;

namespace ValveResourceFormat.Utils
{
    /// <summary>
    /// An <see cref="System.CodeDom.Compiler.IndentedTextWriter"/> that indents with tabs and writes into a <see cref="StringWriter"/>,
    /// so the accumulated text can be retrieved with <see cref="ToString"/>.
    /// </summary>
    public class IndentedTextWriter : System.CodeDom.Compiler.IndentedTextWriter
    {
        /// <summary>
        /// The string emitted once per indentation level.
        /// </summary>
        public const string TabString = "\t";

        /// <summary>
        /// Initializes a new instance of the <see cref="IndentedTextWriter"/> class writing into a new invariant-culture <see cref="StringWriter"/>.
        /// </summary>
#pragma warning disable CA2000 // StringWriter holds no resources, disposing it would only make later writes throw
        public IndentedTextWriter()
            : base(new StringWriter(CultureInfo.InvariantCulture), TabString)
        {
        }
#pragma warning restore CA2000

        /// <summary>
        /// Initializes a new instance of the <see cref="IndentedTextWriter"/> class that writes into the given <see cref="StringWriter"/>.
        /// </summary>
        public IndentedTextWriter(StringWriter writer)
            : base(writer, TabString)
        {
        }

        /// <inheritdoc/>
        [Obsolete("Do not write to string as a generic object, this is probably a mistake.")]
#pragma warning disable CS0809 // Obsolete member overrides non-obsolete member
        public override void Write(object? value)
#pragma warning restore CS0809 // Obsolete member overrides non-obsolete member
        {
            base.Write(value);
        }

        /// <summary>
        /// Returns the text written so far.
        /// </summary>
        public override string ToString()
        {
            return InnerWriter.ToString()!;
        }
    }
}
