using System;
using System.Globalization;
using System.IO;

namespace GUI
{
    internal class InvariantCultureStreamWriter : StreamWriter
    {
        public InvariantCultureStreamWriter(Stream stream)
            : base(stream)
        {
            // :shrug:
        }

        public override IFormatProvider FormatProvider => CultureInfo.InvariantCulture;
    }
}
