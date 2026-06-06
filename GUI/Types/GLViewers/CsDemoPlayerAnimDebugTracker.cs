using System.Linq;

namespace GUI.Types.GLViewers;

sealed class CsDemoPlayerAnimDebugTracker
{
    private const float VisibleDurationSeconds = 3f;
    private const float FadeDurationSeconds = 0.5f;

    private readonly Dictionary<int, Entry> entries = [];

    private sealed class Entry
    {
        public PlayerAnimDebugData Data { get; set; } = null!;
        public string Signature { get; set; } = string.Empty;
        public float LastChangeTime { get; set; }
    }

    public void Update(IReadOnlyList<PlayerAnimDebugData> players, float nowSeconds)
    {
        var activeSlots = new HashSet<int>();

        foreach (var data in players)
        {
            activeSlots.Add(data.Slot);
            var signature = CsDemoPlayerAnimDebugResolver.BuildSignature(data);

            if (!entries.TryGetValue(data.Slot, out var entry))
            {
                entries[data.Slot] = new Entry
                {
                    Data = data,
                    Signature = signature,
                    LastChangeTime = nowSeconds,
                };
                continue;
            }

            if (entry.Signature != signature)
            {
                entry.Signature = signature;
                entry.LastChangeTime = nowSeconds;
            }

            entry.Data = data;
        }

        foreach (var slot in entries.Keys.Except(activeSlots).ToArray())
        {
            entries.Remove(slot);
        }
    }

    public IEnumerable<(PlayerAnimDebugData Data, float Alpha)> GetVisiblePanels(float nowSeconds)
    {
        foreach (var entry in entries.Values)
        {
            var alpha = GetAlpha(entry, nowSeconds);
            if (alpha <= 0.01f)
            {
                continue;
            }

            yield return (entry.Data, alpha);
        }
    }

    private static float GetAlpha(Entry entry, float nowSeconds)
    {
        if (entry.Data.IsSelected)
        {
            return 1f;
        }

        if (IsPersistentWarning(entry.Data))
        {
            return 1f;
        }

        var age = nowSeconds - entry.LastChangeTime;
        if (age <= VisibleDurationSeconds)
        {
            return 1f;
        }

        var fadeProgress = (age - VisibleDurationSeconds) / FadeDurationSeconds;
        return Math.Clamp(1f - fadeProgress, 0f, 1f);
    }

    private static bool IsPersistentWarning(PlayerAnimDebugData data)
        => data.WarningLabel is "missing model" or "missing skeleton";
}
