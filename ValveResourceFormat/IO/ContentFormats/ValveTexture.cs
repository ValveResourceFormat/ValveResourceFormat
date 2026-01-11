using Datamodel.Format;
using DMElement = Datamodel.Element;

namespace ValveResourceFormat.IO.ContentFormats.ValveTexture;

[HungarianProperties]
internal class CDmeVtex : DMElement
{
    /// <summary>
    /// Array of <see cref="CDmeInputTexture"/> elements describing each input image.
    /// </summary>
    public Datamodel.ElementArray InputTextureArray { get; set; } = [];

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
    public Datamodel.ElementArray TextureOutputChannelArray { get; set; } = [];

    /// <summary>
    /// Whether to clamp the S, T and U coordinates respectively.
    /// </summary>
    public Vector3 Clamp { get; set; } = Vector3.Zero;

    /// <summary>
    /// Disables mipmaps.
    /// </summary>
    public bool NoLod { get; set; }

    // <summary>
    // Marks the output file as a hidden asset.
    // </summary>
    //[DMProperty(optional: true)]
    //public bool HiddenAssetFlag { get; set; }

    /// <summary>
    /// Creates a 2D texture configuration from input images.
    /// </summary>
    public static CDmeVtex CreateTexture2D((string FileName, string Channels, string MipAlgorithm)[] images, string outputFormat = "DXT1")
    {
        var vtex = new CDmeVtex
        {
            OutputFormat = outputFormat
        };

        var i = 0;
        foreach (var image in images)
        {
            var inputImageId = "InputTexture" + i++;
            var input = new CDmeInputTexture { Name = inputImageId, FileName = image.FileName };
            input.ImageProcessorArray.Add(new CDmeImageProcessor()); // Empty
            vtex.InputTextureArray.Add(input);

            var output = new CDmeTextureOutputChannel()
            {
                SrcChannels = "rgba"[..image.Channels.Length],
                DstChannels = image.Channels,
            };

            output.InputTextureArray.Add(inputImageId);
            output.MipAlgorithm.Algorithm = image.MipAlgorithm;
            vtex.TextureOutputChannelArray.Add(output);
        }

        return vtex;
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
    public required new string Name { get; init; }

    /// <summary>
    /// Gets or sets the file name of the input texture.
    /// </summary>
    public required string FileName { get; init; }

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
