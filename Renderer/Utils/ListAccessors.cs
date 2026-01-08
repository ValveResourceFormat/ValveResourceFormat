using System.Runtime.CompilerServices;

namespace GUI.Utils;

class ListAccessors<T>
{
    /// <summary>
    /// Only use this when you need to get the backing array as a pointer.
    /// Otherwise use <see cref="System.Runtime.InteropServices.CollectionsMarshal.AsSpan"/>
    /// </summary>
    /// <param name="list">A list.</param>
    /// <returns>The backing array of the list.</returns>
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_items")]
    public extern static ref T[] GetBackingArray(List<T> list);
}
