using System.Collections;
using System.Linq;

namespace ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;

partial class HalfEdgeMesh
{
    /// <summary>
    /// Attaches a named per vertex data stream to the mesh.
    /// </summary>
    /// <typeparam name="TData">Type stored per vertex.</typeparam>
    /// <param name="name">Name to register the stream under.</param>
    public VertexData<TData> CreateVertexData<TData>(string name) where TData : struct =>
        VertexList.CreateDataStream<VertexData<TData>>(name);

    /// <summary>
    /// Attaches a named per face data stream to the mesh.
    /// </summary>
    /// <typeparam name="TData">Type stored per face.</typeparam>
    /// <param name="name">Name to register the stream under.</param>
    public FaceData<TData> CreateFaceData<TData>(string name) where TData : struct =>
        FaceList.CreateDataStream<FaceData<TData>>(name);

    /// <summary>
    /// Attaches a named per half edge data stream to the mesh.
    /// </summary>
    /// <typeparam name="TData">Type stored per half edge.</typeparam>
    /// <param name="name">Name to register the stream under.</param>
    public HalfEdgeData<TData> CreateHalfEdgeData<TData>(string name) where TData : struct =>
        HalfEdgeList.CreateDataStream<HalfEdgeData<TData>>(name);
}

/// <summary>
/// A data stream the mesh grows and compacts alongside its components.
/// </summary>
public interface IDataStream
{
    internal void Allocate(int sourceIndex);
    internal void AllocateMultiple(int count);
}

#pragma warning disable CA1043
/// <summary>
/// Per vertex data, indexed by <see cref="VertexHandle"/>.
/// </summary>
/// <typeparam name="TData">Type stored per vertex.</typeparam>
public sealed class VertexData<TData> : ComponentData<TData> where TData : struct
{
    /// <summary>
    /// Gets or sets the value stored for a vertex.
    /// </summary>
    /// <param name="h">Vertex to address.</param>
    public TData this[VertexHandle h] { get => this[h.Index]; set => this[h.Index] = value; }
}

/// <summary>
/// Per face data, indexed by <see cref="FaceHandle"/>.
/// </summary>
/// <typeparam name="TData">Type stored per face.</typeparam>
public sealed class FaceData<TData> : ComponentData<TData> where TData : struct
{
    /// <summary>
    /// Gets or sets the value stored for a face.
    /// </summary>
    /// <param name="h">Face to address.</param>
    public TData this[FaceHandle h] { get => this[h.Index]; set => this[h.Index] = value; }
}

/// <summary>
/// Per half edge data, indexed by <see cref="HalfEdgeHandle"/>.
/// </summary>
/// <typeparam name="TData">Type stored per half edge.</typeparam>
public sealed class HalfEdgeData<TData> : ComponentData<TData> where TData : struct
{
    /// <summary>
    /// Gets or sets the value stored for a half edge.
    /// </summary>
    /// <param name="h">Half edge to address.</param>
    public TData this[HalfEdgeHandle h] { get => this[h.Index]; set => this[h.Index] = value; }
}
#pragma warning restore CA1043

/// <summary>
/// Backing store shared by the per component data streams. Allocation is driven by the mesh.
/// </summary>
/// <typeparam name="T">Type stored per component.</typeparam>
public abstract class ComponentData<T> : IEnumerable<T>, IDataStream where T : struct
{
    internal readonly List<T> _list = new();

    /// <summary>
    /// Returns an enumerator over every entry, including entries of deallocated components.
    /// </summary>
    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Number of entries, which matches the component count of the mesh.
    /// </summary>
    public int Count => _list.Count;

    /// <summary>
    /// Gets or sets the value at an index. Out of range reads return default and writes are dropped.
    /// </summary>
    /// <param name="index">Component index to address.</param>
    public T this[int index]
    {
        get => index >= 0 && index < Count ? _list[index] : default;
        set
        {
            if (index >= 0 && index < Count)
                _list[index] = value;
        }
    }

    /// <summary>
    /// Overwrites the leading entries from <paramref name="source"/>, stopping at whichever is shorter.
    /// </summary>
    /// <param name="source">Values to copy in.</param>
    public void CopyFrom(T[] source)
    {
        int count = Math.Min(_list.Count, source.Length);
        for (int i = 0; i < count; i++)
            _list[i] = source[i];
    }

#pragma warning disable CA1033
    void IDataStream.Allocate(int sourceIndex)
    {
        _list.Add(sourceIndex >= 0 && sourceIndex < Count ? _list[sourceIndex] : default);
    }

    void IDataStream.AllocateMultiple(int count)
    {
        _list.Capacity += count;
        for (var i = 0; i < count; i++)
            _list.Add(default);
    }
#pragma warning restore CA1033
}

/// <summary>
/// The components of one kind held by a mesh, along with the data streams attached to them.
/// </summary>
/// <typeparam name="T">Component type, one of vertex, face, or half edge.</typeparam>
public class ComponentList<T> : IEnumerable<T>
{
    private readonly List<T> _list = new();
    private readonly List<bool> _active = new();
    private readonly Dictionary<string, IDataStream> _streams = new();

    /// <summary>
    /// Number of slots, counting deallocated ones.
    /// </summary>
    public int Count => _list.Count;

    /// <summary>
    /// Returns an enumerator over every slot, including deallocated ones.
    /// </summary>
    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Indices of the slots still in use.
    /// </summary>
    public IEnumerable<int> ActiveList => Enumerable.Range(0, Count).Where(x => _active[x]);

    /// <summary>
    /// Gets the component at an index.
    /// </summary>
    /// <param name="index">Slot to address.</param>
    public T this[int index]
    {
        get => _list[index];
        internal set => _list[index] = value;
    }

    /// <summary>
    /// Appends a component and grows every attached data stream with it.
    /// </summary>
    /// <param name="component">Component to append.</param>
    /// <param name="sourceIndex">Slot the new stream entries copy from, -1 to leave them default.</param>
    /// <returns>Index of the new slot.</returns>
    public int Allocate(T component, int sourceIndex = -1)
    {
        _list.Add(component);
        _active.Add(true);

        foreach (var stream in _streams.Values)
            stream.Allocate(sourceIndex);

        return Count - 1;
    }

    /// <summary>
    /// Appends several copies of a component and grows every attached data stream with them.
    /// </summary>
    /// <param name="count">How many slots to append.</param>
    /// <param name="component">Component to copy into each slot.</param>
    public void AllocateMultiple(int count, T component)
    {
        _list.Capacity += count;
        for (var i = 0; i < count; i++)
        {
            _list.Add(component);
            _active.Add(true);
        }

        foreach (var stream in _streams.Values)
            stream.AllocateMultiple(count);
    }

    /// <summary>
    /// Marks a slot unused. The slot keeps its data until the mesh is compacted.
    /// </summary>
    /// <param name="index">Slot to release.</param>
    public void Deallocate(int index)
    {
        _active[index] = false;
    }

    /// <summary>
    /// Whether a slot is in use. Out of range indices return false.
    /// </summary>
    /// <param name="index">Slot to test.</param>
    public bool IsAllocated(int index)
    {
        if (index < 0 || index >= _active.Count)
            return false;

        return _active[index];
    }

    /// <summary>
    /// Attaches a named data stream, sized to the current component count.
    /// </summary>
    /// <typeparam name="TDataStream">Stream type to create.</typeparam>
    /// <param name="name">Name to register the stream under.</param>
    /// <exception cref="ArgumentException">A stream with that name already exists.</exception>
    public TDataStream CreateDataStream<TDataStream>(string name) where TDataStream : IDataStream, new()
    {
        if (_streams.ContainsKey(name))
            throw new ArgumentException("A stream with the same name already exists.", nameof(name));

        var stream = new TDataStream();
        stream.AllocateMultiple(_list.Count);
        _streams[name] = stream;

        return stream;
    }
}
