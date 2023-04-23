using DMElement = Datamodel.Element;
using DMAttributeName = Datamodel.Format.Attribute;
using LowercaseProperties = Datamodel.Format.AttributeNameLowercaseAttribute;
using CamelCaseProperties = Datamodel.Format.AttributeNameCamelCaseAttribute;
using System.Numerics;

namespace ValveResourceFormat.IO.ContentFormats.DmxModel;

#pragma warning disable CA2227 // Collection properties should be read only
[CamelCaseProperties]
internal class DmeModel : DMElement
{
    public DmeTransform Transform { get; set; } = new();
    public DMElement Shape { get; set; }
    public bool Visible { get; set; } = true;
    public Datamodel.ElementArray Children { get; } = new();
    public Datamodel.ElementArray JointList { get; set; } = new();

    /// <summary>
    /// List of <see cref="DmeTransformsList"/> elements.
    /// </summary>
    public Datamodel.ElementArray BaseStates { get; set; } = new();
    public DmeAxisSystem AxisSystem { get; set; } = new();
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
    public Datamodel.ElementArray Transforms { get; } = new();
}

[CamelCaseProperties]
public class DmeDag : DMElement
{
    public DmeTransform Transform { get; } = new();
    public DmeMesh Shape { get; } = new();
    public bool Visible { get; set; } = true;
    public Datamodel.ElementArray Children { get; } = new();
}

[CamelCaseProperties]
public class DmeMesh : DMElement
{
    public bool Visible { get; set; } = true;
    public DMElement BindState { get; set; }
    public DMElement CurrentState { get; set; }
    public Datamodel.ElementArray BaseStates { get; } = new();
    public Datamodel.ElementArray DeltaStates { get; } = new();
    public Datamodel.ElementArray FaceSets { get; } = new();
    public Datamodel.Vector2Array DeltaStateWeights { get; } = new();
    public Datamodel.Vector2Array DeltaStateWeightsLagged { get; } = new();
}

[CamelCaseProperties]
public class DmeFaceSet : DMElement
{
    public Datamodel.IntArray Faces { get; } = new();
    public DmeMaterial Material { get; } = new() { Name = "material" };

    public class DmeMaterial : DMElement
    {
        [DMAttributeName("mtlName")]
        public string MaterialName { get; set; } = string.Empty;
    }
}

[CamelCaseProperties]
public class DmeVertexData : DMElement
{
    public Datamodel.StringArray VertexFormat { get; } = new();
    public int JointCount { get; set; }
    public bool FlipVCoordinates { get; set; }

    public void AddStream<TDmx, T>(string name, T[] data, int[] indices)
        where TDmx : Datamodel.Array<T>, new()
    {
        VertexFormat.Add(name);

        var dmData = new TDmx();
        dmData.AddRange(data);
        this[name] = dmData;

        var dmIndices = new Datamodel.IntArray();
        dmIndices.AddRange(indices);
        this[name + "Indices"] = dmIndices;
    }
}
#pragma warning restore CA2227 // Collection properties should be read only
