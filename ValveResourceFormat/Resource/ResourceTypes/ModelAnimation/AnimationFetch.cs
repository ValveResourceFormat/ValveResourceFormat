using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents an animation fetch that specifies a local cycle pose parameter.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animationsystem/CSeqMultiFetch">CSeqMultiFetch</seealso>
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
        public int[] LocalReferenceArray { get; set; }

        /// <summary>
        /// Gets or sets the pose parameter index driving each blend dimension, -1 where unused.
        /// </summary>
        public int[] LocalPose { get; set; }

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
        /// Gets or sets whether a two dimensional blend mixes the three corners of a grid cell triangle
        /// instead of all four corners.
        /// </summary>
        public bool IsTriangle { get; set; }

        /// <summary>
        /// Gets or sets the pose parameter value each referenced animation sits at on the second
        /// dimension of a two dimensional blend.
        /// </summary>
        public float[] PoseKeyArray1 { get; set; }

        /// <summary>
        /// Gets or sets the size of each blend dimension.
        /// </summary>
        public int[] GroupSize { get; set; }

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
            LocalReferenceArray = fetchKV.GetArray<int>("m_localReferenceArray") ?? [];
            LocalPose = fetchKV.GetArray<int>("m_nLocalPose") ?? [];
            PoseKeyArray = fetchKV.GetFloatArray("m_poseKeyArray0");
            var flags = fetchKV.GetSubCollection("m_flags");
            Is1D = flags.GetBooleanProperty("m_b1D");
            IsTriangle = flags.GetBooleanProperty("m_b2D_TRI");
            Is2D = flags.GetBooleanProperty("m_b2D") || IsTriangle;
            PoseKeyArray1 = fetchKV.GetFloatArray("m_poseKeyArray1");
            GroupSize = fetchKV.GetArray<int>("m_nGroupSize") ?? [];
            FixedBlendWeight = fetchKV.GetBooleanProperty("m_bFixedBlendWeight");
            var fixedWeights = fetchKV.GetFloatArray("m_flFixedBlendWeightVals");
            FixedBlendWeightValue = fixedWeights.Length > 0 ? fixedWeights[0] : 0f;

            // A 2D blend spreads its references over a grid walked row first.
            var rows = Is2D && GroupSize.Length > 0 ? GroupSize[0] : 0;
            var columns = Is2D && GroupSize.Length > 1 ? GroupSize[1] : 0;

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
        /// parameter values: bilinear across the grid when <see cref="Is2D"/>, otherwise linear along
        /// <see cref="PoseKeyArray"/>, or at <see cref="FixedBlendWeightValue"/> when the fetch ignores
        /// its pose parameter.
        /// </summary>
        public readonly float GetBlendWeight(int index, float rowValue, float columnValue)
        {
            if (Is2D)
            {
                if (rowKeys.Length == 0 || columnKeys.Length == 0)
                {
                    return 0f;
                }

                if (IsTriangle)
                {
                    return GetTriangleWeight(index, rowValue, columnValue);
                }

                return KeyWeight(rowKeys, rowValue, index % rowKeys.Length)
                    * KeyWeight(columnKeys, columnValue, index / rowKeys.Length);
            }

            return KeyWeight(PoseKeyArray, FixedBlendWeight ? FixedBlendWeightValue : rowValue, index);
        }

        /// <summary>
        /// The weight of <paramref name="index"/> in a triangle blend. The grid cell holding the pose
        /// parameter values is split along a diagonal that alternates from cell to cell like a
        /// checkerboard, and the three corners of the triangle holding the point share the weight.
        /// </summary>
        private readonly float GetTriangleWeight(int index, float rowValue, float columnValue)
        {
            var width = rowKeys.Length;
            var height = columnKeys.Length;

            var cellX = 0;
            var cellY = 0;
            var u = 0f;
            var v = 0f;

            // The first cell containing the point wins. A point outside the grid takes the cell whose
            // centre is closest to it, clamped to that cell.
            var closestDistance = 1000f;
            var cellRows = Math.Max(height - 1, 1);

            for (var x = 0; x < width - 1; x++)
            {
                var rowSpan = rowKeys[x + 1] - rowKeys[x];
                var cellU = rowSpan != 0f ? (rowValue - rowKeys[x]) / rowSpan : 0f;

                for (var y = 0; y < cellRows; y++)
                {
                    var columnSpan = y + 1 < height ? columnKeys[y + 1] - columnKeys[y] : 0f;
                    var cellV = columnSpan != 0f ? (columnValue - columnKeys[y]) / columnSpan : 0f;

                    if (cellU <= 1f && cellV <= 1f)
                    {
                        return GetTriangleCornerWeight(index, width, height, x, y, MathF.Max(cellU, 0f), MathF.Max(cellV, 0f));
                    }

                    var offsetU = cellU - 0.5f;
                    var offsetV = cellV - 0.5f;
                    var distance = (offsetV * offsetV) + (offsetU * offsetU);

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        cellX = x;
                        cellY = y;
                        u = MathUtils.Saturate(cellU);
                        v = MathUtils.Saturate(cellV);
                    }
                }
            }

            return GetTriangleCornerWeight(index, width, height, cellX, cellY, u, v);
        }

        /// <summary>
        /// Adds up the weight that the three corners of the triangle at (<paramref name="u"/>,
        /// <paramref name="v"/>) in a cell give to <paramref name="index"/>.
        /// </summary>
        private static float GetTriangleCornerWeight(int index, int width, int height, int cellX, int cellY, float u, float v)
        {
            var bottomLeft = GetGridIndex(width, height, cellX, cellY);
            var bottomRight = GetGridIndex(width, height, cellX + 1, cellY);
            var topLeft = GetGridIndex(width, height, cellX, cellY + 1);
            var topRight = GetGridIndex(width, height, cellX + 1, cellY + 1);

            int corner0;
            int corner1;
            int corner2;
            float weight0;
            float weight1;

            var isOddCell = ((cellX + cellY) & 1) != 0;

            if (isOddCell && u + v <= 1f)
            {
                corner0 = topLeft;
                corner1 = bottomLeft;
                corner2 = bottomRight;
                weight0 = v;
                weight1 = 1f - u - v;
            }
            else if (isOddCell)
            {
                corner0 = bottomRight;
                corner1 = topRight;
                corner2 = topLeft;
                weight0 = 1f - v;
                weight1 = u + v - 1f;
            }
            else if (u <= v)
            {
                corner0 = topRight;
                corner1 = topLeft;
                corner2 = bottomLeft;
                weight0 = u;
                weight1 = v - u;
            }
            else
            {
                corner0 = bottomLeft;
                corner1 = bottomRight;
                corner2 = topRight;
                weight0 = 1f - u;
                weight1 = u - v;
            }

            if (weight1 < 0.001f)
            {
                weight1 = 0f;
            }

            var weight2 = MathF.Max(0f, 1f - weight0 - weight1);

            // Corners clamped onto the grid edge can share an index, so their weights add up
            var total = 0f;

            if (corner0 == index)
            {
                total += weight0;
            }

            if (corner1 == index)
            {
                total += weight1;
            }

            if (corner2 == index)
            {
                total += weight2;
            }

            return total;
        }

        private static int GetGridIndex(int width, int height, int x, int y)
        {
            var clampedX = Math.Clamp(x, 0, width - 1);
            var clampedY = Math.Clamp(y, 0, height - 1);
            return clampedX + (clampedY * width);
        }

        /// <summary>
        /// The weight of <paramref name="index"/> among an unsorted set of blend keys at
        /// <paramref name="value"/>: the two keys bracketing it split the weight linearly, or the single
        /// nearest key past either end takes it all.
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
