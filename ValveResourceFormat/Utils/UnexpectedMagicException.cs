using System.Runtime.CompilerServices;

namespace ValveResourceFormat.Utils
{
#pragma warning disable CA1032 // Implement standard exception constructors
    public class UnexpectedMagicException : Exception
#pragma warning restore CA1032 // Implement standard exception constructors
    {
        private readonly string Magic;
        private readonly string MagicNameof;

        private readonly bool IsAssertion;

        public override string Message => IsAssertion
            ? base.Message
            : $"{base.Message} for variable '{MagicNameof}': {Magic}";

        public UnexpectedMagicException(string message, int magic, [CallerArgumentExpression(nameof(magic))] string? nameofMagic = null) : base(message)
        {
            Magic = $"{magic} (0x{magic:X})";
            MagicNameof = nameofMagic ?? string.Empty;
        }

        public UnexpectedMagicException(string message, uint magic, [CallerArgumentExpression(nameof(magic))] string? nameofMagic = null) : base(message)
        {
            Magic = $"{magic} (0x{magic:X})";
            MagicNameof = nameofMagic ?? string.Empty;
        }

        public UnexpectedMagicException(string message, string magic, [CallerArgumentExpression(nameof(magic))] string? nameofMagic = null) : base(message)
        {
            Magic = magic;
            MagicNameof = nameofMagic ?? string.Empty;
        }

        private UnexpectedMagicException(string customAssertMessage) : base(customAssertMessage)
        {
            Magic = string.Empty;
            MagicNameof = string.Empty;
            IsAssertion = true;
        }

        public static void Assert<T>(bool condition, T actualMagic,
            [CallerArgumentExpression(nameof(condition))] string? conditionExpression = null)
        {
            if (!condition)
            {
                var formattedMagic = actualMagic is int or uint or byte
                    ? $"{actualMagic} (0x{actualMagic:X})"
                    : $"{actualMagic}";
                throw new UnexpectedMagicException($"Assertion '{conditionExpression}' failed. Value: {formattedMagic}");
            }
        }
    }
}
