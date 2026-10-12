using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ValveResourceFormat.Renderer.RHI
{
    // Matches slang's DescriptorHandle, which is a uint2 of resource index and sampler index
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct DescriptorHandle<T> : IEquatable<DescriptorHandle<T>> where T : IResource
    {
        public readonly uint Index;
        public readonly uint SamplerIndex;

        public DescriptorHandle(uint index, uint samplerIndex = 0)
        {
            Index = index;
            SamplerIndex = samplerIndex;
        }

        public bool Equals(DescriptorHandle<T> other) => Index == other.Index && SamplerIndex == other.SamplerIndex;

        public override bool Equals(object? obj) => obj is DescriptorHandle<T> other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Index, SamplerIndex);

        public static bool operator ==(DescriptorHandle<T> left, DescriptorHandle<T> right) => left.Equals(right);

        public static bool operator !=(DescriptorHandle<T> left, DescriptorHandle<T> right) => !left.Equals(right);
    }

    /// <summary>
    /// Used when swap-in-place is required. Example: Shader hot reloading requires to defer destruction of the old pipeline,
    /// because it is still in use.
    /// </summary>
    /// <typeparam name="T">The resource type</typeparam>
    public sealed class ResourceHandle<T> where T : class, IResource
    {
        private T _current;
        public T Current => Volatile.Read<T>(ref _current);

        internal T Swap(T next)
            => Interlocked.Exchange(ref _current, next);

        public ResourceHandle(T resource)
        {
            _current = resource;
        }
    }

    public interface IResource
    {
        public abstract void Destroy();
    }

    
}
