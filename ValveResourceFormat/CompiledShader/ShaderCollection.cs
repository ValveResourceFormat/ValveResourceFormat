using System.Collections;
using System.Collections.Generic;

namespace ValveResourceFormat.CompiledShader;

public class ShaderCollection : IEnumerable<ShaderFile>
{
    public ShaderFile Features
        => Get(VcsProgramType.Features);
    public ShaderFile Vertex
        => Get(VcsProgramType.VertexShader);
    public ShaderFile Geometry
        => Get(VcsProgramType.GeometryShader);
    public ShaderFile Domain
        => Get(VcsProgramType.DomainShader);
    public ShaderFile Hull
        => Get(VcsProgramType.HullShader);
    public ShaderFile Pixel
        => Get(VcsProgramType.PixelShader);
    public ShaderFile Compute
        => Get(VcsProgramType.ComputeShader);
    public ShaderFile PixelShaderRenderState
        => Get(VcsProgramType.PixelShaderRenderState);
    public ShaderFile Raytracing
        => Get(VcsProgramType.RaytracingShader);

    private readonly SortedDictionary<VcsProgramType, ShaderFile> shaders = new();

    public void Add(ShaderFile shaderFile)
    {
        shaders.Add(shaderFile.VcsProgramType, shaderFile);
    }

    public ShaderFile Get(VcsProgramType type)
    {
        if (shaders.TryGetValue(type, out var shader))
        {
            return shader;
        }

        return null;
    }

    public IEnumerator<ShaderFile> GetEnumerator()
    {
        return shaders.Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)shaders.Values).GetEnumerator();
    }

    public static ShaderCollection FromEnumerable(IEnumerable<ShaderFile> shaders)
    {
        var collection = new ShaderCollection();

        foreach (var shader in shaders)
        {
            collection.Add(shader);
        }

        return collection;
    }
}
