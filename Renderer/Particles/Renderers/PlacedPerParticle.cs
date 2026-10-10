namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// What a renderer keeps placed in the scene for each live particle, keyed by unique particle id.
    /// Each frame the renderer marks the particles it still places with <see cref="Keep(int)"/>, then
    /// releases what <see cref="Sweep"/> returns for the rest.
    /// </summary>
    /// <typeparam name="T">What is placed for one particle.</typeparam>
    internal sealed class PlacedPerParticle<T>
    {
        private readonly Dictionary<int, T> placed = [];
        private readonly HashSet<int> kept = [];
        private readonly List<int> gone = [];
        private readonly List<T> swept = [];

        /// <summary>Gets what is placed for every particle.</summary>
        public IEnumerable<T> Values => placed.Values;

        /// <summary>Starts a frame, with no particle kept yet.</summary>
        public void BeginFrame() => kept.Clear();

        /// <summary>Gets what is placed for a particle.</summary>
        /// <param name="particleId">The particle's unique id.</param>
        /// <param name="value">What is placed for it, when anything is.</param>
        /// <returns>Whether anything is placed for it.</returns>
        public bool TryGetValue(int particleId, out T value) => placed.TryGetValue(particleId, out value!);

        /// <summary>Keeps what is placed for a particle this frame.</summary>
        /// <param name="particleId">The particle's unique id.</param>
        public void Keep(int particleId) => kept.Add(particleId);

        /// <summary>Places <paramref name="value"/> for a particle, replacing what was, and keeps it this frame.</summary>
        /// <param name="particleId">The particle's unique id.</param>
        /// <param name="value">What is placed for it.</param>
        public void Keep(int particleId, T value)
        {
            placed[particleId] = value;
            kept.Add(particleId);
        }

        /// <summary>Forgets what is placed for every particle not kept this frame.</summary>
        /// <returns>What was forgotten, for the caller to release; valid until the next sweep.</returns>
        public List<T> Sweep()
        {
            gone.Clear();
            swept.Clear();

            foreach (var (particleId, value) in placed)
            {
                if (!kept.Contains(particleId))
                {
                    gone.Add(particleId);
                    swept.Add(value);
                }
            }

            foreach (var particleId in gone)
            {
                placed.Remove(particleId);
            }

            return swept;
        }

        /// <summary>Forgets what is placed for every particle.</summary>
        public void Clear() => placed.Clear();
    }
}
