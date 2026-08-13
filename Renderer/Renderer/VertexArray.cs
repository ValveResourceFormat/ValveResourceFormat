using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Binds and deletes vertex array objects, and checks in debug builds that a shader only reads attributes
    /// the VAO it draws with binds. Canonical locations let any shader draw any VAO, which also means a shader
    /// reading an attribute the geometry does not have silently gets the generic value (0, 0, 0, 1).
    /// </summary>
    public static class VertexArray
    {
        private static readonly Dictionary<int, int> BoundByVao = [];
        private static readonly HashSet<(int Vao, int Program)> Validated = [];

        /// <summary>Records which locations a newly created vertex array object binds.</summary>
        /// <param name="vao">The vertex array object.</param>
        /// <param name="boundLocations">Bitmask of the locations it binds.</param>
        [Conditional("DEBUG")]
        public static void Record(int vao, int boundLocations) => BoundByVao[vao] = boundLocations;

        /// <summary>Forgets a deleted vertex array object, whose handle OpenGL hands out again.</summary>
        [Conditional("DEBUG")]
        private static void Forget(int vao)
        {
            BoundByVao.Remove(vao);
            Validated.RemoveWhere(pair => pair.Vao == vao);
        }

        /// <summary>Asserts that <paramref name="shader"/> reads nothing <paramref name="vao"/> leaves
        /// unbound. Each shader and VAO pair is checked once.</summary>
        /// <param name="vao">The vertex array object being drawn.</param>
        /// <param name="shader">The shader it is drawn with.</param>
        [Conditional("DEBUG")]
        public static void Validate(int vao, Shader shader)
        {
#if DEBUG
            if (!shader.EnsureLoaded()
            || !BoundByVao.TryGetValue(vao, out var boundLocations)
            || !Validated.Add((vao, shader.Program)))
            {
                return;
            }

            var missing = shader.RequiredAttributes & ~boundLocations;

            Debug.Assert(missing == 0,
                $"Shader '{shader.Name}' reads {VertexAttributeLocations.DescribeMask(missing)}, which the geometry it draws does not bind.");
#endif
        }

        /// <summary>Binds a vertex array object for drawing with a shader.</summary>
        /// <param name="vao">The vertex array object to bind.</param>
        /// <param name="shader">The shader it is drawn with.</param>
        public static void Bind(int vao, Shader shader)
        {
            Validate(vao, shader);
            GL.BindVertexArray(vao);
        }

        /// <summary>
        /// Sets one VAO attribute's data format, mapping the <see cref="DXGI_FORMAT"/> to the matching float
        /// or integer GL attribute format. Shared by handbuilt formats and the game mesh path.
        /// </summary>
        /// <param name="vao">The OpenGL VAO handle.</param>
        /// <param name="location">Attribute location.</param>
        /// <param name="format">Data format of the attribute.</param>
        /// <param name="offset">Byte offset of the attribute within a vertex.</param>
        public static void SetAttribFormat(int vao, int location, DXGI_FORMAT format, int offset)
        {
            // Integer attributes take the I variant, which has no normalized flag
            var (count, type, normalized, integer) = format switch
            {
                DXGI_FORMAT.R32_FLOAT => (1, VertexAttribType.Float, false, false),
                DXGI_FORMAT.R32G32_FLOAT => (2, VertexAttribType.Float, false, false),
                DXGI_FORMAT.R32G32B32_FLOAT => (3, VertexAttribType.Float, false, false),
                DXGI_FORMAT.R32G32B32A32_FLOAT => (4, VertexAttribType.Float, false, false),
                DXGI_FORMAT.R16G16_FLOAT => (2, VertexAttribType.HalfFloat, false, false),
                DXGI_FORMAT.R16G16B16A16_FLOAT => (4, VertexAttribType.HalfFloat, false, false),

                DXGI_FORMAT.R8G8B8A8_UNORM => (4, VertexAttribType.UnsignedByte, true, false),
                DXGI_FORMAT.R16G16_UNORM => (2, VertexAttribType.UnsignedShort, true, false),
                DXGI_FORMAT.R16G16B16A16_UNORM => (4, VertexAttribType.UnsignedShort, true, false),
                DXGI_FORMAT.R16G16_SNORM => (2, VertexAttribType.Short, true, false),

                DXGI_FORMAT.R32_UINT => (1, VertexAttribType.UnsignedInt, false, true),
                DXGI_FORMAT.R8G8B8A8_UINT => (4, VertexAttribType.UnsignedByte, false, true),
                DXGI_FORMAT.R16G16B16A16_UINT => (4, VertexAttribType.UnsignedShort, false, true),
                DXGI_FORMAT.R16G16_SINT => (2, VertexAttribType.Short, false, true),
                DXGI_FORMAT.R16G16B16A16_SINT => (4, VertexAttribType.Short, false, true),
                DXGI_FORMAT.R32G32B32A32_SINT => (4, VertexAttribType.Int, false, true),

                // :VertexAttributeFormat - When adding new attribute here, also implement it in the VBIB code
                _ => throw new NotImplementedException($"Unknown vertex attribute format {format} (location {location})"),
            };

            if (integer)
            {
                GL.VertexArrayAttribIFormat(vao, location, count, type, offset);
            }
            else
            {
                GL.VertexArrayAttribFormat(vao, location, count, type, normalized, offset);
            }
        }

        /// <summary>Deletes a vertex array object.</summary>
        /// <param name="vao">The vertex array object to delete.</param>
        public static void Delete(int vao)
        {
            GL.DeleteVertexArray(vao);
            Forget(vao);
        }
    }
}
