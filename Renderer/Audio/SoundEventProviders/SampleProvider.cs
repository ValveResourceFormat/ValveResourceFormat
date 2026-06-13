using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.Renderer.Audio.SoundEventProviders
{
    public abstract class SampleProvider : ISampleProvider
    {
        public delegate void OnOverDelegate();
        public event OnOverDelegate OnOver;
        private float volume;
        public float Volume
        {
            get
            {
                return volume;
            }
            set
            {
                volume = value;
                VolumeMultiplier = (float)((Math.Exp(value) - 1) / (Math.E - 1));
            }
        }
        protected float VolumeMultiplier { get; private set; } = 1f;
        public abstract WaveFormat WaveFormat { get; }

        public abstract int Read(float[] buffer, int offset, int count);

        protected void Over()
        {
            OnOver?.Invoke();
        }
    }
}
