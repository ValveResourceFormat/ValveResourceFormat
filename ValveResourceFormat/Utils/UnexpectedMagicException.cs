using System.Runtime.CompilerServices;

namespace ValveResourceFormat.Utils
{
    /// <summary>
    /// Exception thrown when an unexpected magic number or value is encountered.
    /// </summary>
#pragma warning disable CA1032 // Implement standard exception constructors
    public class UnexpectedMagicException : Exception
#pragma warning restore CA1032 // Implement standard exception constructors
    {
        private readonly string Magic;
        private readonly string MagicNameof;

        private readonly bool IsAssertion;

        /// <inheritdoc/>
        public override string Message => IsAssertion
            ? base.Message
            : $"{base.Message} for variable '{MagicNameof}': {Magic}";

        /// <summary>
        /// Initializes a new instance of the <see cref="UnexpectedMagicException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="magic">The unexpected magic value.</param>
        /// <param name="nameofMagic">The name of the magic parameter.</param>
        public UnexpectedMagicException(string message, int magic, [CallerArgumentExpression(nameof(magic))] string? nameofMagic = null) : base(message)
        {
            Magic = $"{magic} (0x{magic:X})";
            MagicNameof = nameofMagic ?? string.Empty;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UnexpectedMagicException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="magic">The unexpected magic value.</param>
        /// <param name="nameofMagic">The name of the magic parameter.</param>
        public UnexpectedMagicException(string message, uint magic, [CallerArgumentExpression(nameof(magic))] string? nameofMagic = null) : base(message)
        {
            Magic = $"{magic} (0x{magic:X})";
            MagicNameof = nameofMagic ?? string.Empty;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UnexpectedMagicException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="magic">The unexpected magic value.</param>
        /// <param name="nameofMagic">The name of the magic parameter.</param>
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

        /// <summary>
        /// Asserts that a condition is true, throwing an exception if it's false.
        /// </summary>
        /// <typeparam name="T">The type of the actual magic value.</typeparam>
        /// <param name="condition">The condition to check.</param>
        /// <param name="actualMagic">The actual magic value.</param>
        /// <param name="conditionExpression">The condition expression string.</param>
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
