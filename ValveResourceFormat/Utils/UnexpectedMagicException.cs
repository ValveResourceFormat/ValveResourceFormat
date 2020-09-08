using System;

namespace ValveResourceFormat.Utils
{
#pragma warning disable CA1032 // Implement standard exception constructors
    public class UnexpectedMagicException : Exception
#pragma warning restore CA1032 // Implement standard exception constructors
    {
        private string Magic;
        private string MagicNameof;

        public override string Message => $"{base.Message} for variable '{MagicNameof}': {Magic}";

        public UnexpectedMagicException(string message, int magic, string nameofMagic) : base(message)
        {
            Magic = $"{magic} (0x{magic:X})";
            MagicNameof = nameofMagic;
        }

        public UnexpectedMagicException(string message, uint magic, string nameofMagic) : base(message)
        {
            Magic = $"{magic} (0x{magic:X})";
            MagicNameof = nameofMagic;
        }

        public UnexpectedMagicException(string message, string magic, string nameofMagic) : base(message)
        {
            Magic = magic;
            MagicNameof = nameofMagic;
        }
    }
}
