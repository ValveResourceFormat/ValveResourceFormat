using System.Globalization;
using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>logic_case</c>. Picks one of up to thirty-two outputs, either by matching a value against the authored
/// cases or at random.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CLogicCase">CLogicCase</seealso>
public sealed class LogicCase : BaseEntity
{
    private const int CaseCount = 32;
    private readonly string?[] cases = new string?[CaseCount];
    private readonly List<int> shuffle = [];
    private int lastShuffleCase = -1;

    /// <summary>Initializes a <c>logic_case</c> from its keyvalues.</summary>
    public LogicCase(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        for (var i = 0; i < CaseCount; i++)
        {
            var value = KeyValues.GetStringProperty(string.Create(CultureInfo.InvariantCulture, $"case{i + 1:00}"));

            cases[i] = string.IsNullOrEmpty(value) ? null : value;
        }
    }

    [EntityInput("InValue")]
    private void InputInValue(EntityInputData data)
    {
        var value = data.Parameter ?? string.Empty;

        for (var i = 0; i < CaseCount; i++)
        {
            if (cases[i] is { } authored && Matches(authored, value))
            {
                FireCase(i, data.Activator, this);
                return;
            }
        }

        EntitySystem.TriggerOutput(this, "OnDefault", data.Activator, value);
    }

    [EntityInput("PickRandom")]
    private void InputPickRandom(EntityInputData data)
    {
        var available = GetAvailableCases().ToList();

        if (available.Count == 0)
        {
            return;
        }

        FireCase(available[Random.Shared.Next(available.Count)], data.Activator, this);
    }

    [EntityInput("PickRandomShuffle")]
    private void InputPickRandomShuffle(EntityInputData data)
    {
        var drawCount = shuffle.Count;

        if (drawCount == 0)
        {
            RefillShuffle();
            drawCount = shuffle.Count;

            // A refilled shuffle may not open with the case the last one ended on, so a repeat cannot
            // straddle two shuffles. That case is swapped to the end and left out of this draw.
            var last = shuffle.IndexOf(lastShuffleCase);

            if (drawCount > 1 && last >= 0)
            {
                (shuffle[last], shuffle[^1]) = (shuffle[^1], shuffle[last]);
                drawCount--;
            }
        }

        if (drawCount == 0)
        {
            return;
        }

        var drawn = Random.Shared.Next(drawCount);
        var picked = shuffle[drawn];

        shuffle[drawn] = shuffle[^1];
        shuffle.RemoveAt(shuffle.Count - 1);
        lastShuffleCase = picked;

        FireCase(picked, data.Activator, data.Caller ?? this);
    }

    // Unlike a refill on draw, this one may open with the case the last shuffle ended on
    [EntityInput("ResetShuffle")] private void InputResetShuffle(EntityInputData data) => RefillShuffle();

    private void RefillShuffle()
    {
        shuffle.Clear();
        shuffle.AddRange(GetAvailableCases());
    }

    // A case can be picked while its output has connections left to fire. The authored value only matters
    // for InValue, and maps that only pick randomly author none.
    private IEnumerable<int> GetAvailableCases()
        => Enumerable.Range(0, CaseCount).Where(i => EntitySystem.GetLiveConnections(this, CaseOutput(i)).Any());

    private void FireCase(int index, BaseEntity? activator, BaseEntity caller)
        => EntitySystem.TriggerOutput(this, CaseOutput(index), activator, caller: caller);

    // As text first, then as a number. math_counter used to send whole numbers as "1" and now sends a float
    // as "1.0", and maps authored against either must still match.
    private static bool Matches(string authored, string value)
    {
        if (string.Equals(authored, value, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return float.TryParse(authored, NumberStyles.Float, CultureInfo.InvariantCulture, out var authoredNumber)
            && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var valueNumber)
            && authoredNumber == valueNumber;
    }

    private static string CaseOutput(int index) => string.Create(CultureInfo.InvariantCulture, $"OnCase{index + 1:00}");
}
