using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Buffers
{
    /// <summary>
    /// Base class for OpenGL buffer objects with automatic binding management.
    /// </summary>
    public abstract class Buffer
    {
        /// <summary>Gets the OpenGL buffer target type.</summary>
        public BufferTarget Target { get; }
        /// <summary>Gets the OpenGL buffer object handle.</summary>
        public int Handle { get; }
        /// <summary>Gets the shader binding point index.</summary>
        public int BindingPoint { get; }
        /// <summary>Gets the debug name for this buffer.</summary>
        public string Name { get; }

        /// <summary>Gets or sets the current size of the buffer in bytes.</summary>
        public virtual int Size { get; set; }


        /// <summary>Initializes a new buffer with the given target, binding point, and debug name.</summary>
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

        /// <summary>Binds this buffer to its binding point using <c>glBindBufferBase</c>.</summary>
        public void BindBufferBase()
        {
            GL.BindBufferBase((BufferRangeTarget)Target, BindingPoint, Handle);
        }

        /// <summary>Sets the uniform block binding in the given shader to match this buffer's binding point.</summary>
        public void SetBlockBinding(Shader shader)
        {
            var blockIndex = shader.GetUniformBlockIndex(Name);
            if (blockIndex > -1)
            {
                GL.UniformBlockBinding(shader.Program, blockIndex, BindingPoint);
            }
        }

        /// <summary>Deletes the underlying OpenGL buffer object.</summary>
        public virtual void Delete()
        {
            GL.DeleteBuffer(Handle);
        }
    }
}
