using System.Collections;

namespace ValveResourceFormat.Renderer.Materials
{
    /// <summary>
    /// A <see cref="RenderMaterial"/>'s inputs of one type, keyed by uniform name.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    public sealed class MaterialInputs<T> : IReadOnlyDictionary<string, T>
    {
        private readonly Dictionary<string, T> Items = [];

        /// <summary>
        /// Gets a value that changes every time an entry is added, removed, or set to a different value.
        /// Combine several of these by summing them; any one of them changing changes the sum.
        /// </summary>
        public uint Version { get; private set; }

        /// <summary>
        /// Gets or sets the value for the given parameter name. Setting counts as a change only when the
        /// value differs from what was there, so rewriting the same value every frame costs nothing.
        /// </summary>
        public T this[string key]
        {
            get => Items[key];
            set
            {
                var changed = !Items.TryGetValue(key, out var existing) || !EqualityComparer<T>.Default.Equals(existing, value);

                Items[key] = value;

                if (changed)
                {
                    unchecked
                    {
                        Version++;
                    }
                }
            }
        }

        /// <summary>Gets the parameter names.</summary>
        public Dictionary<string, T>.KeyCollection Keys => Items.Keys;

        /// <summary>Gets the parameter values.</summary>
        public Dictionary<string, T>.ValueCollection Values => Items.Values;

        /// <inheritdoc/>
        public int Count => Items.Count;

        IEnumerable<string> IReadOnlyDictionary<string, T>.Keys => Items.Keys;

        IEnumerable<T> IReadOnlyDictionary<string, T>.Values => Items.Values;

        /// <summary>Adds a parameter, throwing if it is already present. Counts as a change.</summary>
        public void Add(string key, T value)
        {
            Items.Add(key, value);

            unchecked
            {
                Version++;
            }
        }

        /// <summary>Removes a parameter. Counts as a change when one was removed.</summary>
        public bool Remove(string key)
        {
            if (!Items.Remove(key))
            {
                return false;
            }

            unchecked
            {
                Version++;
            }

            return true;
        }

        /// <summary>Removes every parameter. Counts as a change when there was anything to remove.</summary>
        public void Clear()
        {
            if (Items.Count == 0)
            {
                return;
            }

            Items.Clear();

            unchecked
            {
                Version++;
            }
        }

        /// <inheritdoc/>
        public bool ContainsKey(string key) => Items.ContainsKey(key);

        /// <inheritdoc/>
        public bool TryGetValue(string key, out T value) => Items.TryGetValue(key, out value!);

        /// <summary>Ensures the backing storage can hold the given number of parameters without resizing.</summary>
        /// <param name="capacity">The minimum number of parameters to make room for.</param>
        /// <returns>The resulting capacity.</returns>
        public int EnsureCapacity(int capacity) => Items.EnsureCapacity(capacity);

        /// <summary>
        /// Returns a lookup into the same storage keyed by an alternate type, typically a
        /// <see cref="ReadOnlySpan{T}"/> of <see cref="char"/>, to avoid allocating a string per probe.
        /// </summary>
        /// <remarks>Writes made through the lookup do not count towards <see cref="Version"/>.</remarks>
        /// <typeparam name="TAlternate">The alternate key type.</typeparam>
        public Dictionary<string, T>.AlternateLookup<TAlternate> GetAlternateLookup<TAlternate>()
            where TAlternate : notnull, allows ref struct
            => Items.GetAlternateLookup<TAlternate>();

        /// <summary>Returns an enumerator over the parameters.</summary>
        public Dictionary<string, T>.Enumerator GetEnumerator() => Items.GetEnumerator();

        IEnumerator<KeyValuePair<string, T>> IEnumerable<KeyValuePair<string, T>>.GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => Items.GetEnumerator();
    }
}
