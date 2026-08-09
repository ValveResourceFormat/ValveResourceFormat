using System.Collections;

namespace ValveResourceFormat.Renderer.Particles.Utils
{
    /// <summary>
    /// Shared per-particle logic for reading a control-point snapshot element into a particle attribute,
    /// used by both <c>C_INIT_InitFromCPSnapshot</c> and <c>C_OP_SetFromCPSnapshot</c>.
    /// </summary>
    static class CPSnapshotSampler
    {
        /// <summary>
        /// Picks the snapshot element index for a particle, walking the snapshot by
        /// <paramref name="creationIndex"/> or drawing at random. A non-zero <paramref name="randomSeed"/>
        /// (<c>m_nRandomSeed</c>) moves the random draw onto <paramref name="privateSampleCounter"/>, a counter
        /// private to the calling function, instead of the system's shared draw counter; either way the counter
        /// advances once per particle. The returned index is always in <c>[0, numParticles)</c>
        /// (<paramref name="numParticles"/> must be positive).
        /// </summary>
        public static int SelectIndex(int creationIndex, int numParticles, bool random, bool reverse, int startPoint, int increment,
            int randomSeed, ref int privateSampleCounter, ParticleSystemRenderState particleSystemState)
        {
            if (random)
            {
                var sample = randomSeed != 0
                    ? ParticleRandom.ForSample(particleSystemState.Random.Seed + randomSeed + privateSampleCounter++)
                    : particleSystemState.Random.Next();

                // Sampling (int)(n * rand01) keeps the last element reachable; the table tops out below 1.0
                // so the Min only guards against that changing.
                return Math.Min((int)(numParticles * sample), numParticles - 1);
            }

            // Walk the snapshot from the start point by the increment per particle (defaults 0/1 reproduce the
            // plain ordinal mapping). The ordinal is writable and C# % keeps the dividend's sign, so wrap
            // explicitly to keep the index non-negative and in range.
            var raw = startPoint + (creationIndex * increment);
            var wrapped = ((raw % numParticles) + numParticles) % numParticles;

            return reverse ? numParticles - 1 - wrapped : wrapped;
        }

        /// <summary>
        /// Writes the snapshot value at <paramref name="idx"/> into <paramref name="particle"/>'s
        /// <paramref name="attributeToWrite"/>, moving it into control point <paramref name="localSpaceCP"/>'s
        /// frame first (a negative control point leaves the value alone). Does nothing if the attribute type
        /// does not match the snapshot data or the index is out of range.
        /// When <paramref name="writePositionPrevious"/> is set, a <see cref="ParticleField.Position"/> write also seeds <see cref="Particle.PositionPrevious"/>
        /// (the initializer always mirrors it; the operator only when <c>m_bPrev</c> is set).
        /// <paramref name="atSpawn"/> marks the initializer path, where a velocity write goes through
        /// <see cref="Particle.Velocity"/> so the emit path's Verlet encoding picks it up.
        /// <paramref name="localSpaceAngles"/> extends the control point transform to Roll, Yaw and Pitch writes
        /// (<c>m_bLocalSpaceAngles</c>, which only the initializer carries).
        /// </summary>
        public static void WriteAttribute(ref Particle particle, ParticleField attributeToWrite, IEnumerable readAttributeData,
            int idx, int localSpaceCP, bool writePositionPrevious, bool atSpawn, float frameTime, ParticleSystemRenderState particleSystemState,
            bool localSpaceAngles = false)
        {
            var fieldType = attributeToWrite.FieldType();

            if (fieldType == "vector" && readAttributeData is Vector3[] vectorArray && (uint)idx < (uint)vectorArray.Length)
            {
                var value = vectorArray[idx];

                // Only these three attributes are moved into the local space control point's frame, and a
                // velocity or a normal is rotated by it without picking up its translation.
                if (localSpaceCP >= 0)
                {
                    value = attributeToWrite switch
                    {
                        ParticleField.Position
                            => ControlPointTransformProvider.TransformPosition(particleSystemState, localSpaceCP, value),
                        ParticleField.PositionPrevious or ParticleField.Normal
                            => ControlPointTransformProvider.TransformDirection(particleSystemState, localSpaceCP, value),
                        _ => value,
                    };
                }

                // PREV_XYZ stores a velocity; the previous position is derived from the current
                // position and that velocity over the frame (1/30 fallback when the frame time is unknown).
                if (attributeToWrite == ParticleField.PositionPrevious)
                {
                    if (atSpawn)
                    {
                        particle.Velocity = value;
                        return;
                    }

                    var dt = frameTime > 0f ? frameTime : 1f / 30f;
                    particle.PositionPrevious = particle.Position - (value * dt);
                    return;
                }

                particle.SetVector(attributeToWrite, value);

                if (writePositionPrevious && attributeToWrite == ParticleField.Position)
                {
                    particle.PositionPrevious = value;
                }
            }
            else if (fieldType == "float" && readAttributeData is float[] floatArray && (uint)idx < (uint)floatArray.Length)
            {
                var value = floatArray[idx];

                if (localSpaceAngles && localSpaceCP >= 0
                    && attributeToWrite is ParticleField.Roll or ParticleField.Yaw or ParticleField.Pitch)
                {
                    value = ControlPointTransformProvider.TransformAngle(particleSystemState, localSpaceCP, attributeToWrite, value);
                }

                particle.SetScalar(attributeToWrite, value);
            }
        }
    }
}
