namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Structure of arrays storage for particle attributes, one array per attribute indexed by particle.
    /// <see cref="Particle"/> is a handle to one slot.
    /// </summary>
    internal sealed class ParticleBuffer
    {
        public int[] UniqueParticleId;
        public int[] ParticleId;
        public Vector3[] Position;
        public Vector3[] PositionPrevious;
        public float[] Age;
        public float[] Lifetime;
        public float[] Alpha;
        public float[] AlphaAlternate;
        public Vector3[] Color;
        public float[] Radius;
        public float[] TrailLength;
        public float[] ForceScale;
        public Vector3[] Rotation;
        public Vector3[] RotationSpeed;
        public Vector3[] Normal;
        public Vector3[] ForceAccumulator;
        public float[] SequenceNumber;
        public float[] ManualAnimationFrame;
        public float[] SecondSequenceNumber;
        public int[] ParentParticleIndex;
        public int[] ParentParticleId;
        public int[] RopeSegmentId;
        public int[] UserEventStates;
        public Vector3[] BoxMins;
        public Vector3[] BoxMaxs;
        public Vector3[] BoxAngles;
        public float[] BoxFlags;
        public Vector3[] RopeSegmentData;
        public float[] AlphaWindowThreshold;
        public float[] ScratchFloat0;
        public float[] ScratchFloat1;
        public float[] ScratchFloat2;
        public Vector3[] HitboxOffsetPosition;
        public int[] HitboxIndex;
        public ulong[] BoneIndices;
        public Vector3[] BoneWeights;
        public Vector3[] ScratchVector;
        public Vector3[] ScratchVector2;
        public Vector3[] GlowRgb;
        public float[] GlowAlpha;
        public float[] ShaderExtraData1;
        public float[] ShaderExtraData2;
        public float[] CreationTime;
        public bool[] MarkedAsKilled;

        public ParticleBuffer(int capacity)
        {
            UniqueParticleId = new int[capacity];
            ParticleId = new int[capacity];
            Position = new Vector3[capacity];
            PositionPrevious = new Vector3[capacity];
            Age = new float[capacity];
            Lifetime = new float[capacity];
            Alpha = new float[capacity];
            AlphaAlternate = new float[capacity];
            Color = new Vector3[capacity];
            Radius = new float[capacity];
            TrailLength = new float[capacity];
            ForceScale = new float[capacity];
            Rotation = new Vector3[capacity];
            RotationSpeed = new Vector3[capacity];
            Normal = new Vector3[capacity];
            ForceAccumulator = new Vector3[capacity];
            SequenceNumber = new float[capacity];
            ManualAnimationFrame = new float[capacity];
            SecondSequenceNumber = new float[capacity];
            ParentParticleIndex = new int[capacity];
            ParentParticleId = new int[capacity];
            RopeSegmentId = new int[capacity];
            UserEventStates = new int[capacity];
            BoxMins = new Vector3[capacity];
            BoxMaxs = new Vector3[capacity];
            BoxAngles = new Vector3[capacity];
            BoxFlags = new float[capacity];
            RopeSegmentData = new Vector3[capacity];
            AlphaWindowThreshold = new float[capacity];
            ScratchFloat0 = new float[capacity];
            ScratchFloat1 = new float[capacity];
            ScratchFloat2 = new float[capacity];
            HitboxOffsetPosition = new Vector3[capacity];
            HitboxIndex = new int[capacity];
            BoneIndices = new ulong[capacity];
            BoneWeights = new Vector3[capacity];
            ScratchVector = new Vector3[capacity];
            ScratchVector2 = new Vector3[capacity];
            GlowRgb = new Vector3[capacity];
            GlowAlpha = new float[capacity];
            ShaderExtraData1 = new float[capacity];
            ShaderExtraData2 = new float[capacity];
            CreationTime = new float[capacity];
            MarkedAsKilled = new bool[capacity];
        }

        /// <summary>Gets the number of slots allocated.</summary>
        public int Capacity => Position.Length;

        /// <summary>Grows or shrinks every attribute array to <paramref name="capacity"/> slots, keeping their contents.</summary>
        public void Resize(int capacity)
        {
            Array.Resize(ref UniqueParticleId, capacity);
            Array.Resize(ref ParticleId, capacity);
            Array.Resize(ref Position, capacity);
            Array.Resize(ref PositionPrevious, capacity);
            Array.Resize(ref Age, capacity);
            Array.Resize(ref Lifetime, capacity);
            Array.Resize(ref Alpha, capacity);
            Array.Resize(ref AlphaAlternate, capacity);
            Array.Resize(ref Color, capacity);
            Array.Resize(ref Radius, capacity);
            Array.Resize(ref TrailLength, capacity);
            Array.Resize(ref ForceScale, capacity);
            Array.Resize(ref Rotation, capacity);
            Array.Resize(ref RotationSpeed, capacity);
            Array.Resize(ref Normal, capacity);
            Array.Resize(ref ForceAccumulator, capacity);
            Array.Resize(ref SequenceNumber, capacity);
            Array.Resize(ref ManualAnimationFrame, capacity);
            Array.Resize(ref SecondSequenceNumber, capacity);
            Array.Resize(ref ParentParticleIndex, capacity);
            Array.Resize(ref ParentParticleId, capacity);
            Array.Resize(ref RopeSegmentId, capacity);
            Array.Resize(ref UserEventStates, capacity);
            Array.Resize(ref BoxMins, capacity);
            Array.Resize(ref BoxMaxs, capacity);
            Array.Resize(ref BoxAngles, capacity);
            Array.Resize(ref BoxFlags, capacity);
            Array.Resize(ref RopeSegmentData, capacity);
            Array.Resize(ref AlphaWindowThreshold, capacity);
            Array.Resize(ref ScratchFloat0, capacity);
            Array.Resize(ref ScratchFloat1, capacity);
            Array.Resize(ref ScratchFloat2, capacity);
            Array.Resize(ref HitboxOffsetPosition, capacity);
            Array.Resize(ref HitboxIndex, capacity);
            Array.Resize(ref BoneIndices, capacity);
            Array.Resize(ref BoneWeights, capacity);
            Array.Resize(ref ScratchVector, capacity);
            Array.Resize(ref ScratchVector2, capacity);
            Array.Resize(ref GlowRgb, capacity);
            Array.Resize(ref GlowAlpha, capacity);
            Array.Resize(ref ShaderExtraData1, capacity);
            Array.Resize(ref ShaderExtraData2, capacity);
            Array.Resize(ref CreationTime, capacity);
            Array.Resize(ref MarkedAsKilled, capacity);
        }

        /// <summary>Copies the particle at <paramref name="sourceIndex"/> in <paramref name="source"/> over the one at <paramref name="index"/>.</summary>
        public void Copy(ParticleBuffer source, int sourceIndex, int index)
        {
            UniqueParticleId[index] = source.UniqueParticleId[sourceIndex];
            ParticleId[index] = source.ParticleId[sourceIndex];
            Position[index] = source.Position[sourceIndex];
            PositionPrevious[index] = source.PositionPrevious[sourceIndex];
            Age[index] = source.Age[sourceIndex];
            Lifetime[index] = source.Lifetime[sourceIndex];
            Alpha[index] = source.Alpha[sourceIndex];
            AlphaAlternate[index] = source.AlphaAlternate[sourceIndex];
            Color[index] = source.Color[sourceIndex];
            Radius[index] = source.Radius[sourceIndex];
            TrailLength[index] = source.TrailLength[sourceIndex];
            ForceScale[index] = source.ForceScale[sourceIndex];
            Rotation[index] = source.Rotation[sourceIndex];
            RotationSpeed[index] = source.RotationSpeed[sourceIndex];
            Normal[index] = source.Normal[sourceIndex];
            ForceAccumulator[index] = source.ForceAccumulator[sourceIndex];
            SequenceNumber[index] = source.SequenceNumber[sourceIndex];
            ManualAnimationFrame[index] = source.ManualAnimationFrame[sourceIndex];
            SecondSequenceNumber[index] = source.SecondSequenceNumber[sourceIndex];
            ParentParticleIndex[index] = source.ParentParticleIndex[sourceIndex];
            ParentParticleId[index] = source.ParentParticleId[sourceIndex];
            RopeSegmentId[index] = source.RopeSegmentId[sourceIndex];
            UserEventStates[index] = source.UserEventStates[sourceIndex];
            BoxMins[index] = source.BoxMins[sourceIndex];
            BoxMaxs[index] = source.BoxMaxs[sourceIndex];
            BoxAngles[index] = source.BoxAngles[sourceIndex];
            BoxFlags[index] = source.BoxFlags[sourceIndex];
            RopeSegmentData[index] = source.RopeSegmentData[sourceIndex];
            AlphaWindowThreshold[index] = source.AlphaWindowThreshold[sourceIndex];
            ScratchFloat0[index] = source.ScratchFloat0[sourceIndex];
            ScratchFloat1[index] = source.ScratchFloat1[sourceIndex];
            ScratchFloat2[index] = source.ScratchFloat2[sourceIndex];
            HitboxOffsetPosition[index] = source.HitboxOffsetPosition[sourceIndex];
            HitboxIndex[index] = source.HitboxIndex[sourceIndex];
            BoneIndices[index] = source.BoneIndices[sourceIndex];
            BoneWeights[index] = source.BoneWeights[sourceIndex];
            ScratchVector[index] = source.ScratchVector[sourceIndex];
            ScratchVector2[index] = source.ScratchVector2[sourceIndex];
            GlowRgb[index] = source.GlowRgb[sourceIndex];
            GlowAlpha[index] = source.GlowAlpha[sourceIndex];
            ShaderExtraData1[index] = source.ShaderExtraData1[sourceIndex];
            ShaderExtraData2[index] = source.ShaderExtraData2[sourceIndex];
            CreationTime[index] = source.CreationTime[sourceIndex];
            MarkedAsKilled[index] = source.MarkedAsKilled[sourceIndex];
        }

        /// <summary>Copies <paramref name="count"/> particles from <paramref name="source"/> at <paramref name="sourceIndex"/> to this buffer at <paramref name="index"/>.</summary>
        public void Copy(ParticleBuffer source, int sourceIndex, int index, int count)
        {
            Copy(source.UniqueParticleId, sourceIndex, UniqueParticleId, index, count);
            Copy(source.ParticleId, sourceIndex, ParticleId, index, count);
            Copy(source.Position, sourceIndex, Position, index, count);
            Copy(source.PositionPrevious, sourceIndex, PositionPrevious, index, count);
            Copy(source.Age, sourceIndex, Age, index, count);
            Copy(source.Lifetime, sourceIndex, Lifetime, index, count);
            Copy(source.Alpha, sourceIndex, Alpha, index, count);
            Copy(source.AlphaAlternate, sourceIndex, AlphaAlternate, index, count);
            Copy(source.Color, sourceIndex, Color, index, count);
            Copy(source.Radius, sourceIndex, Radius, index, count);
            Copy(source.TrailLength, sourceIndex, TrailLength, index, count);
            Copy(source.ForceScale, sourceIndex, ForceScale, index, count);
            Copy(source.Rotation, sourceIndex, Rotation, index, count);
            Copy(source.RotationSpeed, sourceIndex, RotationSpeed, index, count);
            Copy(source.Normal, sourceIndex, Normal, index, count);
            Copy(source.ForceAccumulator, sourceIndex, ForceAccumulator, index, count);
            Copy(source.SequenceNumber, sourceIndex, SequenceNumber, index, count);
            Copy(source.ManualAnimationFrame, sourceIndex, ManualAnimationFrame, index, count);
            Copy(source.SecondSequenceNumber, sourceIndex, SecondSequenceNumber, index, count);
            Copy(source.ParentParticleIndex, sourceIndex, ParentParticleIndex, index, count);
            Copy(source.ParentParticleId, sourceIndex, ParentParticleId, index, count);
            Copy(source.RopeSegmentId, sourceIndex, RopeSegmentId, index, count);
            Copy(source.UserEventStates, sourceIndex, UserEventStates, index, count);
            Copy(source.BoxMins, sourceIndex, BoxMins, index, count);
            Copy(source.BoxMaxs, sourceIndex, BoxMaxs, index, count);
            Copy(source.BoxAngles, sourceIndex, BoxAngles, index, count);
            Copy(source.BoxFlags, sourceIndex, BoxFlags, index, count);
            Copy(source.RopeSegmentData, sourceIndex, RopeSegmentData, index, count);
            Copy(source.AlphaWindowThreshold, sourceIndex, AlphaWindowThreshold, index, count);
            Copy(source.ScratchFloat0, sourceIndex, ScratchFloat0, index, count);
            Copy(source.ScratchFloat1, sourceIndex, ScratchFloat1, index, count);
            Copy(source.ScratchFloat2, sourceIndex, ScratchFloat2, index, count);
            Copy(source.HitboxOffsetPosition, sourceIndex, HitboxOffsetPosition, index, count);
            Copy(source.HitboxIndex, sourceIndex, HitboxIndex, index, count);
            Copy(source.BoneIndices, sourceIndex, BoneIndices, index, count);
            Copy(source.BoneWeights, sourceIndex, BoneWeights, index, count);
            Copy(source.ScratchVector, sourceIndex, ScratchVector, index, count);
            Copy(source.ScratchVector2, sourceIndex, ScratchVector2, index, count);
            Copy(source.GlowRgb, sourceIndex, GlowRgb, index, count);
            Copy(source.GlowAlpha, sourceIndex, GlowAlpha, index, count);
            Copy(source.ShaderExtraData1, sourceIndex, ShaderExtraData1, index, count);
            Copy(source.ShaderExtraData2, sourceIndex, ShaderExtraData2, index, count);
            Copy(source.CreationTime, sourceIndex, CreationTime, index, count);
            Copy(source.MarkedAsKilled, sourceIndex, MarkedAsKilled, index, count);
        }

        /// <summary>
        /// Copies only the attributes renderers blend between two steps, of <paramref name="count"/>
        /// particles from <paramref name="first"/>: position, previous position, age, radius, color, both
        /// alphas, rotation, normal, trail length, manual animation frame and the shader extra data.
        /// </summary>
        public void CopyBlendedAttributes(ParticleBuffer source, int first, int count)
        {
            Copy(source.Position, first, Position, first, count);
            Copy(source.PositionPrevious, first, PositionPrevious, first, count);
            Copy(source.Age, first, Age, first, count);
            Copy(source.Radius, first, Radius, first, count);
            Copy(source.Color, first, Color, first, count);
            Copy(source.Alpha, first, Alpha, first, count);
            Copy(source.AlphaAlternate, first, AlphaAlternate, first, count);
            Copy(source.Rotation, first, Rotation, first, count);
            Copy(source.Normal, first, Normal, first, count);
            Copy(source.TrailLength, first, TrailLength, first, count);
            Copy(source.ManualAnimationFrame, first, ManualAnimationFrame, first, count);
            Copy(source.ShaderExtraData1, first, ShaderExtraData1, first, count);
            Copy(source.ShaderExtraData2, first, ShaderExtraData2, first, count);
        }

        /// <summary>Copies the particle at <paramref name="sourceIndex"/> over the one at <paramref name="index"/>.</summary>
        public void Move(int sourceIndex, int index) => Copy(this, sourceIndex, index);

        private static void Copy<T>(T[] source, int sourceIndex, T[] destination, int index, int count)
            => source.AsSpan(sourceIndex, count).CopyTo(destination.AsSpan(index, count));

        /// <summary>Writes the attribute defaults a particle has before any constant or function sets it.</summary>
        public void SetDefaults(int index)
        {
            Lifetime[index] = 1f;
            Alpha[index] = 1.0f;
            AlphaAlternate[index] = 1.0f;
            Color[index] = Vector3.One;
            Radius[index] = 5.0f;
            TrailLength[index] = 0.1f;
            ForceScale[index] = 1.0f;
            Normal[index] = new(0f, 0f, 1f);
            ParentParticleIndex[index] = -1;
            ParentParticleId[index] = int.MinValue;
            GlowRgb[index] = Vector3.One;
            GlowAlpha[index] = 1f;
            ShaderExtraData1[index] = 1f;
            ShaderExtraData2[index] = 1f;
        }
    }
}
