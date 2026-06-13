using GUI.Types.Renderer;
using GUI.Utils;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ValveResourceFormat.Blocks.ResourceIntrospectionManifest.ResourceDiskEnum;

namespace ValveResourceFormat.Renderer.Audio.SoundEventProviders
{
    class SampleProvider3D : SampleProviderSpatial
    {
        public float Range { get; set; } = 512;
        public Vector3 Position { get; set; }

        public bool OutOfRange { get; private set; }

        public SampleProvider3D(ISampleProvider provider) : base(provider)
        {
        }

        public override bool Update(Vector3 cameraPosition, Vector3 rightEarDirection)
        {
            var distance = (cameraPosition - Position).Length();

            OutOfRange = distance > Range;
            if (OutOfRange)
            {
                LeftVolume = 0;
                RightVolume = 0;
                return false;
            }
            base.Update(cameraPosition, rightEarDirection);

            var multiplier = 1f - distance / Range;
            multiplier = (float)((Math.Exp(multiplier) - 1) / (Math.E - 1));
            LeftVolume *= multiplier;
            RightVolume *= multiplier;
            return true;
        }

        protected override float GetDirectionMix(Vector3 cameraPosition, Vector3 rightEarDirection)
        {
            var soundDirectionToCamera = cameraPosition - Position;
            var distance = soundDirectionToCamera.Length();
            soundDirectionToCamera /= distance;

            return Vector3.Dot(soundDirectionToCamera, rightEarDirection);
        }
    }
}
