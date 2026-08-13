using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Marks a vertex struct field as the shader input bound to a <see cref="VertexAttributeSlot"/>. The
    /// buffer format follows from the field type, see <see cref="VertexFormat.FromStruct{TVertex}"/>.
    /// </summary>
    /// <param name="slot">The attribute this field feeds.</param>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class VertexAttributeAttribute(VertexAttributeSlot slot) : Attribute
    {
        /// <summary>Gets the attribute this field feeds.</summary>
        public VertexAttributeSlot Slot { get; } = slot;
    }

    /// <summary>
    /// One attribute of a <see cref="VertexFormat"/>.
    /// </summary>
    /// <param name="Slot">The attribute this element feeds, which is also its shader location.</param>
    /// <param name="Format">Data format in the vertex buffer, in the <see cref="DXGI_FORMAT"/> vocabulary
    /// game mesh layouts use.</param>
    /// <param name="OffsetInBytes">Explicit byte offset within a vertex, or -1 to pack in order.</param>
    public readonly record struct VertexAttribute(VertexAttributeSlot Slot, DXGI_FORMAT Format, int OffsetInBytes = -1);

    /// <summary>
    /// Interleaved vertex layout of handbuilt geometry. Element order is buffer order, and offsets pack in
    /// declaration order unless given explicitly. Game meshes instead resolve their buffer semantics through
    /// <see cref="VertexAttributeLocations"/> in <see cref="GPUMeshBufferCache"/>.
    /// </summary>
    public sealed class VertexFormat
    {
        private readonly VertexAttribute[] elements;
        private readonly int[] offsets;

        /// <summary>Gets the size in bytes of a single vertex.</summary>
        public int Stride { get; }

        /// <summary>Gets the bitmask of attribute locations this format binds.</summary>
        public int BoundLocations { get; }

        /// <summary>Initializes a vertex format.</summary>
        /// <param name="stride">Size in bytes of a single vertex.</param>
        /// <param name="elements">The vertex attributes, in buffer order.</param>
        public VertexFormat(int stride, params VertexAttribute[] elements)
        {
            this.elements = elements;
            offsets = new int[elements.Length];
            Stride = stride;

            var packedOffset = 0;

            for (var i = 0; i < elements.Length; i++)
            {
                var element = elements[i];
                var (elementSize, elementCount) = VBIB.GetFormatInfo(element.Format, element.Slot.ToString());

                offsets[i] = element.OffsetInBytes >= 0 ? element.OffsetInBytes : packedOffset;
                packedOffset = offsets[i] + (elementSize * elementCount);

                Debug.Assert(packedOffset <= stride, $"Attribute '{element.Slot}' ends at byte {packedOffset}, past the vertex stride of {stride}.");
                Debug.Assert((BoundLocations & (1 << (int)element.Slot)) == 0, $"Vertex format binds the slot of '{element.Slot}' twice.");
                BoundLocations |= 1 << (int)element.Slot;
            }
        }

        /// <summary>
        /// Builds the format of a vertex struct whose fields carry <see cref="VertexAttributeAttribute"/>,
        /// deriving formats, offsets and stride from the struct itself.
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

                elements.Add(new VertexAttribute(attribute.Slot, FormatForType(field.FieldType), (int)Marshal.OffsetOf<TVertex>(field.Name)));
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

        /// <summary>Describes this format as vertex buffer input layout fields, for geometry uploaded as a
        /// <see cref="VBIB"/> through <see cref="GPUMeshBufferCache"/>.</summary>
        /// <returns>The input layout fields, in buffer order.</returns>
        public VBIB.RenderInputLayoutField[] ToInputLayout()
        {
            var fields = new VBIB.RenderInputLayoutField[elements.Length];

            for (var i = 0; i < elements.Length; i++)
            {
                var (name, index) = VertexAttributeLocations.GetSemantic(elements[i].Slot);
                fields[i] = new VBIB.RenderInputLayoutField(name, elements[i].Format, (uint)offsets[i]) { SemanticIndex = index };
            }

            return fields;
        }

        /// <summary>Creates a VAO binding one interleaved vertex buffer with this format.</summary>
        /// <param name="debugLabel">Label applied to the VAO in debug builds.</param>
        /// <param name="vertexBuffer">OpenGL handle of the vertex buffer.</param>
        /// <param name="indexBuffer">OpenGL handle of the index buffer, or 0 for non-indexed geometry.</param>
        /// <returns>The OpenGL VAO handle.</returns>
        public int CreateVertexArray(string? debugLabel, int vertexBuffer, int indexBuffer = 0)
        {
            GL.CreateVertexArrays(1, out int vao);

            if (indexBuffer != 0)
            {
                GL.VertexArrayElementBuffer(vao, indexBuffer);
            }

            GL.VertexArrayVertexBuffer(vao, 0, vertexBuffer, 0, Stride);

            for (var i = 0; i < elements.Length; i++)
            {
                var location = (int)elements[i].Slot;

                GL.EnableVertexArrayAttrib(vao, location);
                GL.VertexArrayAttribBinding(vao, location, 0);
                SetVertexArrayAttribFormat(vao, location, elements[i].Format, offsets[i]);
            }

            VertexArray.Record(vao, BoundLocations);

#if DEBUG
            if (debugLabel != null)
            {
                GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vao, Math.Min(GLEnvironment.MaxLabelLength, debugLabel.Length), debugLabel);
            }
#endif

            return vao;
        }

        /// <summary>
        /// Sets one VAO attribute's data format, mapping the <see cref="DXGI_FORMAT"/> to the matching float
        /// or integer GL attribute format. Shared with the game mesh path in <see cref="GPUMeshBufferCache"/>.
        /// </summary>
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
