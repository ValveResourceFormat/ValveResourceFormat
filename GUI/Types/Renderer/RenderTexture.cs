using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    public class RenderTexture
    {
        public TextureTarget Target { get; }
        public int Handle { get; }

        public Texture.SpritesheetData SpritesheetData { get; set; }

        public RenderTexture(TextureTarget target, int handle)
        {
            Target = target;
            Handle = handle;
        }

        public void Bind() => GL.BindTexture(Target, Handle);
        public void Unbind() => GL.BindTexture(Target, 0);

        public static implicit operator RenderTexture(int handle)
            => FromInt32(handle);
        public static RenderTexture FromInt32(int handle)
            => new(TextureTarget.Texture2D, handle);
    }
}
