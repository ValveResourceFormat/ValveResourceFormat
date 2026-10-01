using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace GUI.Forms
{
    /// <summary>
    /// What a cell in <see cref="EntityInfoControl"/> points at, handed to its host when the cell is double clicked.
    /// </summary>
    abstract record LinkedResource;

    /// <summary>A resource file, by the path it is referenced with.</summary>
    sealed record LinkedFile(string Path) : LinkedResource;

    /// <summary>An entity in the same world, named by a property or on either end of a connection.</summary>
    sealed record LinkedEntity(Entity Entity) : LinkedResource;
}
