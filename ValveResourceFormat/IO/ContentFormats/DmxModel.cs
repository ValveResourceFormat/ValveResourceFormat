using DMElement = Datamodel.Element;
using Datamodel.Format;
using System.Numerics;
using System;

namespace ValveResourceFormat.IO.ContentFormats.DmxModel;

#pragma warning disable CA2227 // Collection properties should be read only
[CamelCaseProperties]
internal class DmeModel : DMElement
{
    public DmeTransform Transform { get; set; } = [];
    public DMElement Shape { get; set; }
    public bool Visible { get; set; } = true;
    public Datamodel.ElementArray Children { get; } = [];
    public Datamodel.ElementArray JointList { get; set; } = [];

    /// <summary>
    /// List of <see cref="DmeTransformsList"/> elements.
    /// </summary>
    public Datamodel.ElementArray BaseStates { get; set; } = [];
    public DmeAxisSystem AxisSystem { get; set; } = [];
}

[CamelCaseProperties]
public class DmeTransform : DMElement
{
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Quaternion Orientation { get; set; } = Quaternion.Identity;
}

[CamelCaseProperties]
public class DmeAxisSystem : DMElement
{
    public int UpAxis { get; set; } = 3;
    public int ForwardParity { get; set; } = 1;
    public int CoordSys { get; set; }
}

[CamelCaseProperties]
public class DmeTransformsList : DMElement
{
    /// <summary>
    /// List of <see cref="DmeTransform"/> elements.
    /// </summary>
    public Datamodel.ElementArray Transforms { get; } = [];
}

[CamelCaseProperties]
public class DmeDag : DMElement
{
    public DmeTransform Transform { get; } = [];
    public DmeMesh Shape { get; } = [];
    public bool Visible { get; set; } = true;
    public Datamodel.ElementArray Children { get; } = [];
}

[CamelCaseProperties]
public class DmeMesh : DMElement
{
    public bool Visible { get; set; } = true;
    public DMElement BindState { get; set; }
    public DMElement CurrentState { get; set; }
    public Datamodel.ElementArray BaseStates { get; } = [];
    public Datamodel.ElementArray DeltaStates { get; } = [];
    public Datamodel.ElementArray FaceSets { get; } = [];
    public Datamodel.Vector2Array DeltaStateWeights { get; } = [];
    public Datamodel.Vector2Array DeltaStateWeightsLagged { get; } = [];
}

[CamelCaseProperties]
public class DmeFaceSet : DMElement
{
    public Datamodel.IntArray Faces { get; } = [];
    public DmeMaterial Material { get; } = new() { Name = "material" };

    public class DmeMaterial : DMElement
    {
        [DMProperty(name: "mtlName")]
        public string MaterialName { get; set; } = string.Empty;
    }
}

[CamelCaseProperties]
public class DmeVertexData : DMElement
{
    public Datamodel.StringArray VertexFormat { get; } = [];
    public int JointCount { get; set; }
    public bool FlipVCoordinates { get; set; }

    public void AddStream<T>(string name, T[] data)
    {
        VertexFormat.Add(name);
        this[name] = data;
    }

    public void AddIndexedStream<T>(string name, T[] data, int[] indices)
    {
        VertexFormat.Add(name);
        this[name] = data;
        this[name + "Indices"] = indices;
    }
}

[CamelCaseProperties]
public class DmeAnimationList : DMElement
{
    public Datamodel.ElementArray Animations { get; } = [];
}

[CamelCaseProperties]
public class DmeChannelsClip : DMElement
{
    public DmeTimeFrame TimeFrame { get; } = [];
    public Datamodel.Color Color { get; set; }
    public string Text { get; set; } = "";
    public bool Mute { get; set; }
    public Datamodel.ElementArray TrackGroups { get; } = [];
    public float DisplayScale { get; set; } = 1f;
    public Datamodel.ElementArray Channels { get; } = [];
    public float FrameRate { get; set; } = 30f;
}

[CamelCaseProperties]
public class DmeTimeFrame : DMElement
{
    public TimeSpan Start { get; set; }
    public TimeSpan Duration { get; set; }
    public TimeSpan Offset { get; set; }
    public float Scale { get; set; } = 1f;
}

[CamelCaseProperties]
public class DmeChannel : DMElement
{
    public DMElement FromElement { get; set; }
    public string FromAttribute { get; set; } = "";
    public int FromIndex { get; set; }

    public DMElement ToElement { get; set; }
    public string ToAttribute { get; set; } = "";
    public int ToIndex { get; set; }

    public int Mode { get; set; }

    private DMElement _log;
    public DMElement Log
    {
        get
        {
            return _log;
        }
        set
        {
            var logType = value.GetType();
            if (logType.GetGenericTypeDefinition() != typeof(DmeLog<>))
            {
                throw new ArgumentException($"DmeChannel.Log can only contain DmeLog types");
            }

            _log = value;
        }
    }
}

public abstract class DmeTypedLog<T> : DMElement
{
    protected DmeTypedLog(string namePostfix)
    {
        string typeName;
        if (typeof(T) == typeof(float)) //Name would be 'Single' without this
        {
            typeName = "Float";
        }
        else
        {
            typeName = typeof(T).Name;
        }

        if (char.IsLower(typeName[0]))
        {
            typeName = char.ToUpperInvariant(typeName[0]) + typeName[1..];
        }

        ClassName = $"Dme{typeName}{namePostfix}";
        Name = $"{typeName.ToLowerInvariant()} log";
    }
}

[CamelCaseProperties]
public class DmeLog<T> : DmeTypedLog<T>
{
    public DmeLog() : base("Log") { }
    [DMProperty("layers")]
    public Datamodel.ElementArray Layers { get; set; } = [];
    [DMProperty("curveinfo")]
    public DMElement CurveInfo { get; set; }
    [DMProperty("usedefaultvalue")]
    public bool UseDefaultValue { get; set; }
    [DMProperty("defaultvalue")]
    public T DefaultValue { get; set; }
    public Datamodel.TimeSpanArray BookmarksX { get; } = [];
    public Datamodel.TimeSpanArray BookmarksY { get; } = [];
    public Datamodel.TimeSpanArray BookmarksZ { get; } = [];

    public DmeLogLayer<T> GetLayer(int index)
    {
        return (DmeLogLayer<T>)Layers[index];
    }
    public void AddLayer(DmeLogLayer<T> layer)
    {
        Layers.Add(layer);
    }
    public int LayerCount => Layers.Count;
}

[CamelCaseProperties]
public class DmeLogLayer<T> : DmeTypedLog<T>
{
    public DmeLogLayer() : base("LogLayer") { }
    public Datamodel.TimeSpanArray Times { get; set; } = [];
    [DMProperty("curvetypes")]
    public Datamodel.IntArray CurveTypes { get; } = [];
    [DMProperty("values")]
    public T[] LayerValues { get; set; }


    /// <summary>
    /// Checks if this layer only contains default/zero values.
    /// </summary>
    public bool IsLayerZero()
    {
        object defaultValue;

        //quaternions initialize to all 0s
        if (typeof(T) == typeof(Quaternion))
        {
            defaultValue = Quaternion.Identity;
        }
        else
        {
            defaultValue = default(T);
        }

        foreach (var item in LayerValues)
        {
            if (!item.Equals(defaultValue))
            {
                return false;
            }
        }
        return true;
    }
}
#pragma warning restore CA2227 // Collection properties should be read only
