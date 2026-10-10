using System.Diagnostics;

namespace ValveResourceFormat.Renderer
{
    /// <summary>A clip an animation graph update sampled, see <see cref="AnimationGraph.SampledClips"/>.</summary>
    /// <param name="Name">The clip resource name.</param>
    /// <param name="Time">The time sampled, in seconds.</param>
    /// <param name="Duration">The clip duration, in seconds.</param>
    public readonly record struct SampledClip(string Name, float Time, float Duration);

    /// <summary>How long a graph took to evaluate in an update, see <see cref="AnimationGraph.GraphTimings"/>.</summary>
    /// <param name="GraphName">The display name of the graph.</param>
    /// <param name="Depth">How deeply the graph is referenced, zero for the graph updated.</param>
    /// <param name="Duration">The evaluation time, including the graphs it references.</param>
    public readonly record struct GraphTiming(string GraphName, int Depth, TimeSpan Duration);

    /// <summary>What an update records while <see cref="AnimationGraph.RecordUpdateDetails"/> is set.</summary>
    internal sealed class GraphUpdateDetails
    {
        public List<SampledClip> SampledClips { get; } = new(32);
        public List<GraphTiming> GraphTimings { get; } = new(32);
        private readonly List<long> timingStarts = new(32);

        public void Clear()
        {
            SampledClips.Clear();
            GraphTimings.Clear();
            timingStarts.Clear();
        }

        /// <summary>Starts timing a graph evaluation, returning the entry to finish.</summary>
        public int BeginTiming(string graphName, int depth)
        {
            GraphTimings.Add(new GraphTiming(graphName, depth, TimeSpan.Zero));
            timingStarts.Add(Stopwatch.GetTimestamp());
            return GraphTimings.Count - 1;
        }

        public void EndTiming(int index)
        {
            GraphTimings[index] = GraphTimings[index] with { Duration = Stopwatch.GetElapsedTime(timingStarts[index]) };
        }
    }
}
