using ValveResourceFormat.ResourceTypes.ModelData;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Decides which LoD level of a model is drawn: the level forced by the viewer, or the one the
    /// model's own switch values pick for a screen-size metric. Holds no scene or GL state, so the
    /// decision can be exercised on its own.
    /// </summary>
    internal sealed class ModelLodSelector
    {
        private readonly ModelLodInfo lodInfo;
        private int? overrideLevel;

        /// <summary>The LoD level currently being rendered, auto-selected or forced.</summary>
        public int ActiveLevel { get; private set; }

        /// <summary>Whether the level is chosen automatically by distance rather than forced.</summary>
        public bool IsAuto => overrideLevel == null;

        /// <summary>
        /// Whether switching level changes what is drawn at all, which is what decides whether a
        /// selector is worth showing.
        /// </summary>
        public bool HasDistinctLevels => lodInfo.HasDistinctLevels;

        /// <summary>The levels the model populates.</summary>
        public IReadOnlyList<int> AvailableLevels => lodInfo.AvailableLevels;

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelLodSelector"/> class, starting at the
        /// model's lowest populated level.
        /// </summary>
        public ModelLodSelector(ModelLodInfo lodInfo)
        {
            this.lodInfo = lodInfo;
            ActiveLevel = lodInfo.LowestLevel;
        }

        /// <summary>
        /// Forces a level, clamped to the model's populated range, or returns to automatic selection
        /// when <paramref name="level"/> is <see langword="null"/>.
        /// </summary>
        /// <returns>Whether the active level changed.</returns>
        public bool SetOverride(int? level)
        {
            if (level.HasValue)
            {
                var highestLevel = lodInfo.AvailableLevels.Count > 0 ? lodInfo.AvailableLevels[^1] : lodInfo.LowestLevel;
                level = Math.Clamp(level.Value, lodInfo.LowestLevel, highestLevel);
            }

            overrideLevel = level;

            // A forced level stays put; Auto restarts at the lowest populated level and Update takes over.
            var target = level ?? lodInfo.LowestLevel;

            return Select(target);
        }

        /// <summary>
        /// In automatic mode, picks the level for a screen-size metric: the model drops to level
        /// <c>n</c> once the metric passes that level's switch value.
        /// </summary>
        /// <returns>Whether the active level changed.</returns>
        public bool Update(float metric)
        {
            if (overrideLevel != null || lodInfo.AvailableLevels.Count <= 1 || lodInfo.SwitchDistances.Count <= 1)
            {
                return false;
            }

            return Select(lodInfo.SelectLevel(metric));
        }

        /// <summary>
        /// Whether the mesh at <paramref name="meshIndex"/> is drawn at the active level.
        /// </summary>
        public bool Contains(int meshIndex) => lodInfo.IsMeshInLevel(meshIndex, ActiveLevel);

        private bool Select(int level)
        {
            if (level == ActiveLevel)
            {
                return false;
            }

            ActiveLevel = level;
            return true;
        }
    }
}
