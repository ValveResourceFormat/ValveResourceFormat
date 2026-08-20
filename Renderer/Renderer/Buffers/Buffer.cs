using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Buffers
{
    /// <summary>
    /// Base class for OpenGL buffer objects with automatic binding management.
    /// </summary>
    public abstract class Buffer
    {
        /// <summary>Gets how the pipeline reads this buffer.</summary>
        public BufferType Type { get; }
        /// <summary>Gets the OpenGL buffer object handle.</summary>
        public int Handle { get; }
        /// <summary>Gets the shader binding point index.</summary>
        public int BindingPoint { get; }
        /// <summary>Gets the debug name for this buffer.</summary>
        public string Name { get; }

        /// <summary>Gets or sets the current size of the buffer in bytes.</summary>
        public virtual int Size { get; set; }

        private readonly BufferRangeTarget bindTarget;

        /// <summary>Initializes a new buffer with the given type, binding point, and debug name,
        /// created on the device current on the calling thread.</summary>
        /// <param name="type">How the pipeline reads this buffer.</param>
        /// <param name="bindingPoint">The shader binding point index.</param>
        /// <param name="name">Debug name for the buffer.</param>
        protected Buffer(BufferType type, int bindingPoint, string name)
        {
            Type = type;
            bindTarget = (BufferRangeTarget)type.ToGLBufferTarget();
            Handle = GraphicsDevice.Current.CreateBuffer(name);
            BindingPoint = bindingPoint;
            Name = name;
        }

        /// <summary>Binds this buffer to its binding point using <c>glBindBufferBase</c>.</summary>
        public void BindBufferBase()
        {
            GL.BindBufferBase(bindTarget, BindingPoint, Handle);
        }

        /// <summary>Binds this buffer to a binding point other than its own. Binding one buffer to several
        /// points at once is allowed; all of the blocks reading it are declared <c>readonly</c>.</summary>
        /// <param name="bindingPoint">The slot to bind to instead of <see cref="BindingPoint"/>.</param>
        public void BindBufferBase(ReservedBufferSlots bindingPoint)
        {
            GL.BindBufferBase(bindTarget, (int)bindingPoint, Handle);
        }

        /// <summary>Deletes the underlying OpenGL buffer object.</summary>
        public virtual void Delete()
        {
            GL.DeleteBuffer(Handle);
        }
    }
}
