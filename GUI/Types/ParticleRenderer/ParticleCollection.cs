using GUI.Types.ParticleRenderer.Utils;

namespace GUI.Types.ParticleRenderer
{
    class ParticleCollection
    {
        public const int MAX_PARTICLES = 5000;

        public Span<Particle> Initial => new(initial, 0, Count);
        public Span<Particle> Current => new(current, 0, Count);

        public Particle Constants { get; }
        private readonly Particle[] initial;
        private readonly Particle[] current;

        public int Capacity { get; }
        public int Count { get; private set; }

        public ParticleCollection(Particle constants, int maxParticles)
        {
            Constants = constants;
            Capacity = maxParticles == 0 ? MAX_PARTICLES : Math.Min(maxParticles, MAX_PARTICLES);

            initial = new Particle[Capacity];
            Array.Fill(initial, constants);
            current = new Particle[Capacity];
        }

        public int Add()
        {
            if (Count < Capacity)
            {
                initial[Count] = Constants;
                return Count++;
            }

            return -1;
        }

        public void PruneExpired()
        {
            var alive = 0;
            for (var i = 0; i < Count; i++)
            {
                if (!current[i].MarkedAsKilled)
                {
                    if (i != alive)
                    {
                        MoveParticleIndex(i, alive);
                    }
                    alive++;
                }
            }
            Count = alive;
        }

        public void Clear()
        {
            Count = 0;
        }

        public static float RandomSingle(int particleId)
        {
            return RandomFloats.List[particleId % RandomFloats.List.Length]; // TODO: Add seed
        }

        public static float RandomBetween(int particleId, float min, float max)
        {
            return float.Lerp(min, max, RandomSingle(particleId));
        }

        public static Vector3 RandomBetween(int particleId, Vector3 min, Vector3 max)
        {
            return Vector3.Lerp(min, max, RandomSingle(particleId));
        }

        public static Vector3 RandomBetweenPerComponent(int particleId, Vector3 min, Vector3 max)
        {
            return new Vector3(
                RandomBetween(particleId, min.X, max.X),
                RandomBetween(particleId + 1, min.Y, max.Y),
                RandomBetween(particleId + 2, min.Z, max.Z));
        }

        public static float RandomWithExponentBetween(int particleId, float exponent, float min, float max)
        {
            return float.Lerp(min, max, MathF.Pow(RandomSingle(particleId), exponent));
        }

        private void MoveParticleIndex(int currentIndex, int newIndex)
        {
            initial[newIndex] = initial[currentIndex];
            initial[newIndex].Index = newIndex;
            current[newIndex] = current[currentIndex];
            current[newIndex].Index = newIndex;
        }
    }
}
