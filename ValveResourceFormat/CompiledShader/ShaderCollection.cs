using System.Collections;
using System.Diagnostics;
using System.IO;
using SteamDatabase.ValvePak;

namespace ValveResourceFormat.CompiledShader;

public class ShaderCollection : IEnumerable<VfxProgramData>, IDisposable
{
    public VfxProgramData? Features
        => Get(VcsProgramType.Features);
    public VfxProgramData? Vertex
        => Get(VcsProgramType.VertexShader);
    public VfxProgramData? Geometry
        => Get(VcsProgramType.GeometryShader);
    public VfxProgramData? Domain
        => Get(VcsProgramType.DomainShader);
    public VfxProgramData? Hull
        => Get(VcsProgramType.HullShader);
    public VfxProgramData? Pixel
        => Get(VcsProgramType.PixelShader);
    public VfxProgramData? Compute
        => Get(VcsProgramType.ComputeShader);
    public VfxProgramData? PixelShaderRenderState
        => Get(VcsProgramType.PixelShaderRenderState);
    public VfxProgramData? Raytracing
        => Get(VcsProgramType.RaytracingShader);
    public VfxProgramData? Mesh
        => Get(VcsProgramType.MeshShader);

    private readonly Dictionary<VcsProgramType, VfxProgramData> shaders = new((int)VcsProgramType.Undetermined);

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
                    var program = new VfxProgramData();
                    Stream? stream = null;

                    try
                    {
                        stream = new FileStream(vcsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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

    public void Add(VfxProgramData program)
    {
        if (!shaders.TryAdd(program.VcsProgramType, program))
        {
            throw new ArgumentException($"Shader of type {program.VcsProgramType} already exists in this collection.");
        }
    }

    public VfxProgramData? Get(VcsProgramType type)
    {
        if (shaders.TryGetValue(type, out var shader))
        {
            return shader;
        }

        return null;
    }

    public IEnumerator<VfxProgramData> GetEnumerator()
    {
        return shaders.Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)shaders.Values).GetEnumerator();
    }

    public static ShaderCollection FromEnumerable(IEnumerable<VfxProgramData> shaders)
    {
        var collection = new ShaderCollection();

        foreach (var shader in shaders)
        {
            collection.Add(shader);
        }

        return collection;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

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
