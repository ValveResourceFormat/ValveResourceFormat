using System.Collections;

#nullable disable

namespace ValveResourceFormat.CompiledShader;

public class ShaderCollection : IEnumerable<VfxProgramData>, IDisposable
{
    public VfxProgramData Features
        => Get(VcsProgramType.Features);
    public VfxProgramData Vertex
        => Get(VcsProgramType.VertexShader);
    public VfxProgramData Geometry
        => Get(VcsProgramType.GeometryShader);
    public VfxProgramData Domain
        => Get(VcsProgramType.DomainShader);
    public VfxProgramData Hull
        => Get(VcsProgramType.HullShader);
    public VfxProgramData Pixel
        => Get(VcsProgramType.PixelShader);
    public VfxProgramData Compute
        => Get(VcsProgramType.ComputeShader);
    public VfxProgramData PixelShaderRenderState
        => Get(VcsProgramType.PixelShaderRenderState);
    public VfxProgramData Raytracing
        => Get(VcsProgramType.RaytracingShader);
    public VfxProgramData Mesh
        => Get(VcsProgramType.MeshShader);

    private readonly Dictionary<VcsProgramType, VfxProgramData> shaders = new((int)VcsProgramType.Undetermined);

    public void Add(VfxProgramData program)
    {
        if (!shaders.TryAdd(program.VcsProgramType, program))
        {
            throw new ArgumentException($"Shader of type {program.VcsProgramType} already exists in this collection.");
        }
    }

    public VfxProgramData Get(VcsProgramType type)
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
