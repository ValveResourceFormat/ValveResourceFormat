using System;
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

        private readonly TextWriter writer;
        private int indentLevel;
        private bool tabsPending;

        /// <summary>
        /// Gets the encoding for the text writer to use.
        /// </summary>
        /// <returns>
        /// An <see cref="T:System.Text.Encoding" /> that indicates the encoding for the text writer to use.
        /// </returns>
        public override Encoding Encoding
        {
            get { return writer.Encoding; }
        }

        /// <summary>
        /// Gets or sets the new line character to use.
        /// </summary>
        /// <returns> The new line character to use. </returns>
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
            writer = new StringWriter(CultureInfo.InvariantCulture);
            indentLevel = 0;
            tabsPending = false;
        }

        /// <summary>
        /// Closes the document being written to.
        /// </summary>
        public override void Close()
        {
            writer.Close();
        }

        /// <summary>
        /// Outputs the tab string once for each level of indentation according to the
        /// <see
        ///     cref="P:System.CodeDom.Compiler.IndentedTextWriter.Indent" />
        /// property.
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

        /// <summary>
        /// Writes the specified string to the text stream.
        /// </summary>
        /// <param name="value"> The string to write. </param>
        public override void Write(string value)
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <summary>
        /// Writes the text representation of a Boolean value to the text stream.
        /// </summary>
        /// <param name="value"> The Boolean value to write. </param>
        public override void Write(bool value)
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <summary>
        /// Writes a character to the text stream.
        /// </summary>
        /// <param name="value"> The character to write. </param>
        public override void Write(char value)
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <summary>
        /// Writes a character array to the text stream.
        /// </summary>
        /// <param name="buffer"> The character array to write. </param>
        public override void Write(char[] buffer)
        {
            OutputTabs();
            writer.Write(buffer);
        }

        /// <summary>
        /// Writes a subarray of characters to the text stream.
        /// </summary>
        /// <param name="buffer"> The character array to write data from. </param>
        /// <param name="index"> Starting index in the buffer. </param>
        /// <param name="count"> The number of characters to write. </param>
        public override void Write(char[] buffer, int index, int count)
        {
            OutputTabs();
            writer.Write(buffer, index, count);
        }

        /// <summary>
        /// Writes the text representation of a Double to the text stream.
        /// </summary>
        /// <param name="value"> The double to write. </param>
        public override void Write(double value)
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <summary>
        /// Writes the text representation of a Single to the text stream.
        /// </summary>
        /// <param name="value"> The single to write. </param>
        public override void Write(float value)
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <summary>
        /// Writes the text representation of an integer to the text stream.
        /// </summary>
        /// <param name="value"> The integer to write. </param>
        public override void Write(int value)
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <summary>
        /// Writes the text representation of an 8-byte integer to the text stream.
        /// </summary>
        /// <param name="value"> The 8-byte integer to write. </param>
        public override void Write(long value)
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <summary>
        /// Writes the text representation of an object to the text stream.
        /// </summary>
        /// <param name="value"> The object to write. </param>
        public override void Write(object value)
        {
            OutputTabs();
            writer.Write(value);
        }

        /// <summary>
        /// Writes out a formatted string, using the same semantics as specified.
        /// </summary>
        /// <param name="format"> The formatting string. </param>
        /// <param name="arg0"> The object to write into the formatted string. </param>
        public override void Write(string format, object arg0)
        {
            OutputTabs();
            writer.Write(format, arg0);
        }

        /// <summary>
        /// Writes out a formatted string, using the same semantics as specified.
        /// </summary>
        /// <param name="format"> The formatting string to use. </param>
        /// <param name="arg0"> The first object to write into the formatted string. </param>
        /// <param name="arg1"> The second object to write into the formatted string. </param>
        public override void Write(string format, object arg0, object arg1)
        {
            OutputTabs();
            writer.Write(format, arg0, arg1);
        }

        /// <summary>
        /// Writes out a formatted string, using the same semantics as specified.
        /// </summary>
        /// <param name="format"> The formatting string to use. </param>
        /// <param name="arg"> The argument array to output. </param>
        public override void Write(string format, params object[] arg)
        {
            OutputTabs();
            writer.Write(format, arg);
        }

        /// <summary>
        /// Writes the specified string to a line without tabs.
        /// </summary>
        /// <param name="value"> The string to write. </param>
        public void WriteLineNoTabs(string value)
        {
            writer.WriteLine(value);
        }

        /// <summary>
        /// Writes the specified string, followed by a line terminator, to the text stream.
        /// </summary>
        /// <param name="value"> The string to write. </param>
        public override void WriteLine(string value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <summary>
        /// Writes a line terminator.
        /// </summary>
        public override void WriteLine()
        {
            OutputTabs();
            writer.WriteLine();
            tabsPending = true;
        }

        /// <summary>
        /// Writes the text representation of a Boolean, followed by a line terminator, to the text stream.
        /// </summary>
        /// <param name="value"> The Boolean to write. </param>
        public override void WriteLine(bool value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <summary>
        /// Writes a character, followed by a line terminator, to the text stream.
        /// </summary>
        /// <param name="value"> The character to write. </param>
        public override void WriteLine(char value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <summary>
        /// Writes a character array, followed by a line terminator, to the text stream.
        /// </summary>
        /// <param name="buffer"> The character array to write. </param>
        public override void WriteLine(char[] buffer)
        {
            OutputTabs();
            writer.WriteLine(buffer);
            tabsPending = true;
        }

        /// <summary>
        /// Writes a subarray of characters, followed by a line terminator, to the text stream.
        /// </summary>
        /// <param name="buffer"> The character array to write data from. </param>
        /// <param name="index"> Starting index in the buffer. </param>
        /// <param name="count"> The number of characters to write. </param>
        public override void WriteLine(char[] buffer, int index, int count)
        {
            OutputTabs();
            writer.WriteLine(buffer, index, count);
            tabsPending = true;
        }

        /// <summary>
        /// Writes the text representation of a Double, followed by a line terminator, to the text stream.
        /// </summary>
        /// <param name="value"> The double to write. </param>
        public override void WriteLine(double value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <summary>
        /// Writes the text representation of a Single, followed by a line terminator, to the text stream.
        /// </summary>
        /// <param name="value"> The single to write. </param>
        public override void WriteLine(float value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <summary>
        /// Writes the text representation of an integer, followed by a line terminator, to the text stream.
        /// </summary>
        /// <param name="value"> The integer to write. </param>
        public override void WriteLine(int value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <summary>
        /// Writes the text representation of an 8-byte integer, followed by a line terminator, to the text stream.
        /// </summary>
        /// <param name="value"> The 8-byte integer to write. </param>
        public override void WriteLine(long value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <summary>
        /// Writes the text representation of an object, followed by a line terminator, to the text stream.
        /// </summary>
        /// <param name="value"> The object to write. </param>
        public override void WriteLine(object value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        /// <summary>
        /// Writes out a formatted string, followed by a line terminator, using the same semantics as specified.
        /// </summary>
        /// <param name="format"> The formatting string. </param>
        /// <param name="arg0"> The object to write into the formatted string. </param>
        public override void WriteLine(string format, object arg0)
        {
            OutputTabs();
            writer.WriteLine(format, arg0);
            tabsPending = true;
        }

        /// <summary>
        /// Writes out a formatted string, followed by a line terminator, using the same semantics as specified.
        /// </summary>
        /// <param name="format"> The formatting string to use. </param>
        /// <param name="arg0"> The first object to write into the formatted string. </param>
        /// <param name="arg1"> The second object to write into the formatted string. </param>
        public override void WriteLine(string format, object arg0, object arg1)
        {
            OutputTabs();
            writer.WriteLine(format, arg0, arg1);
            tabsPending = true;
        }

        /// <summary>
        /// Writes out a formatted string, followed by a line terminator, using the same semantics as specified.
        /// </summary>
        /// <param name="format"> The formatting string to use. </param>
        /// <param name="arg"> The argument array to output. </param>
        public override void WriteLine(string format, params object[] arg)
        {
            OutputTabs();
            writer.WriteLine(format, arg);
            tabsPending = true;
        }

        /// <summary>
        /// Writes the text representation of a UInt32, followed by a line terminator, to the text stream.
        /// </summary>
        /// <param name="value"> A UInt32 to output. </param>
        public override void WriteLine(uint value)
        {
            OutputTabs();
            writer.WriteLine(value);
            tabsPending = true;
        }

        public override string ToString()
        {
            return writer.ToString();
        }
    }
}
