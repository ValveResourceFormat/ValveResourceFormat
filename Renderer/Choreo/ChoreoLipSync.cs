using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Choreo
{
    /// <summary>
    /// Moves the mouth of a model along the phonemes of the sentence of a sound. Flex scene files
    /// hold the flex controller values of each phoneme.
    /// </summary>
    public sealed class ChoreoLipSync
    {
        // Phonemes overlap this far ahead of the time, which blends each one into the next
        private const float FilterDuration = 0.08f;

        private const float NeutralEmphasis = 0.5f;
        private const float MinEmphasis = 0.4f;
        private const float MaxEmphasis = 0.6f;
        private const float MinEmphasisSegment = 0.01f;

        private const int NoController = -1;

        private readonly FlexSceneFile.FlexSceneFile phonemes;
        private readonly int[] keyControllers;

        /// <summary>Creates lip sync for a model.</summary>
        /// <param name="phonemes">The flex controller values of each phoneme.</param>
        /// <param name="controllerIndices">The indices of the flex controllers of the model by name.</param>
        public ChoreoLipSync(FlexSceneFile.FlexSceneFile phonemes, IReadOnlyDictionary<string, int> controllerIndices)
        {
            ArgumentNullException.ThrowIfNull(phonemes);
            ArgumentNullException.ThrowIfNull(controllerIndices);

            this.phonemes = phonemes;

            keyControllers = new int[phonemes.KeyNames.Length];
            for (var i = 0; i < keyControllers.Length; i++)
            {
                keyControllers[i] = controllerIndices.GetValueOrDefault(phonemes.KeyNames[i], NoController);
            }
        }

        /// <summary>Adds what a sentence does to the mouth at a time to flex controller values.</summary>
        /// <param name="sentence">The sentence.</param>
        /// <param name="time">The time in seconds from the start of the sound.</param>
        /// <param name="duration">The length of the sound in seconds.</param>
        /// <param name="values">The values of the flex controllers of the model.</param>
        public void AddVisemes(Sentence sentence, float time, float duration, float[] values)
        {
            ArgumentNullException.ThrowIfNull(sentence);
            ArgumentNullException.ThrowIfNull(values);

            var tags = sentence.RunTimePhonemes;
            var strength = 2f * Math.Clamp(GetEmphasis(sentence, time, duration), MinEmphasis, MaxEmphasis);
            var window = FilterDuration;

            for (var i = 0; i < tags.Length; i++)
            {
                var tag = tags[i];

                // The window stretches over the phoneme being spoken, up to its own length, and stays that wide for the rest
                if (time > tag.StartTime && tag.EndTime > time && i < tags.Length - 1)
                {
                    var next = tags[i + 1];
                    var nextTime = next.StartTime == tag.EndTime ? next.EndTime : next.StartTime;
                    window = MathF.Max(window, MathF.Min(nextTime - time, tag.EndTime - tag.StartTime));
                }

                var start = (tag.StartTime - time) / window;
                var end = (tag.EndTime - time) / window;

                if (start < 1f && end > 0f)
                {
                    var overlap = MathF.Min(end, 1f) - MathF.Max(start, 0f);
                    AddViseme(tag.PhonemeCode, overlap * strength, values);
                }
            }
        }

        private void AddViseme(int phoneme, float scale, float[] values)
        {
            var setting = phonemes.GetSettingForPhonemeCode(phoneme);
            if (setting == null)
            {
                return;
            }

            foreach (var (key, weight) in setting.Settings)
            {
                var controller = keyControllers[key];

                if (controller != NoController)
                {
                    values[controller] += scale * weight.Weight;
                }
            }
        }

        private static float GetEmphasis(Sentence sentence, float time, float duration)
        {
            var samples = sentence.EmphasisSamples;
            if (samples.Length == 0)
            {
                return NeutralEmphasis;
            }

            var segment = 0;
            while (segment < samples.Length && !IsInSegment(samples, segment, time, duration))
            {
                segment++;
            }

            var pre = GetEmphasisPoint(samples, segment - 1, duration);
            var start = GetEmphasisPoint(samples, segment, duration);
            var end = GetEmphasisPoint(samples, segment + 1, duration);
            var next = GetEmphasisPoint(samples, segment + 2, duration);

            var length = Math.Clamp(end.X - start.X, MinEmphasisSegment, 1f);
            var fraction = MathUtils.Saturate((time - start.X) / length);

            return MathUtils.Saturate(CableMeshBuilder.CatmullRom(pre.Y, start.Y, end.Y, next.Y, fraction));
        }

        private static bool IsInSegment(EmphasisSample[] samples, int segment, float time, float duration)
            => samples[segment].Time <= time && time <= GetEmphasisPoint(samples, segment + 1, duration).X;

        // Emphasis is neutral at the ends of the sound
        private static Vector2 GetEmphasisPoint(EmphasisSample[] samples, int index, float duration)
        {
            if (index < 0)
            {
                return new Vector2(0f, NeutralEmphasis);
            }

            if (index >= samples.Length)
            {
                return new Vector2(duration, NeutralEmphasis);
            }

            return new Vector2(samples[index].Time, samples[index].Value);
        }
    }
}
