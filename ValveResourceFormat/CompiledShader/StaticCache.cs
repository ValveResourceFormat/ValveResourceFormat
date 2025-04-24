namespace ValveResourceFormat.CompiledShader
{
    public sealed class StaticCache : IDisposable
    {
        private readonly ShaderFile shaderFile;
        private readonly Dictionary<long, ZFrameFile> cache = [];
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
        /// <param name="shader">Shader file to read zframes from. This reference will be used as a reading lock.</param>
        public StaticCache(ShaderFile shader)
        {
            shaderFile = shader;
        }

        public ZFrameFile Get(long zFrameId)
        {
            lock (shaderFile)
            {
                if (cache.TryGetValue(zFrameId, out var zFrame))
                {
                    return zFrame;
                }

                zFrame = shaderFile.GetZFrameFile(zFrameId);
                cache.Add(zFrameId, zFrame);

                lru.AddLast(zFrameId);
                TrimLRU();

                return zFrame;
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
                var zFrameId = lru.First!.Value;
                lru.RemoveFirst();
                cache[zFrameId].Dispose();
                didTrim = cache.Remove(zFrameId) || didTrim;
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
