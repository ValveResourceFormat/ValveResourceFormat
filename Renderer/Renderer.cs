namespace ValveResourceFormat.Renderer;

public interface IRenderer
{
    float Uptime { get; }
    RendererContext RendererContext { get; }
}

/// <summary>
/// Base class for renderers, providing common functionality like camera, input, and timing.
/// </summary>
public abstract class Renderer : IRenderer
{
    public float Uptime { get; protected set; }
    public Camera Camera { get; protected set; }
    public UserInput Input { get; protected set; }
    public RendererContext RendererContext { get; }

    protected Renderer(RendererContext rendererContext)
    {
        RendererContext = rendererContext;
        Camera = new Camera(rendererContext);
        Input = new UserInput(this);
    }
}
