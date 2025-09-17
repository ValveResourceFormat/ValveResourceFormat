using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;

namespace ValveResourceFormat
{
    /// <summary>
    /// The same as <see cref="System.CodeDom.Compiler.IndentedTextWriter" /> but works in partial trust.
    /// Taken from System.Data.Entity.Migrations.Utilities.IndentedTextWriter.
    /// </summary>
    public class IndentedTextWriter : TextWriter
    {
        /// <summary>
        /// Specifies the default tab string. This field is constant.
        /// </summary>
        public const string TabString = "\t";

        private readonly StringWriter writer;
        private readonly bool writerIsOwned;
        private int indentLevel;
        private bool tabsPending;

        /// <summary>
        /// Gets the encoding for the text writer to use.
        /// </summary>
        /// <returns>
        /// An <see cref="System.Text.Encoding" /> that indicates the encoding for the text writer to use.
        /// </returns>
        public override Encoding Encoding
        {
            get { return writer.Encoding; }
        }

        /// <summary>
        /// Gets or sets the new line character to use.
        /// </summary>
        /// <returns> The new line character to use. </returns>
        [AllowNull]
        public override string NewLine
        {
            get { return writer.NewLine; }
            set { writer.NewLine = value; }
        }

        /// <summary>
        /// Gets or sets the number of spaces to indent.
        /// </summary>
        /// <returns> The number of spaces to indent. </returns>
        public int Indent
        {
            get
            {
                return indentLevel;
            }

            set
            {
                if (value < 0)
                {
                    value = 0;
                }

                indentLevel = value;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IndentedTextWriter"/> class.
        /// </summary>
        public IndentedTextWriter()
            : base(CultureInfo.InvariantCulture)
        {
            var builder = new StringBuilder();
            writer = new StringWriter(builder, CultureInfo.InvariantCulture);
            writerIsOwned = true;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IndentedTextWriter"/> class.
        /// </summary>
        public IndentedTextWriter(StringWriter writer)
            : base(writer.FormatProvider)
        {
            this.writer = writer;
        }

        /// <inheritdoc/>
        public override void Close()
        {
            writer.Close();
        }

        /// <inheritdoc/>
        public override void Flush()
        {
            writer.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && writerIsOwned)
            {
                writer.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Grows this writer to match the specified min capacity.
        /// </summary>
        /// <param name="minCapacity">The minimum capacity for this writer.</param>
        public int Grow(int minCapacity) => writer.GetStringBuilder().EnsureCapacity(writer.GetStringBuilder().Length + minCapacity);

        /// <summary>
        /// Ensures that the capacity of this writer is at least the specified value.
        /// </summary>
        /// <param name="capacity">The new capacity for this writer.</param>
        public int EnsureCapacity(int capacity) => writer.GetStringBuilder().EnsureCapacity(capacity);

        /// <summary>
        /// Outputs the tab string once for each level of indentation according to the <see cref="Indent" /> property.
        /// </summary>
        protected virtual void OutputTabs()
        {
            if (!tabsPending)
            {
                return;
            }

            for (var index = 0; index < indentLevel; ++index)
            {
                writer.Write(TabString);
            }

            tabsPending = false;
        }

        /// <inheritdoc/>
        public override void Write(string? value)
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <inheritdoc/>
        public override void Write(bool value)
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <inheritdoc/>
        public override void Write(char value)
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <inheritdoc/>
        public override void Write(char[]? buffer)
        {
            OutputTabs();
            writer.Write(buffer);
        }

        /// <inheritdoc/>
        public override void Write(char[] buffer, int index, int count)
        {
            OutputTabs();
            writer.Write(buffer, index, count);
        }

        /// <inheritdoc/>
        public override void Write(decimal value)
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <inheritdoc/>
        public override void Write(double value)
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <inheritdoc/>
        public override void Write(float value)
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <inheritdoc/>
        public override void Write(int value)
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <inheritdoc/>
        public override void Write(long value)
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <inheritdoc/>
        [Obsolete("Do not write to string as a generic object, this is probably a mistake.")]
#pragma warning disable CS0809 // Obsolete member overrides non-obsolete member
        public override void Write(object? value)
#pragma warning restore CS0809 // Obsolete member overrides non-obsolete member
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <inheritdoc/>
        public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0)
        {
            OutputTabs();
            writer.Write(format, arg0);
        }

        /// <inheritdoc/>
        public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1)
        {
            OutputTabs();
            writer.Write(format, arg0, arg1);
        }

        /// <inheritdoc/>
        public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params object?[] arg)
        {
            OutputTabs();
            writer.Write(format, arg);
        }

        /// <inheritdoc/>
        public override void WriteLine(string? value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine(ReadOnlySpan<char> buffer)
        {
            OutputTabs();
            writer.WriteLine(buffer);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine()
        {
            OutputTabs();
            writer.WriteLine();
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine(bool value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine(char value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine(char[]? buffer)
        {
            OutputTabs();
            writer.WriteLine(buffer);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine(char[] buffer, int index, int count)
        {
            OutputTabs();
            writer.WriteLine(buffer, index, count);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine(double value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine(float value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine(int value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine(long value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine(object? value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine(uint value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine(ulong value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0)
        {
            OutputTabs();
            writer.WriteLine(format, arg0);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1)
        {
            OutputTabs();
            writer.WriteLine(format, arg0, arg1);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1, object? arg2)
        {
            OutputTabs();
            writer.WriteLine(format, arg0, arg1, arg2);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params object?[] arg)
        {
            OutputTabs();
            writer.WriteLine(format, arg);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params ReadOnlySpan<object?> arg)
        {
            OutputTabs();
            writer.WriteLine(format, arg);
            tabsPending = true;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return writer.ToString();
        }
    }
}
