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

        /// <summary>Deletes a vertex array object.</summary>
        /// <param name="vao">The vertex array object to delete.</param>
        public static void Delete(int vao)
        {
            GL.DeleteVertexArray(vao);
            Forget(vao);
        }
    }
}
