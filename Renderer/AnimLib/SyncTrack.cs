using System.Diagnostics;
using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>
/// Synchronizes animations by dividing their timeline into named sync periods. Positions on the
/// track are expressed as <see cref="SyncTrackTime"/> (event index plus percentage through it),
/// which lets differently-timed clips advance in lockstep. Port of Esoterica's SyncTrack.
/// </summary>
class SyncTrack
{
    /// <summary>A default track with one full-length unnamed event.</summary>
    public static SyncTrack Default { get; } = new SyncTrack([new SyncTrack__Event(default, 0f, 1f)], 0);

    public SyncTrack__Event[] SyncEvents { get; }
    public int StartEventOffset { get; }

    public int NumEvents => SyncEvents.Length;

    public SyncTrack(KVObject data)
    {
        SyncEvents = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_syncEvents"), kv => new SyncTrack__Event(kv))];
        StartEventOffset = data.GetInt32Property("m_nStartEventOffset");

        if (SyncEvents.Length == 0)
        {
            SyncEvents = [new SyncTrack__Event(default, 0f, 1f)];
        }
    }

    public SyncTrack(SyncTrack__Event[] events, int startEventOffset)
    {
        Debug.Assert(events.Length > 0);
        SyncEvents = events;
        StartEventOffset = ClampIndexToTrack(startEventOffset, events.Length);
    }

    /// <summary>
    /// Creates a track by blending two tracks: event durations are blended, start times follow from
    /// the blended durations. The event count is the LCM of the two tracks' counts.
    /// </summary>
    public SyncTrack(SyncTrack track0, SyncTrack track1, float blendWeight)
    {
        var numEvents0 = track0.NumEvents;
        var numEvents1 = track1.NumEvents;
        Debug.Assert(blendWeight >= 0f && blendWeight <= 1f);

        var lcm = LowestCommonMultiple(numEvents0, numEvents1);
        var durationScale0 = (float)numEvents0 / lcm;
        var durationScale1 = (float)numEvents1 / lcm;

        SyncEvents = new SyncTrack__Event[lcm];
        var blendedStartPercent = 0f;

        for (var i = 0; i < lcm; i++)
        {
            var event0 = track0.GetEvent(i);
            var event1 = track1.GetEvent(i);
            var event0Duration = event0.Duration.Value * durationScale0;
            var event1Duration = event1.Duration.Value * durationScale1;

            var blendedDuration = float.Lerp(event0Duration, event1Duration, blendWeight);
            SyncEvents[i] = new SyncTrack__Event(blendWeight > 0.5f ? event1.ID : event0.ID, blendedStartPercent, blendedDuration);
            blendedStartPercent += blendedDuration;
        }

        // Normalize and make sure the last event reaches the end of the track
        var normalizedScalingFactor = 1f / blendedStartPercent;
        for (var i = 0; i < lcm; i++)
        {
            var e = SyncEvents[i];
            SyncEvents[i] = new SyncTrack__Event(e.ID, e.StartTime.Value * normalizedScalingFactor, e.Duration.Value * normalizedScalingFactor);
        }

        var last = SyncEvents[^1];
        SyncEvents[^1] = new SyncTrack__Event(last.ID, last.StartTime.Value, 1f - last.StartTime.Value);

        StartEventOffset = 0;
    }

    /// <summary>Calculates the duration resulting from a blend of two synchronized tracks.</summary>
    public static float CalculateDurationSynchronized(float duration0, float duration1, int numEvents0, int numEvents1, int eventsLCM, float blendWeight)
    {
        Debug.Assert(numEvents0 > 0 && numEvents1 > 0 && eventsLCM > 0);
        var scaledDuration0 = duration0 * ((float)eventsLCM / numEvents0);
        var scaledDuration1 = duration1 * ((float)eventsLCM / numEvents1);
        return float.Lerp(scaledDuration0, scaledDuration1, blendWeight);
    }

    public static int LowestCommonMultiple(int a, int b)
    {
        static int Gcd(int x, int y) => y == 0 ? x : Gcd(y, x % y);
        return a / Gcd(a, b) * b;
    }

    private static int ClampIndexToTrack(int eventIndex, int numEvents)
    {
        var clampedIndex = eventIndex % numEvents;
        if (clampedIndex < 0)
        {
            clampedIndex += numEvents;
        }

        return clampedIndex;
    }

    private int ClampIndexToTrack(int eventIndex) => ClampIndexToTrack(eventIndex, NumEvents);

    public bool HasStartOffset => StartEventOffset != 0;

    /// <summary>Gets the event at the specified index, including the start offset.</summary>
    public SyncTrack__Event GetEvent(int i) => SyncEvents[ClampIndexToTrack(i + StartEventOffset)];

    /// <summary>Gets the ID for the event at the specified index, including the start offset.</summary>
    public GlobalSymbol GetEventID(int i) => GetEvent(i).ID;

    public bool HasEventWithID(GlobalSymbol id)
    {
        foreach (var syncEvent in SyncEvents)
        {
            if (syncEvent.ID == id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the first event matching the ID; when nothing matches, the first event including offset.</summary>
    public int GetEventIndexForID(GlobalSymbol id)
    {
        for (var i = 0; i < SyncEvents.Length; i++)
        {
            if (SyncEvents[i].ID == id)
            {
                return ClampIndexToTrack(i - StartEventOffset);
            }
        }

        return StartEventOffset;
    }

    /// <summary>Gets the event matching the ID closest to the given time; when nothing matches, the first event including offset.</summary>
    public int GetClosestEventIndexForID(SyncTrackTime time, GlobalSymbol id)
    {
        var specifiedIdx = ClampIndexToTrack(time.EventIdx);

        if (SyncEvents[specifiedIdx].ID == id)
        {
            return ClampIndexToTrack(specifiedIdx - StartEventOffset);
        }

        // Search events lower than us
        var lowerFoundIdx = -1;
        var lowerDistance = 0;
        for (var i = specifiedIdx - 1; i > 0; i--)
        {
            if (SyncEvents[i].ID == id)
            {
                lowerFoundIdx = i;
                lowerDistance = specifiedIdx - i;
                break;
            }
        }

        // Search events higher than us
        var upperFoundIdx = -1;
        var upperDistance = 0;
        for (var i = specifiedIdx + 1; i < SyncEvents.Length; i++)
        {
            if (SyncEvents[i].ID == id)
            {
                upperFoundIdx = i;
                upperDistance = i - specifiedIdx;
                break;
            }
        }

        if (lowerFoundIdx != -1 && upperFoundIdx != -1)
        {
            var foundIdx = lowerDistance < upperDistance ? lowerFoundIdx : upperFoundIdx;
            return ClampIndexToTrack(foundIdx - StartEventOffset);
        }
        else if (lowerFoundIdx != -1)
        {
            return ClampIndexToTrack(lowerFoundIdx - StartEventOffset);
        }
        else if (upperFoundIdx != -1)
        {
            return ClampIndexToTrack(upperFoundIdx - StartEventOffset);
        }

        return StartEventOffset;
    }

    public SyncTrackTime GetStartTime() => new(0, 0f);
    public SyncTrackTime GetEndTime() => new(NumEvents - 1, 1f);

    /// <summary>Calculates the track time resulting from a start time advanced by a percentage delta.</summary>
    public SyncTrackTime UpdateEventTime(SyncTrackTime startTime, float deltaPercentage)
    {
        var startPercentageThrough = GetPercentageThrough(startTime, withOffset: true);
        var endPercentageThrough = startPercentageThrough + deltaPercentage;
        return GetTime(endPercentageThrough, withOffset: false);
    }

    /// <summary>Gets the sync track time for a percentage through the track, starting at the offset event.</summary>
    public SyncTrackTime GetTime(float percentage) => GetTime(percentage, withOffset: true);

    /// <summary>Gets the percentage through the clip for a sync time, starting at the offset event.</summary>
    public float GetPercentageThrough(SyncTrackTime time) => GetPercentageThrough(time, withOffset: true);

    public SyncTrackTime GetTime(float percentage, bool withOffset)
    {
        var numSyncEvents = NumEvents;

        // The end of an animation returns the last event at 100% instead of the first at 0%
        if (percentage == 1f)
        {
            return new SyncTrackTime(numSyncEvents - 1, 1f);
        }

        // Normalize the percentage into [0, 1) and count the loops
        var loopCount = (int)MathF.Floor(percentage);
        var percentageThrough = percentage - loopCount;

        int eventIdx;
        float eventPercentageThrough;

        if (percentageThrough < SyncEvents[0].StartTime.Value)
        {
            // A looping sequence, so this position lies within the last event
            eventIdx = numSyncEvents - 1;
            var lastEvent = SyncEvents[^1];
            Debug.Assert(lastEvent.Duration.Value > float.Epsilon);

            var eventDelta = SyncEvents[0].StartTime.Value - percentageThrough;
            eventPercentageThrough = (lastEvent.Duration.Value - eventDelta) / lastEvent.Duration.Value;
        }
        else
        {
            // Find the first event whose end time is greater than the playback percent
            eventIdx = numSyncEvents - 1;
            eventPercentageThrough = 1f;

            for (var syncEventIdx = 0; syncEventIdx < numSyncEvents; syncEventIdx++)
            {
                var syncEvent = SyncEvents[syncEventIdx];
                if (syncEvent.StartTime.Value + syncEvent.Duration.Value >= percentageThrough)
                {
                    Debug.Assert(syncEvent.Duration.Value > float.Epsilon);
                    eventIdx = syncEventIdx;
                    eventPercentageThrough = (percentageThrough - syncEvent.StartTime.Value) / syncEvent.Duration.Value;
                    break;
                }
            }
        }

        Debug.Assert(eventPercentageThrough >= 0f && eventPercentageThrough <= 1f);

        // Take into account loop count, then the start offset
        eventIdx += loopCount * numSyncEvents;
        var offset = withOffset ? StartEventOffset : 0;
        return new SyncTrackTime(ClampIndexToTrack(eventIdx - offset), eventPercentageThrough);
    }

    public float GetPercentageThrough(SyncTrackTime time, bool withOffset)
    {
        var offset = withOffset ? StartEventOffset : 0;
        var eventIdx = ClampIndexToTrack(time.EventIdx + offset);

        var syncEvent = SyncEvents[eventIdx];
        var percentageThroughClip = syncEvent.StartTime.Value + (syncEvent.Duration.Value * time.PercentageThrough.Value);
        Debug.Assert(percentageThroughClip >= 0f);

        if (percentageThroughClip > 1f)
        {
            // Only the last event is allowed to wrap around
            Debug.Assert(eventIdx == NumEvents - 1);
            percentageThroughClip -= MathF.Floor(percentageThroughClip);
        }

        return percentageThroughClip;
    }

    /// <summary>Gets the duration of an event as a percentage of the whole track.</summary>
    public float GetEventDuration(int eventIdx, bool withOffset = true)
    {
        Debug.Assert(eventIdx >= 0 && eventIdx < NumEvents);
        var startTime = GetPercentageThrough(new SyncTrackTime(eventIdx, 0f), withOffset);
        var endTime = GetPercentageThrough(new SyncTrackTime(eventIdx, 1f), withOffset);

        var duration = endTime < startTime
            ? 1f - startTime + endTime
            : endTime - startTime;

        Debug.Assert(duration >= 0f && duration <= 1f);
        return duration;
    }

    /// <summary>Returns the percentage of the sync track covered by the specified range.</summary>
    public float CalculatePercentageCovered(SyncTrackTime startTime, SyncTrackTime endTime)
    {
        float syncTimeDistance;

        if (startTime.EventIdx == endTime.EventIdx && startTime.PercentageThrough.Value < endTime.PercentageThrough.Value)
        {
            syncTimeDistance = endTime.PercentageThrough.Value - startTime.PercentageThrough.Value;
        }
        else
        {
            // Multi-event/looped distance: to the end of the start event, whole events between, then into the end event
            syncTimeDistance = 1f - startTime.PercentageThrough.Value;

            var eventIdx = startTime.EventIdx + 1;
            while (eventIdx != endTime.EventIdx)
            {
                if (eventIdx >= NumEvents)
                {
                    eventIdx = 0;
                }
                else
                {
                    syncTimeDistance += 1f;
                    eventIdx++;
                }
            }

            syncTimeDistance += endTime.PercentageThrough.Value;
        }

        return syncTimeDistance / NumEvents;
    }

    public float CalculatePercentageCovered(SyncTrackTimeRange range) => CalculatePercentageCovered(range.StartTime, range.EndTime);
}
