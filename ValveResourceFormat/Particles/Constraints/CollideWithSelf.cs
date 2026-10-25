using System.Numerics.Tensors;
using System.Runtime.Intrinsics;

namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Pushes the system's particles out of each other, treating each as a sphere of its radius
    /// scaled by <c>m_flRadiusScale</c>. Particles are taken in blocks of four, each pushed against
    /// every particle in turn, so the result depends on particle order. A block is only handled when
    /// one of its particles moved faster than <c>m_flMinimumSpeed</c> over the step, but then all four
    /// are pushed. Only <see cref="ParticleField.Position"/> is written, so a push also becomes velocity.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_CollideWithSelf">C_OP_CollideWithSelf</seealso>
    class CollideWithSelf : ParticleFunctionConstraint
    {
        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Position.Mask();

        private const int BlockSize = 4;

        private readonly INumberProvider radiusScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider minimumSpeed = new LiteralNumberProvider(1f);
        private readonly bool checkSpeed;

        public CollideWithSelf(ParticleDefinitionParser parse) : base(parse)
        {
            radiusScale = parse.NumberProvider("m_flRadiusScale", radiusScale);
            minimumSpeed = parse.NumberProvider("m_flMinimumSpeed", minimumSpeed);
            checkSpeed = minimumSpeed is not LiteralNumberProvider { Value: <= 0f };
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;

            var radii = radiusScale.Fill(current, particleSystemState);
            TensorPrimitives.Multiply(radii, current.ReadFloats(ParticleField.Radius), radii);

            var changed = false;
            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = checkSpeed ? current.ReadVectors(ParticleField.PositionPrevious) : default;

            foreach (var (blockStart, block) in current.Blocks())
            {
                if (checkSpeed && !AnyFastEnough(block, positions, previousPositions, blockStart, frameTime, particleSystemState))
                {
                    continue;
                }

                changed |= PushBlock(positions, radii, blockStart, block.Length);
            }

            return changed;
        }

        /// <summary>
        /// Pushes the particles of one block, one per lane, out of every particle in turn. A block
        /// particle is read from its lane while the block runs, so later pushes see earlier ones.
        /// </summary>
        private static bool PushBlock(Span<Vector3> positions, ReadOnlySpan<float> radii, int blockStart, int blockLength)
        {
            Span<float> x = stackalloc float[BlockSize];
            Span<float> y = stackalloc float[BlockSize];
            Span<float> z = stackalloc float[BlockSize];
            Span<float> r = stackalloc float[BlockSize];

            for (var lane = 0; lane < blockLength; lane++)
            {
                var position = positions[blockStart + lane];
                x[lane] = position.X;
                y[lane] = position.Y;
                z[lane] = position.Z;
                r[lane] = radii[blockStart + lane];
            }

            var blockX = Vector128.Create<float>(x);
            var blockY = Vector128.Create<float>(y);
            var blockZ = Vector128.Create<float>(z);
            var blockRadii = Vector128.Create<float>(r);
            var used = Vector128.LessThan(Vector128.Create(0f, 1f, 2f, 3f), Vector128.Create((float)blockLength));
            var changed = false;

            for (var j = 0; j < positions.Length; j++)
            {
                var otherLane = j - blockStart;
                var other = (uint)otherLane < (uint)blockLength
                    ? new Vector3(blockX[otherLane], blockY[otherLane], blockZ[otherLane])
                    : positions[j];

                var otherX = Vector128.Create(other.X);
                var otherY = Vector128.Create(other.Y);
                var otherZ = Vector128.Create(other.Z);
                var reach = Vector128.Create(radii[j]) + blockRadii;
                var deltaX = blockX - otherX;
                var deltaY = blockY - otherY;
                var deltaZ = blockZ - otherZ;

                // Summed in the order Vector3.LengthSquared uses, and without fused multiply-adds,
                // so each lane matches the scalar push bit for bit
                var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ);

                // Negated rather than LessThan, so a NaN distance counts as touching as it does in scalar code
                var touching = ~Vector128.GreaterThanOrEqual(distanceSquared, reach * reach) & used;

                if (touching == Vector128<float>.Zero)
                {
                    continue;
                }

                changed = true;

                var pushed = touching & Vector128.GreaterThan(distanceSquared, Vector128<float>.Zero);
                var scale = reach / Vector128.Sqrt(distanceSquared);
                blockX = Vector128.ConditionalSelect(pushed, (deltaX * scale) + otherX, blockX);
                blockY = Vector128.ConditionalSelect(pushed, (deltaY * scale) + otherY, blockY);
                blockZ = Vector128.ConditionalSelect(pushed, (deltaZ * scale) + otherZ, blockZ);
            }

            for (var lane = 0; lane < blockLength; lane++)
            {
                positions[blockStart + lane] = new Vector3(blockX[lane], blockY[lane], blockZ[lane]);
            }

            return changed;
        }

        private bool AnyFastEnough(ParticleSpan block, ReadOnlySpan<Vector3> positions, ReadOnlySpan<Vector3> previousPositions, int blockStart, float frameTime, ParticleSystemState particleSystemState)
        {
            var length = block.Length;
            Span<float> speeds = stackalloc float[BlockSize];
            speeds = speeds[..length];
            minimumSpeed.Fill(block, particleSystemState, speeds);

            for (var lane = 0; lane < length; lane++)
            {
                var speed = speeds[lane];
                var i = blockStart + lane;

                if (speed * speed * frameTime < Vector3.DistanceSquared(positions[i], previousPositions[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
