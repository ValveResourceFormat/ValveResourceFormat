using System.Buffers;

namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

/// <summary>
/// Sums multiple sample providers into one stream. Providers that run out of samples are removed automatically.
/// Thread safe: providers can be added and removed while the mixing thread is reading.
/// </summary>
public class SampleProviderMulti : AudioSampleProvider
{
    private readonly LinkedList<IAudioSampleProvider> providers;

    public SampleProviderMulti()
    {
        providers = new();
    }

    public SampleProviderMulti(IEnumerable<IAudioSampleProvider> sampleProviders)
    {
        providers = new(sampleProviders);
    }

    public void AddProvider(IAudioSampleProvider provider)
    {
        lock (providers)
        {
            providers.AddLast(provider);
        }
    }

    public void RemoveProvider(IAudioSampleProvider provider)
    {
        lock (providers)
        {
            providers.Remove(provider);
        }
    }

    public void ClearProviders()
    {
        lock (providers)
        {
            providers.Clear();
        }
    }

    /// <inheritdoc/>
    public override int Read(float[] buffer, int offset, int count)
    {
        var maxRead = 0;

        lock (providers)
        {
            if (providers.Count == 0)
            {
                return 0;
            }

            var readBuffer = ArrayPool<float>.Shared.Rent(count);

            try
            {
                var node = providers.First;
                while (node != null)
                {
                    var next = node.Next;

                    var read = node.Value.Read(readBuffer, 0, count);
                    var index = offset;
                    for (var i = 0; i < read; i++)
                    {
                        if (i >= maxRead)
                        {
                            buffer[index++] = readBuffer[i];
                        }
                        else
                        {
                            buffer[index++] += readBuffer[i];
                        }
                    }

                    if (read < count && node.List == providers)
                    {
                        providers.Remove(node);
                    }

                    maxRead = Math.Max(maxRead, read);

                    node = next;
                }
            }
            finally
            {
                ArrayPool<float>.Shared.Return(readBuffer);
            }
        }

        if (maxRead < count)
        {
            Over();
        }

        return maxRead;
    }
}
