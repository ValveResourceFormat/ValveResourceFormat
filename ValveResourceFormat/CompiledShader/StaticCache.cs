namespace ValveResourceFormat.CompiledShader
{
    public sealed class StaticCache : IDisposable
    {
        private readonly VfxProgramData Program;
        private readonly Dictionary<long, VfxStaticComboData> cache = [];
        private readonly LinkedList<long> lru = new();
        private int maxCacheSize = 1;

        public int MaxCachedFrames
        {
            get => maxCacheSize;
            set
            {
                maxCacheSize = Math.Min(value, 1);
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

        public void Dispose()
        {
            foreach (var zFrame in cache.Values)
            {
                zFrame.Dispose();
            }
        }
    }
}
