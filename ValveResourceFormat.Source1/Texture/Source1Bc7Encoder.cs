using System.Buffers.Binary;

namespace ValveResourceFormat.Source1.Texture;

/// <summary>
/// Encodes 8-bit images as BC7, using only mode 6: one pair of 7-bit RGBA endpoints with a p-bit each and 4-bit indices per 4x4 block.
/// </summary>
internal static class Source1Bc7Encoder
{
    private const int BlockSize = 4;
    private const int BlockPixels = BlockSize * BlockSize;
    private const int BlockBytes = 16;
    private const int IndexCount = 16;
    private const int Mode6Prefix = 1 << 6;
    private const int RefinementPasses = 2;

    private static ReadOnlySpan<int> Weights => [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

    private readonly record struct Endpoint(int R, int G, int B, int A, int P)
    {
        public Vector4 Color => new((R << 1) | P, (G << 1) | P, (B << 1) | P, (A << 1) | P);
    }

    /// <summary>Encodes an image of BGRA pixels. The edges of an image that is not a multiple of four repeat.</summary>
    /// <param name="bgra">The pixels, four bytes each, row by row.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>The 16 byte blocks, row by row.</returns>
    public static byte[] Encode(ReadOnlySpan<byte> bgra, int width, int height)
    {
        var blocksX = (width + BlockSize - 1) / BlockSize;
        var blocksY = (height + BlockSize - 1) / BlockSize;
        var output = new byte[blocksX * blocksY * BlockBytes];
        Span<Vector4> pixels = stackalloc Vector4[BlockPixels];

        for (var blockY = 0; blockY < blocksY; blockY++)
        {
            for (var blockX = 0; blockX < blocksX; blockX++)
            {
                ReadBlock(bgra, width, height, blockX, blockY, pixels);

                var destination = output.AsSpan(((blockY * blocksX) + blockX) * BlockBytes, BlockBytes);
                EncodeBlock(pixels, destination);
            }
        }

        return output;
    }

    private static void ReadBlock(ReadOnlySpan<byte> bgra, int width, int height, int blockX, int blockY, Span<Vector4> pixels)
    {
        for (var y = 0; y < BlockSize; y++)
        {
            var sourceY = Math.Min((blockY * BlockSize) + y, height - 1);

            for (var x = 0; x < BlockSize; x++)
            {
                var sourceX = Math.Min((blockX * BlockSize) + x, width - 1);
                var offset = ((sourceY * width) + sourceX) * 4;

                pixels[(y * BlockSize) + x] = new Vector4(bgra[offset + 2], bgra[offset + 1], bgra[offset], bgra[offset + 3]);
            }
        }
    }

    private static void EncodeBlock(ReadOnlySpan<Vector4> pixels, Span<byte> destination)
    {
        Span<int> indices = stackalloc int[BlockPixels];
        Span<int> bestIndices = stackalloc int[BlockPixels];
        Span<Vector4> palette = stackalloc Vector4[IndexCount];

        FindAxisEndpoints(pixels, out var low, out var high);

        var bestLow = Quantize(low);
        var bestHigh = Quantize(high);
        var bestError = float.MaxValue;

        for (var pass = 0; pass <= RefinementPasses; pass++)
        {
            var lowEndpoint = Quantize(low);
            var highEndpoint = Quantize(high);

            BuildPalette(lowEndpoint, highEndpoint, palette);
            var error = SelectIndices(pixels, palette, indices);

            if (error < bestError)
            {
                bestError = error;
                bestLow = lowEndpoint;
                bestHigh = highEndpoint;
                indices.CopyTo(bestIndices);
            }

            if (!FitEndpoints(pixels, indices, out low, out high))
            {
                break;
            }
        }

        Write(destination, bestLow, bestHigh, bestIndices);
    }

    // The endpoints are the extremes of the pixels along their principal axis, found by power iteration
    private static void FindAxisEndpoints(ReadOnlySpan<Vector4> pixels, out Vector4 low, out Vector4 high)
    {
        var mean = Vector4.Zero;
        var minimum = new Vector4(255f);
        var maximum = Vector4.Zero;

        foreach (var pixel in pixels)
        {
            mean += pixel;
            minimum = Vector4.Min(minimum, pixel);
            maximum = Vector4.Max(maximum, pixel);
        }

        mean /= BlockPixels;

        var axis = maximum - minimum;

        if (axis == Vector4.Zero)
        {
            low = mean;
            high = mean;
            return;
        }

        for (var iteration = 0; iteration < 4; iteration++)
        {
            var next = Vector4.Zero;

            foreach (var pixel in pixels)
            {
                var offset = pixel - mean;
                next += offset * Vector4.Dot(offset, axis);
            }

            var length = next.Length();

            if (length < 1e-6f)
            {
                break;
            }

            axis = next / length;
        }

        axis = Vector4.Normalize(axis);

        var lowest = float.MaxValue;
        var highest = float.MinValue;

        foreach (var pixel in pixels)
        {
            var projection = Vector4.Dot(pixel - mean, axis);
            lowest = Math.Min(lowest, projection);
            highest = Math.Max(highest, projection);
        }

        low = Vector4.Clamp(mean + (axis * lowest), Vector4.Zero, new Vector4(255f));
        high = Vector4.Clamp(mean + (axis * highest), Vector4.Zero, new Vector4(255f));
    }

    // Picks the p-bit that gives the closest 8-bit color
    private static Endpoint Quantize(Vector4 color)
    {
        var best = default(Endpoint);
        var bestError = float.MaxValue;

        for (var p = 0; p < 2; p++)
        {
            var candidate = new Endpoint(
                Quantize7(color.X, p),
                Quantize7(color.Y, p),
                Quantize7(color.Z, p),
                Quantize7(color.W, p),
                p);

            var error = Vector4.DistanceSquared(candidate.Color, color);

            if (error < bestError)
            {
                bestError = error;
                best = candidate;
            }
        }

        return best;
    }

    private static int Quantize7(float value, int p) => Math.Clamp((int)MathF.Round((value - p) / 2f), 0, 127);

    private static void BuildPalette(Endpoint low, Endpoint high, Span<Vector4> palette)
    {
        var lowColor = low.Color;
        var highColor = high.Color;

        for (var i = 0; i < IndexCount; i++)
        {
            var weight = Weights[i];

            palette[i] = new Vector4(
                Interpolate(lowColor.X, highColor.X, weight),
                Interpolate(lowColor.Y, highColor.Y, weight),
                Interpolate(lowColor.Z, highColor.Z, weight),
                Interpolate(lowColor.W, highColor.W, weight));
        }
    }

    private static int Interpolate(float low, float high, int weight) => (((int)low * (64 - weight)) + ((int)high * weight) + 32) >> 6;

    private static float SelectIndices(ReadOnlySpan<Vector4> pixels, ReadOnlySpan<Vector4> palette, Span<int> indices)
    {
        var totalError = 0f;

        for (var i = 0; i < BlockPixels; i++)
        {
            var bestIndex = 0;
            var bestError = float.MaxValue;

            for (var index = 0; index < IndexCount; index++)
            {
                var error = Vector4.DistanceSquared(pixels[i], palette[index]);

                if (error < bestError)
                {
                    bestError = error;
                    bestIndex = index;
                }
            }

            indices[i] = bestIndex;
            totalError += bestError;
        }

        return totalError;
    }

    // Least squares endpoints for the interpolation weights of the chosen indices
    private static bool FitEndpoints(ReadOnlySpan<Vector4> pixels, ReadOnlySpan<int> indices, out Vector4 low, out Vector4 high)
    {
        var alpha = 0f;
        var beta = 0f;
        var gamma = 0f;
        var lowSum = Vector4.Zero;
        var highSum = Vector4.Zero;

        for (var i = 0; i < BlockPixels; i++)
        {
            var highWeight = Weights[indices[i]] / 64f;
            var lowWeight = 1f - highWeight;

            alpha += lowWeight * lowWeight;
            beta += lowWeight * highWeight;
            gamma += highWeight * highWeight;
            lowSum += pixels[i] * lowWeight;
            highSum += pixels[i] * highWeight;
        }

        var determinant = (alpha * gamma) - (beta * beta);

        if (determinant < 1e-4f)
        {
            low = Vector4.Zero;
            high = Vector4.Zero;
            return false;
        }

        low = Vector4.Clamp(((lowSum * gamma) - (highSum * beta)) / determinant, Vector4.Zero, new Vector4(255f));
        high = Vector4.Clamp(((highSum * alpha) - (lowSum * beta)) / determinant, Vector4.Zero, new Vector4(255f));
        return true;
    }

    private static void Write(Span<byte> destination, Endpoint low, Endpoint high, Span<int> indices)
    {
        // The first index has no top bit, so it must be below 8
        if (indices[0] >= IndexCount / 2)
        {
            (low, high) = (high, low);

            for (var i = 0; i < indices.Length; i++)
            {
                indices[i] = IndexCount - 1 - indices[i];
            }
        }

        var bits = (UInt128)Mode6Prefix;
        var position = 7;

        WriteBits(ref bits, ref position, low.R, 7);
        WriteBits(ref bits, ref position, high.R, 7);
        WriteBits(ref bits, ref position, low.G, 7);
        WriteBits(ref bits, ref position, high.G, 7);
        WriteBits(ref bits, ref position, low.B, 7);
        WriteBits(ref bits, ref position, high.B, 7);
        WriteBits(ref bits, ref position, low.A, 7);
        WriteBits(ref bits, ref position, high.A, 7);
        WriteBits(ref bits, ref position, low.P, 1);
        WriteBits(ref bits, ref position, high.P, 1);

        WriteBits(ref bits, ref position, indices[0], 3);

        for (var i = 1; i < indices.Length; i++)
        {
            WriteBits(ref bits, ref position, indices[i], 4);
        }

        BinaryPrimitives.WriteUInt128LittleEndian(destination, bits);
    }

    private static void WriteBits(ref UInt128 bits, ref int position, int value, int count)
    {
        bits |= (UInt128)(uint)value << position;
        position += count;
    }
}
