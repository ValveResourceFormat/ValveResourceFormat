namespace ValveResourceFormat.CompiledShader
{
    /// <summary>
    /// LRU cache for shader static combo data.
    /// </summary>
    public sealed class StaticCache : IDisposable
    {
        private readonly VfxProgramData Program;
        private readonly Dictionary<long, VfxStaticComboData> cache = [];
        private readonly LinkedList<long> lru = new();
        private int maxCacheSize = 1;

        /// <summary>
        /// Gets or sets the maximum number of cached static combos.
        /// </summary>
        public int MaxCachedFrames
        {
            get => maxCacheSize;
            set
            {
                maxCacheSize = Math.Max(value, 1);
                cache.EnsureCapacity(maxCacheSize);
                TrimLRU();
            }
        }

        /// <summary>
        ///  A ZFrame file cache with a set maximum size, trimmed on a LRU basis.
        /// </summary>
        /// <param name="program">Shader file to read zframes from. This reference will be used as a reading lock.</param>
        public StaticCache(VfxProgramData program)
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

                lru.AddLast(staticComboId);
                TrimLRU();

                return staticCombo;
            }
        }

        /// <summary>
        /// Ensures the cache has at least the specified capacity.
        /// </summary>
        public void EnsureCapacity(int capacity)
        {
            MaxCachedFrames = Math.Max(capacity, MaxCachedFrames);
        }

        private void TrimLRU()
        {
            var didTrim = false;
            while (lru.Count > maxCacheSize)
            {
                var staticComboId = lru.First!.Value;
                lru.RemoveFirst();
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
            foreach (var zFrame in cache.Values)
            {
                zFrame.Dispose();
            }
        }
    }
}
