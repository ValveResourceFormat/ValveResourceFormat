using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer.Buffers
{
    abstract class Buffer
    {
        public BufferTarget Target { get; }
        public int Handle { get; }
        public int BindingPoint { get; }
        public string Name { get; }

        public virtual int Size { get; set; }


        protected Buffer(BufferTarget target, int bindingPoint, string name)
        {
            Target = target;
            GL.CreateBuffers(1, out int handle);
            Handle = handle;
            BindingPoint = bindingPoint;
            Name = name;

#if DEBUG
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, Handle, Name.Length, Name);
#endif
        }

        public void BindBufferBase()
        {
            GL.BindBufferBase((BufferRangeTarget)Target, BindingPoint, Handle);
        }

        public void SetBlockBinding(Shader shader)
        {
            var blockIndex = shader.GetUniformBlockIndex(Name);
            if (blockIndex > -1)
            {
                GL.UniformBlockBinding(shader.Program, blockIndex, BindingPoint);
            }
        }

        public void Delete()
        {
            GL.DeleteBuffer(Handle);
        }
    }
}
