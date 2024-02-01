namespace GUI.Utils;

// https://github.com/dotnet/runtime/issues/934#issuecomment-1642662272
internal static class SpanExtensions
{
    /// <summary>
    /// Splits the span by the given sentinel, removing empty segments.
    /// </summary>
    /// <param name="span">The span to split</param>
    /// <param name="sentinel">The sentinel to split the span on.</param>
    /// <returns>An enumerator over the span segments.</returns>
    public static StringSplitEnumerator Split(this ReadOnlySpan<char> span, ReadOnlySpan<char> sentinel) => new(span, sentinel);

    internal ref struct StringSplitEnumerator
    {
        private readonly ReadOnlySpan<char> _sentinel;
        private ReadOnlySpan<char> _span;

        public StringSplitEnumerator(ReadOnlySpan<char> span, ReadOnlySpan<char> sentinel)
        {
            _span = span;
            _sentinel = sentinel;
        }

        public bool MoveNext()
        {
            if (_span.Length == 0)
            {
                return false;
            }

            var index = _span.IndexOf(_sentinel, StringComparison.Ordinal);
            if (index < 0)
            {
                Current = _span;
                _span = default;
            }
            else
            {
                Current = _span[..index];
                _span = index + 1 >= _span.Length ? default : _span[(index + 1)..];
            }

            if (Current.Length == 0)
            {
                return MoveNext();
            }

            return true;
        }

        public ReadOnlySpan<char> Current { readonly get; private set; }

        public readonly StringSplitEnumerator GetEnumerator() => this;
    }
}
