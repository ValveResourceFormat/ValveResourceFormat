using System.Collections.Generic;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace ValveResourceFormat.Renderer.AnimLib;

static class EventConditionRulesExtensions
{
    public static bool IsRuleSet(this BitFlags flags, EventConditionRules rule)
        => flags.IsFlagSet(1u << (int)rule);
}

/// <summary>
/// One event sampled during a graph update: either an animation event coming from a clip's
/// timeline, or a graph event emitted by a state (entry/execute/exit).
/// </summary>
readonly struct SampledEvent
{
    public short SourceNodeIdx { get; init; }
    public bool IsGraphEvent { get; init; }
    public bool IsFromActiveBranch { get; init; }
    public bool IsIgnored { get; init; }

    /// <summary>Blend weight of the branch that sampled this event.</summary>
    public float Weight { get; init; }

    /// <summary>How far through the event we are, in [0, 1].</summary>
    public float PercentageThrough { get; init; }

    /// <summary>Graph event ID, or the ID of an ID animation event; default otherwise.</summary>
    public GlobalSymbol ID { get; init; }

    /// <summary>The clip event, for animation events.</summary>
    public NmClipEvent? AnimEvent { get; init; }

    public bool IsAnimationEvent => !IsGraphEvent;
}

/// <summary>A contiguous range of events in the sampled events buffer.</summary>
struct SampledEventRange
{
    public int StartIdx;
    public int EndIdx;

    public SampledEventRange(int startIdx, int endIdx)
    {
        StartIdx = startIdx;
        EndIdx = endIdx;
    }

    public readonly bool IsValid => StartIdx >= 0 && EndIdx >= StartIdx;
}

/// <summary>
/// Append-only buffer of the events sampled during one graph update. Nodes record the range they
/// appended so conditions can restrict their search to a source state's events.
/// </summary>
class SampledEventsBuffer
{
    private readonly List<SampledEvent> events = [];

    public int Count => events.Count;

    public SampledEvent this[int index] => events[index];

    public void Clear() => events.Clear();

    public void EmplaceAnimationEvent(short sourceNodeIdx, NmClipEvent animEvent, float percentageThrough, bool isFromActiveBranch, float weight = 1f)
    {
        var id = animEvent is NmIDEvent idEvent ? new GlobalSymbol(idEvent.ID) : default;

        events.Add(new SampledEvent
        {
            SourceNodeIdx = sourceNodeIdx,
            IsGraphEvent = false,
            IsFromActiveBranch = isFromActiveBranch,
            Weight = weight,
            PercentageThrough = percentageThrough,
            ID = id,
            AnimEvent = animEvent,
        });
    }

    public void EmplaceGraphEvent(short sourceNodeIdx, GlobalSymbol id, bool isFromActiveBranch, float weight = 1f)
    {
        events.Add(new SampledEvent
        {
            SourceNodeIdx = sourceNodeIdx,
            IsGraphEvent = true,
            IsFromActiveBranch = isFromActiveBranch,
            Weight = weight,
            PercentageThrough = 0f,
            ID = id,
        });
    }
}
