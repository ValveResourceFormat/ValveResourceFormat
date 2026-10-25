using ValveKeyValue;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Structure of arrays storage for particle attributes, one array per attribute indexed by particle.
    /// Attributes are looked up by <see cref="ParticleField"/>: floats and integers share a table of
    /// 4-byte columns, vectors have their own, and bone indices are 8 bytes each. The pointer fields hold
    /// no value.
    /// A buffer may store only some attributes; the rest are null, or another buffer's arrays. An
    /// attribute without an array has the same value for every particle, its constant.
    /// </summary>
    internal sealed class ParticleBuffer
    {
        /// <summary>The number of <see cref="ParticleField"/> values, the size of the field tables.</summary>
        public const int FieldCount = (int)ParticleField.RopeSegmentData + 1;

        /// <summary>The value every particle has for each attribute without an array.</summary>
        public sealed class ConstantValues
        {
            /// <summary>The value of each 4-byte attribute by field, 0 for every other field.</summary>
            public float[] Scalars { get; } = new float[FieldCount];

            /// <summary>The value of each vector attribute by field, zero for every other field.</summary>
            public Vector3[] Vectors { get; } = new Vector3[FieldCount];

            /// <summary>The bone indices.</summary>
            public ulong BoneIndices { get; init; }
        }

        private static readonly ParticleField[] FloatFields =
        [
            ParticleField.LifeDuration,
            ParticleField.Radius,
            ParticleField.Roll,
            ParticleField.RollSpeed,
            ParticleField.Alpha,
            ParticleField.CreationTime,
            ParticleField.SequenceNumber,
            ParticleField.TrailLength,
            ParticleField.Yaw,
            ParticleField.SecondSequenceNumber,
            ParticleField.AlphaAlternate,
            ParticleField.ScratchFloat,
            ParticleField.Pitch,
            ParticleField.GlowAlpha,
            ParticleField.ScratchFloat1,
            ParticleField.ScratchFloat2,
            ParticleField.ForceScale,
            ParticleField.ManualAnimationFrame,
            ParticleField.ShaderExtraData1,
            ParticleField.ShaderExtraData2,
        ];

        private static readonly ParticleField[] IntegerFields =
        [
            ParticleField.ParticleId,
            ParticleField.HitboxIndex,
            ParticleField.ParentParticleIndex,
            ParticleField.BoxFlags,
            ParticleField.UserEventStates,
            ParticleField.ParentParticleId,
            ParticleField.RopeSegmentId,
        ];

        private static readonly ParticleField[] ScalarFields = [.. FloatFields, .. IntegerFields];

        private static readonly ParticleField[] VectorFields =
        [
            ParticleField.Position,
            ParticleField.PositionPrevious,
            ParticleField.Color,
            ParticleField.HitboxOffsetPosition,
            ParticleField.ScratchVector,
            ParticleField.Normal,
            ParticleField.GlowRgb,
            ParticleField.ScratchVector2,
            ParticleField.BoneWeights,
            ParticleField.BoxMins,
            ParticleField.BoxMaxs,
            ParticleField.BoxAngles,
            ParticleField.RopeSegmentData,
        ];

        private static readonly ulong IntegerFieldMask = MaskOf(IntegerFields);

        private static readonly ulong ScalarFieldMask = MaskOf(ScalarFields);

        private static readonly ulong VectorFieldMask = MaskOf(VectorFields);

        /// <summary>The attributes that hold a value per particle: every field except the pointer fields.</summary>
        public static readonly ulong ValueFieldMask = ScalarFieldMask | VectorFieldMask | MaskOf([ParticleField.BoneIndices]);

        /// <summary>
        /// The bit mask of the fields renderers blend between two steps, besides the alpha2 field each
        /// renderer configures. Every other field is drawn as it is in the current step.
        /// </summary>
        public static readonly ulong BlendedFieldMask = MaskOf(
        [
            ParticleField.Position,
            ParticleField.Radius,
            ParticleField.Roll,
            ParticleField.Color,
            ParticleField.Alpha,
            ParticleField.Yaw,
            ParticleField.Pitch,
        ]);

        /// <summary>
        /// The 4-byte attributes by field, null for a field stored otherwise or not at all. An integer
        /// attribute is stored as its bits.
        /// </summary>
        public float[]?[] Scalars { get; } = new float[FieldCount][];

        /// <summary>The vector attributes by field, null for a field stored otherwise or not at all.</summary>
        public Vector3[]?[] Vectors { get; } = new Vector3[FieldCount][];

        /// <summary>
        /// The four model bones each particle follows, as 16-bit indices from the low bits up. See
        /// <see cref="PackedBones"/>.
        /// </summary>
        public ulong[]? BoneIndices;

        /// <summary>The value of each attribute without an array.</summary>
        public ConstantValues Constants = new();

        /// <summary>The system time particle ages are measured against.</summary>
        public float Time;

        private ulong ownedFields;

        /// <summary>
        /// Creates storage for the attributes in the <paramref name="fields"/> bit mask only. The others
        /// read their constants until <see cref="ShareFrom"/> points them at another buffer's.
        /// </summary>
        public ParticleBuffer(int capacity, ulong fields)
        {
            Capacity = capacity;
            SetStoredFields(fields);
        }

        private static ulong MaskOf(ReadOnlySpan<ParticleField> fields)
        {
            var mask = 0UL;

            foreach (var field in fields)
            {
                mask |= field.Mask();
            }

            return mask;
        }

        private static bool Contains(ulong mask, ParticleField field) => (mask & field.Mask()) != 0;

        private bool Owns(ParticleField field) => Contains(ownedFields, field);

        /// <summary>The bit mask of the attributes this buffer allocated itself.</summary>
        public ulong OwnedFields => ownedFields;

        /// <summary>Whether <paramref name="field"/> is an integer attribute, stored as its bits in <see cref="Scalars"/>.</summary>
        private static bool IsInteger(ParticleField field) => (uint)field < FieldCount && Contains(IntegerFieldMask, field);

        /// <summary>Whether <paramref name="field"/> is a float or integer attribute, kept in <see cref="Scalars"/>.</summary>
        public static bool IsScalar(ParticleField field) => (uint)field < FieldCount && Contains(ScalarFieldMask, field);

        /// <summary>Whether <paramref name="field"/> is a vector attribute, kept in <see cref="Vectors"/>.</summary>
        public static bool IsVector(ParticleField field) => (uint)field < FieldCount && Contains(VectorFieldMask, field);

        /// <summary>Gets where <paramref name="field"/> is kept, or <see cref="ParticleFieldStorage.None"/> for a field that holds no value.</summary>
        public static ParticleFieldStorage StorageOf(ParticleField field)
        {
            if (field == ParticleField.BoneIndices)
            {
                return ParticleFieldStorage.BoneIndices;
            }

            if (IsVector(field))
            {
                return ParticleFieldStorage.Vector;
            }

            if (IsInteger(field))
            {
                return ParticleFieldStorage.Integer;
            }

            if (IsScalar(field))
            {
                return ParticleFieldStorage.Scalar;
            }

            return ParticleFieldStorage.None;
        }

        /// <summary>Gets the number of slots allocated.</summary>
        public int Capacity { get; }

        /// <summary>
        /// Allocates <paramref name="field"/> in this buffer, if it is not already. The new array starts as
        /// a copy of the array the field shared, or else holds its constant.
        /// </summary>
        private void Allocate(ParticleField field)
        {
            if (Owns(field))
            {
                return;
            }

            ownedFields |= field.Mask();

            if (field == ParticleField.BoneIndices)
            {
                var bones = new ulong[Capacity];
                Fill(BoneIndices, bones, Constants.BoneIndices);
                BoneIndices = bones;
            }
            else if (Contains(VectorFieldMask, field))
            {
                var vectors = new Vector3[Capacity];
                Fill(Vectors[(int)field], vectors, Constants.Vectors[(int)field]);
                Vectors[(int)field] = vectors;
            }
            else
            {
                var scalars = new float[Capacity];
                Fill(Scalars[(int)field], scalars, Constants.Scalars[(int)field]);
                Scalars[(int)field] = scalars;
            }
        }

        private static void Fill<T>(T[]? shared, T[] column, T constant)
        {
            if (shared != null)
            {
                shared.CopyTo(column);
                return;
            }

            column.AsSpan().Fill(constant);
        }

        /// <summary>
        /// Makes the value attributes in the <paramref name="fields"/> bit mask the ones this buffer
        /// allocates: those it lacks are allocated, and those outside the mask are dropped.
        /// </summary>
        public void SetStoredFields(ulong fields)
        {
            for (var field = 0; field < FieldCount; field++)
            {
                var particleField = (ParticleField)field;

                if (!Contains(ValueFieldMask, particleField))
                {
                    continue;
                }

                if (Contains(fields, particleField))
                {
                    Allocate(particleField);
                }
                else if (Owns(particleField))
                {
                    ownedFields &= ~particleField.Mask();
                    Scalars[field] = null;
                    Vectors[field] = null;

                    if (particleField == ParticleField.BoneIndices)
                    {
                        BoneIndices = null;
                    }
                }
            }
        }

        /// <summary>
        /// Takes <paramref name="source"/>'s constants, and points every attribute in the
        /// <paramref name="fields"/> bit mask that this buffer did not allocate at <paramref name="source"/>'s
        /// array, so reads and writes of those go to <paramref name="source"/>. Every other attribute this
        /// buffer did not allocate reads its constant. Call it again whenever <paramref name="source"/>
        /// allocates or drops attributes, since the arrays it shares are then replaced.
        /// </summary>
        public void ShareFrom(ParticleBuffer source, ulong fields)
        {
            Constants = source.Constants;

            for (var field = 0; field < FieldCount; field++)
            {
                var particleField = (ParticleField)field;

                if (Owns(particleField))
                {
                    continue;
                }

                var shared = Contains(fields, particleField);
                Scalars[field] = shared ? source.Scalars[field] : null;
                Vectors[field] = shared ? source.Vectors[field] : null;
            }

            if (!Owns(ParticleField.BoneIndices))
            {
                BoneIndices = Contains(fields, ParticleField.BoneIndices) ? source.BoneIndices : null;
            }
        }

        /// <summary>
        /// Copies <paramref name="count"/> particles from <paramref name="source"/> at
        /// <paramref name="sourceIndex"/> to this buffer at <paramref name="index"/>, for every attribute this
        /// buffer allocated.
        /// </summary>
        public void Copy(ParticleBuffer source, int sourceIndex, int index, int count)
        {
            foreach (var field in ScalarFields)
            {
                if (Owns(field))
                {
                    Copy(source.Scalars[(int)field], source.Constants.Scalars[(int)field], sourceIndex, Scalars[(int)field]!, index, count);
                }
            }

            foreach (var field in VectorFields)
            {
                if (Owns(field))
                {
                    Copy(source.Vectors[(int)field], source.Constants.Vectors[(int)field], sourceIndex, Vectors[(int)field]!, index, count);
                }
            }

            if (Owns(ParticleField.BoneIndices))
            {
                Copy(source.BoneIndices, source.Constants.BoneIndices, sourceIndex, BoneIndices!, index, count);
            }
        }

        /// <summary>Sets the particle at <paramref name="index"/> to the constants, for every attribute this buffer allocated.</summary>
        public void ResetToConstants(int index)
        {
            foreach (var field in ScalarFields)
            {
                if (Owns(field))
                {
                    Scalars[(int)field]![index] = Constants.Scalars[(int)field];
                }
            }

            foreach (var field in VectorFields)
            {
                if (Owns(field))
                {
                    Vectors[(int)field]![index] = Constants.Vectors[(int)field];
                }
            }

            if (Owns(ParticleField.BoneIndices))
            {
                BoneIndices![index] = Constants.BoneIndices;
            }
        }

        /// <summary>
        /// Moves particles within the buffer, each move in turn copying the particle at its source index
        /// over the one at its destination.
        /// </summary>
        public void Move(ReadOnlySpan<(int From, int To)> moves)
        {
            for (var field = 0; field < FieldCount; field++)
            {
                if (!Owns((ParticleField)field))
                {
                    continue;
                }

                if (Scalars[field] is { } scalars)
                {
                    Move(scalars, moves);
                }
                else if (Vectors[field] is { } vectors)
                {
                    Move(vectors, moves);
                }
            }

            if (Owns(ParticleField.BoneIndices))
            {
                Move(BoneIndices!, moves);
            }
        }

        private static void Move<T>(T[] column, ReadOnlySpan<(int From, int To)> moves)
        {
            foreach (var (from, to) in moves)
            {
                column[to] = column[from];
            }
        }

        private static void Copy<T>(T[]? source, T constant, int sourceIndex, T[] destination, int index, int count)
        {
            var target = destination.AsSpan(index, count);

            if (source == null)
            {
                target.Fill(constant);
                return;
            }

            source.AsSpan(sourceIndex, count).CopyTo(target);
        }

        /// <summary>
        /// Creates what every new particle of a system starts as: the attribute defaults with the
        /// constant attributes of the definition on top.
        /// </summary>
        public static ConstantValues CreateConstants(ParticleDefinitionParser parse)
        {
            var constants = new ConstantValues();
            var scalars = constants.Scalars;
            var vectors = constants.Vectors;

            // The defaults a particle has before any constant or function sets it
            scalars[(int)ParticleField.LifeDuration] = 1f;
            scalars[(int)ParticleField.Alpha] = 1.0f;
            scalars[(int)ParticleField.AlphaAlternate] = 1.0f;
            vectors[(int)ParticleField.Color] = Vector3.One;
            scalars[(int)ParticleField.Radius] = 5.0f;
            scalars[(int)ParticleField.TrailLength] = 0.1f;
            scalars[(int)ParticleField.ForceScale] = 1.0f;
            vectors[(int)ParticleField.Normal] = new Vector3(0f, 0f, 1f);
            scalars[(int)ParticleField.ParentParticleIndex] = BitConverter.Int32BitsToSingle(-1);
            scalars[(int)ParticleField.ParentParticleId] = BitConverter.Int32BitsToSingle(int.MinValue);
            vectors[(int)ParticleField.GlowRgb] = Vector3.One;
            scalars[(int)ParticleField.GlowAlpha] = 1f;
            scalars[(int)ParticleField.ShaderExtraData1] = 1f;
            scalars[(int)ParticleField.ShaderExtraData2] = 1f;

            if (parse.Data.TryGetValue("m_ConstantColor", out var constantColor))
            {
                vectors[(int)ParticleField.Color] = constantColor.ToVector3() / 255f;
                if (constantColor.Count > 3)
                {
                    var alpha = constantColor[3];
                    scalars[(int)ParticleField.Alpha] = alpha.ValueType == KVValueType.Null ? 0f : (int)alpha / 255f;
                }
            }

            SetConstant(ParticleField.Radius, "m_flConstantRadius");
            SetConstant(ParticleField.LifeDuration, "m_flConstantLifespan");
            // Unlike every other source of these attributes, the constants are not converted from degrees
            scalars[(int)ParticleField.Roll] = parse.Float("m_flConstantRotation", 0f);
            scalars[(int)ParticleField.RollSpeed] = parse.Float("m_flConstantRotationSpeed", 0f);

            vectors[(int)ParticleField.Normal] = parse.Vector3("m_ConstantNormal", vectors[(int)ParticleField.Normal]);

            SetConstant(ParticleField.SequenceNumber, "m_nConstantSequenceNumber");
            SetConstant(ParticleField.SecondSequenceNumber, "m_nConstantSequenceNumber1");

            return constants;

            void SetConstant(ParticleField field, string key)
            {
                scalars[(int)field] = parse.Float(key, scalars[(int)field]);
            }
        }
    }
}
