using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GUI.Types.Audio.SoundEventProviders
{
    class SampleProviderDirectional : SampleProviderSpatial
    {
        public Vector3 Direction { get; set; }
        public SampleProviderDirectional(ISampleProvider provider) : base(provider)
        {
        }

        protected override float GetDirectionMix(Vector3 cameraPosition, Vector3 rightEarDirection)
        {
            return Vector3.Dot(Direction, rightEarDirection);
        }
    }
}
