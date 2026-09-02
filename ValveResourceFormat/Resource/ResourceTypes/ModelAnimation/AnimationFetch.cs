using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents an animation fetch that specifies a local cycle pose parameter.
    /// </summary>
    public struct AnimationFetch
    {
        private readonly float[] rowKeys;
        private readonly float[] columnKeys;

        /// <summary>
        /// Gets or sets the local cycle pose parameter index.
        /// </summary>
        public int LocalCyclePoseParameter { get; set; }

        /// <summary>
        /// Gets or sets the entries of the sequence group name array the sequence plays, one per
        /// animation it blends between.
        /// </summary>
        public long[] LocalReferenceArray { get; set; }

        /// <summary>
        /// Gets or sets the pose parameter index driving each blend dimension, -1 where unused.
        /// </summary>
        public long[] LocalPose { get; set; }

        /// <summary>
        /// Gets or sets the pose parameter value each referenced animation sits at.
        /// </summary>
        public float[] PoseKeyArray { get; set; }

        /// <summary>
        /// Gets or sets whether the sequence blends its references along one pose parameter.
        /// </summary>
        public bool Is1D { get; set; }

        /// <summary>
        /// Gets or sets whether the sequence blends its references across two pose parameters.
        /// </summary>
        public bool Is2D { get; set; }

        /// <summary>
        /// Gets or sets the pose parameter value each referenced animation sits at on the second
        /// dimension of a two dimensional blend.
        /// </summary>
        public float[] PoseKeyArray1 { get; set; }

        /// <summary>
        /// Gets or sets the size of each blend dimension.
        /// </summary>
        public long[] GroupSize { get; set; }

        /// <summary>
        /// Gets or sets whether the blend ignores its pose parameter and sits at a fixed weight.
        /// </summary>
        public bool FixedBlendWeight { get; set; }

        /// <summary>
        /// Gets or sets the weight a fixed blend sits at.
        /// </summary>
        public float FixedBlendWeightValue { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationFetch"/> struct.
        /// </summary>
        /// <param name="fetchKV">The KeyValues object containing the fetch data.</param>
        public AnimationFetch(KVObject fetchKV)
        {
            LocalCyclePoseParameter = fetchKV.GetInt32Property("m_nLocalCyclePoseParameter");
            LocalReferenceArray = fetchKV.GetIntegerArray("m_localReferenceArray");
            LocalPose = fetchKV.GetIntegerArray("m_nLocalPose");
            PoseKeyArray = fetchKV.GetFloatArray("m_poseKeyArray0");
            var flags = fetchKV.GetSubCollection("m_flags");
            Is1D = flags.GetBooleanProperty("m_b1D");
            // A triangular blend has no document node of its own, so it is rebuilt as the grid it
            // spreads its animations over.
            Is2D = flags.GetBooleanProperty("m_b2D") || flags.GetBooleanProperty("m_b2D_TRI");
            PoseKeyArray1 = fetchKV.GetFloatArray("m_poseKeyArray1");
            GroupSize = fetchKV.GetIntegerArray("m_nGroupSize");
            FixedBlendWeight = fetchKV.GetBooleanProperty("m_bFixedBlendWeight");
            var fixedWeights = fetchKV.GetFloatArray("m_flFixedBlendWeightVals");
            FixedBlendWeightValue = fixedWeights.Length > 0 ? fixedWeights[0] : 0f;

            // A 2D blend spreads its references over a grid the compiler walks row first, and the row
            // and column keys are the two pose parameter axes it is addressed along.
            var rows = Is2D && GroupSize.Length > 0 ? (int)GroupSize[0] : 0;
            var columns = Is2D && GroupSize.Length > 1 ? (int)GroupSize[1] : 0;

            rowKeys = new float[rows];
            for (var row = 0; row < rows; row++)
            {
                rowKeys[row] = row < PoseKeyArray.Length ? PoseKeyArray[row] : 0f;
            }

            columnKeys = new float[columns];
            for (var column = 0; column < columns; column++)
            {
                var key = rows * column;
                columnKeys[column] = key < PoseKeyArray1.Length ? PoseKeyArray1[key] : 0f;
            }
        }

        /// <summary>
        /// The weight one entry of <see cref="LocalReferenceArray"/> carries at the given live pose
        /// parameter values: bilinear across the grid for a two dimensional blend
        /// (<see cref="Is2D"/>), otherwise linear along <see cref="PoseKeyArray"/> - using
        /// <see cref="FixedBlendWeightValue"/> in place of <paramref name="rowValue"/> when the fetch
        /// ignores its pose parameter (<see cref="FixedBlendWeight"/>).
        /// </summary>
        public readonly float GetBlendWeight(int index, float rowValue, float columnValue)
        {
            if (Is2D)
            {
                if (rowKeys.Length == 0 || columnKeys.Length == 0)
                {
                    return 0f;
                }

                return KeyWeight(rowKeys, rowValue, index % rowKeys.Length)
                    * KeyWeight(columnKeys, columnValue, index / rowKeys.Length);
            }

            return KeyWeight(PoseKeyArray, FixedBlendWeight ? FixedBlendWeightValue : rowValue, index);
        }

        /// <summary>
        /// The weight of <paramref name="index"/> among a small, not-necessarily-sorted set of blend keys
        /// at <paramref name="value"/>: the two keys immediately bracketing it split the weight linearly
        /// between their indices, or the single nearest key past either end takes it all.
        /// </summary>
        private static float KeyWeight(ReadOnlySpan<float> keys, float value, int index)
        {
            if ((uint)index >= (uint)keys.Length)
            {
                return 0f;
            }

            var lower = -1;
            var upper = -1;

            for (var i = 0; i < keys.Length; i++)
            {
                if (keys[i] <= value && (lower == -1 || keys[i] > keys[lower]))
                {
                    lower = i;
                }

                if (keys[i] >= value && (upper == -1 || keys[i] < keys[upper]))
                {
                    upper = i;
                }
            }

            if (lower == -1 || upper == -1 || upper == lower)
            {
                var only = lower != -1 ? lower : upper;
                return only == index ? 1f : 0f;
            }

            if (index != lower && index != upper)
            {
                return 0f;
            }

            var span = keys[upper] - keys[lower];
            var t = span != 0f ? (value - keys[lower]) / span : 0f;

            return index == lower ? 1f - t : t;
        }
    }
}
