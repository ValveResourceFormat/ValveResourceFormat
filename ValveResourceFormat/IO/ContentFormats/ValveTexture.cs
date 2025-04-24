using Datamodel.Format;
using DMElement = Datamodel.Element;

namespace ValveResourceFormat.IO.ContentFormats.ValveTexture;

#nullable disable

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

    public int OutputMinDimension { get; set; }
    public int OutputMaxDimension { get; set; }

    //[DMProperty(optional: true)]
    //public int OutputDimensionReduce { get; set; } = 1;

    /// <summary>
    /// Array of <see cref="CDmeTextureOutputChannel"/> elements describing sets of output channels.
    public Datamodel.ElementArray TextureOutputChannelArray { get; set; } = [];

    /// <summary>
    /// Whether to clamp the S, T and U coordinates respectively.
    /// </summary>
    public Vector3 Clamp { get; set; } = Vector3.Zero;

    /// <summary>
    /// Disables mipmaps.
    /// </summary>
    public bool NoLod { get; set; }

    /// <summary>
    /// Marks the output file as a hidden asset.
    /// </summary>
    //[DMProperty(optional: true)]
    //public bool HiddenAssetFlag { get; set; }

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

[HungarianProperties]
public class CDmeInputTexture : DMElement
{
    public new string Name { get; set; }

    public string FileName { get; set; }
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

    public string TypeString { get; set; } = "2D";
    public Datamodel.ElementArray ImageProcessorArray { get; } = [];
}

[HungarianProperties]
public class CDmeTextureOutputChannel : DMElement
{
    public Datamodel.StringArray InputTextureArray { get; } = [];
    public string SrcChannels { get; set; } = "rgba";
    public string DstChannels { get; set; } = "rgba";
    public CDmeImageProcessor MipAlgorithm { get; } = [];
    public string OutputColorSpace { get; set; } = "srgb";
}

[HungarianProperties]
public class CDmeImageProcessor : DMElement
{
    public string Algorithm { get; set; } = "None";
    public string StringArg { get; set; } = string.Empty;
    [DMProperty(name: "m_vFloat4Arg")]
    public Vector4 Float4Arg { get; set; } = Vector4.Zero;
}
