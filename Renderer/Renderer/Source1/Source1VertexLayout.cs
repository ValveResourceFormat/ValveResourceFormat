using System.Diagnostics.CodeAnalysis;

namespace ValveResourceFormat.Renderer.Source1
{
    /// <summary>
    /// Creates the vertex formats of the handbuilt meshes that the Source 1 vertex shaders draw. Custom attributes
    /// are placed around every attribute such a shader declares, whether a combo enables it or not.
    /// </summary>
    internal static class Source1VertexLayout
    {
        private static readonly VertexSlot[] DeclaredSlots =
        [
            VertexSlot.Position,
            VertexSlot.BlendIndices,
            VertexSlot.BlendWeight,
            VertexSlot.TexCoord,
            VertexSlot.BlendIndices2,
            VertexSlot.BlendWeight2,
            VertexSlot.Color,
        ];

        /// <summary>Gets the format of a vertex struct for a shader that includes <c>common_vs.slang</c>.</summary>
        /// <param name="customNames">The attributes the shader declares on its own.</param>
        /// <typeparam name="TVertex">The vertex struct.</typeparam>
        public static VertexInputLayout Create<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] TVertex>(params string[] customNames)
            where TVertex : struct
        {
            var declaredNames = new List<string>();

            foreach (var slot in DeclaredSlots)
            {
                declaredNames.Add(VertexAttributeLocations.GetName(slot));
            }

            declaredNames.AddRange(customNames);

            return VertexInputLayout.FromStruct<TVertex>(declaredNames.ToArray());
        }
    }
}
