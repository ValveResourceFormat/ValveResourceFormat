using System.Globalization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>logic_case</c>. Picks one of up to sixteen outputs, either by matching a value against the authored
/// cases or at random - the switch statement of entity I/O.
/// </summary>
public sealed class LogicCase : BaseEntity
{
    // The FGD's Case01 to Case16
    private const int CaseCount = 16;

    private readonly string?[] cases = new string?[CaseCount];
    private readonly List<int> available = [];
    private readonly List<int> shuffle = [];
    private int lastShuffleCase = -1;

    /// <summary>Initializes a <c>logic_case</c> from its keyvalues.</summary>
    public LogicCase(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        for (var i = 0; i < CaseCount; i++)
        {
            // Compiled keyvalues are lowercased, and the case number is one-based and zero-padded
            var value = KeyValues.GetStringProperty($"case{i + 1:00}");

            cases[i] = string.IsNullOrEmpty(value) ? null : value;

            // A case counts as available because something is wired to its output, which is what Source's
            // BuildCaseMap tests. The authored value only decides what InValue matches, so a case picked at
            // random needs no value at all - and maps that only ever pick randomly author none.
            if (HasOutput($"OnCase{i + 1:00}"))
            {
                available.Add(i);
            }
        }
    }

    private bool HasOutput(string outputName)
    {
        if (Data?.Connections == null)
        {
            return false;
        }

        foreach (var connection in Data.Connections)
        {
            if (connection.OutputName.Equals(outputName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    [EntityInput("InValue")]
    private void InputInValue(EntityInputData data)
    {
        var value = data.Parameter;

        for (var i = 0; i < CaseCount; i++)
        {
            if (cases[i] is not { } authored || !Matches(authored, value))
            {
                continue;
            }

            FireCase(i, data.Activator);
            return;
        }

        EntitySystem.TriggerOutput(this, "OnDefault", data.Activator);
    }

    [EntityInput("PickRandom")]
    private void InputPickRandom(EntityInputData data)
    {
        if (available.Count == 0)
        {
            return;
        }

        FireCase(available[Random.Shared.Next(available.Count)], data.Activator);
    }

    [EntityInput("PickRandomShuffle")]
    private void InputPickRandomShuffle(EntityInputData data)
    {
        if (shuffle.Count == 0)
        {
            if (available.Count == 0)
            {
                return;
            }

            shuffle.AddRange(available);

            // A fresh batch may not open with the case the last one closed on, so that a repeat cannot
            // straddle the boundary. Source swaps it to the end and shortens the draw by one.
            if (shuffle.Count > 1 && lastShuffleCase != -1)
            {
                shuffle.Remove(lastShuffleCase);

                var reopened = Random.Shared.Next(shuffle.Count);
                var first = shuffle[reopened];

                shuffle.RemoveAt(reopened);
                shuffle.Add(lastShuffleCase);

                Fire(first);
                return;
            }
        }

        Fire(shuffle[Random.Shared.Next(shuffle.Count)]);

        void Fire(int picked)
        {
            shuffle.Remove(picked);
            lastShuffleCase = picked;

            FireCase(picked, data.Activator);
        }
    }

    private void FireCase(int index, BaseEntity? activator)
        => EntitySystem.TriggerOutput(this, $"OnCase{index + 1:00}", activator);

    // As text first, then as a number so that "1" and "1.0" are the same case
    private static bool Matches(string authored, string? value)
    {
        if (string.Equals(authored, value, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return float.TryParse(authored, NumberStyles.Float, CultureInfo.InvariantCulture, out var authoredNumber)
            && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var valueNumber)
            && authoredNumber == valueNumber;
    }
}
