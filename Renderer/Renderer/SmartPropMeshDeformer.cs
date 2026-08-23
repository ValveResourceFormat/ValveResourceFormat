using System.Runtime.InteropServices;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace ValveResourceFormat.Renderer;

/// <summary>Applies a saved VMAP SmartProp deformation cage to mesh vertex positions.</summary>
public static class SmartPropMeshDeformer
{
    /// <summary>Creates per-instance vertex buffers deformed in the coordinate space of a SmartProp part.</summary>
    /// <param name="source">The source model vertex and index buffers.</param>
    /// <param name="partTransform">The undeformed part transform in placed SmartProp space.</param>
    /// <param name="deformer">The saved VMAP deformation cage.</param>
    /// <returns>A copy of the buffers containing deformed positions.</returns>
    public static VBIB Deform(VBIB source, Matrix4x4 partTransform, SmartPropMapDeformer deformer)
    {
        Matrix4x4.Invert(partTransform, out var inversePartTransform);
        var result = new VBIB { Resource = source.Resource };

        foreach (var sourceBuffer in source.VertexBuffers)
        {
            var buffer = sourceBuffer;
            buffer.Data = (byte[])sourceBuffer.Data.Clone();
            buffer.InputLayoutFields = (VBIB.RenderInputLayoutField[])sourceBuffer.InputLayoutFields.Clone();

            foreach (var attribute in buffer.InputLayoutFields)
            {
                if (attribute.SemanticName != "POSITION"
                    || attribute.SemanticIndex != 0
                    || attribute.Format != DXGI_FORMAT.R32G32B32_FLOAT)
                {
                    continue;
                }

                var data = buffer.Data.AsSpan();
                var offset = (int)attribute.Offset;
                for (var i = 0; i < buffer.ElementCount; i++)
                {
                    var positionBytes = data.Slice(offset, Marshal.SizeOf<Vector3>());
                    var position = MemoryMarshal.Read<Vector3>(positionBytes);
                    var smartPropPosition = Vector3.Transform(position, partTransform);
                    var deformedPosition = deformer.DeformPosition(smartPropPosition);
                    var localPosition = Vector3.Transform(deformedPosition, inversePartTransform);
                    MemoryMarshal.Write(positionBytes, in localPosition);
                    offset += (int)buffer.ElementSizeInBytes;
                }
            }

            result.VertexBuffers.Add(buffer);
        }

        foreach (var sourceBuffer in source.IndexBuffers)
        {
            var buffer = sourceBuffer;
            buffer.Data = (byte[])sourceBuffer.Data.Clone();
            buffer.InputLayoutFields = (VBIB.RenderInputLayoutField[])sourceBuffer.InputLayoutFields.Clone();
            result.IndexBuffers.Add(buffer);
        }

        return result;
    }
}
