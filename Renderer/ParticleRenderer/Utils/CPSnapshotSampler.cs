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
        /// Picks the snapshot element index for a particle. The returned index is always in
        /// <c>[0, numParticles)</c> (<paramref name="numParticles"/> must be positive).
        /// </summary>
        public static int SelectIndex(int particleId, int numParticles, bool random, bool reverse, int startPoint, int increment)
        {
            if (random)
            {
                // Sampling (int)(n * rand01) keeps the last element reachable; RandomSingle is in
                // [0, 1) so the Min is only a safety clamp. ParticleID can be driven negative, so mask the
                // sign off before it reaches RandomSingle's array index.
                return Math.Min((int)(numParticles * ParticleCollection.RandomSingle(particleId & int.MaxValue)), numParticles - 1);
            }

            // Walk the snapshot from the start point by the increment per particle (defaults 0/1 reproduce the
            // plain particle-id mapping). ParticleID is writable and C# % keeps the dividend's sign, so wrap
            // explicitly to keep the index non-negative and in range.
            var raw = startPoint + (particleId * increment);
            var wrapped = ((raw % numParticles) + numParticles) % numParticles;

            return reverse ? numParticles - 1 - wrapped : wrapped;
        }

        /// <summary>
        /// Writes the snapshot value at <paramref name="idx"/> into <paramref name="particle"/>'s
        /// <paramref name="attributeToWrite"/>, applying an optional local-space control point offset. Does
        /// nothing if the attribute type does not match the snapshot data or the index is out of range.
        /// When <paramref name="writePositionPrevious"/> is set, a Position write also seeds PositionPrevious
        /// (the initializer always mirrors it; the operator only when <c>m_bPrev</c> is set).
        /// <paramref name="atSpawn"/> marks the initializer path, where a velocity write goes through
        /// <see cref="Particle.Velocity"/> so the emit path's Verlet encoding picks it up.
        /// </summary>
        public static void WriteAttribute(ref Particle particle, ParticleField attributeToWrite, IEnumerable readAttributeData,
            int idx, int localSpaceCP, bool writePositionPrevious, bool atSpawn, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            var fieldType = attributeToWrite.FieldType();

            if (fieldType == "vector" && readAttributeData is Vector3[] vectorArray && (uint)idx < (uint)vectorArray.Length)
            {
                var value = vectorArray[idx];

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

                if (localSpaceCP >= 0)
                {
                    // Transform the sampled value by the control point's full local frame (rotation +
                    // translation), not just an origin offset. Identity orientation collapses to a plain
                    // position offset.
                    value = ControlPointTransformProvider.TransformPosition(particleSystemState, localSpaceCP, value);
                }

                particle.SetVector(attributeToWrite, value);

                if (writePositionPrevious && attributeToWrite == ParticleField.Position)
                {
                    particle.PositionPrevious = value;
                }
            }
            else if (fieldType == "float" && readAttributeData is float[] floatArray && (uint)idx < (uint)floatArray.Length)
            {
                particle.SetScalar(attributeToWrite, floatArray[idx]);
            }
        }
    }
}
