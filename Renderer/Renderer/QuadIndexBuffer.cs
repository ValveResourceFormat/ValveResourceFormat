namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Pre-computed index buffer for rendering quads as triangle pairs.
    /// </summary>
    public class QuadIndexBuffer
    {
        /// <summary>Gets the OpenGL buffer object handle.</summary>
        public int GLHandle { get; }

        /// <summary>Allocates the buffer and fills it with quad-to-triangle index patterns.</summary>
        /// <param name="size">Total number of indices to generate (must be a multiple of 6).</param>
        public QuadIndexBuffer(int size)
        {
            System.Diagnostics.Debug.Assert(size % 6 == 0);

            using var rented = new RentedBuffer<ushort>(size);
            var indices = rented.Span;

            for (var i = 0; i < size / 6; ++i)
            {
                indices[(i * 6) + 0] = (ushort)((i * 4) + 0);
                indices[(i * 6) + 1] = (ushort)((i * 4) + 1);
                indices[(i * 6) + 2] = (ushort)((i * 4) + 2);
                indices[(i * 6) + 3] = (ushort)((i * 4) + 0);
                indices[(i * 6) + 4] = (ushort)((i * 4) + 2);
                indices[(i * 6) + 5] = (ushort)((i * 4) + 3);
            }

            GLHandle = GraphicsDevice.CreateBuffer<ushort>(nameof(QuadIndexBuffer), indices, BufferUsage.Static);
        }
    }
}
