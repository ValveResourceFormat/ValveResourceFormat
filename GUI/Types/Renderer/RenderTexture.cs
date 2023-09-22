using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    class RenderTexture
    {
        public TextureTarget Target { get; }
        public int Handle { get; }

        public Texture.SpritesheetData SpriteSheetData { get; }

        public int Width { get; }
        public int Height { get; }
        public int Depth { get; }
        public int NumMipLevels { get; }

        RenderTexture(TextureTarget target)
        {
            Target = target;
            Handle = GL.GenTexture();
        }

        public RenderTexture(TextureTarget target, Texture data) : this(target)
        {
            Width = data.Width;
            Height = data.Height;
            Depth = data.Depth;
            NumMipLevels = data.NumMipLevels;
            SpriteSheetData = data.GetSpriteSheetData();
        }

        public RenderTexture(TextureTarget target, int width, int height, int depth, int mipcount)
            : this(target)
        {
            Width = width;
            Height = height;
            Depth = depth;
            NumMipLevels = mipcount;
        }

        public void Bind() => GL.BindTexture(Target, Handle);
        public void Unbind() => GL.BindTexture(Target, 0);
        public BindingContext BindingContext() => new(Bind, Unbind);
    }
}
