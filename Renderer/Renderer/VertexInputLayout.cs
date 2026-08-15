using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Marks a vertex struct field as a shader input. The buffer format follows from the field type, see
    /// <see cref="VertexInputLayout.FromStruct{TVertex}"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class VertexAttributeAttribute(string name, DXGI_FORMAT format = DXGI_FORMAT.UNKNOWN, int location = -1) : Attribute
    {
        /// <summary>Gets the shader input name this field supplies.</summary>
        public string Name { get; } = name;

        /// <summary>Gets the buffer format, or <see cref="DXGI_FORMAT.UNKNOWN"/> to derive it from the field type.</summary>
        public DXGI_FORMAT Format { get; } = format;

        /// <summary>Gets the location this field is pinned to, or -1 to let the layout place it. Pin it only to
        /// match a shader that declares its own <c>layout (location = ...)</c>.</summary>
        public int Location { get; } = location;

        /// <summary>Marks a field supplying a mesh attribute.</summary>
        [SuppressMessage("Design", "CA1019:Define accessors for attribute arguments", Justification = "The slot is exposed as its name")]
        public VertexAttributeAttribute(VertexSlot slot, DXGI_FORMAT format = DXGI_FORMAT.UNKNOWN)
            : this(VertexAttributeLocations.GetName(slot), format)
        {
        }
    }

    /// <summary>One attribute of a <see cref="VertexInputLayout"/>. An offset of -1 packs it after the last
    /// one, and a location of -1 lets the layout place it.</summary>
    public readonly record struct VertexAttribute(string Name, DXGI_FORMAT Format, int OffsetInBytes = -1, int Location = -1)
    {
        /// <summary>Initializes an attribute supplying a mesh slot.</summary>
        public VertexAttribute(VertexSlot slot, DXGI_FORMAT format, int offsetInBytes = -1)
            : this(VertexAttributeLocations.GetName(slot), format, offsetInBytes)
        {
        }
    }

    /// <summary>
    /// Interleaved vertex layout of handbuilt geometry, held as the same input layout fields a mesh carries.
    /// Element order is buffer order, and offsets pack in declaration order unless given explicitly.
    /// </summary>
    public sealed class VertexInputLayout
    {
        private readonly VBIB.RenderInputLayoutField[] fields;
        private readonly int[] locations;

        /// <summary>Gets the size in bytes of a single vertex.</summary>
        public int Stride { get; }

        /// <summary>Initializes a vertex format from a stride and its attributes in buffer order.</summary>
        public VertexInputLayout(int stride, params VertexAttribute[] elements)
        {
            fields = new VBIB.RenderInputLayoutField[elements.Length];
            locations = new int[elements.Length];
            Stride = stride;

            // The declaring set decides where a custom attribute goes, so a shader declaring the same set
            // arrives at the same locations without either side naming a number
            var pinned = elements.Where(element => element.Location >= 0).ToDictionary(element => element.Name, element => element.Location, StringComparer.Ordinal);
            var allocated = VertexAttributeLocations.Allocate(Array.ConvertAll(elements, element => element.Name), pinned);
            var packedOffset = 0;
            var boundLocations = 0;

            for (var i = 0; i < elements.Length; i++)
            {
                var element = elements[i];
                var (elementSize, elementCount) = VBIB.GetFormatInfo(element.Format, element.Name);
                var offset = element.OffsetInBytes >= 0 ? element.OffsetInBytes : packedOffset;

                packedOffset = offset + (elementSize * elementCount);
                locations[i] = allocated[element.Name];

                // A custom attribute has no semantic to carry, its name is what resolves it
                var (semantic, semanticIndex) = VertexAttributeLocations.GetSemantic(locations[i]);

                fields[i] = new VBIB.RenderInputLayoutField(semantic, element.Format, (uint)offset)
                {
                    SemanticIndex = semanticIndex,
                    ShaderSemantic = element.Name,
                };

                Debug.Assert(packedOffset <= stride, $"Attribute '{element.Name}' ends at byte {packedOffset}, past the vertex stride of {stride}.");
                Debug.Assert((boundLocations & (1 << locations[i])) == 0, $"Vertex format binds the location of '{element.Name}' twice.");
                boundLocations |= 1 << locations[i];
            }
        }

        /// <summary>Derives the format of a vertex struct from its <see cref="VertexAttributeAttribute"/> fields.</summary>
        public static VertexInputLayout FromStruct<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] TVertex>() where TVertex : struct
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

                elements.Add(new VertexAttribute(attribute.Name, format, (int)Marshal.OffsetOf<TVertex>(field.Name), attribute.Location));
            }

            return new VertexInputLayout(Marshal.SizeOf<TVertex>(), [.. elements]);
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

        /// <summary>This format as <see cref="VBIB"/> input layout fields, for the upload path.</summary>
        public VBIB.RenderInputLayoutField[] Fields() => fields;

        /// <summary>Creates a VAO binding one vertex buffer. An index buffer of 0 means non-indexed.</summary>
        public int CreateVertexArray(string? debugLabel, int vertexBuffer, int indexBuffer = 0)
        {
            GL.CreateVertexArrays(1, out int vao);
            VertexArray.StartRecording(vao, Array.ConvertAll(fields, field => field.ShaderSemantic));

            if (indexBuffer != 0)
            {
                GL.VertexArrayElementBuffer(vao, indexBuffer);
            }

            GL.VertexArrayVertexBuffer(vao, 0, vertexBuffer, 0, Stride);

            for (var i = 0; i < locations.Length; i++)
            {
                GL.EnableVertexArrayAttrib(vao, locations[i]);
                GL.VertexArrayAttribBinding(vao, locations[i], 0);
                VertexArray.SetAttribFormat(vao, locations[i], fields[i].Format, (int)fields[i].Offset);
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
