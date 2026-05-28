using GUI.Types.Audio.SoundEventProviders;
using GUI.Utils;
using NAudio.Mixer;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using static System.Windows.Forms.AxHost;

namespace GUI.Types.Audio
{
    internal abstract class WorldSoundEvent : IDisposable
    {
        public delegate void OnSoundOverDelegate(WorldSoundEvent soundEvent);
        public event OnSoundOverDelegate OnSoundOver;
        public delegate void OnSoundStartDelegate(WorldSoundEvent soundEvent);
        public event OnSoundStartDelegate OnSoundStart;
        public delegate void OnStartDelegate(WorldSoundEvent soundEvent);
        public event OnStartDelegate OnStart;
        public delegate void OnStopDelegate(WorldSoundEvent soundEvent);
        public event OnStopDelegate OnStop;
        public bool Playing { get; protected set; }
        public bool Started { get; protected set; }

        private Vector3 position;
        public Vector3 Position
        {
            get
            {
                return position;
            }
            set
            {
                position = value;
            }
        }
        public KVObject SoundEventData { get; init; }
        public string SoundName => SoundEventData.Key;
        public SampleProviderMulti SampleProvider { get; private set; }
        public List<WorldSoundEvent> ChildSoundEvents { get; private set; } = new();
        public List<SampleProvider> SampleProviders { get; private set; } = new();
        public bool SoundScape { get; set; }

        public WorldSoundMixer Mixer { get; private set; }
        public int SampleRate { get; private set; }

        public WorldSoundEvent(KVObject soundEvent)
        {
            SoundEventData = soundEvent;
        }

        public void Start()
        {
            SampleProviders.Clear();
            ChildSoundEvents.Clear();

            DoStart();

            if (SampleProviders.Count > 0)
            {
                foreach (var item in SampleProviders)
                {
                    SampleProvider.AddProvider(item);
                }

                if (!Playing)
                {
                    OnStarted();
                }
            }
            else
            {
                if (Playing)
                {
                    OnFinished();
                }
            }
            if (!Started)
            {
                Started = true;
                OnStart?.Invoke(this);
            }
        }

        protected void StartAsChild(WorldSoundEvent childSoundEvent)
        {
            childSoundEvent.Init(Mixer, SampleRate);
            ChildSoundEvents.Add(childSoundEvent);
            SampleProviders.Add(childSoundEvent.SampleProvider);
            childSoundEvent.OnSoundStart += ChildSoundStarted;
            childSoundEvent.OnSoundOver += ChildSoundOver;
            childSoundEvent.Start();
        }

        private void ChildSoundOver(WorldSoundEvent soundEvent)
        {
            if (Playing)
            {
                OnFinished();
            }
        }

        private void ChildSoundStarted(WorldSoundEvent soundEvent)
        {
            if (!Playing)
            {
                OnStarted();
            }
        }

        protected virtual void DoStart()
        {

        }

        public void Stop()
        {
            if (Playing)
            {
                OnFinished();
            }
            if (Started)
            {
                OnStop?.Invoke(this);
                Started = false;
            }
            foreach (var item in ChildSoundEvents)
            {
                item.Stop();
            }
        }

        public void Init(WorldSoundMixer mixer, int sampleRate)
        {
            Mixer = mixer;
            SampleRate = sampleRate;

            SampleProvider = new SampleProviderMulti(SampleProviders, Mixer.WaveFormat);
            SampleProvider.OnOver += OnFinished;
        }

        protected virtual void OnFinished()
        {
            Playing = false;
            OnSoundOver?.Invoke(this);
        }

        protected virtual void OnStarted()
        {
            Playing = true;
            OnSoundStart?.Invoke(this);
        }

        //protected abstract void DoInit(WorldSoundMixer mixer, int sampleRate);
        public virtual bool Update(Vector3 cameraPosition, Vector3 rightEarDirection)
        {
            bool anyPlaying = false;
            foreach (var item in SampleProviders)
            {
                if (item is SampleProvider3D spatialProvider)
                {
                    if (spatialProvider.Update(cameraPosition, rightEarDirection))
                    {
                        anyPlaying = true;
                    }
                }
            }
            foreach (var child in ChildSoundEvents)
            {
                if (child.Update(cameraPosition, rightEarDirection))
                {
                    anyPlaying = true;
                }
            }
            return anyPlaying;
        }

        public virtual void Dispose()
        {
            OnSoundOver = null;
            OnSoundStart = null;
        }

        public static WorldSoundEvent Build(KVObject soundEventData)
        {
            var type = soundEventData.GetStringProperty("type");
            switch (type)
            {
                case "csgo_mega":
                    return new WorldSoundEventCSGOMega(soundEventData);
                default:
                    throw new NotImplementedException();
                    //return new WorldSoundEventTest(soundEventData);
            }
        }

        protected T[] GetArrayProperty<T>(string name)
        {
            if (!SoundEventData.Properties.TryGetValue(name, out var value))
            {
                return Array.Empty<T>();
            }

            if (value.Type == KVType.ARRAY || value.Type == KVType.ARRAY_TYPED)
            {
                return ((KVObject)value.Value).Properties.Values.Select(v => (T)v.Value).ToArray();
            }
            else
            {
                return [(T)value.Value];
            }
        }
    }
}
