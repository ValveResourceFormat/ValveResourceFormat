using System.Collections.Generic;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace ValveResourceFormat.Renderer.AnimLib;

static class EventConditionRulesExtensions
{
    public static bool IsRuleSet(this BitFlags flags, EventConditionRules rule)
        => flags.IsFlagSet(1u << (int)rule);
}

// The kind of graph event a state or clip emitted (Esoterica GraphEventType)
enum GraphEventType : byte
{
    Entry,
    FullyInState,
    Exit,
    Timed,
    Generic,
}

/// <summary>
/// One event sampled during a graph update: either an animation event coming from a clip's
/// timeline, or a graph event emitted by a state (entry/execute/exit/timed).
/// </summary>
struct SampledEvent
{
    public short SourceNodeIdx { get; init; }
    public bool IsGraphEvent { get; init; }
    public bool IsFromActiveBranch { get; set; }
    public bool IsIgnored { get; set; }

    /// <summary>The kind of graph event, for graph events.</summary>
    public GraphEventType GraphEventType { get; init; }

    /// <summary>Blend weight of the branch that sampled this event.</summary>
    public float Weight { get; set; }

    /// <summary>How far through the event we are, in [0, 1].</summary>
    public float PercentageThrough { get; init; }

    /// <summary>Graph event ID, or the ID of an ID animation event; default otherwise.</summary>
    public GlobalSymbol ID { get; init; }

    /// <summary>The clip event, for animation events.</summary>
    public NmClipEvent? AnimEvent { get; init; }

    public readonly bool IsAnimationEvent => !IsGraphEvent;
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

    /// <summary>Appends all of another buffer's events, e.g. to surface a child graph's events to its parent.</summary>
    public void AppendFrom(SampledEventsBuffer other)
    {
        events.AddRange(other.events);
    }

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

    public void EmplaceGraphEvent(short sourceNodeIdx, GraphEventType type, GlobalSymbol id, bool isFromActiveBranch, float weight = 1f)
    {
        events.Add(new SampledEvent
        {
            SourceNodeIdx = sourceNodeIdx,
            IsGraphEvent = true,
            GraphEventType = type,
            IsFromActiveBranch = isFromActiveBranch,
            Weight = weight,
            PercentageThrough = 0f,
            ID = id,
        });
    }

    /// <summary>Scales the weight of every event in the range.</summary>
    public void UpdateWeights(SampledEventRange range, float weightMultiplier)
    {
        for (var i = range.StartIdx; i < range.EndIdx && i < events.Count; i++)
        {
            var e = events[i];
            e.Weight *= weightMultiplier;
            events[i] = e;
        }
    }

    /// <summary>Marks every event in the range as ignored.</summary>
    public void MarkEventsAsIgnored(SampledEventRange range)
    {
        for (var i = range.StartIdx; i < range.EndIdx && i < events.Count; i++)
        {
            var e = events[i];
            e.IsIgnored = true;
            events[i] = e;
        }
    }

    /// <summary>Marks every event in the range as coming from an inactive branch.</summary>
    public void MarkEventsAsFromInactiveBranch(SampledEventRange range)
    {
        for (var i = range.StartIdx; i < range.EndIdx && i < events.Count; i++)
        {
            var e = events[i];
            e.IsFromActiveBranch = false;
            events[i] = e;
        }
    }

    /// <summary>
    /// Scales the weights of two consecutive event ranges by their blend weights and returns the
    /// combined range (Esoterica SampledEventsBuffer::BlendEventRanges).
    /// </summary>
    public SampledEventRange BlendEventRanges(SampledEventRange range0, SampledEventRange range1, float blendWeight)
    {
        var numEvents0 = range0.EndIdx - range0.StartIdx;
        var numEvents1 = range1.EndIdx - range1.StartIdx;

        if (numEvents0 + numEvents1 > 0)
        {
            UpdateWeights(range0, 1f - blendWeight);
            UpdateWeights(range1, blendWeight);

            if (numEvents0 > 0 && numEvents1 > 0)
            {
                return new SampledEventRange(range0.StartIdx, range1.EndIdx);
            }

            return numEvents0 > 0 ? range0 : range1;
        }

        return new SampledEventRange(Count, Count);
    }
}
