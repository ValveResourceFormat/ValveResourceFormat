using System.Linq;
using Datamodel.Format;
using DMElement = Datamodel.Element;

namespace ValveResourceFormat.IO.ContentFormats.ValveTexture;

/// <summary>
/// Root element of a texture content file (.vtex), describing the inputs and the output image to compile.
/// </summary>
[HungarianProperties]
public class CDmeVtex : DMElement
{
    /// <summary>
    /// Array of <see cref="CDmeInputTexture"/> elements describing each input image.
    /// </summary>
    public Datamodel.ElementArray InputTextureArray { get; } = [];

    /// <summary>
    /// Type of texture to generate.
    /// </summary>
    public string OutputTypeString { get; set; } = "2D";

    /// <summary>
    /// Image data format to generate.
    /// </summary>
    public string OutputFormat { get; set; } = "DXT1";

    /// <summary>
    /// Initial color before input textures are applied to the output.
    /// </summary>
    [DMProperty(name: "m_outputClearColor")]
    public Vector4 OutputClearColor { get; set; } = Vector4.Zero;

    /// <summary>
    /// Gets or sets the minimum dimension for the output texture.
    /// </summary>
    public int OutputMinDimension { get; set; }

    /// <summary>
    /// Gets or sets the maximum dimension for the output texture.
    /// </summary>
    public int OutputMaxDimension { get; set; }

    //[DMProperty(optional: true)]
    //public int OutputDimensionReduce { get; set; } = 1;

    /// <summary>
    /// Array of <see cref="CDmeTextureOutputChannel"/> elements describing sets of output channels.
    /// </summary>
    public Datamodel.ElementArray TextureOutputChannelArray { get; } = [];

    /// <summary>
    /// Whether to clamp the S, T and U coordinates respectively.
    /// </summary>
    public Vector3 Clamp { get; set; } = Vector3.Zero;

    /// <summary>
    /// Sets the <c>NO_LOD</c> flag on the compiled texture; mips are still generated.
    /// </summary>
    public bool NoLod { get; set; }

    // <summary>
    // Marks the output file as a hidden asset.
    // </summary>
    //[DMProperty(optional: true)]
    //public bool HiddenAssetFlag { get; set; }

    /// <summary>
    /// Adds an input image.
    /// </summary>
    /// <param name="name">Name the output channels refer to the input by.</param>
    /// <param name="fileName">Content relative path of the image.</param>
    /// <param name="typeString">Layout of the image, e.g. "2D", "CUBE", "2DARRAY" or "3D".</param>
    /// <param name="colorSpace">Color space of the image, "srgb" or "linear".</param>
    /// <param name="imageProcessors">Algorithms to run on the image, in order.</param>
    public void AddInputTexture(string name, string fileName, string typeString, string colorSpace, IEnumerable<string> imageProcessors)
    {
        var input = new CDmeInputTexture { Name = name, FileName = fileName, TypeString = typeString, ColorSpace = colorSpace };

        foreach (var processor in imageProcessors.DefaultIfEmpty("None"))
        {
            input.ImageProcessorArray.Add(new CDmeImageProcessor { Algorithm = processor });
        }

        InputTextureArray.Add(input);
    }

    /// <summary>
    /// Adds the output channels that are compiled from the named inputs.
    /// </summary>
    /// <param name="inputNames">Names of the inputs, as given to <see cref="AddInputTexture"/>.</param>
    /// <param name="channels">Channels to read and write, e.g. "rgb" or "a".</param>
    /// <param name="colorSpace">Color space of the output, "srgb" or "linear".</param>
    /// <param name="mipAlgorithm">Mipmap generation algorithm, "None" for a single mip.</param>
    public void AddOutputChannel(IEnumerable<string> inputNames, string channels, string colorSpace, string mipAlgorithm)
    {
        var output = new CDmeTextureOutputChannel { SrcChannels = channels, DstChannels = channels, OutputColorSpace = colorSpace };
        output.InputTextureArray.AddRange(inputNames);
        output.MipAlgorithm.Algorithm = mipAlgorithm;
        TextureOutputChannelArray.Add(output);
    }
}

/// <summary>
/// Represents an input texture for compilation.
/// </summary>
[HungarianProperties]
public class CDmeInputTexture : DMElement
{
    /// <summary>
    /// Gets or sets the name of the input texture.
    /// </summary>
    public new string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file name of the input texture.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the color space of the input texture.
    /// </summary>
    public string ColorSpace { get; set; } = "srgb";

    /*
    [DMProperty(name: "m_fileExt", optional: true)]
    public string FileExtension { get; set; } = string.Empty;

    [DMProperty(optional: true)]
    public int MinBitsPerChannel { get; set; } = -1;

    [DMProperty(optional: true)]
    public bool PassThroughToCompiledVtex { get; set; }

    [DMProperty(name: "m_n3dSliceCount", optional: true)]
    public int SliceCount { get; set; } = -1;
    [DMProperty(name: "m_n3dSliceWidth", optional: true)]
    public int SliceWidth { get; set; } = -1;
    [DMProperty(name: "m_n3dSliceHeight", optional: true)]
    public int SliceHeight { get; set; } = -1;
    */

    /// <summary>
    /// Gets or sets the texture type.
    /// </summary>
    public string TypeString { get; set; } = "2D";

    /// <summary>
    /// Gets the array of image processors to apply.
    /// </summary>
    public Datamodel.ElementArray ImageProcessorArray { get; } = [];
}

/// <summary>
/// Represents an output channel configuration for texture compilation.
/// </summary>
[HungarianProperties]
public class CDmeTextureOutputChannel : DMElement
{
    /// <summary>
    /// Gets the array of input texture names to use.
    /// </summary>
    public Datamodel.StringArray InputTextureArray { get; } = [];

    /// <summary>
    /// Gets or sets the source channels to read from input textures.
    /// </summary>
    public string SrcChannels { get; set; } = "rgba";

    /// <summary>
    /// Gets or sets the destination channels to write to output texture.
    /// </summary>
    public string DstChannels { get; set; } = "rgba";

    /// <summary>
    /// Gets the mipmap generation algorithm configuration.
    /// </summary>
    public CDmeImageProcessor MipAlgorithm { get; } = [];

    /// <summary>
    /// Gets or sets the output color space.
    /// </summary>
    public string OutputColorSpace { get; set; } = "srgb";
}

/// <summary>
/// Represents an image processing algorithm configuration.
/// </summary>
[HungarianProperties]
public class CDmeImageProcessor : DMElement
{
    /// <summary>
    /// Gets or sets the processing algorithm name.
    /// </summary>
    public string Algorithm { get; set; } = "None";

    /// <summary>
    /// Gets or sets the string argument for the algorithm.
    /// </summary>
    public string StringArg { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the float4 argument for the algorithm.
    /// </summary>
    [DMProperty(name: "m_vFloat4Arg")]
    public Vector4 Float4Arg { get; set; } = Vector4.Zero;
}
