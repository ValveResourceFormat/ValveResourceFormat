using System.Collections;
using System.Diagnostics;
using System.IO;
using SteamDatabase.ValvePak;

namespace ValveResourceFormat.CompiledShader;

// Disable broken analyzer (Use recommended dispose pattern)
#pragma warning disable CA2000

/// <summary>
/// Collection of related shader programs.
/// </summary>
public class ShaderCollection : IEnumerable<VfxProgramData>, IDisposable
{
    /// <summary>Gets the features shader.</summary>
    public VfxProgramData? Features
        => Get(VcsProgramType.Features);
    /// <summary>Gets the vertex shader.</summary>
    public VfxProgramData? Vertex
        => Get(VcsProgramType.VertexShader);
    /// <summary>Gets the geometry shader.</summary>
    public VfxProgramData? Geometry
        => Get(VcsProgramType.GeometryShader);
    /// <summary>Gets the domain shader.</summary>
    public VfxProgramData? Domain
        => Get(VcsProgramType.DomainShader);
    /// <summary>Gets the hull shader.</summary>
    public VfxProgramData? Hull
        => Get(VcsProgramType.HullShader);
    /// <summary>Gets the pixel shader.</summary>
    public VfxProgramData? Pixel
        => Get(VcsProgramType.PixelShader);
    /// <summary>Gets the compute shader.</summary>
    public VfxProgramData? Compute
        => Get(VcsProgramType.ComputeShader);
    /// <summary>Gets the pixel shader render state.</summary>
    public VfxProgramData? PixelShaderRenderState
        => Get(VcsProgramType.PixelShaderRenderState);
    /// <summary>Gets the raytracing shader.</summary>
    public VfxProgramData? Raytracing
        => Get(VcsProgramType.RaytracingShader);
    /// <summary>Gets the mesh shader.</summary>
    public VfxProgramData? Mesh
        => Get(VcsProgramType.MeshShader);

    private readonly Dictionary<VcsProgramType, VfxProgramData> shaders = new((int)VcsProgramType.Undetermined);

    /// <summary>
    /// Loads all related shader files for a given shader.
    /// </summary>
    public static ShaderCollection GetShaderCollection(string targetFilename, Package? vrfPackage)
    {
        ShaderCollection shaderCollection = [];

        var filename = Path.GetFileName(targetFilename);
        var vcsCollectionName = filename.AsSpan(0, filename.LastIndexOf('_')); // in the form water_dota_pcgl_40

        if (vrfPackage != null)
        {
            Debug.Assert(vrfPackage.Entries != null);

            // search the package
            var vcsEntries = vrfPackage.Entries["vcs"];

            foreach (var vcsEntry in vcsEntries)
            {
                // vcsEntry.FileName is in the form bloom_dota_pcgl_30_ps (without vcs extension)
                if (vcsEntry.FileName.AsSpan().StartsWith(vcsCollectionName, StringComparison.InvariantCulture))
                {
                    vrfPackage.ReadEntry(vcsEntry, out var shaderDatabytes);

                    var relatedShaderFile = new VfxProgramData();

                    try
                    {
                        relatedShaderFile.Read(vcsEntry.GetFileName(), new MemoryStream(shaderDatabytes));
                        shaderCollection.Add(relatedShaderFile);
                        relatedShaderFile = null;
                    }
                    finally
                    {
                        relatedShaderFile?.Dispose();
                    }
                }
            }
        }
        else
        {
            // search file-system
            foreach (var vcsFile in Directory.GetFiles(Path.GetDirectoryName(targetFilename)!))
            {
                if (Path.GetFileName(vcsFile.AsSpan()).StartsWith(vcsCollectionName, StringComparison.InvariantCulture))
                {
                    VfxProgramData? program = null;
                    Stream? stream = null;

                    try
                    {
                        stream = new FileStream(vcsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        program = new VfxProgramData();
                        program.Read(Path.GetFileName(vcsFile), stream);
                        shaderCollection.Add(program);
                        program = null;
                        stream = null;
                    }
                    finally
                    {
                        stream?.Dispose();
                        program?.Dispose();
                    }
                }
            }
        }

        return shaderCollection;
    }

    /// <summary>
    /// Adds a shader program to the collection.
    /// </summary>
    public void Add(VfxProgramData program)
    {
        if (!shaders.TryAdd(program.VcsProgramType, program))
        {
            throw new ArgumentException($"Shader of type {program.VcsProgramType} already exists in this collection.");
        }
    }

    /// <summary>
    /// Gets a shader program by type.
    /// </summary>
    public VfxProgramData? Get(VcsProgramType type)
    {
        if (shaders.TryGetValue(type, out var shader))
        {
            return shader;
        }

        return null;
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<VfxProgramData> GetEnumerator()
    {
        return shaders.Values.GetEnumerator();
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)shaders.Values).GetEnumerator();
    }

    /// <summary>
    /// Creates a collection from an enumerable of shaders.
    /// </summary>
    public static ShaderCollection FromEnumerable(IEnumerable<VfxProgramData> shaders)
    {
        var collection = new ShaderCollection();

        foreach (var shader in shaders)
        {
            collection.Add(shader);
        }

        return collection;
    }

    /// <summary>
    /// Disposes all shaders in the collection.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="ShaderCollection"/> and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var shader in shaders.Values)
            {
                shader.Dispose();
            }
        }
    }
}
