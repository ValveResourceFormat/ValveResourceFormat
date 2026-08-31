namespace ValveResourceFormat.CompiledShader
{
    /// <summary>
    /// Size-limited cache for shader static combo data, evicting entries in insertion order (FIFO).
    /// </summary>
    public sealed class StaticComboCache : IDisposable
    {
        private readonly VfxProgramData Program;
        private readonly Dictionary<long, VfxStaticComboData> cache = [];
        private readonly LinkedList<long> insertionOrder = new();
        private int maxCacheSize = 1;

        /// <summary>
        /// Gets or sets the maximum number of cached static combos.
        /// </summary>
        public int MaxCachedCombos
        {
            get => maxCacheSize;
            set
            {
                maxCacheSize = Math.Max(value, 1);
                cache.EnsureCapacity(maxCacheSize);
                Trim();
            }
        }

        /// <summary>
        /// A static combo cache with a set maximum size, trimmed in insertion order (FIFO).
        /// </summary>
        /// <param name="program">Program to read static combos from. This reference will be used as a reading lock.</param>
        public StaticComboCache(VfxProgramData program)
        {
            Program = program;
        }

        /// <summary>
        /// Gets the static combo data for the specified ID.
        /// </summary>
        public VfxStaticComboData Get(long staticComboId)
        {
            lock (Program)
            {
                if (cache.TryGetValue(staticComboId, out var staticCombo))
                {
                    return staticCombo;
                }

                staticCombo = Program.GetStaticCombo(staticComboId);
                cache.Add(staticComboId, staticCombo);

                insertionOrder.AddLast(staticComboId);
                Trim();

                return staticCombo;
            }
        }

        /// <summary>
        /// Raises <see cref="MaxCachedCombos"/> to at least the given size, so that many combos stay cached.
        /// </summary>
        public void EnsureMinimumCacheSize(int size)
        {
            MaxCachedCombos = Math.Max(size, MaxCachedCombos);
        }

        private void Trim()
        {
            var didTrim = false;
            while (insertionOrder.Count > maxCacheSize)
            {
                var staticComboId = insertionOrder.First!.Value;
                insertionOrder.RemoveFirst();
                cache[staticComboId].Dispose();
                didTrim = cache.Remove(staticComboId) || didTrim;
            }

            if (didTrim)
            {
                cache.TrimExcess(maxCacheSize);
            }
        }

        /// <summary>
        /// Disposes all cached data.
        /// </summary>
        public void Dispose()
        {
            foreach (var staticCombo in cache.Values)
            {
                staticCombo.Dispose();
            }
        }
    }
}
