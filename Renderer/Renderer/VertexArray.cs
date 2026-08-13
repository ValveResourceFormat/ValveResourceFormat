using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Binds and deletes vertex array objects, and reports in debug builds when a shader disagrees with the
    /// VAO it draws with. Canonical locations let any shader draw any VAO, so nothing else catches an
    /// attribute the geometry does not have, or one it supplies in the wrong kind of format.
    /// </summary>
    public static class VertexArray
    {
        /// <summary>What a vertex array object supplies, as bitmasks over attribute locations.</summary>
        private record struct Supplies(int Locations, int AsInteger);

        private static readonly Dictionary<int, Supplies> SuppliesByVao = [];
        private static readonly HashSet<(int Program, int Missing, int WrongKind)> Reported = [];

        // Draws are batched by material, so the pair hardly ever changes. Without this every draw call pays
        // for two hash lookups, which a debug build feels.
        private static int lastVao = -1;
        private static int lastProgram = -1;

        /// <summary>Forgets a deleted vertex array object, whose handle OpenGL hands out again.</summary>
        [Conditional("DEBUG")]
        private static void Forget(int vao)
        {
            SuppliesByVao.Remove(vao);
            lastVao = -1;
        }

        [Conditional("DEBUG")]
        private static void Record(int vao, int location, bool integer)
        {
            ref var supplies = ref CollectionsMarshal.GetValueRefOrAddDefault(SuppliesByVao, vao, out _);

            supplies.Locations |= 1 << location;

            if (integer)
            {
                supplies.AsInteger |= 1 << location;
            }
        }

        /// <summary>Logs where <paramref name="shader"/> and <paramref name="vao"/> disagree about the
        /// attributes being drawn. Reported once per shader and problem, naming the first geometry it was
        /// seen on, because every mesh drawn with that shader hits the same one.</summary>
        /// <param name="vao">The vertex array object being drawn.</param>
        /// <param name="shader">The shader it is drawn with.</param>
        [Conditional("DEBUG")]
        public static void Validate(int vao, Shader shader)
        {
            if (vao == lastVao && shader.Program == lastProgram)
            {
                return;
            }

            lastVao = vao;
            lastProgram = shader.Program;

            if (!shader.EnsureLoaded() || !SuppliesByVao.TryGetValue(vao, out var supplies))
            {
                return;
            }

            var missing = shader.RequiredAttributes & ~supplies.Locations;

            // An integer attribute fed through the float path, or the reverse, reads undefined values. Nvidia
            // patches the shader to cope with it, which shows up as "recompiled based on GL state"
            var wrongKind = (shader.IntegerAttributes ^ supplies.AsInteger) & shader.RequiredAttributes & supplies.Locations;

            if ((missing | wrongKind) == 0 || !Reported.Add((shader.Program, missing, wrongKind)))
            {
                return;
            }

            var geometry = DescribeVertexArray(vao);

            if (missing != 0)
            {
                shader.Logger.LogDebug("{Attributes} ({ShaderName}) missing from vbib {Geometry}",
                    shader.DescribeAttributes(missing), shader.Name, geometry);
            }

            if (wrongKind != 0)
            {
                shader.Logger.LogDebug("Shader {ShaderName} declares {Attributes} as {Declared}, but {Geometry} supplies {Supplied} data",
                    shader.Name, shader.DescribeAttributes(wrongKind), (shader.IntegerAttributes & wrongKind) != 0 ? "integer" : "float",
                    geometry, (supplies.AsInteger & wrongKind) != 0 ? "integer" : "float");
            }
        }

        /// <summary>Names a vertex array object by the debug label its geometry was created with.</summary>
        private static string DescribeVertexArray(int vao)
        {
            GL.GetObjectLabel(ObjectLabelIdentifier.VertexArray, vao, 256, out _, out var label);

            return string.IsNullOrEmpty(label) ? $"vertex array {vao}" : label;
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

            Record(vao, location, integer);

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
