using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Marks a vertex struct field as a shader vertex attribute, giving its location and shader input
    /// name.
    ///
    /// Use <see cref="VertexFormat.FromStruct{TVertex}"/> to automatically derive field types, offsets and stride from the struct.
    /// </summary>
    /// <param name="location">Attribute location, matching the shader's <c>layout (location = N)</c>.</param>
    /// <param name="name">Shader input name, e.g. <c>aVertexPosition</c>.</param>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class VertexAttributeAttribute(int location, string name) : Attribute
    {
        /// <summary>Gets the attribute location, matching the shader's <c>layout (location = N)</c>.</summary>
        public int Location { get; } = location;

        /// <summary>Gets the shader input name.</summary>
        public string Name { get; } = name;
    }

    /// <summary>
    /// One attribute of a <see cref="VertexFormat"/>.
    /// </summary>
    /// <param name="Location">Attribute location, matching the shader's <c>layout (location = N)</c>.</param>
    /// <param name="Name">Shader input name, e.g. <c>aVertexPosition</c>.</param>
    /// <param name="Format">Data format of the attribute in the vertex buffer, in the same
    /// <see cref="DXGI_FORMAT"/> vocabulary game mesh layouts use.</param>
    /// <param name="OffsetInBytes">Explicit byte offset within a vertex, or -1 to pack in order.</param>
    public readonly record struct VertexAttribute(int Location, string Name, DXGI_FORMAT Format, int OffsetInBytes = -1);

    /// <summary>
    /// Describes the interleaved vertex layout of handbuilt geometry or special renderers.
    ///
    /// Each element carries an explicit location that the shader <c>layout (location = N)</c> repeats, this match is checked
    /// in debug build.
    ///
    /// Element order is buffer order, offsets pack in declaration order unless given explicitly.
    ///
    /// Mesh geometry with arbitrary gamedata layouts instead resolves semantic names through
    /// <see cref="VertexAttributeLocations"/> in <see cref="GPUMeshBufferCache"/>.
    /// 
    /// </summary>
    public sealed class VertexFormat
    {
        private readonly VertexAttribute[] elements;
        private readonly int[] offsets;

        /// <summary>Gets the size in bytes of a single vertex.</summary>
        public int Stride { get; }

        /// <summary>Initializes a vertex format.</summary>
        /// <param name="stride">Size in bytes of a single vertex.</param>
        /// <param name="elements">The vertex attributes, in buffer order.</param>
        public VertexFormat(int stride, params VertexAttribute[] elements)
        {
            this.elements = elements;
            offsets = new int[elements.Length];
            Stride = stride;

            var packedOffset = 0;
            var usedLocations = 0;

            for (var i = 0; i < elements.Length; i++)
            {
                var element = elements[i];
                var (elementSize, elementCount) = VBIB.GetFormatInfo(element.Format, element.Name);

                offsets[i] = element.OffsetInBytes >= 0 ? element.OffsetInBytes : packedOffset;
                packedOffset = offsets[i] + (elementSize * elementCount);

                Debug.Assert(packedOffset <= stride, $"Attribute '{element.Name}' ends at byte {packedOffset}, past the vertex stride of {stride}.");
                Debug.Assert((usedLocations & (1 << element.Location)) == 0, $"Vertex format uses location {element.Location} twice.");
                usedLocations |= 1 << element.Location;
            }
        }

        /// <summary>
        ///
        /// Builds the format of a vertex struct whose fields carry <see cref="VertexAttributeAttribute"/>.
        ///
        /// This is the recommended way to build vertex formats, as it makes the struct be the single source of truth.
        /// </summary>
        /// <typeparam name="TVertex">The vertex struct.</typeparam>
        /// <returns>The vertex format.</returns>
        public static VertexFormat FromStruct<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] TVertex>() where TVertex : struct
        {
            var elements = new List<VertexAttribute>();

            foreach (var field in typeof(TVertex).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.GetCustomAttribute<VertexAttributeAttribute>() is not { } attribute)
                {
                    continue;
                }

                elements.Add(new VertexAttribute(attribute.Location, attribute.Name, FormatForType(field.FieldType), (int)Marshal.OffsetOf<TVertex>(field.Name)));
            }

            return new VertexFormat(Marshal.SizeOf<TVertex>(), [.. elements]);
        }

        private static DXGI_FORMAT FormatForType(Type fieldType)
        {
            if (fieldType == typeof(float))
            {
                return DXGI_FORMAT.R32_FLOAT;
            }

            if (fieldType == typeof(Vector2))
            {
                return DXGI_FORMAT.R32G32_FLOAT;
            }

            if (fieldType == typeof(Vector3))
            {
                return DXGI_FORMAT.R32G32B32_FLOAT;
            }

            if (fieldType == typeof(Vector4))
            {
                return DXGI_FORMAT.R32G32B32A32_FLOAT;
            }

            if (fieldType == typeof(Color32))
            {
                return DXGI_FORMAT.R8G8B8A8_UNORM;
            }

            if (fieldType == typeof(uint))
            {
                return DXGI_FORMAT.R32_UINT;
            }

            throw new NotImplementedException($"No vertex attribute format mapping for field type {fieldType.Name}");
        }

        /// <summary>
        ///
        /// Creates a VAO binding one interleaved vertex buffer with this format.
        ///
        /// Debug builds verify that <paramref name="shader"/> inputs match this format by name and location.</summary>
        /// 
        /// <param name="debugLabel">Label applied to the VAO in debug builds.</param>
        /// <param name="shader">The shader family this format's geometry is drawn with.</param>
        /// <param name="vertexBuffer">OpenGL handle of the vertex buffer.</param>
        /// <param name="indexBuffer">OpenGL handle of the index buffer, or 0 for non-indexed geometry.</param>
        /// <returns>The OpenGL VAO handle.</returns>
        public int CreateVertexArray(string? debugLabel, Shader shader, int vertexBuffer, int indexBuffer = 0)
        {
            ValidateMatches(shader);

            GL.CreateVertexArrays(1, out int vao);

            if (indexBuffer != 0)
            {
                GL.VertexArrayElementBuffer(vao, indexBuffer);
            }

            GL.VertexArrayVertexBuffer(vao, 0, vertexBuffer, 0, Stride);

            for (var i = 0; i < elements.Length; i++)
            {
                var element = elements[i];

                GL.EnableVertexArrayAttrib(vao, element.Location);
                GL.VertexArrayAttribBinding(vao, element.Location, 0);
                SetVertexArrayAttribFormat(vao, element.Location, element.Format, offsets[i]);
            }

#if DEBUG
            if (debugLabel != null)
            {
                GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vao, Math.Min(GLEnvironment.MaxLabelLength, debugLabel.Length), debugLabel);
            }
#endif

            return vao;
        }

        /// <summary>
        ///
        /// Debug check that every attribute the shader actually reads exists in this format
        /// with the same name and location.
        ///
        /// Attributes the driver eliminated (e.g. behind a disabled combo) are simply absent from the active list and stay unchecked.</summary>
        /// <param name="shader">The linked shader this format's geometry is drawn with.</param>
        [Conditional("DEBUG")]
        private void ValidateMatches(Shader shader)
        {
            if (!shader.EnsureLoaded())
            {
                return;
            }

            GL.GetProgram(shader.Program, GetProgramParameterName.ActiveAttributes, out var attributeCount);

            for (var i = 0; i < attributeCount; i++)
            {
                GL.GetActiveAttrib(shader.Program, i, 64, out _, out _, out _, out var name);

                if (name.StartsWith("gl_", StringComparison.Ordinal))
                {
                    continue;
                }

                var location = GL.GetAttribLocation(shader.Program, name);
                var index = Array.FindIndex(elements, e => e.Name == name);

                Debug.Assert(index >= 0,
                    $"Shader '{shader.Name}' reads attribute '{name}' which is not an element of the vertex format.");
                Debug.Assert(index < 0 || elements[index].Location == location,
                    $"Shader '{shader.Name}' attribute '{name}' is at location {location}, but the vertex format declares it at {elements[index].Location}.");
            }
        }

        /// <summary>
        ///
        /// Sets one VAO attribute's data format, mapping the <see cref="DXGI_FORMAT"/> to the appropriate float or integer GL attribute format.
        ///
        /// Shared between handbuilt formats and the gamemesh path in <see cref="GPUMeshBufferCache"/>.</summary>
        /// 
        /// <param name="vao">The OpenGL VAO handle.</param>
        /// <param name="location">Attribute location.</param>
        /// <param name="format">Data format of the attribute.</param>
        /// <param name="offset">Byte offset of the attribute within a vertex.</param>
        internal static void SetVertexArrayAttribFormat(int vao, int location, DXGI_FORMAT format, int offset)
        {
            switch (format)
            {
                case DXGI_FORMAT.R32G32B32_FLOAT:
                    GL.VertexArrayAttribFormat(vao, location, 3, VertexAttribType.Float, false, offset);
                    break;

                case DXGI_FORMAT.R8G8B8A8_UNORM:
                    GL.VertexArrayAttribFormat(vao, location, 4, VertexAttribType.UnsignedByte, true, offset);
                    break;

                case DXGI_FORMAT.R32_FLOAT:
                    GL.VertexArrayAttribFormat(vao, location, 1, VertexAttribType.Float, false, offset);
                    break;

                case DXGI_FORMAT.R32G32_FLOAT:
                    GL.VertexArrayAttribFormat(vao, location, 2, VertexAttribType.Float, false, offset);
                    break;

                case DXGI_FORMAT.R16G16_FLOAT:
                    GL.VertexArrayAttribFormat(vao, location, 2, VertexAttribType.HalfFloat, false, offset);
                    break;

                case DXGI_FORMAT.R32G32B32A32_FLOAT:
                    GL.VertexArrayAttribFormat(vao, location, 4, VertexAttribType.Float, false, offset);
                    break;

                case DXGI_FORMAT.R32G32B32A32_SINT:
                    GL.VertexArrayAttribIFormat(vao, location, 4, VertexAttribType.Int, offset);
                    break;

                case DXGI_FORMAT.R8G8B8A8_UINT:
                    GL.VertexArrayAttribIFormat(vao, location, 4, VertexAttribType.UnsignedByte, offset);
                    break;

                case DXGI_FORMAT.R16G16_SINT:
                    GL.VertexArrayAttribIFormat(vao, location, 2, VertexAttribType.Short, offset);
                    break;

                case DXGI_FORMAT.R16G16B16A16_SINT:
                    GL.VertexArrayAttribIFormat(vao, location, 4, VertexAttribType.Short, offset);
                    break;

                case DXGI_FORMAT.R16G16B16A16_UINT:
                    GL.VertexArrayAttribIFormat(vao, location, 4, VertexAttribType.UnsignedShort, offset);
                    break;

                case DXGI_FORMAT.R16G16B16A16_UNORM:
                    GL.VertexArrayAttribFormat(vao, location, 4, VertexAttribType.UnsignedShort, true, offset);
                    break;

                case DXGI_FORMAT.R16G16B16A16_FLOAT:
                    GL.VertexArrayAttribFormat(vao, location, 4, VertexAttribType.HalfFloat, false, offset);
                    break;

                case DXGI_FORMAT.R16G16_SNORM:
                    GL.VertexArrayAttribFormat(vao, location, 2, VertexAttribType.Short, true, offset);
                    break;

                case DXGI_FORMAT.R16G16_UNORM:
                    GL.VertexArrayAttribFormat(vao, location, 2, VertexAttribType.UnsignedShort, true, offset);
                    break;

                case DXGI_FORMAT.R32_UINT:
                    GL.VertexArrayAttribIFormat(vao, location, 1, VertexAttribType.UnsignedInt, offset);
                    break;

                // :VertexAttributeFormat - When adding new attribute here, also implement it in the VBIB code
                default:
                    throw new NotImplementedException($"Unknown vertex attribute format {format} (location {location})");
            }
        }

    }
}
