using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;

#nullable disable

namespace GUI.Types.Renderer
{
    class RenderTexture //: IDisposable
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
            GL.CreateTextures(target, 1, out int handle);
            Handle = handle;
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

        public void SetWrapMode(TextureWrapMode wrap)
        {
            GL.TextureParameter(Handle, TextureParameterName.TextureWrapS, (int)wrap);

            if (Height > 1)
            {
                GL.TextureParameter(Handle, TextureParameterName.TextureWrapT, (int)wrap);
            }

            if (Depth > 1)
            {
                GL.TextureParameter(Handle, TextureParameterName.TextureWrapR, (int)wrap);
            }
        }

        public void SetFiltering(TextureMinFilter min, TextureMagFilter mag)
        {
            GL.TextureParameter(Handle, TextureParameterName.TextureMinFilter, (int)min);
            GL.TextureParameter(Handle, TextureParameterName.TextureMagFilter, (int)mag);
        }

        public void Dispose()
        {
            GL.DeleteTexture(Handle);
        }
    }
}
