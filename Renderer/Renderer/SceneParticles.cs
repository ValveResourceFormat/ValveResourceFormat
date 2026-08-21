using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// The particle effects in a scene, run together across the thread pool once every node has placed
/// itself. Simulating an effect reaches nothing outside it: its control points, its particles and the
/// light each of its renderers owns one of. Anything that does reach out asks rather than acts, the way
/// <see cref="Audio.SoundEventPlayer.PlayQueued"/> takes a sound the game thread starts afterwards.
/// </summary>
public sealed class SceneParticles : IParallelWork, IDisposable
{
    /// <summary>
    /// Gets or sets whether the effects simulate across the thread pool. Off runs the same effects in
    /// the same order on the calling thread, so the two are worth comparing in one session.
    /// </summary>
    public bool UseThreadPool { get; set; } = true;

    private readonly List<ParticleSceneNode> nodes = [];
    private readonly ParallelDispatch dispatch = new();
    private Scene.UpdateContext context;

    internal void Add(SceneNode node)
    {
        if (node is ParticleSceneNode particle)
        {
            nodes.Add(particle);
        }
    }

    internal void Remove(SceneNode node)
    {
        if (node is ParticleSceneNode particle)
        {
            nodes.Remove(particle);
        }
    }

    internal void Clear() => nodes.Clear();

    /// <summary>
    /// Runs every effect for this frame. The nodes have been placed by their own updates before this,
    /// so an effect bound to another node reads a transform that has already settled.
    /// </summary>
    internal void Simulate(Scene.UpdateContext updateContext)
    {
        if (!UseThreadPool)
        {
            foreach (var particle in nodes)
            {
                particle.Simulate(updateContext);
            }

            return;
        }

        // Published to the workers by the dispatch, which runs counts too small to fan out inline
        context = updateContext;

        dispatch.Run(this, nodes.Count);
    }

    void IParallelWork.Execute(int index) => nodes[index].Simulate(context);

    /// <inheritdoc/>
    public void Dispose() => dispatch.Dispose();
}
