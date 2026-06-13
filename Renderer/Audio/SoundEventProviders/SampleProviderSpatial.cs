using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.Renderer.Audio.SoundEventProviders
{
    abstract class SampleProviderSpatial : SampleProvider2D
    {
        public float LeftVolume { get; protected set; }
        public float RightVolume { get; protected set; }

        protected float LastLeftVolume;
        protected float LastRightVolume;

        public SampleProviderSpatial(ISampleProvider provider) : base(provider)
        {
        }

        public override int Read(float[] buffer, int offset, int count)
        {
            var read = Provider.Read(buffer, offset, count);
            for (var i = 0; i < read; i++)
            {
                var left = i % 2 == 0;
                var lastVolume = left ? LastLeftVolume : LastRightVolume;
                var volume = left ? LeftVolume : RightVolume;
                buffer[i] = float.Lerp(buffer[i] * lastVolume, buffer[i] * volume, (float)i / count);
            }

            LastLeftVolume = LeftVolume;
            LastRightVolume = RightVolume;

            if (read < count)
            {
                Over();
            }

            return read;
        }

        public virtual bool Update(Vector3 cameraPosition, Vector3 rightEarDirection)
        {
            var dot = GetDirectionMix(cameraPosition, rightEarDirection);

            LeftVolume = Math.Max(-dot + 1, 0) * Volume;
            RightVolume = Math.Max(dot + 1, 0) * Volume;
            return true;
        }

        protected abstract float GetDirectionMix(Vector3 cameraPosition, Vector3 rightEarDirection);
    }
}
