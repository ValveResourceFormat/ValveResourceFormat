using ValveResourceFormat.ResourceTypes.Choreo;

namespace ValveResourceFormat.Renderer.Choreo
{
    /// <summary>
    /// A place that holds the choreography scenes of a game.
    /// </summary>
    public interface IChoreoSceneSource
    {
        /// <summary>Reads the scenes.</summary>
        /// <returns>Every scene of this source.</returns>
        IEnumerable<ChoreoScene> EnumerateScenes();
    }
}
