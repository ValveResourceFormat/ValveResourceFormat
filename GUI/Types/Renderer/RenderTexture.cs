using System.Diagnostics;
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

        // todo: bindless parameters TexParameter -> TextureParameter
        public void SetWrapMode(TextureWrapMode wrap)
        {
            Assert_IsBound();
            GL.TexParameter(Target, TextureParameterName.TextureWrapS, (int)wrap);

            if (Height > 1)
            {
                GL.TexParameter(Target, TextureParameterName.TextureWrapT, (int)wrap);
            }

            if (Depth > 1)
            {
                GL.TexParameter(Target, TextureParameterName.TextureWrapR, (int)wrap);
            }
        }

        public void SetFiltering(TextureMinFilter min, TextureMagFilter mag)
        {
            Assert_IsBound();
            GL.TexParameter(Target, TextureParameterName.TextureMinFilter, (int)min);
            GL.TexParameter(Target, TextureParameterName.TextureMagFilter, (int)mag);
        }

#if DEBUG
        private void Assert_IsBound()
        {
            // no GetPName for this
            if (Target == TextureTarget.TextureCubeMapArray)
            {
                return;
            }

            var current = GL.GetInteger(Target switch
            {
                TextureTarget.Texture2D => GetPName.TextureBinding2D,
                TextureTarget.Texture2DArray => GetPName.TextureBinding2DArray,
                TextureTarget.TextureCubeMap => GetPName.TextureBindingCubeMap,
                TextureTarget.TextureCubeMapArray => GetPName.TextureBindingCubeMap,
                TextureTarget.Texture2DMultisample => GetPName.TextureBinding2DMultisample,
                _ => GetPName.TextureBinding2D,
            });

            Debug.Assert(current == Handle, $"Texture {Handle} is not bound, current is {current}");
        }
#else
        private static void Assert_IsBound()
        {
            // noop
        }
#endif
    }
}
