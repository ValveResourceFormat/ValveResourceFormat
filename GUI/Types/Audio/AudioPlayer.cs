using System;
using System.IO;
using NAudio.Wave;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Audio
{
    internal class Player
    {
        public Player(Resource resource)
        {
            var soundData = (Sound)resource.Blocks[BlockType.DATA];

            var stream = soundData.GetSoundStream();
            var waveOut = new WaveOut(WaveCallbackInfo.FunctionCallback());

            if (soundData.Type == Sound.AudioFileType.WAV)
            {
                var rawSource = new WaveFileReader(stream);
                waveOut.Init(rawSource);
            }
            else if (soundData.Type == Sound.AudioFileType.MP3)
            {
                var rawSource = new Mp3FileReader(stream);
                waveOut.Init(rawSource);
            }

            waveOut.Play();
        }
    }
}
