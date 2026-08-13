using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Marks a vertex struct field as the shader input bound to a <see cref="VertexAttributeSlot"/>. The
    /// buffer format follows from the field type, see <see cref="VertexFormat.FromStruct{TVertex}"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class VertexAttributeAttribute(VertexAttributeSlot slot, DXGI_FORMAT format = DXGI_FORMAT.UNKNOWN) : Attribute
    {
        /// <summary>Gets the slot this field supplies.</summary>
        public VertexAttributeSlot Slot { get; } = slot;

        /// <summary>Gets the buffer format, or <see cref="DXGI_FORMAT.UNKNOWN"/> to derive it from the field type.</summary>
        public DXGI_FORMAT Format { get; } = format;
    }

    /// <summary>One attribute of a <see cref="VertexFormat"/>. An offset of -1 packs it after the last one.</summary>
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

        /// <summary>Initializes a vertex format from a stride and its attributes in buffer order.</summary>
        public VertexFormat(int stride, params VertexAttribute[] elements)
        {
            this.elements = elements;
            offsets = new int[elements.Length];
            Stride = stride;

            var packedOffset = 0;
            var boundLocations = 0;

            for (var i = 0; i < elements.Length; i++)
            {
                var element = elements[i];
                var (elementSize, elementCount) = VBIB.GetFormatInfo(element.Format, element.Slot.ToString());

                offsets[i] = element.OffsetInBytes >= 0 ? element.OffsetInBytes : packedOffset;
                packedOffset = offsets[i] + (elementSize * elementCount);

                Debug.Assert(packedOffset <= stride, $"Attribute '{element.Slot}' ends at byte {packedOffset}, past the vertex stride of {stride}.");
                Debug.Assert((boundLocations & (1 << (int)element.Slot)) == 0, $"Vertex format binds the slot of '{element.Slot}' twice.");
                boundLocations |= 1 << (int)element.Slot;
            }
        }

        /// <summary>Derives the format of a vertex struct from its <see cref="VertexAttributeAttribute"/> fields.</summary>
        public static VertexFormat FromStruct<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] TVertex>() where TVertex : struct
        {
            // Offsets come from the marshalled layout, the buffer gets the managed one. They only agree
            // while the struct stays blittable and sequential.
            Debug.Assert(Marshal.SizeOf<TVertex>() == Unsafe.SizeOf<TVertex>(),
                $"{typeof(TVertex).Name} marshals to {Marshal.SizeOf<TVertex>()} bytes but is {Unsafe.SizeOf<TVertex>()} in memory, so its attribute offsets would not match the uploaded vertices.");

            var elements = new List<VertexAttribute>();

            foreach (var field in typeof(TVertex).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.GetCustomAttribute<VertexAttributeAttribute>() is not { } attribute)
                {
                    continue;
                }

                var format = attribute.Format == DXGI_FORMAT.UNKNOWN ? FormatForType(field.FieldType) : attribute.Format;

                elements.Add(new VertexAttribute(attribute.Slot, format, (int)Marshal.OffsetOf<TVertex>(field.Name)));
            }

            return new VertexFormat(Marshal.SizeOf<TVertex>(), [.. elements]);
        }

        private static readonly FrozenDictionary<Type, DXGI_FORMAT> FormatByFieldType = new Dictionary<Type, DXGI_FORMAT>
        {
            [typeof(float)] = DXGI_FORMAT.R32_FLOAT,
            [typeof(Vector2)] = DXGI_FORMAT.R32G32_FLOAT,
            [typeof(Vector3)] = DXGI_FORMAT.R32G32B32_FLOAT,
            [typeof(Vector4)] = DXGI_FORMAT.R32G32B32A32_FLOAT,
            [typeof(Color32)] = DXGI_FORMAT.R8G8B8A8_UNORM,
            [typeof(uint)] = DXGI_FORMAT.R32_UINT,
        }.ToFrozenDictionary();

        private static DXGI_FORMAT FormatForType(Type fieldType)
            => FormatByFieldType.TryGetValue(fieldType, out var format)
                ? format
                : throw new NotImplementedException($"Field type {fieldType.Name} maps to no vertex attribute format, pass one to [VertexAttribute].");

        /// <summary>Describes this format as <see cref="VBIB"/> input layout fields, for the upload path.</summary>
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

        /// <summary>Creates a VAO binding one vertex buffer. An index buffer of 0 means non-indexed.</summary>
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
                VertexArray.SetAttribFormat(vao, location, elements[i].Format, offsets[i]);
            }

#if DEBUG
            if (debugLabel != null)
            {
                GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vao, Math.Min(GLEnvironment.MaxLabelLength, debugLabel.Length), debugLabel);
            }
#endif

            return vao;
        }
    }
}
