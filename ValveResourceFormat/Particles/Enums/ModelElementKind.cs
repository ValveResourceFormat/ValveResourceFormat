namespace ValveResourceFormat.Particles
{
    /// <summary>Which named parts of a model a function looks up.</summary>
    internal enum ModelElementKind
    {
        /// <summary>Sequences, by index among the model's sequences.</summary>
        Sequence,

        /// <summary>Body parts, by index.</summary>
        BodyPart,

        /// <summary>Mesh groups, by their bit in a mesh group mask.</summary>
        MeshGroup,
    }
}
