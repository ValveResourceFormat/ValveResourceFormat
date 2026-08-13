using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Binds and deletes vertex array objects. Canonical locations let any shader draw any VAO, so nothing
    /// else catches a mismatch between the two. Debug builds check it here.
    /// </summary>
    public static class VertexArray
    {
        /// <summary>What a VAO supplies: location bitmasks, and the names when the geometry knows them.</summary>
        private record struct Supplies(int Locations, int AsInteger, string[]? Names);

        private static readonly Dictionary<int, Supplies> SuppliesByVao = [];
        private static readonly HashSet<(int Program, int Missing, int WrongKind)> Reported = [];

        // Every GL context has its own render loop thread, and they all create geometry through here
        private static readonly Lock StateLock = new();

        // Draws are batched by material, so this pair rarely changes. Without it every draw costs two hash lookups.
        private static int lastVao = -1;
        private static int lastProgram = -1;

        /// <summary>Forgets a deleted VAO, whose handle OpenGL hands out again.</summary>
        [Conditional("DEBUG")]
        private static void Forget(int vao)
        {
            lock (StateLock)
            {
                SuppliesByVao.Remove(vao);
                lastVao = -1;
            }
        }

        /// <summary>Starts recording a newly created VAO. The handle may have belonged to a deleted one.
        /// Handbuilt geometry also passes the names it declares, which locate its custom attributes.</summary>
        [Conditional("DEBUG")]
        internal static void StartRecording(int vao, string[]? names = null)
        {
            lock (StateLock)
            {
                SuppliesByVao[vao] = new Supplies(0, 0, names);
                lastVao = -1;
            }
        }

        [Conditional("DEBUG")]
        private static void Record(int vao, int location, bool integer)
        {
            lock (StateLock)
            {
                ref var supplies = ref CollectionsMarshal.GetValueRefOrAddDefault(SuppliesByVao, vao, out _);

                supplies.Locations |= 1 << location;

                if (integer)
                {
                    supplies.AsInteger |= 1 << location;
                }
            }
        }

        /// <summary>Reports a shader and VAO that disagree about attributes. Once per shader and problem,
        /// since every mesh drawn with that shader repeats it.</summary>
        [Conditional("DEBUG")]
        public static void Validate(int vao, Shader shader)
        {
            Supplies supplies;

            lock (StateLock)
            {
                if (vao == lastVao && shader.Program == lastProgram)
                {
                    return;
                }

                lastVao = vao;
                lastProgram = shader.Program;

                if (!SuppliesByVao.TryGetValue(vao, out supplies))
                {
                    return;
                }
            }

            if (!shader.EnsureLoaded())
            {
                return;
            }

            var missing = shader.RequiredAttributes & ~supplies.Locations;

            // Undefined per spec, and Nvidia patches the shader for it: "recompiled based on GL state"
            var wrongKind = (shader.IntegerAttributes ^ supplies.AsInteger) & shader.RequiredAttributes & supplies.Locations;

            if ((missing | wrongKind) == 0)
            {
                return;
            }

            lock (StateLock)
            {
                if (!Reported.Add((shader.Program, missing, wrongKind)))
                {
                    return;
                }
            }

            var geometry = DescribeVertexArray(vao);

            if (missing != 0)
            {
                shader.Logger.LogDebug("{Attributes} ({ShaderName}) missing from mesh {Geometry}",
                    shader.DescribeAttributes(missing), shader.Name, geometry);

                // A name the geometry does not know shifts the slots of the custom attributes sorted after
                // it, so the other locations are wrong too, not just this one
                foreach (var name in shader.DescribeAttributes(missing).Split(", "))
                {
                    if (supplies.Names != null && Array.IndexOf(supplies.Names, name) < 0)
                    {
                        shader.Logger.LogDebug("{Geometry} declares {Names}, so every custom attribute after {Attribute} sits at a different location than {ShaderName} expects",
                            geometry, string.Join(", ", supplies.Names), name, shader.Name);
                    }
                }
            }

            if (wrongKind != 0)
            {
                shader.Logger.LogDebug("Shader {ShaderName} declares {Attributes} as {Declared}, but {Geometry} supplies {Supplied} data",
                    shader.Name, shader.DescribeAttributes(wrongKind), (shader.IntegerAttributes & wrongKind) != 0 ? "integer" : "float",
                    geometry, (supplies.AsInteger & wrongKind) != 0 ? "integer" : "float");
            }
        }

        /// <summary>Names a VAO by the debug label of its geometry.</summary>
        private static string DescribeVertexArray(int vao)
        {
            GL.GetObjectLabel(ObjectLabelIdentifier.VertexArray, vao, 256, out _, out var label);

            return string.IsNullOrEmpty(label) ? $"vertex array {vao}" : label;
        }

        /// <summary>Binds a VAO to draw with a shader.</summary>
        public static void Bind(int vao, Shader shader)
        {
            Validate(vao, shader);
            GL.BindVertexArray(vao);
        }

        /// <summary>Sets one attribute's format, for both the handbuilt and the game mesh paths.</summary>
        public static void SetAttribFormat(int vao, int location, DXGI_FORMAT format, int offset)
        {
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

        /// <summary>Deletes a VAO.</summary>
        public static void Delete(int vao)
        {
            GL.DeleteVertexArray(vao);
            Forget(vao);
        }
    }
}
